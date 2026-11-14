using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>Real transactions/constraints via a file-backed SQLite database (the PostgreSQL migrations are raw SQL).</summary>
public sealed class SetupFixture : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"trazo-test-{Guid.NewGuid():N}.db");
    public IDataProtectionProvider Protection { get; } = new EphemeralDataProtectionProvider();
    public string ConnectionString => $"Data Source={_path};Pooling=False";

    public SetupFixture()
    {
        using var db = NewContext();
        db.Database.ExecuteSqlRaw("""
            CREATE TABLE app_settings ("Key" TEXT PRIMARY KEY, "Value" TEXT NOT NULL, "IsSecret" INTEGER NOT NULL, "UpdatedAt" TEXT NOT NULL);
            CREATE TABLE users (
              "Id" TEXT PRIMARY KEY, "Email" TEXT NOT NULL, "NormalizedEmail" TEXT NOT NULL UNIQUE, "DisplayName" TEXT NOT NULL,
              "EmailVerifiedAt" TEXT NULL, "PasswordHash" TEXT NULL, "GoogleSubject" TEXT NULL, "Role" TEXT NOT NULL,
              "TwoFactorEnabled" INTEGER NOT NULL, "ProtectedAuthenticatorKey" TEXT NULL, "LastTotpStep" INTEGER NULL,
              "FailedLoginAttempts" INTEGER NOT NULL, "LockoutEnd" TEXT NULL, "LastLoginAt" TEXT NULL, "DisabledAt" TEXT NULL,
              "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
            CREATE TABLE refresh_tokens (
              "Id" TEXT PRIMARY KEY, "UserId" TEXT NOT NULL, "TokenHash" TEXT NOT NULL, "CreatedAt" TEXT NOT NULL,
              "ExpiresAt" TEXT NOT NULL, "RevokedAt" TEXT NULL, "ReplacedByTokenHash" TEXT NULL, "DeviceName" TEXT NULL);
            CREATE TABLE subscriptions (
              "Id" TEXT PRIMARY KEY, "UserId" TEXT NOT NULL, "Source" TEXT NOT NULL, "ProductId" TEXT NOT NULL,
              "PurchaseTokenHash" TEXT NOT NULL UNIQUE, "ProtectedPurchaseToken" TEXT NOT NULL, "Status" TEXT NOT NULL,
              "StoreState" TEXT NULL, "LatestOrderId" TEXT NULL, "StartedAt" TEXT NOT NULL, "ExpiresAt" TEXT NOT NULL,
              "AutoRenewing" INTEGER NOT NULL, "AcknowledgedAt" TEXT NULL, "IsTestPurchase" INTEGER NOT NULL,
              "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
            """);
    }

    public TrazoDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(ConnectionString).Options);

    public (TrazoDbContext Db, SetupService Setup, RuntimeSettings Runtime, SettingsWriter Writer) NewServices(
        IDictionary<string, string?>? env = null)
    {
        var db = NewContext();
        var config = new ConfigurationBuilder().AddInMemoryCollection(env ?? new Dictionary<string, string?>()).Build();
        var protector = new SecretProtector(Protection);
        var runtime = new RuntimeSettings(db, config, protector);
        var writer = new SettingsWriter(db, protector);
        return (db, new SetupService(db, runtime, writer, new PasswordHasher<TrazoUser>()), runtime, writer);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch (IOException) { }
    }

    public static SetupInput ValidInput(string email = "owner@example.com") => new(
        new GoogleConfig("123-abc.apps.googleusercontent.com"),
        new EmailConfig("smtp.example.com", 587, true, "mailer", "S3cret-smtp-pass", "TRAZO <no-reply@example.com>", "https://trazostrength.duckdns.org/"),
        new AdminAccountInput(email, "Owner", "CorrectHorse1Battery"));
}

public sealed class SetupStatusTests
{
    [Fact]
    public async Task FreshInstall_IsOpen_AndClosesAfterCompletion()
    {
        using var fx = new SetupFixture();
        var (db, setup, _, _) = fx.NewServices();
        using var _ = db;

        var before = await setup.GetStatusAsync();
        Assert.True(before.SetupRequired);
        Assert.False(before.ConfigurationComplete);

        var result = await setup.CompleteAsync(SetupFixture.ValidInput());
        Assert.Equal(SetupOutcome.Completed, result.Outcome);

        var after = await setup.GetStatusAsync();
        Assert.False(after.SetupRequired);
        Assert.True(after.ConfigurationComplete);
        var user = await db.Users.SingleAsync();
        Assert.Equal("superadmin", user.Role);
        Assert.NotNull(user.EmailVerifiedAt);
        Assert.NotEqual("CorrectHorse1Battery", user.PasswordHash);
    }

    [Fact]
    public async Task SecondSetupAttempt_IsRejected_AndDoesNotCreateAnotherUser()
    {
        using var fx = new SetupFixture();
        var (db, setup, _, _) = fx.NewServices();
        using var _ = db;
        await setup.CompleteAsync(SetupFixture.ValidInput());

        var second = await setup.CompleteAsync(SetupFixture.ValidInput("intruder@example.com"));

        Assert.Equal(SetupOutcome.AlreadyCompleted, second.Outcome);
        Assert.Equal(1, await db.Users.CountAsync());
    }

    [Fact]
    public async Task ExistingUsers_ClosePublicSetup_EvenWithoutMarker()
    {
        // Installations created before the wizard (e.g. via BootstrapAdmin) must not be claimable.
        using var fx = new SetupFixture();
        var (db, setup, _, _) = fx.NewServices();
        using var _ = db;
        db.Users.Add(new TrazoUser
        {
            Id = Guid.NewGuid(), Email = "a@b.c", NormalizedEmail = "A@B.C", DisplayName = "Existing", Role = "superadmin",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.False(await setup.IsOpenAsync());
        Assert.Equal(SetupOutcome.AlreadyCompleted, (await setup.CompleteAsync(SetupFixture.ValidInput())).Outcome);
    }

    [Fact]
    public async Task InvalidInput_IsRejected_AndNothingIsPersisted()
    {
        using var fx = new SetupFixture();
        var (db, setup, _, _) = fx.NewServices();
        using var _ = db;
        var bad = SetupFixture.ValidInput() with
        {
            Google = new GoogleConfig("not-a-client-id"),
            Admin = new AdminAccountInput("nope", "X", "short")
        };

        var result = await setup.CompleteAsync(bad);

        Assert.Equal(SetupOutcome.Invalid, result.Outcome);
        Assert.Contains("google.clientId", result.Errors!.Keys);
        Assert.Contains("admin.password", result.Errors.Keys);
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.AppSettings.CountAsync());
        Assert.True(await setup.IsOpenAsync());
    }

    [Fact]
    public async Task ConcurrentSetupRequests_CreateExactlyOneSuperadmin()
    {
        using var fx = new SetupFixture();
        const int attackers = 8;
        using var gate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, attackers).Select(i => Task.Run(async () =>
        {
            var (db, setup, _, _) = fx.NewServices();
            using var _ = db;
            gate.Wait();
            return (await setup.CompleteAsync(SetupFixture.ValidInput($"admin{i}@example.com"))).Outcome;
        })).ToArray();
        await Task.Delay(100);
        gate.Set();
        var outcomes = await Task.WhenAll(tasks);

        Assert.Equal(1, outcomes.Count(o => o == SetupOutcome.Completed));
        Assert.Equal(attackers - 1, outcomes.Count(o => o == SetupOutcome.AlreadyCompleted));
        using var check = fx.NewContext();
        Assert.Equal(1, await check.Users.CountAsync());
        Assert.Equal(1, await check.Users.CountAsync(u => u.Role == "superadmin"));
        Assert.Equal(1, await check.AppSettings.CountAsync(s => s.Key == SettingKeys.SetupCompletedAt));
    }
}

public sealed class SettingsEncryptionAndPrecedenceTests
{
    [Fact]
    public async Task SecretsAreEncryptedAtRest_AndRoundTrip_AndNeverInView()
    {
        using var fx = new SetupFixture();
        var (db, setup, runtime, _) = fx.NewServices();
        using var _ = db;
        await setup.CompleteAsync(SetupFixture.ValidInput());

        var row = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == SettingKeys.SmtpPassword);
        Assert.True(row.IsSecret);
        Assert.DoesNotContain("S3cret-smtp-pass", row.Value);

        Assert.Equal("S3cret-smtp-pass", (await runtime.GetEmailAsync()).Password);
        var view = await runtime.GetViewAsync();
        Assert.True(view.SmtpPasswordSet);
        Assert.DoesNotContain("S3cret", System.Text.Json.JsonSerializer.Serialize(view));
    }

    [Fact]
    public void Protector_RejectsTamperedAndForeignCiphertext()
    {
        var a = new SecretProtector(new EphemeralDataProtectionProvider());
        var b = new SecretProtector(new EphemeralDataProtectionProvider());
        var cipher = a.Protect("value");

        Assert.Equal("value", a.Unprotect(cipher));
        Assert.Null(b.Unprotect(cipher));
        Assert.Null(a.Unprotect(cipher[..^3] + "AAA"));
        Assert.Null(a.Unprotect("plaintext"));
    }

    [Fact]
    public async Task EnvironmentIsFallback_DatabaseTakesPriority()
    {
        using var fx = new SetupFixture();
        var env = new Dictionary<string, string?>
        {
            [SettingKeys.GoogleClientId] = "env-id.apps.googleusercontent.com",
            [SettingKeys.SmtpHost] = "env-smtp.example.com",
            [SettingKeys.EmailFrom] = "env@example.com",
            [SettingKeys.EmailActionBaseUrl] = "trazo://account"
        };
        var (db, _, runtime, writer) = fx.NewServices(env);
        using var _ = db;

        Assert.Equal("env-id.apps.googleusercontent.com", (await runtime.GetGoogleAsync()).ClientId);
        Assert.True((await runtime.GetEmailAsync()).IsComplete);

        var input = SetupFixture.ValidInput();
        Assert.Empty(await writer.UpdateAsync(input.Google, input.Email, default));

        Assert.Equal("123-abc.apps.googleusercontent.com", (await runtime.GetGoogleAsync()).ClientId);
        Assert.Equal("smtp.example.com", (await runtime.GetEmailAsync()).Host);
    }

    [Fact]
    public async Task UpdateKeepsPasswordWhenOmitted_AndClearsWhenEmpty()
    {
        using var fx = new SetupFixture();
        var (db, setup, runtime, writer) = fx.NewServices();
        using var _ = db;
        var input = SetupFixture.ValidInput();
        await setup.CompleteAsync(input);

        await writer.UpdateAsync(input.Google, input.Email with { Password = null, Host = "other.example.com" }, default);
        var kept = await runtime.GetEmailAsync();
        Assert.Equal("other.example.com", kept.Host);
        Assert.Equal("S3cret-smtp-pass", kept.Password);

        await writer.UpdateAsync(input.Google, input.Email with { Password = "" }, default);
        Assert.True(string.IsNullOrEmpty((await runtime.GetEmailAsync()).Password));
    }

    [Fact]
    public async Task SmtpEmailSender_ReadsSettingsHot_WithoutRestart()
    {
        using var fx = new SetupFixture();
        var (db, setup, runtime, writer) = fx.NewServices();
        using var _ = db;
        var input = SetupFixture.ValidInput();
        await setup.CompleteAsync(input);
        var transport = new RecordingTransport();
        var sender = new SmtpEmailSender(runtime, transport);

        await sender.SendAsync("u@example.com", "s", "b", default);
        Assert.Equal("smtp.example.com", transport.Hosts.Single());

        await writer.UpdateAsync(input.Google, input.Email with { Host = "new-smtp.example.com", Port = 2525 }, default);
        await sender.SendAsync("u@example.com", "s", "b", default);
        Assert.Equal(["smtp.example.com", "new-smtp.example.com"], transport.Hosts);
        Assert.Equal(2525, transport.LastPort);
    }

    [Fact]
    public async Task SmtpEmailSender_Throws_WhenNotConfigured()
    {
        using var fx = new SetupFixture();
        var (db, _, runtime, _) = fx.NewServices();
        using var _ = db;
        var sender = new SmtpEmailSender(runtime, new RecordingTransport());
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync("u@example.com", "s", "b", default));
    }

    [Theory]
    [InlineData("abc.apps.googleusercontent.com", true)]
    [InlineData(".apps.googleusercontent.com", false)]
    [InlineData("abc.example.com", false)]
    [InlineData("a b.apps.googleusercontent.com", false)]
    public void GoogleClientIdValidation(string id, bool valid) =>
        Assert.Equal(valid, SettingsValidator.ValidateGoogle(new GoogleConfig(id)).Count == 0);

    private sealed class RecordingTransport : ISmtpTransport
    {
        public List<string> Hosts { get; } = [];
        public int LastPort { get; private set; }
        public Task SendAsync(EmailConfig config, string recipient, string subject, string body, CancellationToken ct)
        {
            Hosts.Add(config.Host); LastPort = config.Port;
            return Task.CompletedTask;
        }
    }
}
