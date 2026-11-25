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
    private readonly ToggleEmailSender _mail = new();
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
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_mail);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Records what would be mailed; with <see cref="Fail"/> set, every send throws the way an SMTP error would (quoting the address).</summary>
    private sealed class ToggleEmailSender : IEmailSender
    {
        public System.Collections.Concurrent.ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();
        public volatile bool Fail;
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("550 mailbox unavailable for " + recipient);
            Sent.Enqueue((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    /// <summary>The confirmation goes out in the background: waits for the expected number of mails.</summary>
    private async Task<List<(string To, string Subject, string Body)>> WaitForMail(int count)
    {
        for (var i = 0; i < 100 && _mail.Sent.Count < count; i++) await Task.Delay(50);
        return _mail.Sent.ToList();
    }

    private static string TokenIn(string body) =>
        Uri.UnescapeDataString(System.Text.RegularExpressions.Regex.Match(body, @"token=([^&\s]+)").Groups[1].Value);

    private async Task<Guid> AddEntry(string email, string language, bool confirmed, bool invited = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var entry = new Trazo.Domain.Waitlist.WaitlistEntry
        {
            Id = Guid.NewGuid(), Email = email, Language = language, ConsentAt = now, CreatedAt = now,
            ConfirmedAt = confirmed ? now : null, InvitedAt = invited ? now : null
        };
        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    private async Task<Trazo.Domain.Waitlist.WaitlistEntry> Entry(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        return await db.WaitlistEntries.AsNoTracking().SingleAsync(x => x.Email == email);
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
        Assert.Equal(HttpStatusCode.BadRequest, (await Join(client, "a@example.com", language: "vi")).StatusCode);
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
            Assert.Equal(expected, (await client.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { Guid.NewGuid() } })).StatusCode);
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

    [Fact]
    public async Task Join_MailsAConfirmationLinkAndConfirmingItWorksOnce()
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Join(_factory.CreateClient(), "Ana@Example.com", "es")).StatusCode);
        var mail = Assert.Single(await WaitForMail(1));
        Assert.Equal("ana@example.com", mail.To);
        Assert.Contains("Confirma tu correo", mail.Subject);
        Assert.Contains("/waitlist/confirm?token=", mail.Body);
        Assert.Contains("lang=es", mail.Body);
        var token = TokenIn(mail.Body);

        var pending = await Entry("ana@example.com");
        Assert.Null(pending.ConfirmedAt);
        Assert.NotNull(pending.ConfirmTokenHash);
        Assert.DoesNotContain(token, pending.ConfirmTokenHash!); // only the hash is stored

        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/waitlist/confirm", new { token })).StatusCode);
        var confirmed = await Entry("ana@example.com");
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Null(confirmed.ConfirmTokenHash);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/waitlist/confirm", new { token })).StatusCode);
    }

    [Fact]
    public async Task Join_InEnglishMailsInEnglish()
    {
        await Join(_factory.CreateClient(), "ben@example.com", "en");
        var mail = Assert.Single(await WaitForMail(1));
        Assert.StartsWith("Confirm your email", mail.Subject);
        Assert.Contains("lang=en", mail.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-real-token")]
    public async Task Confirm_RejectsMissingAndUnknownTokens(string? token)
    {
        await Join(_factory.CreateClient(), "x@example.com");
        await WaitForMail(1);
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/waitlist/confirm", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await Entry("x@example.com")).ConfirmedAt);
    }

    [Fact]
    public async Task Confirm_RejectsAnExpiredLink()
    {
        await Join(_factory.CreateClient(), "late@example.com");
        var token = TokenIn((await WaitForMail(1)).Single().Body);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var entry = await db.WaitlistEntries.SingleAsync();
            entry.ConfirmTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _factory.CreateClient().PostAsJsonAsync("/api/v1/waitlist/confirm", new { token })).StatusCode);
        Assert.Null((await Entry("late@example.com")).ConfirmedAt);
    }

    [Fact]
    public async Task Join_AgainWhileUnconfirmedSendsAFreshLinkAndTheOldOneStopsWorking()
    {
        var client = _factory.CreateClient();
        await Join(client, "again@example.com");
        await WaitForMail(1);
        await Join(client, "again@example.com");
        var mails = await WaitForMail(2);
        Assert.Equal(2, mails.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/waitlist/confirm", new { token = TokenIn(mails[0].Body) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/waitlist/confirm", new { token = TokenIn(mails[1].Body) })).StatusCode);
    }

    [Fact]
    public async Task Join_ConfirmedAddressGetsTheSameAnswerAndNoMoreMail()
    {
        var client = _factory.CreateClient();
        var first = await Join(client, "done@example.com");
        await client.PostAsJsonAsync("/api/v1/waitlist/confirm", new { token = TokenIn((await WaitForMail(1)).Single().Body) });
        var again = await Join(client, "done@example.com");
        Assert.Equal(first.StatusCode, again.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await again.Content.ReadAsStringAsync());
        await Task.Delay(400);
        Assert.Single(_mail.Sent);
    }

    [Fact]
    public async Task Join_LimitsTheMailsOneAddressCanTrigger()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Accepted, (await Join(client, "flood@example.com")).StatusCode);
        await WaitForMail(DeletionRequestThrottle.MaximumPerWindow);
        await Task.Delay(400);
        Assert.Equal(DeletionRequestThrottle.MaximumPerWindow, _mail.Sent.Count);
    }

    [Fact]
    public async Task Join_StillAnswersWhenTheMailCannotBeSentAndNeverLogsTheAddress()
    {
        _mail.Fail = true;
        Assert.Equal(HttpStatusCode.Accepted, (await Join(_factory.CreateClient(), "nomail.person@example.com")).StatusCode);
        await Task.Delay(500);
        Assert.Single(await Stored());
        Assert.DoesNotContain("nomail.person", _logs.All, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InvalidOperationException", _logs.All); // the type is logged, not the text
    }

    [Fact]
    public async Task Invite_SendsOnlyToConfirmedAddressesOnceAndMarksThem()
    {
        var es = await AddEntry("es@example.com", "es", confirmed: true);
        var en = await AddEntry("en@example.com", "en", confirmed: true);
        var pending = await AddEntry("pending@example.com", "es", confirmed: false);
        var admin = await ClientForRoleAsync("superadmin");

        var response = await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { es, en, pending, Guid.NewGuid() } });
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, result.GetProperty("invited").GetInt32());
        Assert.Equal(0, result.GetProperty("failed").GetInt32());
        Assert.Equal(2, result.GetProperty("skipped").GetInt32());

        var mails = _mail.Sent.ToList();
        var spanish = Assert.Single(mails, m => m.To == "es@example.com");
        Assert.Contains("Ya puedes probar TRAZO", spanish.Subject);
        Assert.Contains(Trazo.Api.Endpoints.WaitlistEndpoints.DefaultTestInviteUrl, spanish.Body);
        Assert.Contains("/waitlist/unsubscribe?token=", spanish.Body);
        Assert.StartsWith("You can now try TRAZO", Assert.Single(mails, m => m.To == "en@example.com").Subject);
        Assert.DoesNotContain(mails, m => m.To == "pending@example.com");
        Assert.NotNull((await Entry("es@example.com")).InvitedAt);
        Assert.Null((await Entry("pending@example.com")).InvitedAt);

        // Already invited: nothing is sent twice.
        var second = await (await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { es, en } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, second.GetProperty("invited").GetInt32());
        Assert.Equal(2, _mail.Sent.Count);
    }

    [Fact]
    public async Task Invite_KeepsFailedOnesForAnotherTryAndNeverLogsTheAddress()
    {
        var id = await AddEntry("fails.person@example.com", "es", confirmed: true);
        var admin = await ClientForRoleAsync("superadmin");
        _mail.Fail = true;
        var result = await (await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { id } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, result.GetProperty("failed").GetInt32());
        Assert.Null((await Entry("fails.person@example.com")).InvitedAt);
        Assert.DoesNotContain("fails.person", _logs.All, StringComparison.OrdinalIgnoreCase);

        _mail.Fail = false;
        var retry = await (await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { id } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, retry.GetProperty("invited").GetInt32());
    }

    [Fact]
    public async Task Invite_ValidatesTheNumberOfIds()
    {
        var admin = await ClientForRoleAsync("superadmin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = (Guid[]?)null })).StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_WithTheLinkOfTheInvitationDeletesTheEntry()
    {
        var id = await AddEntry("bye@example.com", "en", confirmed: true);
        var admin = await ClientForRoleAsync("superadmin");
        await admin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { id } });
        var token = TokenIn(Assert.Single(_mail.Sent).Body);

        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/v1/waitlist/unsubscribe", new { token })).StatusCode);
        Assert.Empty(await Stored());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/v1/waitlist/unsubscribe", new { token })).StatusCode); // idempotent
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/waitlist/unsubscribe", new { token = "forged" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/waitlist/unsubscribe", new { token = (string?)null })).StatusCode);
    }

    [Fact]
    public async Task Admin_ListsByStatusAndCountsEachOne()
    {
        await AddEntry("p1@example.com", "es", confirmed: false);
        await AddEntry("c1@example.com", "es", confirmed: true);
        await AddEntry("c2@example.com", "en", confirmed: true);
        await AddEntry("i1@example.com", "es", confirmed: true, invited: true);
        var admin = await ClientForRoleAsync("superadmin");

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/waitlist");
        var overall = all.GetProperty("overall");
        Assert.Equal(4, overall.GetProperty("total").GetInt32());
        Assert.Equal(1, overall.GetProperty("pending").GetInt32());
        Assert.Equal(2, overall.GetProperty("confirmed").GetInt32());
        Assert.Equal(1, overall.GetProperty("invited").GetInt32());

        foreach (var (status, expected) in new[] { ("pending", 1), ("confirmed", 2), ("invited", 1) })
        {
            var list = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/waitlist?status={status}");
            Assert.Equal(expected, list.GetProperty("totalCount").GetInt32());
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/waitlist?status=nope")).StatusCode);

        var csv = await (await admin.GetAsync("/api/v1/admin/waitlist/export.csv")).Content.ReadAsStringAsync();
        Assert.Contains("email,language,consent_at,created_at,confirmed_at,invited_at", csv);
    }
}
