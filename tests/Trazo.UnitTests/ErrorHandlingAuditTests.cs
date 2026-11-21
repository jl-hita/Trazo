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
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trazo.Api.Endpoints;
using Trazo.Api.Overview;
using Trazo.Api.Security;
using Trazo.Api.Support;
using Trazo.Domain.Identity;
using Trazo.Domain.Support;
using Trazo.Domain.Waitlist;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Push;
using Trazo.Infrastructure.Support;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>
/// The error contract of the API, end to end over HTTP: an unexpected exception anywhere becomes a ticket and a structured, safe answer
/// with a trace id; expected failures (validation, 401, 403, 404, expected conditions) answer as before and open no ticket; one exception
/// is reported once however many places see it; work started by an endpoint reports its failures; and a report survives a restart.
/// </summary>
public sealed class ErrorHandlingAuditTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly SwitchableEmail _email = new();
    private readonly SwitchablePush _push = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ErrorHandlingAuditTests()
    {
        CreateTables("email_action_tokens");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.UseSetting("Account:PublicBaseUrl", "https://trazo.example");
            builder.UseSetting("Email:ActionBaseUrl", "https://trazo.example");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o
                    .UseSqlite(_fixture.ConnectionString)
                    .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>());
                // The tests flush the pipeline themselves, so no background writer competes for the reports.
                foreach (var writer in services.Where(d => d.ImplementationType == typeof(ErrorTicketWriter)).ToList()) services.Remove(writer);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_email);
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(_push);
                services.AddSingleton<IStartupFilter>(new FaultRoutes());
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void CreateTables(params string[] tables)
    {
        var options = new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(_fixture.ConnectionString)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>().Options;
        using var db = new TrazoDbContext(options);
        var script = db.GetService<IRelationalDatabaseCreator>().GenerateCreateScript();
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (statement.StartsWith("CREATE", StringComparison.Ordinal) &&
                tables.Any(t => statement.StartsWith($"CREATE TABLE \"{t}\"", StringComparison.Ordinal) || statement.Contains($" ON \"{t}\" ", StringComparison.Ordinal)))
                db.Database.ExecuteSqlRaw(statement);
    }

    // ------------------------------------------------------------------ doubles

    /// <summary>Throws what the test asks for (an exception the sender marked as a delivery failure, or any other).</summary>
    private sealed class SwitchableEmail : IEmailSender
    {
        public Func<Exception>? Failure { get; set; }
        public int Calls;
        public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Failure is { } make) throw make();
            return Task.CompletedTask;
        }
    }

    private sealed class SwitchablePush : IPushSender
    {
        public Func<Exception>? Failure { get; set; }
        public Task<PushSendResult> SendAsync(IReadOnlyCollection<Guid>? userIds, PushMessage message, CancellationToken ct) =>
            Failure is { } make ? throw make() : Task.FromResult(PushSendResult.None);
        public Task CheckConnectionAsync(CancellationToken ct) => Failure is { } make ? throw make() : Task.CompletedTask;
    }

    /// <summary>The way the real sender reports a mail server that does not answer: an exception marked as an expected condition.</summary>
    private static Exception MailServerDown() => new IOException("smtp.example.org refused ana@example.com").AsExpected();

    /// <summary>Routes that fail in the ways real endpoints can, behind the whole pipeline.</summary>
    private sealed class FaultRoutes : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Map("/boom", b => b.Run(_ => throw new InvalidOperationException("kaboom for secret.person@example.com SELECT * FROM users")));
            // Logged as an error and then rethrown: the two places that see it must produce one ticket, not two.
            app.Map("/log-and-throw", b => b.Run(context =>
            {
                var error = new InvalidOperationException("seen twice");
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Trazo.Test").LogError(error, "failed");
                throw error;
            }));
            // Reported by a catch block through the common mechanism and then thrown again.
            app.Map("/report-and-throw", b => b.Run(async context =>
            {
                var error = new NotSupportedException("seen by a catch block");
                await context.RequestServices.GetRequiredService<IErrorReporter>().ReportAsync(error, null, CancellationToken.None);
                throw error;
            }));
            // A mail server that does not answer, reaching the handler without any catch.
            app.Map("/mail-down", b => b.Run(_ => throw MailServerDown()));
            // The pattern of the overview endpoint: a real fault whose answer is known (503) and that still needs a ticket.
            app.Map("/db-down", b => b.Run(context => ErrorResults.Failure(new OverviewUnavailableException(new IOException("db")),
                503, "database_unavailable", "La base de datos no está disponible.", keepAnswerWhenReported: true).ExecuteAsync(context)));
            // A catch block that answers a delivery failure (503, nothing to ticket) or anything else (500 and a ticket).
            app.Map("/catch-expected", b => b.Run(context => ErrorResults.Failure(MailServerDown(), 503, "email_unavailable", "No se ha podido enviar el correo. Inténtalo más tarde.").ExecuteAsync(context)));
            app.Map("/catch-unexpected", b => b.Run(context => ErrorResults.Failure(new NullReferenceException("x"), 503, "email_unavailable", "No se ha podido enviar el correo. Inténtalo más tarde.").ExecuteAsync(context)));
        };
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(HttpClient Client, Guid Id)> NewUserAsync(string role, bool withPassword = true)
    {
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var email = $"{role}-{Guid.NewGuid():N}@trazo.test";
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = role,
            Role = role, EmailVerifiedAt = Now, CreatedAt = Now, UpdatedAt = Now
        };
        if (withPassword) user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, "CorrectHorse1Battery");
        else user.GoogleSubject = "google-" + user.Id;
        db.Users.Add(user);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return (client, user.Id);
    }

    private async Task<HttpClient> ClientForAsync(string role) => (await NewUserAsync(role)).Client;

    private async Task FlushAsync() => await _factory.Services.GetRequiredService<ErrorTicketPipeline>().FlushAsync(CancellationToken.None);

    private async Task WaitForBackgroundWorkAsync()
    {
        var queue = _factory.Services.GetRequiredService<BackgroundWorkQueue>();
        for (var i = 0; i < 500 && queue.InFlight > 0; i++) await Task.Delay(20);
        Assert.Equal(0, queue.InFlight);
    }

    private async Task<List<ErrorTicket>> TicketsAsync()
    {
        await FlushAsync();
        return await StoredAsync();
    }

    /// <summary>The tickets as they are, without letting the host's own pipeline (and its own budget) process anything.</summary>
    private async Task<List<ErrorTicket>> StoredAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().ErrorTickets.OrderBy(x => x.FirstSeenAt).ToListAsync();
    }

    private async Task<int> OutboxCountAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().ErrorReportOutbox.CountAsync();
    }

    private static async Task<JsonElement> ProblemOf(HttpResponseMessage response)
    {
        Assert.Contains("json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    /// <summary>The contract of every problem answer: type, title, status, detail, traceId and a stable code, and nothing internal.</summary>
    private static void AssertProblem(JsonElement problem, int status, string code)
    {
        foreach (var field in new[] { "type", "title", "status", "detail", "traceId", "code" })
            Assert.True(problem.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Null, $"the problem has no '{field}'");
        Assert.Equal(status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        var text = problem.GetRawText();
        foreach (var leak in new[] { "kaboom", "secret.person", "SELECT", "   at ", "System.", "Exception", "smtp.example.org", "ana@example.com" })
            Assert.DoesNotContain(leak, text);
    }

    // ------------------------------------------------------------------ 1-4: unexpected exceptions

    [Fact]
    public async Task UnexpectedException_BecomesATicketAndAStructuredSafeAnswerWithATraceId()
    {
        var response = await _factory.CreateClient().GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var problem = await ProblemOf(response);
        AssertProblem(problem, 500, "server_error");
        Assert.Equal("server_error", problem.GetProperty("title").GetString()); // the Android app reads the code from the title

        var ticket = Assert.Single(await TicketsAsync());
        Assert.Equal("backend", ticket.Source);
        Assert.Equal("System.InvalidOperationException", ticket.ErrorType);
        Assert.Contains("kaboom", ticket.StackTrace); // the ticket has the real trace...
        Assert.DoesNotContain("secret.person", ticket.StackTrace); // ...scrubbed of personal data
    }

    [Fact]
    public async Task TheTraceIdOfTheAnswerIsTheOneRecordedForTheTicket()
    {
        var client = _factory.CreateClient();
        var problem = await ProblemOf(await client.GetAsync("/boom"));
        var traceId = problem.GetProperty("traceId").GetString()!;
        await using var scope = _factory.Services.CreateAsyncScope();
        // The outbox row is the evidence of the link (the row is gone once the ticket is stored, so it is read before the flush).
        var row = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().ErrorReportOutbox.SingleAsync();
        Assert.Equal(traceId, row.TraceId);
    }

    [Fact]
    public async Task AnUnexpectedExceptionAcrossRequests_IsOneTicketWithManyOccurrences()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 3; i++) await client.GetAsync("/boom");
        var ticket = Assert.Single(await TicketsAsync());
        Assert.Equal(3, ticket.Occurrences);
    }

    [Fact]
    public async Task OneExceptionSeenByALogAndByTheMiddleware_IsReportedOnce()
    {
        var response = await _factory.CreateClient().GetAsync("/log-and-throw");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var ticket = Assert.Single(await TicketsAsync());
        Assert.Equal(1, ticket.Occurrences);
    }

    [Fact]
    public async Task OneExceptionReportedByACatchBlockAndThenThrown_IsReportedOnce()
    {
        var response = await _factory.CreateClient().GetAsync("/report-and-throw");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var ticket = Assert.Single(await TicketsAsync());
        Assert.Equal(1, ticket.Occurrences);
    }

    // ------------------------------------------------------------------ 5-8: explicit catch blocks and expected conditions

    [Fact]
    public async Task ACatchBlockThatMeetsAnUnexpectedException_ReportsItAndAnswers500NotTheExpected503()
    {
        var response = await _factory.CreateClient().GetAsync("/catch-unexpected");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertProblem(await ProblemOf(response), 500, "server_error");
        Assert.Equal("System.NullReferenceException", Assert.Single(await TicketsAsync()).ErrorType);
    }

    [Fact]
    public async Task ACatchBlockThatMeetsAnExpectedCondition_Answers503WithItsCodeAndOpensNoTicket()
    {
        var response = await _factory.CreateClient().GetAsync("/catch-expected");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertProblem(await ProblemOf(response), 503, "email_unavailable");
        Assert.Empty(await TicketsAsync());
    }

    [Fact]
    public async Task AnExpectedConditionThatReachesTheMiddleware_Answers503AndOpensNoTicket()
    {
        var response = await _factory.CreateClient().GetAsync("/mail-down");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertProblem(await ProblemOf(response), 503, "service_unavailable");
        Assert.Empty(await TicketsAsync());
    }

    [Fact]
    public async Task ARealFaultWithAKnownAnswer_KeepsItsStatusAndStillOpensATicket()
    {
        var response = await _factory.CreateClient().GetAsync("/db-down");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertProblem(await ProblemOf(response), 503, "database_unavailable");
        Assert.Equal(typeof(OverviewUnavailableException).FullName, Assert.Single(await TicketsAsync()).ErrorType);
    }

    // ------------------------------------------------------------------ 9-12: errors that are not defects open no ticket

    [Fact]
    public async Task ValidationAuthenticationAuthorizationAndMissingResources_OpenNoTicket()
    {
        var anonymous = _factory.CreateClient();
        var user = await ClientForAsync("user");
        var superadmin = await ClientForAsync("superadmin");

        var validation = await anonymous.PostAsJsonAsync("/api/v1/diagnostics/errors", new { appVersion = "1.0.3", stackTrace = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        var problem = await ProblemOf(validation);
        Assert.Equal("validation_error", problem.GetProperty("code").GetString()); // stable code even where the title is a sentence
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.True(problem.TryGetProperty("errors", out _));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/admin/error-tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await superadmin.GetAsync($"/api/v1/admin/error-tickets/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/v1/waitlist", new { email = "nope", language = "es", consent = true })).StatusCode);

        Assert.Empty(await TicketsAsync());
        Assert.Equal(0, await OutboxCountAsync());
    }

    [Fact]
    public async Task AMalformedRequest_OpensNoTicket()
    {
        var client = _factory.CreateClient();
        var broken = await client.PostAsync("/api/v1/diagnostics/errors", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"));
        Assert.True((int)broken.StatusCode is >= 400 and < 500);
        Assert.Empty(await TicketsAsync());
    }

    // ------------------------------------------------------------------ explicit catch blocks in real endpoints

    [Fact]
    public async Task PasswordSetup_AMailServerThatDoesNotAnswer_Is503WithACodeAndNoTicket()
    {
        var client = (await NewUserAsync("user", withPassword: false)).Client;
        _email.Failure = MailServerDown;
        var response = await client.PostAsync("/api/v1/account/password/setup", null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertProblem(await ProblemOf(response), 503, "email_unavailable");
        Assert.Empty(await TicketsAsync());
    }

    [Fact]
    public async Task PasswordSetup_AnUnexpectedFailure_IsATicketAndA500()
    {
        var client = (await NewUserAsync("user", withPassword: false)).Client;
        _email.Failure = () => new NullReferenceException("a bug while building the message");
        var response = await client.PostAsync("/api/v1/account/password/setup", null);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertProblem(await ProblemOf(response), 500, "server_error");
        Assert.Equal("System.NullReferenceException", Assert.Single(await TicketsAsync()).ErrorType);
    }

    [Fact]
    public async Task PasswordInvitationByAnAdmin_FollowsTheSameRules()
    {
        var superadmin = await ClientForAsync("superadmin");
        var target = (await NewUserAsync("user", withPassword: false)).Id;

        _email.Failure = MailServerDown;
        var down = await superadmin.PostAsync($"/api/v1/admin/users/{target}/password-invitation", null);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        AssertProblem(await ProblemOf(down), 503, "email_unavailable");
        Assert.Empty(await TicketsAsync());

        _email.Failure = () => new InvalidOperationException("template broken");
        // Another invitation to the same account right away is throttled, so use a second account.
        var other = (await NewUserAsync("user", withPassword: false)).Id;
        var bug = await superadmin.PostAsync($"/api/v1/admin/users/{other}/password-invitation", null);
        Assert.Equal(HttpStatusCode.InternalServerError, bug.StatusCode);
        Assert.Single(await TicketsAsync());
    }

    [Fact]
    public async Task Push_ExpectedResultsStay200WithoutATicket_AndAProviderFaultIsReportedWithItsTraceId()
    {
        var superadmin = await ClientForAsync("superadmin");

        foreach (var expected in new Func<Exception>[]
        {
            () => new PushException(PushFailure.NotConfigured, "Push notifications are not configured."),
            () => new PushException(PushFailure.Unauthorized, "Firebase rejected the service account or project."),
            () => new PushException(PushFailure.Unavailable, "Firebase could not be reached.", new HttpRequestException("network"))
        })
        {
            _push.Failure = expected;
            var response = await superadmin.PostAsync("/api/v1/admin/settings/push/check", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); // the contract of the console is unchanged
            var body = await ProblemOf(response);
            Assert.False(body.GetProperty("ok").GetBoolean());
            Assert.Equal(JsonValueKind.Null, body.GetProperty("traceId").ValueKind);
        }
        Assert.Empty(await TicketsAsync());

        // Firebase answering with an error status (no inner exception) is a fault of the provider or of ours: reported.
        _push.Failure = () => new PushException(PushFailure.Unavailable, "Firebase returned an error. Try again later.");
        var fault = await superadmin.PostAsync("/api/v1/admin/settings/push/check", null);
        Assert.Equal(HttpStatusCode.OK, fault.StatusCode);
        var faultBody = await ProblemOf(fault);
        Assert.False(faultBody.GetProperty("ok").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(faultBody.GetProperty("traceId").GetString()));
        Assert.Equal(typeof(PushException).FullName, Assert.Single(await TicketsAsync()).ErrorType);
    }

    [Fact]
    public async Task Push_NoRegisteredDevicesIsANormalResultNotAFailure()
    {
        var superadmin = await ClientForAsync("superadmin");
        _push.Failure = null; // SendAsync answers PushSendResult.None
        var response = await superadmin.PostAsync("/api/v1/admin/settings/push/test", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await ProblemOf(response)).GetProperty("ok").GetBoolean());
        Assert.Empty(await TicketsAsync());
    }

    [Fact]
    public async Task WaitlistInvitations_ADeliveryFailureIsCountedWithoutATicket_AnythingElseIsReported()
    {
        var superadmin = await ClientForAsync("superadmin");
        var ids = new List<Guid>();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            for (var i = 0; i < 2; i++)
            {
                var entry = new WaitlistEntry
                {
                    Id = Guid.NewGuid(), Email = $"w{i}@example.com", Language = "es", ConsentAt = Now,
                    CreatedAt = Now, ConfirmedAt = Now
                };
                db.WaitlistEntries.Add(entry);
                ids.Add(entry.Id);
            }
            await db.SaveChangesAsync();
        }

        _email.Failure = MailServerDown;
        var down = await superadmin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { ids[0] } });
        Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        Assert.Equal(1, (await ProblemOf(down)).GetProperty("failed").GetInt32());
        Assert.Empty(await TicketsAsync());

        _email.Failure = () => new InvalidOperationException("broken template for w1@example.com");
        var bug = await superadmin.PostAsJsonAsync("/api/v1/admin/waitlist/invite", new { ids = new[] { ids[1] } });
        Assert.Equal(1, (await ProblemOf(bug)).GetProperty("failed").GetInt32());
        var ticket = Assert.Single(await TicketsAsync());
        Assert.DoesNotContain("w1@example.com", ticket.StackTrace); // the address never reaches a ticket
    }

    // ------------------------------------------------------------------ background work

    [Fact]
    public async Task WaitlistConfirmation_FailsInTheBackground_AndTheFailureIsReportedNotLost()
    {
        var anonymous = _factory.CreateClient();
        _email.Failure = () => new InvalidOperationException("broken template for new@example.com");
        var accepted = await anonymous.PostAsJsonAsync("/api/v1/waitlist", new { email = "new@example.com", language = "es", consent = true });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode); // the answer does not depend on the mail
        await WaitForBackgroundWorkAsync();

        var ticket = Assert.Single(await TicketsAsync());
        Assert.Equal("System.InvalidOperationException", ticket.ErrorType);
        Assert.DoesNotContain("new@example.com", ticket.StackTrace);
    }

    [Fact]
    public async Task WaitlistConfirmation_AMailServerThatDoesNotAnswerInTheBackground_IsNotATicket()
    {
        _email.Failure = MailServerDown;
        var accepted = await _factory.CreateClient().PostAsJsonAsync("/api/v1/waitlist", new { email = "down@example.com", language = "en", consent = true });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await WaitForBackgroundWorkAsync();
        Assert.True(_email.Calls > 0);
        Assert.Empty(await TicketsAsync());
    }

    [Fact]
    public async Task AccountDeletionLink_RunsInItsOwnScope_AndItsFailureIsReported()
    {
        await NewUserAsync("user");
        string email;
        await using (var scope = _factory.Services.CreateAsyncScope())
            email = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Users.Select(u => u.Email).FirstAsync();

        // Works after the request is over: the job reads the database and sends the mail with services of its own scope.
        var mails = 0;
        _email.Failure = () => { mails++; return new InvalidOperationException("boom for " + email); };
        var accepted = await _factory.CreateClient().PostAsJsonAsync("/api/v1/account/deletion-request", new { email });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        await WaitForBackgroundWorkAsync();

        Assert.Equal(1, mails);
        var ticket = Assert.Single(await TicketsAsync());
        Assert.DoesNotContain(email, ticket.StackTrace);
    }

    [Fact]
    public async Task ABackgroundJob_ThatFails_IsLoggedAndReportedOnce_AndOneThatSucceedsIsNot()
    {
        var work = _factory.Services.GetRequiredService<IBackgroundWork>();
        var ran = new TaskCompletionSource();
        Assert.True(work.Enqueue("ok", (_, _) => { ran.SetResult(); return Task.CompletedTask; }));
        Assert.True(work.Enqueue("broken", (_, _) => throw new FormatException("bad data")));
        Assert.True(work.Enqueue("down", (_, _) => throw MailServerDown()));
        // The scope of a job is alive for the whole job (the old Task.Run used the request's services after they were disposed).
        Assert.True(work.Enqueue("scoped", async (services, ct) =>
        {
            await Task.Delay(30, ct);
            Assert.Equal(0, await services.GetRequiredService<TrazoDbContext>().ErrorTickets.CountAsync(ct));
        }));
        await WaitForBackgroundWorkAsync();
        await ran.Task;

        var ticket = Assert.Single(await TicketsAsync());
        Assert.Equal("System.FormatException", ticket.ErrorType);
    }

    // ------------------------------------------------------------------ the queue and the outbox

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A pipeline with its own memory and clock over the same database: what a restarted process looks like.</summary>
    private (ErrorTicketPipeline Pipeline, ErrorTicketQueue Queue, ErrorTicketRecorder Recorder) NewProcess(TimeProvider? clock = null)
    {
        var services = _factory.Services;
        clock ??= TimeProvider.System;
        var loggers = services.GetRequiredService<ILoggerFactory>();
        var recorder = new ErrorTicketRecorder(services.GetRequiredService<IServiceScopeFactory>(), clock, loggers, services.GetRequiredService<R8MappingStore>());
        var queue = new ErrorTicketQueue();
        return (new ErrorTicketPipeline(queue, recorder, services.GetRequiredService<IServiceScopeFactory>(), clock, loggers), queue, recorder);
    }

    private static ErrorReport Report(int n) =>
        new("backend", "1.0", $"System.Exception{n}: x\n   at Some.Type{n}.Method()", $"trace-{n}");

    [Fact]
    public async Task AReportThatWasAccepted_SurvivesARestart()
    {
        var first = NewProcess();
        Assert.True(await first.Pipeline.TryAppendAsync([Report(1), Report(2)], CancellationToken.None)); // accepted, then the process dies
        Assert.Equal(2, await OutboxCountAsync());

        var restarted = NewProcess();
        Assert.True(await restarted.Pipeline.FlushAsync(CancellationToken.None));
        Assert.Equal(2, (await StoredAsync()).Count);
        Assert.Equal(0, await OutboxCountAsync());
    }

    [Fact]
    public async Task ReportsStillInMemoryAtShutdown_AreMovedToTheOutbox()
    {
        var (pipeline, queue, _) = NewProcess();
        queue.TryWrite(Report(1));
        queue.TryWrite(Report(2));
        await pipeline.DrainToOutboxAsync(CancellationToken.None);
        Assert.Equal(2, await OutboxCountAsync());
        Assert.False(queue.Reader.TryRead(out _));
    }

    [Fact]
    public async Task WhenTheDatabaseFails_NothingIsLost_AndTheReportsAreStoredOnceItAnswers()
    {
        var (pipeline, queue, _) = NewProcess();
        string createScript;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            createScript = """
                CREATE TABLE error_report_outbox ("Id" TEXT PRIMARY KEY, "Source" TEXT NOT NULL, "AppVersion" TEXT NULL, "StackTrace" TEXT NOT NULL,
                  "TraceId" TEXT NULL, "ReportedAt" TEXT NOT NULL, "Attempts" INTEGER NOT NULL, "NextAttemptAt" TEXT NOT NULL)
                """;
            await db.Database.ExecuteSqlRawAsync("DROP TABLE error_report_outbox");
        }

        queue.TryWrite(Report(1));
        Assert.False(await pipeline.FlushAsync(CancellationToken.None)); // the database is not usable: the cycle fails, it does not throw
        Assert.True(pipeline.HasPendingWork);
        Assert.False(queue.Reader.TryRead(out _)); // it was taken from the queue but is held by the pipeline

        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Database.ExecuteSqlRawAsync(createScript);
        Assert.True(await pipeline.FlushAsync(CancellationToken.None));
        Assert.Single(await StoredAsync());
    }

    [Fact]
    public async Task AFailureWhileStoringATicket_KeepsTheRow_RetriesLater_AndGivesUpAfterTheLimit()
    {
        var clock = new MutableClock(Now);
        var (pipeline, _, _) = NewProcess(clock);
        await pipeline.TryAppendAsync([Report(1)], CancellationToken.None);
        await using (var scope = _factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Database.ExecuteSqlRawAsync("DROP TABLE error_tickets");

        Assert.False(await pipeline.FlushAsync(CancellationToken.None));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().ErrorReportOutbox.SingleAsync();
            Assert.Equal(1, row.Attempts);
            Assert.True(row.NextAttemptAt > clock.Now); // not retried in a tight loop
        }
        Assert.True(await pipeline.FlushAsync(CancellationToken.None)); // nothing is due yet: nothing happens, nothing fails

        for (var attempt = 2; attempt <= ErrorTicketPipeline.MaxAttempts; attempt++)
        {
            clock.Now = clock.Now.AddHours(1);
            await pipeline.FlushAsync(CancellationToken.None);
        }
        Assert.Equal(0, await OutboxCountAsync()); // discarded after the last attempt, with a warning (not endlessly kept)
    }

    [Fact]
    public async Task ABurstBeyondTheQueue_IsCountedAndATicketReportsTheLoss()
    {
        var (pipeline, queue, _) = NewProcess();
        for (var i = 0; i < ErrorTicketQueue.Capacity + 25; i++) queue.TryWrite(Report(i));
        await pipeline.FlushAsync(CancellationToken.None);

        // Everything that fit was kept, and the 25 that did not are accounted for by a report of their own.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var waiting = await db.ErrorReportOutbox.AsNoTracking().ToListAsync();
        var stored = await db.ErrorTickets.AsNoTracking().ToListAsync();
        Assert.Equal(ErrorTicketQueue.Capacity + 1, waiting.Count + stored.Count);
        Assert.True(
            waiting.Any(r => r.StackTrace.Contains(nameof(DroppedReportsException)) && r.StackTrace.Contains("25 error report")) ||
            stored.Any(t => t.ErrorType == typeof(DroppedReportsException).FullName && t.StackTrace.Contains("25 error report")));
    }

    [Fact]
    public async Task WhenTheHourlyBudgetIsSpent_NewErrorsWaitInTheOutboxInsteadOfBeingDropped()
    {
        var clock = new MutableClock(Now);
        var (pipeline, _, _) = NewProcess(clock);
        var total = ErrorTicketRecorder.MaxNewTicketsPerHour + 5;
        await pipeline.TryAppendAsync(Enumerable.Range(0, total).Select(Report).ToList(), CancellationToken.None);

        for (var i = 0; i < 6; i++) await pipeline.FlushAsync(CancellationToken.None); // batches of 50 until the budget is spent
        Assert.Equal(ErrorTicketRecorder.MaxNewTicketsPerHour, (await StoredAsync()).Count);
        Assert.Equal(5, await OutboxCountAsync()); // kept, not lost
        Assert.True(pipeline.HasPendingWork);

        clock.Now = clock.Now.AddHours(1).AddMinutes(1); // the next window
        await pipeline.FlushAsync(CancellationToken.None);
        Assert.Equal(total, (await StoredAsync()).Count);
        Assert.Equal(0, await OutboxCountAsync());
    }

    [Fact]
    public async Task ARepeatedErrorIsCountedEvenWhileTheBudgetIsSpent_AndDifferentErrorsAreNotMerged()
    {
        var (pipeline, _, _) = NewProcess();
        await pipeline.TryAppendAsync([Report(1), Report(1), Report(2)], CancellationToken.None);
        await pipeline.FlushAsync(CancellationToken.None);
        var tickets = await StoredAsync();
        Assert.Equal(2, tickets.Count);
        Assert.Equal(2, tickets.Single(t => t.ErrorType == "System.Exception1").Occurrences);
        Assert.Equal(1, tickets.Single(t => t.ErrorType == "System.Exception2").Occurrences);
    }

    [Fact]
    public async Task TheTicketSystemsOwnFailures_DoNotFeedBackIntoItself()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            await db.Database.ExecuteSqlRawAsync("DROP TABLE error_tickets");
            await db.Database.ExecuteSqlRawAsync("DROP TABLE error_report_outbox");
        }
        var queue = _factory.Services.GetRequiredService<ErrorTicketQueue>();
        var reporter = _factory.Services.GetRequiredService<IErrorReporter>();

        // Neither the append nor the processing may leave a report behind for the database errors they log on the way.
        Assert.False(await _factory.Services.GetRequiredService<ErrorTicketPipeline>().FlushAsync(CancellationToken.None));
        var accepted = await reporter.ReportAsync(new InvalidOperationException("x"), null, CancellationToken.None);
        Assert.True(accepted); // accepted into memory while the database is down
        Assert.True(queue.Reader.TryRead(out _)); // that one report is the only one: no second report was generated by the failures
        Assert.False(queue.Reader.TryRead(out _));
    }

    // ------------------------------------------------------------------ guard

    [Fact]
    public void NoCodeStartsWorkThatNobodyAwaits()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src", "Trazo.Api"))) root = root.Parent;
        Assert.NotNull(root);
        var offenders = Directory.EnumerateFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("Task.Run(") || File.ReadAllText(f).Contains("async void"))
            .Select(Path.GetFileName)
            .ToList();
        // Work that must outlive a request goes through IBackgroundWork (a scope of its own, reported failures, finished at shutdown).
        Assert.Empty(offenders);
    }
}
