using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Waitlist;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>The public waitlist endpoint (validation, silent duplicates, rate limit, no logging) and its superadmin console routes.</summary>
public sealed class WaitlistApiTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly CapturingLoggerProvider _logs = new();
    private readonly WebApplicationFactory<Program> _factory;

    public WaitlistApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o
                    .UseSqlite(_fixture.ConnectionString)
                    .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>());
                services.AddLogging(l => l.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _lines = [];
        public string All { get { lock (_lines) return string.Join("\n", _lines); } }
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._lines) owner._lines.Add(formatter(state, exception) + exception);
            }
        }
    }

    private async Task<HttpClient> ClientForRoleAsync(string? role)
    {
        var client = _factory.CreateClient();
        if (role is null) return client;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var email = $"{role}-{Guid.NewGuid():N}@trazo.test";
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = role,
            Role = role, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now
        };
        user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, "CorrectHorse1Battery");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private static Task<HttpResponseMessage> Join(HttpClient c, string? email, string? language = "es", bool consent = true) =>
        c.PostAsJsonAsync("/api/v1/waitlist", new { email, language, consent });

    private async Task<List<(string Email, string Language)>> Stored()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        return (await db.WaitlistEntries.OrderBy(x => x.Email).ToListAsync()).Select(x => (x.Email, x.Language)).ToList();
    }

    [Fact]
    public async Task Join_StoresNormalizedEmailWithConsentAndDates()
    {
        var response = await Join(_factory.CreateClient(), "  Ana.Lopez@Example.COM ", "EN");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var entry = await db.WaitlistEntries.SingleAsync();
        Assert.Equal("ana.lopez@example.com", entry.Email);
        Assert.Equal("en", entry.Language);
        Assert.True(entry.ConsentAt > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.True(entry.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Join_DuplicateGetsTheSameAnswerAndNoSecondRow()
    {
        var client = _factory.CreateClient();
        var first = await Join(client, "dup@example.com");
        var second = await Join(client, "DUP@example.com", "en");
        Assert.Equal(first.StatusCode, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        var rows = await Stored();
        Assert.Single(rows);
        Assert.Equal("es", rows[0].Language); // the first sign-up is kept untouched
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("a b@example.com")]
    [InlineData("Ana <ana@example.com>")]
    public async Task Join_RejectsInvalidEmail(string? email)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Join(_factory.CreateClient(), email)).StatusCode);
        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task Join_RequiresConsentAndKnownLanguage()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await Join(client, "a@example.com", consent: false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Join(client, "a@example.com", language: "fr")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Join(client, "a@example.com", language: null)).StatusCode);
        Assert.Empty(await Stored());
    }

    [Fact]
    public async Task Join_IsRateLimitedPerIp()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await Join(client, $"user{i}@example.com")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Join(client, "user6@example.com")).StatusCode);
    }

    [Fact]
    public async Task Join_AddsNoCorsHeadersAndNeverLogsTheEmail()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/waitlist")
        {
            Content = JsonContent.Create(new { email = "secret.person@example.com", language = "es", consent = true })
        };
        request.Headers.Add("Origin", "https://evil.example");
        var response = await client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        await Join(client, "secret.person@example.com"); // the duplicate path as well
        Assert.DoesNotContain("secret.person", _logs.All, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdminRoutes_RequireSuperadmin()
    {
        foreach (var role in new string?[] { null, "user", "admin" })
        {
            var client = await ClientForRoleAsync(role);
            var expected = role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await client.GetAsync("/api/v1/admin/waitlist")).StatusCode);
            Assert.Equal(expected, (await client.GetAsync("/api/v1/admin/waitlist/export.csv")).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync($"/api/v1/admin/waitlist/{Guid.NewGuid()}")).StatusCode);
        }
    }

    [Fact]
    public async Task Admin_ListsCountsExportsAndDeletes()
    {
        var anonymous = _factory.CreateClient();
        await Join(anonymous, "uno@example.com", "es");
        await Join(anonymous, "dos@example.com", "en");
        await Join(anonymous, "+tres@example.com", "es");
        var admin = await ClientForRoleAsync("superadmin");

        var list = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/waitlist");
        Assert.Equal(3, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, list.GetProperty("overall").GetProperty("es").GetInt32());
        Assert.Equal(1, list.GetProperty("overall").GetProperty("en").GetInt32());

        var search = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/waitlist?q=DOS");
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, search.GetProperty("overall").GetProperty("total").GetInt32());

        var csv = await admin.GetAsync("/api/v1/admin/waitlist/export.csv");
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("trazo-waitlist-", csv.Content.Headers.ContentDisposition?.FileName);
        var text = await csv.Content.ReadAsStringAsync();
        Assert.Contains("email,language,consent_at,created_at", text);
        Assert.Contains("uno@example.com,es,", text);
        Assert.Contains("'+tres@example.com", text); // formula-safe

        var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/admin/waitlist/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/admin/waitlist/{id}")).StatusCode);
        Assert.Equal(2, (await Stored()).Count);
    }

    [Fact]
    public async Task DeleteByEmail_RemovesTheEntryIgnoringCase()
    {
        await Join(_factory.CreateClient(), "forget.me@example.com");
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Equal(1, await WaitlistService.DeleteByEmailAsync(db, " Forget.Me@Example.com ", CancellationToken.None));
        Assert.Empty(await Stored());
    }
}
