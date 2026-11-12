using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>HTTP-level behaviour of the setup wizard endpoints on a real (in-process) API host.</summary>
public sealed class SetupApiTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly FakeTransport _smtp = new();
    private readonly WebApplicationFactory<Program> _factory;

    public SetupApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            // UseSetting is applied before Program reads configuration (ConfigureAppConfiguration is too late).
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString));
                services.RemoveAll<ISmtpTransport>();
                services.AddSingleton<ISmtpTransport>(_smtp);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static object Payload(string email = "owner@example.com") => new
    {
        google = new { clientId = "123-abc.apps.googleusercontent.com" },
        email = new { host = "smtp.example.com", port = 587, enableSsl = true, username = "u", password = "pw", from = "no-reply@example.com", actionBaseUrl = "trazo://account" },
        admin = new { email, displayName = "Owner", password = "CorrectHorse1Battery" }
    };

    private async Task<bool> SetupRequiredAsync(HttpClient client)
    {
        var status = await client.GetFromJsonAsync<Dictionary<string, System.Text.Json.JsonElement>>("/api/v1/setup/status");
        return status!["setupRequired"].GetBoolean();
    }

    [Fact]
    public async Task Wizard_OpensOnFreshInstall_ThenReturns404ForEverySetupEndpoint()
    {
        var client = _factory.CreateClient();
        Assert.True(await SetupRequiredAsync(client));

        var first = await client.PostAsJsonAsync("/api/v1/setup/complete", Payload());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.False(await SetupRequiredAsync(client));

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/setup/complete", Payload("intruder@example.com"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/setup/test/google", new { clientId = "x.apps.googleusercontent.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/setup/test/smtp", new { email = new { host = "h", port = 25, enableSsl = false, from = "a@b.c", actionBaseUrl = "trazo://a" }, recipient = "a@b.c" })).StatusCode);
        using var check = _fixture.NewContext();
        Assert.Equal(1, await check.Users.CountAsync());
    }

    [Fact]
    public async Task InvalidSetupPayload_Returns400_AndKeepsWizardOpen()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/setup/complete", new
        {
            google = new { clientId = "bad" },
            email = new { host = "", port = 0, enableSsl = true, from = "x", actionBaseUrl = "" },
            admin = new { email = "nope", displayName = "", password = "short" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(await SetupRequiredAsync(client));
    }

    [Fact]
    public async Task SmtpTestEndpoint_SendsThroughTransport_WhileOpen()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/setup/test/smtp", new
        {
            email = new { host = "smtp.example.com", port = 25, enableSsl = false, from = "no-reply@example.com", actionBaseUrl = "trazo://account" },
            recipient = "owner@example.com"
        });
        response.EnsureSuccessStatusCode();
        Assert.Equal("smtp.example.com", Assert.Single(_smtp.Hosts));
    }

    [Fact]
    public async Task RegistrationIsBlocked_UntilSetupCompletes()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = "early@example.com", password = "CorrectHorse1Battery", displayName = "Early" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var check = _fixture.NewContext();
        Assert.Equal(0, await check.Users.CountAsync());
    }

    [Fact]
    public async Task AdminSettings_RequireSuperadmin()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/setup/complete", Payload());

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/settings")).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<AuthTokenService>();
        var admin = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Users.SingleAsync();
        var regular = new TrazoUser { Id = Guid.NewGuid(), Email = "u@example.com", NormalizedEmail = "U@EXAMPLE.COM", DisplayName = "U", Role = "user" };

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateAccessToken(regular));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/settings")).StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.CreateAccessToken(admin));
        var view = await client.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        var body = await view.Content.ReadAsStringAsync();
        Assert.Contains("smtp.example.com", body);
        Assert.DoesNotContain("\"pw\"", body);
        Assert.Contains("\"smtpPasswordSet\":true", body);

        var put = await client.PutAsJsonAsync("/api/v1/admin/settings", new
        {
            google = new { clientId = "456-def.apps.googleusercontent.com" },
            email = new { host = "new-smtp.example.com", port = 465, enableSsl = true, from = "no-reply@example.com", actionBaseUrl = "https://example.com" }
        });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var runtime = scope.ServiceProvider.GetRequiredService<IRuntimeSettings>();
        Assert.Equal("new-smtp.example.com", (await runtime.GetEmailAsync()).Host);
        Assert.Equal("pw", (await runtime.GetEmailAsync()).Password); // omitted password keeps the stored one
    }

    private sealed class FakeTransport : ISmtpTransport
    {
        public List<string> Hosts { get; } = [];
        public Task SendAsync(EmailConfig config, string recipient, string subject, string body, CancellationToken ct)
        {
            Hosts.Add(config.Host);
            return Task.CompletedTask;
        }
    }
}
