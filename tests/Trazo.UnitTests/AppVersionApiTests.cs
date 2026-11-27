using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Security;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>The Android version policy: public to read, superadmin to change, off until someone sets it.</summary>
public sealed class AppVersionApiTests : IDisposable
{
    private const string Public = "/api/v1/app/version-policy";
    private const string Admin = "/api/v1/admin/settings/app-version";
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public AppVersionApiTests()
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
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task<HttpClient> SuperadminClientAsync()
    {
        var client = _factory.CreateClient();
        var setup = await client.PostAsJsonAsync("/api/v1/setup/complete", new
        {
            google = new { clientId = "123-abc.apps.googleusercontent.com" },
            email = new { host = "smtp.example.com", port = 587, enableSsl = true, from = "no-reply@example.com", actionBaseUrl = "https://trazo.example.com" },
            admin = new { email = "owner@example.com", displayName = "Owner", password = "CorrectHorse1Battery" }
        });
        setup.EnsureSuccessStatusCode();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var admin = await db.Users.SingleAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateAccessToken(admin));
        return client;
    }

    private static async Task<(int Minimum, int Recommended)> ReadAsync(HttpClient client, string url)
    {
        var body = await client.GetFromJsonAsync<JsonElement>(url);
        return (body.GetProperty("minimumAndroidVersionCode").GetInt32(), body.GetProperty("recommendedAndroidVersionCode").GetInt32());
    }

    private static object Policy(int minimum, int recommended) =>
        new { minimumAndroidVersionCode = minimum, recommendedAndroidVersionCode = recommended };

    [Fact]
    public async Task ItIsOffUntilSet_AndAnyoneCanReadIt() =>
        Assert.Equal((0, 0), await ReadAsync(_factory.CreateClient(), Public));

    [Fact]
    public async Task OnlyASuperadminChangesIt()
    {
        var anonymous = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync(Admin, Policy(1, 1))).StatusCode);
    }

    [Fact]
    public async Task WhatIsSaved_IsWhatTheAppReads_AndItCanBeTurnedOffAgain()
    {
        var admin = await SuperadminClientAsync();

        (await admin.PutAsJsonAsync(Admin, Policy(30100, 30110))).EnsureSuccessStatusCode();
        Assert.Equal((30100, 30110), await ReadAsync(_factory.CreateClient(), Public));
        Assert.Equal((30100, 30110), await ReadAsync(admin, Admin));

        (await admin.PutAsJsonAsync(Admin, Policy(0, 0))).EnsureSuccessStatusCode();
        Assert.Equal((0, 0), await ReadAsync(_factory.CreateClient(), Public));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(30110, 30100)] // inviting to update to a version older than the one required makes no sense
    public async Task ImpossiblePolicies_AreRefused_AndNothingChanges(int minimum, int recommended)
    {
        var admin = await SuperadminClientAsync();

        var answer = await admin.PutAsJsonAsync(Admin, Policy(minimum, recommended));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Equal((0, 0), await ReadAsync(admin, Admin));
    }

    [Fact]
    public async Task AMinimumWithoutARecommendation_IsValid()
    {
        var admin = await SuperadminClientAsync();

        (await admin.PutAsJsonAsync(Admin, Policy(30100, 0))).EnsureSuccessStatusCode();

        Assert.Equal((30100, 0), await ReadAsync(admin, Public));
    }

    [Fact]
    public async Task ARowThatCannotBeRead_IsTreatedAsOff()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            db.AppSettings.Add(new AppSetting { Key = SettingKeys.AndroidMinimumVersionCode, Value = "latest", UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        Assert.Equal((0, 0), await ReadAsync(_factory.CreateClient(), Public));
    }
}
