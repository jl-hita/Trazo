using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Security;
using Trazo.Api.Support;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Push;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>How the backup panel reads the files the server's backup scripts leave behind, and who may see it.</summary>
public sealed class AdminBackupsApiTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string _status = Path.Combine(Path.GetTempPath(), $"trazo-backups-{Guid.NewGuid():N}");
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly SetupFixture _fixture = new();
    private readonly FakePushSender _push = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AdminBackupsApiTests()
    {
        Directory.CreateDirectory(_status);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.UseSetting("Backups:StatusPath", _status);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString));
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(_push);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        foreach (var dir in new[] { _status, _keys })
            try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string Copy(DateTimeOffset at, long size) =>
        $$"""{"Name":"trazo-{{at:yyyyMMdd'T'HHmmss'Z'}}.tar.age","Size":{{size}},"ModTime":"{{at:O}}","IsDir":false}""";

    private void WriteStatus(DateTimeOffset checkedAt, params string[] copies) =>
        File.WriteAllText(Path.Combine(_status, BackupStatusReader.StatusFile),
            $$"""{"checkedAt":"{{checkedAt:O}}","remote":"trazo-drive:trazo-backups","maxAgeHours":36,"copies":[{{string.Join(',', copies)}}]}""");

    private void WriteLastRun(bool ok, DateTimeOffset finished, string? error = null) =>
        File.WriteAllText(Path.Combine(_status, BackupStatusReader.LastRunFile),
            $$"""{"ok":{{(ok ? "true" : "false")}},"startedAt":"{{finished.AddMinutes(-1):O}}","finishedAt":"{{finished:O}}","copy":null,"error":{{(error is null ? "null" : JsonSerializer.Serialize(error))}}}""");

    private BackupStatusView Read() => BackupStatusReader.Read(_status, Now);

    [Fact]
    public void WithoutFiles_ReportsNothingRecordedYet()
    {
        var view = Read();
        Assert.False(view.Configured);
        Assert.Equal("unknown", view.Health);
        Assert.Empty(view.Copies);
    }

    [Fact]
    public void RecentCopy_IsHealthy_AndCopiesComeNewestFirst()
    {
        WriteStatus(Now.AddHours(-3), Copy(Now.AddHours(-30), 4_000_000), Copy(Now.AddHours(-6), 4_700_000), Copy(Now.AddHours(-54), 500_000_000));
        WriteLastRun(true, Now.AddHours(-6));

        var view = Read();

        Assert.Equal("healthy", view.Health);
        Assert.Equal(3, view.Copies.Count);
        Assert.Equal(Now.AddHours(-6), view.Copies[0].CreatedAt);
        Assert.Equal(4_700_000, view.Copies[0].SizeBytes);
        Assert.Equal("trazo-drive:trazo-backups", view.Remote);
    }

    [Fact]
    public void StaleLatestCopy_IsAnError()
    {
        WriteStatus(Now.AddHours(-1), Copy(Now.AddHours(-40), 1000));
        Assert.Equal("error", Read().Health);
    }

    [Fact]
    public void NoCopiesInDrive_IsAnError()
    {
        WriteStatus(Now.AddHours(-1));
        Assert.Equal("error", Read().Health);
    }

    [Fact]
    public void FailedLastRun_AfterTheLatestCopy_IsAnError_WithItsMessage()
    {
        WriteStatus(Now.AddHours(-1), Copy(Now.AddHours(-20), 1000));
        WriteLastRun(false, Now.AddHours(-2), "pg_dump: connection refused");

        var view = Read();

        Assert.Equal("error", view.Health);
        Assert.Equal("pg_dump: connection refused", view.LastRun!.Error);
    }

    [Fact]
    public void FailedLastRun_BeforeALaterGoodCopy_DoesNotKeepTheErrorForever()
    {
        WriteStatus(Now.AddHours(-1), Copy(Now.AddHours(-2), 1000));
        WriteLastRun(false, Now.AddHours(-10), "boom");
        Assert.Equal("healthy", Read().Health);
    }

    [Fact]
    public void CheckThatStoppedRunning_IsAWarning()
    {
        WriteStatus(Now.AddDays(-4), Copy(Now.AddHours(-2), 1000));
        Assert.Equal("warning", Read().Health);
    }

    [Fact]
    public void BrokenFilesAndForeignNames_AreIgnored()
    {
        File.WriteAllText(Path.Combine(_status, BackupStatusReader.StatusFile), "{ not json");
        Assert.False(Read().Configured);

        WriteStatus(Now.AddHours(-1), """{"Name":"notes.txt","Size":1,"ModTime":"2026-11-22T10:00:00Z"}""", Copy(Now.AddHours(-2), 1000));
        Assert.Single(Read().Copies);
    }

    private async Task<HttpClient> ClientForAsync(string? role)
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

    [Fact]
    public async Task OnlySuperadminsCanSeeTheBackups_AndTheAnswerUsesCamelCase()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ClientForAsync(null)).GetAsync("/api/v1/admin/backups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientForAsync("admin")).GetAsync("/api/v1/admin/backups")).StatusCode);

        WriteStatus(DateTimeOffset.UtcNow, Copy(DateTimeOffset.UtcNow.AddHours(-1), 2048));
        var response = await (await ClientForAsync("superadmin")).GetAsync("/api/v1/admin/backups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>())!;
        Assert.True(body.GetProperty("configured").GetBoolean());
        Assert.Equal("healthy", body.GetProperty("health").GetString());
        Assert.Equal(2048, body.GetProperty("copies")[0].GetProperty("sizeBytes").GetInt64());
    }

    // ------------------------------------------------------------------ who is told when the backups go wrong

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Value { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Value;
    }

    private async Task<Guid> AddUserAsync(string role, bool disabled = false, string? name = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var email = $"{role}-{Guid.NewGuid():N}@trazo.test";
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = name ?? role,
            Role = role, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now, DisabledAt = disabled ? now : null
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<BackupAlertOutcome> RunAlerterAsync(FixedTime time)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var alerter = new BackupAlerter(new BackupAlertSettings(db, time), _push, configuration, time, NullLogger<BackupAlerter>.Instance);
        return await alerter.RunOnceAsync(CancellationToken.None);
    }

    private async Task ChooseAsync(Guid? userId)
    {
        using var scope = _factory.Services.CreateScope();
        var settings = new BackupAlertSettings(scope.ServiceProvider.GetRequiredService<TrazoDbContext>(), TimeProvider.System);
        Assert.True(await settings.SetRecipientAsync(userId, CancellationToken.None));
    }

    [Fact]
    public async Task TheRecipientIsChosenFromActiveStaffOnly_AndOnlyBySuperadmins()
    {
        var admin = await AddUserAsync("admin", name: "Ana");
        var superadmin = await AddUserAsync("superadmin", name: "Beto");
        var disabled = await AddUserAsync("admin", disabled: true, name: "Caído");
        var plain = await AddUserAsync("user", name: "Dani");

        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientForAsync("admin")).GetAsync("/api/v1/admin/backups/alerts")).StatusCode);

        var client = await ClientForAsync("superadmin");
        var view = (await (await client.GetAsync("/api/v1/admin/backups/alerts")).Content.ReadFromJsonAsync<JsonElement>())!;
        Assert.Equal(JsonValueKind.Null, view.GetProperty("recipientUserId").ValueKind);
        var ids = view.GetProperty("candidates").EnumerateArray().Select(c => c.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(admin, ids);
        Assert.Contains(superadmin, ids);
        Assert.DoesNotContain(disabled, ids);
        Assert.DoesNotContain(plain, ids);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/admin/backups/alerts", new { userId = plain })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/admin/backups/alerts", new { userId = disabled })).StatusCode);

        var saved = await client.PutAsJsonAsync("/api/v1/admin/backups/alerts", new { userId = admin });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var after = (await saved.Content.ReadFromJsonAsync<JsonElement>())!;
        Assert.Equal(admin, after.GetProperty("recipientUserId").GetGuid());
        Assert.Equal("Ana", after.GetProperty("recipientName").GetString());

        var off = (await (await client.PutAsJsonAsync("/api/v1/admin/backups/alerts", new { userId = (Guid?)null })).Content.ReadFromJsonAsync<JsonElement>())!;
        Assert.Equal(JsonValueKind.Null, off.GetProperty("recipientUserId").ValueKind);
    }

    [Fact]
    public async Task TestNotice_NeedsARecipient_AndGoesToThem()
    {
        var client = await ClientForAsync("superadmin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/admin/backups/alerts/test", null)).StatusCode);

        var admin = await AddUserAsync("admin");
        await ChooseAsync(admin);
        var response = await client.PostAsync("/api/v1/admin/backups/alerts/test", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_push.Sent);
        Assert.Equal([admin], sent.Users);
        Assert.Equal("backup-test", sent.Message.Kind);
    }

    [Fact]
    public async Task NoRecipient_NothingIsSent()
    {
        WriteStatus(Now.AddHours(-1));
        Assert.Equal(BackupAlertOutcome.Nothing, await RunAlerterAsync(new FixedTime(Now)));
        Assert.Empty(_push.Sent);
    }

    [Fact]
    public async Task AFailure_IsAnnouncedOnce_RepeatedAfterADay_AndTheRecoveryIsToldOnce()
    {
        var admin = await AddUserAsync("admin");
        await ChooseAsync(admin);
        var time = new FixedTime(Now);
        WriteStatus(Now.AddHours(-1), Copy(Now.AddHours(-40), 1000));

        Assert.Equal(BackupAlertOutcome.Alerted, await RunAlerterAsync(time));
        var first = Assert.Single(_push.Sent);
        Assert.Equal([admin], first.Users);
        Assert.Equal("backup-alert", first.Message.Kind);
        Assert.Contains("36 horas", first.Message.Body);

        time.Value = Now.AddHours(3);
        Assert.Equal(BackupAlertOutcome.Nothing, await RunAlerterAsync(time));
        Assert.Single(_push.Sent);

        time.Value = Now.AddHours(25);
        Assert.Equal(BackupAlertOutcome.Alerted, await RunAlerterAsync(time));
        Assert.Equal(2, _push.Sent.Count);

        WriteStatus(Now.AddHours(26), Copy(Now.AddHours(25), 5000));
        time.Value = Now.AddHours(26);
        Assert.Equal(BackupAlertOutcome.Recovered, await RunAlerterAsync(time));
        Assert.Equal("backup-ok", _push.Sent[^1].Message.Kind);
        Assert.Equal(BackupAlertOutcome.Nothing, await RunAlerterAsync(time));
        Assert.Equal(3, _push.Sent.Count);
    }

    [Fact]
    public async Task APushThatFails_IsTriedAgainNextRound()
    {
        var admin = await AddUserAsync("admin");
        await ChooseAsync(admin);
        var time = new FixedTime(Now);
        WriteStatus(Now.AddHours(-1));
        _push.Failure = new PushException(PushFailure.NotConfigured, "not configured");

        Assert.Equal(BackupAlertOutcome.PushFailed, await RunAlerterAsync(time));

        _push.Failure = null;
        Assert.Equal(BackupAlertOutcome.Alerted, await RunAlerterAsync(time));
        Assert.Single(_push.Sent);
    }

    [Fact]
    public async Task ADisabledRecipient_StopsReceiving()
    {
        var admin = await AddUserAsync("admin");
        await ChooseAsync(admin);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            (await db.Users.SingleAsync(u => u.Id == admin)).DisabledAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }
        WriteStatus(Now.AddHours(-1));

        Assert.Equal(BackupAlertOutcome.Nothing, await RunAlerterAsync(new FixedTime(Now)));
        Assert.Empty(_push.Sent);
    }
}
