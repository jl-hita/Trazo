using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trazo.Api.Endpoints;
using Trazo.Api.Security;
using Trazo.Api.Support;
using Trazo.Domain.Identity;
using Trazo.Domain.Support;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Support;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>Anonymous error tickets: scrubbing, grouping, the app's report endpoint, the exception handler and the console routes.</summary>
public sealed class ErrorTicketsApiTests : IDisposable
{
    private const string AndroidTrace = """
        java.lang.IllegalStateException: no plan for ana.lopez@example.com
            at com.trazo.android.ui.plan.PlanViewModel.load(PlanViewModel.kt:42)
            at com.trazo.android.ui.plan.PlanViewModel$load$1.invokeSuspend(PlanViewModel.kt:50)
        Caused by: java.io.IOException: boom
            at okhttp3.internal.connection.RealCall.execute(RealCall.kt:10)
        """;

    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly FakeEmail _email = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ErrorTicketsApiTests()
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
                // The tests drain the queue themselves, so no background writer competes for the reports.
                foreach (var writer in services.Where(d => d.ImplementationType == typeof(ErrorTicketWriter)).ToList()) services.Remove(writer);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_email);
                services.AddSingleton<IStartupFilter>(new BoomFilter());
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private sealed class FakeEmail : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            lock (Sent) Sent.Add((recipient, subject, body));
            return Task.CompletedTask;
        }
    }

    /// <summary>Adds a route that throws, behind the whole pipeline, to prove the handler turns it into a ticket.</summary>
    private sealed class BoomFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/boom", b => b.Run(_ => throw new InvalidOperationException("kaboom for secret.person@example.com")));
        };
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

    /// <summary>Does what the background writer does, so the test sees the stored result.</summary>
    private async Task DrainAsync()
    {
        var queue = _factory.Services.GetRequiredService<ErrorTicketQueue>();
        var recorder = _factory.Services.GetRequiredService<ErrorTicketRecorder>();
        while (queue.Reader.TryRead(out var report)) await recorder.ProcessAsync(report, CancellationToken.None);
    }

    private async Task<List<ErrorTicket>> StoredAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().ErrorTickets.OrderBy(x => x.FirstSeenAt).ToListAsync();
    }

    private static Task<HttpResponseMessage> Report(HttpClient c, string? trace, string? version = "1.0.3") =>
        c.PostAsJsonAsync("/api/v1/diagnostics/errors", new { appVersion = version, stackTrace = trace });

    // ---- scrubbing and grouping ----

    [Fact]
    public void Scrub_RemovesWhatCouldIdentifyAPerson()
    {
        var jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2lnbmF0dXJl";
        var raw = $"System.Exception: user ana@example.com id 3fa85f64-5717-4562-b3fc-2c963f66afa6 from 192.168.1.20 token {jwt}\n" +
                  "  at X.Y() in C:\\Users\\ana\\src\\X.cs:line 10\n  GET https://api.trazo.test/v1/plan?email=ana@example.com&x=1\n  Authorization: Bearer abc.def-ghi";
        var scrubbed = ErrorTicketService.Scrub(raw);
        Assert.DoesNotContain("ana@example.com", scrubbed);
        Assert.DoesNotContain("3fa85f64", scrubbed);
        Assert.DoesNotContain("192.168.1.20", scrubbed);
        Assert.DoesNotContain("eyJ", scrubbed);
        Assert.DoesNotContain("abc.def-ghi", scrubbed);
        Assert.DoesNotContain("email=", scrubbed);
        Assert.DoesNotContain("\\ana\\", scrubbed);
        Assert.Contains("System.Exception", scrubbed);
        Assert.Contains("at X.Y()", scrubbed);
    }

    [Fact]
    public void Scrub_KeepsTheWholeTraceUpToTheLimit()
    {
        var frames = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"    at Some.Namespace.Type{i}.Method{i}(File.cs:{i})"));
        var scrubbed = ErrorTicketService.Scrub("System.Exception: x\n" + frames);
        Assert.Contains("Type199.Method199", scrubbed);
        Assert.True(ErrorTicketService.Scrub(new string('a', 40_000).Replace("a", "ab ")).Length <= ErrorTicketService.MaxStackTraceLength);
    }

    [Fact]
    public void Fingerprint_IgnoresLineNumbersAndMessagesButNotTypesOrSources()
    {
        string Fp(string source, string trace) => ErrorTicketService.Fingerprint(source, ErrorTicketService.Scrub(trace));
        var a = Fp("android", "java.lang.IllegalStateException: one\n    at a.B.c(B.kt:10)\n    at a.D.e(D.kt:5)");
        var b = Fp("android", "java.lang.IllegalStateException: another\n    at a.B.c(B.kt:99)\n    at a.D.e(D.kt:7)");
        Assert.Equal(a, b);
        Assert.NotEqual(a, Fp("backend", "java.lang.IllegalStateException: one\n    at a.B.c(B.kt:10)\n    at a.D.e(D.kt:5)"));
        Assert.NotEqual(a, Fp("android", "java.lang.IllegalArgumentException: one\n    at a.B.c(B.kt:10)\n    at a.D.e(D.kt:5)"));
        Assert.NotEqual(a, Fp("android", "java.lang.IllegalStateException: one\n    at a.B.other(B.kt:10)"));
        Assert.Equal("System.InvalidOperationException", ErrorTicketService.ErrorTypeOf("System.InvalidOperationException: nope\n at x"));
    }

    // ---- the app's report endpoint ----

    [Fact]
    public async Task Report_IsAnonymousStoresAScrubbedTicketAndGroupsRepeats()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Accepted, (await Report(client, AndroidTrace)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await Report(client, AndroidTrace.Replace("42", "43"), "1.0.4")).StatusCode);
        await DrainAsync();

        var ticket = Assert.Single(await StoredAsync());
        Assert.Equal("android", ticket.Source);
        Assert.Equal("java.lang.IllegalStateException", ticket.ErrorType);
        Assert.Equal(2, ticket.Occurrences);
        Assert.Equal("1.0.4", ticket.AppVersion);
        Assert.Null(ticket.OriginalStackTrace); // no mapping configured: keep and show the received obfuscated trace
        Assert.Equal(ErrorTicketStatus.New, ticket.Status);
        Assert.DoesNotContain("ana.lopez", ticket.StackTrace);
        Assert.Contains("PlanViewModel.load", ticket.StackTrace);
        Assert.Contains("Caused by: java.io.IOException", ticket.StackTrace); // the complete trace, not only the first line
    }

    [Fact]
    public async Task Report_RejectsMissingOrHugeTraces()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(client, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(client, "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Report(client, new string('x', ErrorTicketEndpoints.MaxIncomingLength + 1))).StatusCode);
        await DrainAsync();
        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async Task Report_IsRateLimitedPerIp()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 10; i++) Assert.Equal(HttpStatusCode.Accepted, (await Report(client, AndroidTrace)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Report(client, AndroidTrace)).StatusCode);
    }

    [Fact]
    public async Task Report_IgnoresAnInvalidVersionString()
    {
        await Report(_factory.CreateClient(), AndroidTrace, "1.0 <script>");
        await DrainAsync();
        Assert.Null(Assert.Single(await StoredAsync()).AppVersion);
    }

    // ---- the API's own errors ----

    [Fact]
    public async Task UnhandledException_AnswersAPlain500AndBecomesATicketWithTheFullTrace()
    {
        var response = await _factory.CreateClient().GetAsync("/boom");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("server_error", body);
        Assert.DoesNotContain("kaboom", body); // the client never sees the exception text

        await DrainAsync();
        var ticket = Assert.Single(await StoredAsync());
        Assert.Equal("backend", ticket.Source);
        Assert.Equal("System.InvalidOperationException", ticket.ErrorType);
        Assert.Contains("kaboom", ticket.StackTrace);
        Assert.DoesNotContain("secret.person", ticket.StackTrace);
        Assert.Contains("   at ", ticket.StackTrace);
    }

    [Fact]
    public async Task LoggedErrorsWithAnException_BecomeTicketsButWarningsAndMessagesWithoutExceptionDoNot()
    {
        var logger = _factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Trazo.Something");
        logger.LogWarning(new InvalidOperationException("only a warning"), "warn");
        logger.LogError("an error without exception");
        try { throw new NotSupportedException("handled but logged"); }
        catch (Exception ex) { logger.LogError(ex, "background job failed"); }
        await DrainAsync();
        var ticket = Assert.Single(await StoredAsync());
        Assert.Equal("System.NotSupportedException", ticket.ErrorType);
    }

    [Fact]
    public async Task ResolvedTicket_ReopensWhenTheErrorHappensAgain_AndOnlyNewOrReopenedOnesAreEmailedToSuperadmins()
    {
        var superadmin = await ClientForRoleAsync("superadmin");
        await ClientForRoleAsync("admin");
        var client = _factory.CreateClient();

        await Report(client, AndroidTrace);
        await DrainAsync();
        Assert.Single(_email.Sent);
        Assert.StartsWith("superadmin-", _email.Sent[0].To);
        Assert.Contains("java.lang.IllegalStateException", _email.Sent[0].Subject);
        Assert.DoesNotContain("PlanViewModel", _email.Sent[0].Body); // the notice never carries the trace

        await Report(client, AndroidTrace);
        await DrainAsync();
        Assert.Single(_email.Sent); // a repeat is counted, not announced again

        var id = (await StoredAsync()).Single().Id;
        Assert.Equal(HttpStatusCode.NoContent, (await superadmin.PutAsJsonAsync($"/api/v1/admin/error-tickets/{id}/status", new { status = "resolved" })).StatusCode);
        await Report(client, AndroidTrace);
        await DrainAsync();

        var ticket = Assert.Single(await StoredAsync());
        Assert.Equal(ErrorTicketStatus.New, ticket.Status);
        Assert.Equal(3, ticket.Occurrences);
        Assert.Equal(2, _email.Sent.Count);
        Assert.Contains("reabierto", _email.Sent[1].Subject);
    }

    [Fact]
    public async Task AFailureWhileStoringATicket_DoesNotCreateReportsInAnEndlessLoop()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE error_tickets");
        var queue = _factory.Services.GetRequiredService<ErrorTicketQueue>();
        await _factory.Services.GetRequiredService<ErrorTicketRecorder>()
            .ProcessAsync(new ErrorReport("android", "1", "java.lang.Error: x\n    at a.B.c(B.kt:1)"), CancellationToken.None);
        Assert.False(queue.Reader.TryRead(out _)); // the database errors logged on the way were not reported
    }

    [Fact]
    public async Task NewTickets_AreCappedPerHour()
    {
        var recorder = _factory.Services.GetRequiredService<ErrorTicketRecorder>();
        for (var i = 0; i < ErrorTicketRecorder.MaxNewTicketsPerHour + 20; i++)
            await recorder.ProcessAsync(new ErrorReport("android", "1", $"java.lang.Error{i}: x\n    at a.B.c{i}(B.kt:1)"), CancellationToken.None);
        Assert.Equal(ErrorTicketRecorder.MaxNewTicketsPerHour, (await StoredAsync()).Count);
        // A known error is still counted once the cap is reached.
        await recorder.ProcessAsync(new ErrorReport("android", "1", "java.lang.Error0: x\n    at a.B.c0(B.kt:1)"), CancellationToken.None);
        Assert.Equal(2, (await StoredAsync()).First(x => x.ErrorType == "java.lang.Error0").Occurrences);
    }

    // ---- console routes ----

    [Fact]
    public async Task TestException_ThrowsOnPurposeForTheSuperadminAndLeavesATicket()
    {
        foreach (var role in new string?[] { null, "user", "admin" })
        {
            var denied = await ClientForRoleAsync(role);
            var expected = role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            Assert.Equal(expected, (await denied.PostAsync("/api/v1/admin/error-tickets/test-exception", null)).StatusCode);
        }
        await DrainAsync();
        Assert.Empty(await StoredAsync()); // refused callers throw nothing

        var superadmin = await ClientForRoleAsync("superadmin");
        var response = await superadmin.PostAsync("/api/v1/admin/error-tickets/test-exception", null);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        await DrainAsync();
        var ticket = Assert.Single(await StoredAsync());
        Assert.Equal("backend", ticket.Source);
        Assert.Equal("System.InvalidOperationException", ticket.ErrorType);
        Assert.Contains("Test exception triggered on purpose", ticket.StackTrace);
    }

    [Fact]
    public async Task ConsoleRoutes_RequireSuperadmin()
    {
        foreach (var role in new string?[] { null, "user", "admin" })
        {
            var client = await ClientForRoleAsync(role);
            var expected = role is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            var id = Guid.NewGuid();
            Assert.Equal(expected, (await client.GetAsync("/api/v1/admin/error-tickets")).StatusCode);
            Assert.Equal(expected, (await client.GetAsync($"/api/v1/admin/error-tickets/{id}")).StatusCode);
            Assert.Equal(expected, (await client.PutAsJsonAsync($"/api/v1/admin/error-tickets/{id}/status", new { status = "seen" })).StatusCode);
            Assert.Equal(expected, (await client.DeleteAsync($"/api/v1/admin/error-tickets/{id}")).StatusCode);
        }
    }

    [Fact]
    public async Task Console_ListsFiltersShowsTheTraceChangesStatusAndDeletes()
    {
        var anonymous = _factory.CreateClient();
        await Report(anonymous, AndroidTrace);
        await _factory.CreateClient().GetAsync("/boom");
        await DrainAsync();
        var admin = await ClientForRoleAsync("superadmin");

        var all = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/error-tickets");
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, all.GetProperty("overall").GetProperty("new").GetInt32());
        Assert.False(all.GetProperty("items")[0].TryGetProperty("stackTrace", out _)); // the list stays light

        var android = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/error-tickets?source=android");
        var item = Assert.Single(android.GetProperty("items").EnumerateArray());
        var id = item.GetProperty("id").GetGuid();
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/error-tickets?q=kaboom")).GetProperty("totalCount").GetInt32());

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/error-tickets/{id}");
        Assert.Contains("Caused by: java.io.IOException", detail.GetProperty("stackTrace").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/admin/error-tickets/{id}/status", new { status = "seen" })).StatusCode);
        var seen = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/error-tickets?status=seen");
        Assert.Equal(1, seen.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/v1/admin/error-tickets/{id}/status", new { status = "bogus" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/error-tickets?status=bogus")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/admin/error-tickets/{Guid.NewGuid()}/status", new { status = "seen" })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/admin/error-tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/admin/error-tickets/{id}")).StatusCode);
        Assert.Single(await StoredAsync());
    }
}
