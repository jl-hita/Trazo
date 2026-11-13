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
using Microsoft.Extensions.Logging;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Logging;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>HTTP-level behaviour of the log viewer endpoint (level and date filters, text search, paging).</summary>
public sealed class AdminLogsApiTests : IDisposable
{
    private static readonly DateTimeOffset Day = new(2026, 11, 13, 0, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public AdminLogsApiTests()
    {
        using (var db = _fixture.NewContext())
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE app_logs (
                  id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp_utc TEXT NOT NULL, level INTEGER NOT NULL,
                  category TEXT NOT NULL, message TEXT NOT NULL, exception TEXT NULL, trace_id TEXT NULL);
                """);
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

    private async Task<TrazoUser> AddUserAsync(string email, string role)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email.Split('@')[0],
            Role = role, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now
        };
        user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, "CorrectHorse1Battery");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private HttpClient ClientFor(TrazoUser? user)
    {
        var client = _factory.CreateClient();
        if (user is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private async Task<HttpClient> SuperadminAsync() => ClientFor(await AddUserAsync($"root-{Guid.NewGuid():N}@trazo.test", "superadmin"));

    private async Task Log(DateTimeOffset at, LogLevel level, string message, string category = "Trazo.Test", string? exception = null)
    {
        await using var db = _fixture.NewContext();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO app_logs (timestamp_utc, level, category, message, exception) VALUES ({0}, {1}, {2}, {3}, {4})",
            at, (int)level, category, message, exception!);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    private static List<string> Messages(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("message").GetString()!).ToList();

    [Fact]
    public async Task OnlySuperadminsCanReadTheLogs()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/admin/logs")).StatusCode);
        var admin = ClientFor(await AddUserAsync("admin@trazo.test", "admin"));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/admin/logs")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await SuperadminAsync()).GetAsync("/api/v1/admin/logs")).StatusCode);
    }

    [Fact]
    public async Task FiltersByLevelGroup_AndCountsDescribeThePeriodNotTheLevelSelection()
    {
        await Log(Day.AddHours(1), LogLevel.Information, "started");
        await Log(Day.AddHours(2), LogLevel.Warning, "slow");
        await Log(Day.AddHours(3), LogLevel.Error, "failed");
        await Log(Day.AddHours(4), LogLevel.Critical, "crashed");
        var client = await SuperadminAsync();

        Assert.Equal(["crashed", "failed", "slow", "started"], Messages(await ReadAsync(await client.GetAsync("/api/v1/admin/logs"))));
        Assert.Equal(["slow"], Messages(await ReadAsync(await client.GetAsync("/api/v1/admin/logs?levels=warning"))));
        var errors = await ReadAsync(await client.GetAsync("/api/v1/admin/logs?levels=error"));
        Assert.Equal(["crashed", "failed"], Messages(errors)); // Critical belongs to the error group
        Assert.All(errors.GetProperty("items").EnumerateArray(), x => Assert.Equal("error", x.GetProperty("level").GetString()));
        var counts = errors.GetProperty("counts");
        Assert.Equal((1, 1, 2), (counts.GetProperty("info").GetInt32(), counts.GetProperty("warning").GetInt32(), counts.GetProperty("error").GetInt32()));
        Assert.Equal(["slow", "started"], Messages(await ReadAsync(await client.GetAsync("/api/v1/admin/logs?levels=info,warning"))));
    }

    [Fact]
    public async Task FiltersByDatePeriod_WithAnExclusiveEnd()
    {
        await Log(Day.AddDays(-1).AddHours(23), LogLevel.Information, "yesterday");
        await Log(Day.AddHours(10), LogLevel.Information, "today");
        await Log(Day.AddDays(1), LogLevel.Information, "tomorrow");
        var client = await SuperadminAsync();
        var from = Uri.EscapeDataString(Day.ToString("O"));
        var to = Uri.EscapeDataString(Day.AddDays(1).ToString("O"));

        Assert.Equal(["today"], Messages(await ReadAsync(await client.GetAsync($"/api/v1/admin/logs?from={from}&to={to}"))));
        Assert.Equal(["tomorrow", "today"], Messages(await ReadAsync(await client.GetAsync($"/api/v1/admin/logs?from={from}"))));
        Assert.Equal(["yesterday"], Messages(await ReadAsync(await client.GetAsync($"/api/v1/admin/logs?to={from}"))));
    }

    [Fact]
    public async Task SearchesMessageCategoryAndException_TreatingWildcardsAsPlainText()
    {
        await Log(Day.AddHours(1), LogLevel.Error, "Upload finished", "Trazo.Images");
        await Log(Day.AddHours(2), LogLevel.Error, "Disk is at 100% usage");
        await Log(Day.AddHours(3), LogLevel.Error, "Something broke", exception: "System.IO.IOException: disk full");
        await Log(Day.AddHours(4), LogLevel.Error, "Looks like 100 percent");
        var client = await SuperadminAsync();

        Assert.Equal(["Upload finished"], Messages(await ReadAsync(await client.GetAsync("/api/v1/admin/logs?q=images"))));
        Assert.Equal(["Disk is at 100% usage"], Messages(await ReadAsync(await client.GetAsync("/api/v1/admin/logs?q=" + Uri.EscapeDataString("100%")))));
        Assert.Equal(["Something broke"], Messages(await ReadAsync(await client.GetAsync("/api/v1/admin/logs?q=ioexception"))));
        Assert.Empty(Messages(await ReadAsync(await client.GetAsync("/api/v1/admin/logs?q=" + Uri.EscapeDataString("%")))).Where(m => !m.Contains('%')));
    }

    [Fact]
    public async Task PagesNewestFirstWithoutRepeatingOrSkipping_EvenWhenTimestampsAreEqual()
    {
        for (var i = 0; i < 7; i++)
            await Log(Day.AddHours(i / 2), LogLevel.Information, $"entry {i}"); // pairs share a timestamp
        var client = await SuperadminAsync();

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = "/api/v1/admin/logs?pageSize=3" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
            var body = await ReadAsync(await client.GetAsync(url));
            seen.AddRange(Messages(body));
            cursor = body.GetProperty("nextCursor").GetString();
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(Enumerable.Range(0, 7).Reverse().Select(i => $"entry {i}"), seen);
    }

    [Fact]
    public async Task NoNextCursorWhenEverythingFitsInOnePage_AndNothingSelectedReturnsNothing()
    {
        await Log(Day, LogLevel.Information, "only");
        var client = await SuperadminAsync();

        var all = await ReadAsync(await client.GetAsync("/api/v1/admin/logs?pageSize=1"));
        Assert.Equal(JsonValueKind.Null, all.GetProperty("nextCursor").ValueKind);
        Assert.Equal(["only"], Messages(all));
    }

    [Theory]
    [InlineData("levels=debug")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=201")]
    [InlineData("cursor=not-a-cursor")]
    [InlineData("from=2026-11-14T00:00:00Z&to=2026-11-13T00:00:00Z")]
    public async Task RejectsInvalidFilters(string query)
    {
        var client = await SuperadminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/admin/logs?" + query)).StatusCode);
    }
}

public sealed class DatabaseLoggerTests
{
    [Fact]
    public void KeepsInformationAndAbove_WithTheFormattedMessageAndTheException()
    {
        var queue = new AppLogQueue();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new DatabaseLoggerProvider(queue)));
        var logger = factory.CreateLogger("Trazo.Tests");

        logger.LogDebug("hidden");
        logger.LogInformation("Imported {Count} exercises", 3);
        logger.LogError(new InvalidOperationException("boom"), "It failed");

        Assert.True(queue.Reader.TryRead(out var info));
        Assert.Equal((2, "Trazo.Tests", "Imported 3 exercises", null), (info.Level, info.Category, info.Message, info.Exception));
        Assert.True(queue.Reader.TryRead(out var error));
        Assert.Equal(4, error.Level);
        Assert.Contains("InvalidOperationException: boom", error.Exception);
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public void LongMessagesAreTrimmed_AndAFullQueueDropsTheOldestEntries()
    {
        var queue = new AppLogQueue();
        using var factory = LoggerFactory.Create(b => b.AddProvider(new DatabaseLoggerProvider(queue)));
        var logger = factory.CreateLogger("Trazo.Tests");

        logger.LogInformation(new string('x', 20_000));
        Assert.True(queue.Reader.TryRead(out var big));
        Assert.Equal(8_000, big.Message.Length);

        for (var i = 0; i < AppLogQueue.Capacity + 5; i++) logger.LogInformation("n{I}", i);
        var count = 0;
        string? first = null;
        while (queue.Reader.TryRead(out var entry)) { first ??= entry.Message; count++; }
        Assert.Equal(AppLogQueue.Capacity, count);
        Assert.Equal("n5", first); // n0..n4 were dropped
    }

    [Fact]
    public void CursorsRoundTripAndRejectGarbage()
    {
        var at = new DateTimeOffset(2026, 11, 13, 21, 5, 7, 123, TimeSpan.Zero);
        var cursor = AppLogReader.EncodeCursor(at, 4242);

        Assert.True(AppLogReader.TryDecodeCursor(cursor, out var time, out var id));
        Assert.Equal((at, 4242L), (time, id));
        Assert.False(AppLogReader.TryDecodeCursor("garbage!", out _, out _));
        Assert.False(AppLogReader.TryDecodeCursor(new string('A', 200), out _, out _));
    }
}
