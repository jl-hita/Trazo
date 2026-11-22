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
using Xunit;

namespace Trazo.UnitTests;

/// <summary>How the backup panel reads the files the server's backup scripts leave behind, and who may see it.</summary>
public sealed class AdminBackupsApiTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly string _status = Path.Combine(Path.GetTempPath(), $"trazo-backups-{Guid.NewGuid():N}");
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly SetupFixture _fixture = new();
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
}
