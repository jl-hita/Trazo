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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trazo.Api.Security;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;
using Xunit;

namespace Trazo.UnitTests;

public sealed class OperationalValidatorTests
{
    [Theory]
    [InlineData("https://admin.example.com", "https://admin.example.com")]
    [InlineData("HTTPS://Admin.Example.com/", "https://admin.example.com")]
    [InlineData("https://admin.example.com:8443", "https://admin.example.com:8443")]
    [InlineData("http://localhost:4200", "http://localhost:4200")]
    public void ValidOrigins_AreNormalized(string raw, string expected)
    {
        Assert.True(OperationalValidator.TryNormalizeOrigin(raw, out var origin));
        Assert.Equal(expected, origin);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("admin.example.com")]
    [InlineData("http://admin.example.com")]            // plain http is only for localhost
    [InlineData("https://admin.example.com/path")]
    [InlineData("https://admin.example.com?x=1")]
    [InlineData("https://user:pw@admin.example.com")]
    [InlineData("javascript:alert(1)")]
    public void InvalidOrigins_AreRejected(string raw) => Assert.False(OperationalValidator.TryNormalizeOrigin(raw, out _));

    [Fact]
    public void OriginList_RejectsOneBadEntry_AndTooManyEntries()
    {
        Assert.True(OperationalValidator.TryParseOrigins("https://a.example.com\nhttps://b.example.com, https://a.example.com", out var ok));
        Assert.Equal(2, ok.Count);
        Assert.False(OperationalValidator.TryParseOrigins("https://a.example.com\nnope", out _));
        var many = string.Join('\n', Enumerable.Range(0, OperationalValidator.MaxCorsOrigins + 1).Select(i => $"https://h{i}.example.com"));
        Assert.False(OperationalValidator.TryParseOrigins(many, out _));
    }

    [Theory]
    [InlineData("https://dieto.example.com", true)]
    [InlineData("http://localhost:5000", true)]
    [InlineData("http://dieto.example.com", false)]
    [InlineData("https://dieto.example.com/?k=1", false)]
    [InlineData("https://u:p@dieto.example.com", false)]
    [InlineData("ftp://dieto.example.com", false)]
    [InlineData("not a url", false)]
    public void EndpointValidation(string raw, bool valid) => Assert.Equal(valid, OperationalValidator.TryParseEndpoint(raw, out _));

    [Theory]
    [InlineData("Information", "Information")]
    [InlineData("warning", "Warning")]
    public void LogLevels_AreCaseInsensitive(string raw, string expected)
    {
        Assert.True(OperationalValidator.TryParseLogLevel(raw, out var level));
        Assert.Equal(expected, level);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("Trace")]
    [InlineData("verbose")]
    public void LogLevels_NoneAndUnknownAreRejected(string raw) => Assert.False(OperationalValidator.TryParseLogLevel(raw, out _));

    [Fact]
    public void Validate_EnforcesRanges_AndKeyRules()
    {
        var ok = new OperationsInput(["https://a.example.com"], 30, 5, 30, "Warning", 30, "https://dieto.example.com", "0123456789abcdef");
        Assert.Empty(OperationalValidator.Validate(ok));

        var errors = OperationalValidator.Validate(new OperationsInput(
            ["nope"], 9, 61, 4, "None", 4000, "http://remote.example.com", "short key"));
        foreach (var field in new[] { "additionalCorsOrigins", "setupPerMinute", "setupTestPerMinute", "subscriptionPerMinute",
                     "logLevel", "logRetentionDays", "dietoExpressEndpoint", "dietoExpressApiKey" })
            Assert.Contains(field, errors.Keys);
        Assert.Empty(OperationalValidator.Validate(ok with { DietoExpressApiKey = null, DietoExpressEndpoint = "", LogLevel = "" }));
    }
}

public sealed class OperationalResolutionTests
{
    private static Dictionary<string, string?> None => [];

    [Fact]
    public void Defaults_WhenNothingIsConfigured()
    {
        var config = OperationalResolver.Build(k => None.GetValueOrDefault(k), k => None.GetValueOrDefault(k));
        Assert.Equal(OperationalConfig.Defaults.SetupPerMinute, config.SetupPerMinute);
        Assert.Equal(30, config.LogRetentionDays);
        Assert.Equal("Information", config.LogLevel);
        Assert.Null(config.LogLevelOverride);
        Assert.Empty(config.AdditionalCorsOrigins);
    }

    [Fact]
    public void Priority_DatabaseThenEnvironmentThenDefault()
    {
        var env = new Dictionary<string, string?> { [SettingKeys.RateLimitSetup] = "60", [SettingKeys.RateLimitSubscription] = "90" };
        var db = new Dictionary<string, string?> { [SettingKeys.RateLimitSetup] = "120" };
        var config = OperationalResolver.Build(k => db.GetValueOrDefault(k), k => env.GetValueOrDefault(k));
        Assert.Equal(120, config.SetupPerMinute);        // database wins
        Assert.Equal(90, config.SubscriptionPerMinute);  // environment when the database has nothing
        Assert.Equal(5, config.SetupTestPerMinute);      // default
    }

    [Fact]
    public void InvalidDatabaseValue_FallsBackToEnvironment_AndIsReported_AndNeverThrows()
    {
        var env = new Dictionary<string, string?>
        {
            [SettingKeys.RateLimitSetup] = "60", [SettingKeys.LogRetentionDays] = "7", [SettingKeys.LogLevelDefault] = "Warning"
        };
        var db = new Dictionary<string, string?>
        {
            [SettingKeys.RateLimitSetup] = "999999", [SettingKeys.LogRetentionDays] = "-3", [SettingKeys.LogLevelDefault] = "None",
            [SettingKeys.CorsAdditionalOrigins] = "https://ok.example.com\n*", [SettingKeys.RateLimitSetupTest] = "abc"
        };
        var reported = new List<string>();
        var config = OperationalResolver.Build(k => db.GetValueOrDefault(k), k => env.GetValueOrDefault(k), (k, s) => reported.Add($"{s}:{k}"));

        Assert.Equal(60, config.SetupPerMinute);
        Assert.Equal(7, config.LogRetentionDays);
        Assert.Equal("Warning", config.LogLevel);
        Assert.Null(config.LogLevelOverride);              // an invalid level is never layered over the environment
        Assert.Empty(config.AdditionalCorsOrigins);        // one bad entry invalidates the whole list
        Assert.Equal(5, config.SetupTestPerMinute);        // invalid and no environment: default
        Assert.Contains($"database:{SettingKeys.RateLimitSetup}", reported);
        Assert.Contains($"database:{SettingKeys.CorsAdditionalOrigins}", reported);
    }

    [Fact]
    public void InvalidEnvironmentValue_FallsBackToDefault()
    {
        var env = new Dictionary<string, string?> { [SettingKeys.RateLimitSetup] = "-1", [SettingKeys.LogRetentionDays] = "many" };
        var config = OperationalResolver.Build(_ => null, k => env.GetValueOrDefault(k));
        Assert.Equal(30, config.SetupPerMinute);
        Assert.Equal(30, config.LogRetentionDays);
    }

    [Fact]
    public async Task RuntimeSettings_ReadsStoredRows_AndSurvivesGarbageRows()
    {
        using var fx = new SetupFixture();
        var (db, _, runtime, _) = fx.NewServices(new Dictionary<string, string?> { [SettingKeys.RateLimitSetupTest] = "8" });
        using var _ = db;
        db.AppSettings.AddRange(
            new AppSetting { Key = SettingKeys.RateLimitSetup, Value = "garbage", IsSecret = false, UpdatedAt = DateTimeOffset.UtcNow },
            new AppSetting { Key = SettingKeys.RateLimitSetupTest, Value = "oops", IsSecret = false, UpdatedAt = DateTimeOffset.UtcNow },
            // A secret that cannot be decrypted (the key ring was replaced) must not break anything either.
            new AppSetting { Key = SettingKeys.DietoExpressApiKey, Value = "not-a-valid-ciphertext", IsSecret = true, UpdatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var config = await runtime.GetOperationalAsync();
        Assert.Equal(30, config.SetupPerMinute);
        Assert.Equal(8, config.SetupTestPerMinute);
        Assert.Null((await runtime.GetDietoExpressAsync()).ApiKey);
    }
}

public sealed class OperationsSettingsWriterTests
{
    private const string Key = "super-secret-dietoexpress-key-1234";

    private static OperationsInput Input(string? apiKey = Key) => new(
        ["https://admin.example.com"], 45, 6, 40, "Warning", 14, "https://dieto.example.com/", apiKey);

    [Fact]
    public async Task Save_PersistsValues_EncryptsTheKey_AndNeverExposesIt()
    {
        using var fx = new SetupFixture();
        var (db, _, runtime, writer) = fx.NewServices();
        using var _ = db;

        var result = await writer.UpdateOperationsAsync(Input(), default);
        Assert.True(result.Succeeded);

        var row = await db.AppSettings.AsNoTracking().SingleAsync(x => x.Key == SettingKeys.DietoExpressApiKey);
        Assert.True(row.IsSecret);
        Assert.DoesNotContain(Key, row.Value);
        Assert.Equal(Key, (await runtime.GetDietoExpressAsync()).ApiKey);

        var view = await runtime.GetOperationsViewAsync();
        Assert.True(view.DietoExpressApiKeySet);
        Assert.Equal("database", view.DietoExpressApiKeySource);
        Assert.Equal("https://dieto.example.com", view.DietoExpressEndpoint);
        Assert.DoesNotContain(Key, System.Text.Json.JsonSerializer.Serialize(view));
        // The audit entries name the key but never contain its value.
        Assert.DoesNotContain(result.Changes, c => c.Contains(Key));
        Assert.Contains(result.Changes, c => c.StartsWith(SettingKeys.DietoExpressApiKey) && c.Contains("replaced"));

        var config = await runtime.GetOperationalAsync();
        Assert.Equal(45, config.SetupPerMinute);
        Assert.Equal("Warning", config.LogLevelOverride);
        Assert.Equal(14, config.LogRetentionDays);
        Assert.Equal(["https://admin.example.com"], config.AdditionalCorsOrigins);
    }

    [Fact]
    public async Task NullKeyKeepsTheStoredOne_EmptyClearsIt()
    {
        using var fx = new SetupFixture();
        var (db, _, runtime, writer) = fx.NewServices();
        using var _ = db;
        await writer.UpdateOperationsAsync(Input(), default);

        await writer.UpdateOperationsAsync(Input(apiKey: null) with { SetupPerMinute = 50 }, default);
        Assert.Equal(Key, (await runtime.GetDietoExpressAsync()).ApiKey);
        Assert.Equal(50, (await runtime.GetOperationalAsync()).SetupPerMinute);

        await writer.UpdateOperationsAsync(Input(apiKey: ""), default);
        Assert.Null((await runtime.GetDietoExpressAsync()).ApiKey);
        Assert.Equal("none", (await runtime.GetOperationsViewAsync()).DietoExpressApiKeySource);
    }

    [Fact]
    public async Task InvalidInput_WritesNothing()
    {
        using var fx = new SetupFixture();
        var (db, _, _, writer) = fx.NewServices();
        using var _ = db;

        var result = await writer.UpdateOperationsAsync(Input() with { SetupPerMinute = 1, LogLevel = "None" }, default);

        Assert.False(result.Succeeded);
        Assert.Contains("setupPerMinute", result.Errors.Keys);
        Assert.Contains("logLevel", result.Errors.Keys);
        Assert.Equal(0, await db.AppSettings.CountAsync());
    }

    [Fact]
    public async Task EnvironmentVariables_StillWork_WhenTheDatabaseHasNoValue_AndDatabaseWinsOtherwise()
    {
        using var fx = new SetupFixture();
        var env = new Dictionary<string, string?>
        {
            [SettingKeys.DietoExpressEndpoint] = "https://env-dieto.example.com",
            [SettingKeys.DietoExpressApiKey] = "environment-key-0123456789",
            [SettingKeys.LogRetentionDays] = "3"
        };
        var (db, _, runtime, writer) = fx.NewServices(env);
        using var _ = db;

        var before = await runtime.GetDietoExpressAsync();
        Assert.Equal("https://env-dieto.example.com", before.Endpoint);
        Assert.Equal("environment-key-0123456789", before.ApiKey);
        Assert.Equal("environment", (await runtime.GetOperationsViewAsync()).DietoExpressApiKeySource);
        Assert.Equal(3, (await runtime.GetOperationalAsync()).LogRetentionDays);

        await writer.UpdateOperationsAsync(Input(), default);
        var after = await runtime.GetDietoExpressAsync();
        Assert.Equal("https://dieto.example.com", after.Endpoint);
        Assert.Equal(Key, after.ApiKey);
        Assert.Equal(14, (await runtime.GetOperationalAsync()).LogRetentionDays);

        // Clearing the database values hands control back to the environment.
        await writer.UpdateOperationsAsync(Input(apiKey: "") with { DietoExpressEndpoint = "", LogRetentionDays = 30 }, default);
        Assert.Equal("https://env-dieto.example.com", (await runtime.GetDietoExpressAsync()).Endpoint);
        Assert.Equal("environment-key-0123456789", (await runtime.GetDietoExpressAsync()).ApiKey);
    }
}

public sealed class RuntimeOptionsTests
{
    [Fact]
    public async Task Refresh_AppliesDatabaseValues_AndEnvironmentOriginsAreAlwaysAllowed()
    {
        using var fx = new SetupFixture();
        var env = new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "https://env.example.com" };
        var (db, _, _, writer) = fx.NewServices(env);
        using var _ = db;
        var config = new ConfigurationBuilder().AddInMemoryCollection(env).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ISecretProtector>(new SecretProtector(fx.Protection));
        services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(fx.ConnectionString));
        services.AddScoped<IRuntimeSettings, RuntimeSettings>();
        services.AddSingleton<IRuntimeOptions, RuntimeOptions>();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IRuntimeOptions>();

        Assert.True(options.IsCorsOriginAllowed("https://env.example.com"));
        Assert.False(options.IsCorsOriginAllowed("https://extra.example.com"));

        await writer.UpdateOperationsAsync(new OperationsInput(["https://extra.example.com"], 70, 7, 70, "", 30, "", null), default);
        await options.RefreshAsync();

        Assert.True(options.IsCorsOriginAllowed("https://extra.example.com"));
        Assert.True(options.IsCorsOriginAllowed("https://extra.example.com/"));
        Assert.True(options.IsCorsOriginAllowed("https://env.example.com"));
        Assert.False(options.IsCorsOriginAllowed("https://evil.example.com"));
        Assert.Equal(70, options.Current.SetupPerMinute);

        // Removing the extra origins never touches the environment ones.
        await writer.UpdateOperationsAsync(new OperationsInput([], 70, 7, 70, "", 30, "", null), default);
        await options.RefreshAsync();
        Assert.False(options.IsCorsOriginAllowed("https://extra.example.com"));
        Assert.True(options.IsCorsOriginAllowed("https://env.example.com"));
    }

    [Fact]
    public async Task Refresh_KeepsPreviousValues_WhenTheDatabaseIsUnavailable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddScoped<IRuntimeSettings>(_ => throw new InvalidOperationException("database down"));
        services.AddSingleton<IRuntimeOptions, RuntimeOptions>();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IRuntimeOptions>();

        await options.RefreshAsync(); // must not throw: settings are never needed to run

        Assert.Equal(30, options.Current.SetupPerMinute);
        Assert.True(options.IsCorsOriginAllowed("http://localhost:4200")); // the historical default origin
    }

    [Fact]
    public void LogLevelOverride_ChangesTheLoggingFilterWithoutRestart_AndRevertsToTheEnvironment()
    {
        var overrides = new RuntimeOverridesConfigurationSource();
        var manager = new ConfigurationManager();
        manager.AddInMemoryCollection(new Dictionary<string, string?> { ["Logging:LogLevel:Default"] = "Information" });
        ((IConfigurationBuilder)manager).Add(overrides);
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConfiguration(manager.GetSection("Logging")));
        using var provider = services.BuildServiceProvider();
        var monitor = provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>();

        Assert.Equal(LogLevel.Information, monitor.CurrentValue.MinLevel == LogLevel.None ? LogLevel.Information : monitor.CurrentValue.Rules.First(r => r.CategoryName is null).LogLevel!.Value);

        overrides.Provider.SetLogLevel("Warning");
        Assert.Equal("Information", ConfigurationBaseline.Get(manager, "Logging:LogLevel:Default")); // the environment value is still visible
        Assert.Equal("Warning", manager["Logging:LogLevel:Default"]);
        Assert.Equal(LogLevel.Warning, monitor.CurrentValue.Rules.Last(r => r.CategoryName is null && r.ProviderName is null).LogLevel);

        overrides.Provider.SetLogLevel(null);
        Assert.Equal("Information", manager["Logging:LogLevel:Default"]);
        Assert.Equal(LogLevel.Information, monitor.CurrentValue.Rules.Last(r => r.CategoryName is null && r.ProviderName is null).LogLevel);
    }
}

/// <summary>The operational settings endpoints on a real in-process host: authorization, audit log and hot CORS.</summary>
public sealed class OperationsSettingsApiTests : IDisposable
{
    private const string ApiKey = "api-key-never-in-responses-9876";
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly CapturingLoggerProvider _logs = new();
    private readonly WebApplicationFactory<Program> _factory;

    public OperationsSettingsApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.UseSetting("Cors:AllowedOrigins:0", "https://env.example.com");
            builder.ConfigureLogging(l => l.AddProvider(_logs));
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
            email = new { host = "smtp.example.com", port = 587, enableSsl = true, from = "no-reply@example.com", actionBaseUrl = "trazo://account" },
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

    private static object Payload(string[]? origins = null, int setup = 40, string? apiKey = ApiKey) => new
    {
        additionalCorsOrigins = origins ?? ["https://admin.example.com"],
        setupPerMinute = setup, setupTestPerMinute = 6, subscriptionPerMinute = 35,
        logLevel = "Warning", logRetentionDays = 10,
        dietoExpressEndpoint = "https://dieto.example.com", dietoExpressApiKey = apiKey
    };

    private static async Task<string?> AllowedOriginAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);
        return response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values) ? values.Single() : null;
    }

    [Fact]
    public async Task Endpoints_RequireSuperadmin()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/admin/settings/operations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync("/api/v1/admin/settings/operations", Payload())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/admin/settings/operations/test/dietoexpress", new { })).StatusCode);
    }

    [Fact]
    public async Task Save_AppliesCorsWithoutRestart_NeverReturnsTheKey_AndWritesAnAuditEntryWithoutSecrets()
    {
        var client = await SuperadminClientAsync();
        Assert.Null(await AllowedOriginAsync(client, "https://admin.example.com"));
        Assert.Equal("https://env.example.com", await AllowedOriginAsync(client, "https://env.example.com"));

        var put = await client.PutAsJsonAsync("/api/v1/admin/settings/operations", Payload());
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var body = await put.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiKey, body);
        Assert.Contains("\"dietoExpressApiKeySet\":true", body);

        var get = await (await client.GetAsync("/api/v1/admin/settings/operations")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiKey, get);
        Assert.Contains("https://env.example.com", get);   // read-only environment origins are listed

        Assert.Equal("https://admin.example.com", await AllowedOriginAsync(client, "https://admin.example.com"));
        Assert.Equal("https://env.example.com", await AllowedOriginAsync(client, "https://env.example.com"));
        Assert.Null(await AllowedOriginAsync(client, "https://evil.example.com"));

        var audit = Assert.Single(_logs.Messages, m => m.Contains("updated the operational settings"));
        Assert.Contains("Superadmin", audit);
        Assert.Contains(SettingKeys.CorsAdditionalOrigins, audit);
        Assert.DoesNotContain(ApiKey, audit);
        Assert.DoesNotContain(ApiKey, string.Join('\n', _logs.Messages));
    }

    [Fact]
    public async Task InvalidSave_Returns400_AndChangesNothing()
    {
        var client = await SuperadminClientAsync();
        var put = await client.PutAsJsonAsync("/api/v1/admin/settings/operations", Payload(origins: ["*"], setup: 1));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
        var body = await put.Content.ReadAsStringAsync();
        Assert.Contains("additionalCorsOrigins", body);
        Assert.Contains("setupPerMinute", body);
        using var check = _fixture.NewContext();
        Assert.False(await check.AppSettings.AnyAsync(x => x.Key == SettingKeys.RateLimitSetup));
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly List<string> _messages = [];
    public IReadOnlyList<string> Messages { get { lock (_messages) return _messages.ToArray(); } }
    public ILogger CreateLogger(string categoryName) => new Capture(this);
    public void Dispose() { }

    private sealed class Capture(CapturingLoggerProvider owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (owner._messages) owner._messages.Add(formatter(state, exception));
        }
    }
}
