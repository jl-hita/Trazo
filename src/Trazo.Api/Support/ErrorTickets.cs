using System.Threading.Channels;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Domain.Support;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Support;

namespace Trazo.Api.Support;

/// <summary>
/// An error waiting to become (or to count towards) a ticket. It carries no user, token or request data; <paramref name="TraceId"/> is
/// the identifier the client was given in the error answer, so an incident reference can be matched with the logs.
/// </summary>
public sealed record ErrorReport(string Source, string? AppVersion, string StackTrace, string? TraceId = null);

/// <summary>
/// Bounded in-memory queue in front of the outbox: it is the only thing that can be used where a report has to be handed over
/// synchronously and without failing (a logger, a fallback while the database is down). When it is full the oldest reports are
/// dropped, but they are counted and the writer files a ticket about the loss, so a storm never loses reports silently.
/// </summary>
public sealed class ErrorTicketQueue
{
    public const int Capacity = 1_000;
    private long _dropped;
    private readonly Channel<ErrorReport> _channel;

    public ErrorTicketQueue() => _channel = Channel.CreateBounded<ErrorReport>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
        _ => Interlocked.Increment(ref _dropped));

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public ChannelReader<ErrorReport> Reader => _channel.Reader;
    public bool TryWrite(ErrorReport report) => _channel.Writer.TryWrite(report);

    /// <summary>Tells the writer there is something in the outbox (a report that was appended directly, not through the queue).</summary>
    public void Signal() => _wake.Writer.TryWrite(true);

    /// <summary>Waits until a report is queued, <see cref="Signal"/> is called, the time is up or the token is cancelled.</summary>
    public async Task WaitForWorkAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wait.CancelAfter(timeout);
        try { await Task.WhenAny(_channel.Reader.WaitToReadAsync(wait.Token).AsTask(), _wake.Reader.WaitToReadAsync(wait.Token).AsTask()); }
        finally { wait.Cancel(); }
        _wake.Reader.TryRead(out _);
    }

    /// <summary>How many reports were dropped since the last call (and resets the counter).</summary>
    public long TakeDropped() => Interlocked.Exchange(ref _dropped, 0);
}

/// <summary>Allows a number of events per hour; used so a storm of errors cannot fill the table or the inbox.</summary>
public sealed class HourlyBudget(int limit, TimeProvider clock)
{
    private readonly object _lock = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _used;

    public bool TryTake()
    {
        lock (_lock)
        {
            var now = clock.GetUtcNow();
            if (now - _windowStart >= TimeSpan.FromHours(1)) { _windowStart = now; _used = 0; }
            if (_used >= limit) return false;
            _used++;
            return true;
        }
    }
}

/// <summary>
/// Marks on the exception object itself, so the several places that see one exception (a log call, a catch block, the exception
/// middleware) agree on what was already done with it and on what is an expected operational condition.
/// </summary>
public static class ErrorMarks
{
    private const string ReportedKey = "trazo.reported";
    private const string ExpectedKey = "trazo.expected";
    private const int MaxDepth = 8;

    /// <summary>
    /// An operational condition that is not a defect and needs no ticket: the mail server did not answer, the mailbox is not
    /// configured. It still gets a clear answer and a log line, and the overview shows it where it matters.
    /// </summary>
    public static T AsExpected<T>(this T exception) where T : Exception => Set(exception, ExpectedKey);

    public static bool IsExpected(Exception? exception) => Has(exception, ExpectedKey);
    public static bool IsReported(Exception? exception) => Has(exception, ReportedKey);
    internal static void MarkReported(Exception exception) => Set(exception, ReportedKey);

    private static T Set<T>(T exception, string key) where T : Exception
    {
        try { exception.Data[key] = true; }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException) { /* a read-only Data dictionary: the mark is best effort */ }
        return exception;
    }

    /// <summary>The mark may be on the exception or on any of its inner exceptions (a wrapper does not hide it).</summary>
    private static bool Has(Exception? exception, string key)
    {
        for (var depth = 0; exception is not null && depth < MaxDepth; depth++, exception = exception.InnerException)
            if (exception.Data.Contains(key)) return true;
        return false;
    }
}

/// <summary>
/// The one way to report an unexpected exception as an error ticket, whoever finds it (a catch block, the exception middleware, a
/// log call, a background job). An exception is reported at most once, whichever of those sees it first.
/// </summary>
public interface IErrorReporter
{
    /// <summary>Synchronous and never failing: hands the exception to the in-memory queue. Use where nothing can be awaited.</summary>
    bool Report(Exception exception, string? traceId = null);

    /// <summary>Stores the report in the outbox before returning (falls back to the queue when the database does not answer in time).</summary>
    ValueTask<bool> ReportAsync(Exception exception, string? traceId, CancellationToken ct);

    /// <summary>The same for a report that is not an exception of this process (a crash trace sent by the app).</summary>
    ValueTask<bool> ReportAsync(ErrorReport report, CancellationToken ct);
}

public sealed class ErrorReporter(ErrorTicketQueue queue, ErrorTicketPipeline pipeline) : IErrorReporter
{
    /// <summary>The request is answered while this lasts at most: reporting must not make a failing request slower than the failure.</summary>
    private static readonly TimeSpan PersistBudget = TimeSpan.FromSeconds(2);
    /// <summary>After a failed append the database is not asked again for a while: a database that is down would slow every failing request.</summary>
    private static readonly TimeSpan SkipAfterFailure = TimeSpan.FromSeconds(30);
    private long _skipUntilTicks;

    public bool Report(Exception exception, string? traceId = null) => Enqueue(queue, exception, traceId);

    /// <summary>Shared with the logger provider, which cannot depend on the reporter (the reporter needs the logger factory).</summary>
    internal static bool Enqueue(ErrorTicketQueue queue, Exception exception, string? traceId)
    {
        if (!ShouldReport(exception)) return false;
        ErrorMarks.MarkReported(exception);
        try { return queue.TryWrite(ToReport(exception, traceId)); }
        catch { return false; /* reporting an error must never fail the code that found it */ }
    }

    public async ValueTask<bool> ReportAsync(Exception exception, string? traceId, CancellationToken ct)
    {
        if (!ShouldReport(exception)) return false;
        ErrorMarks.MarkReported(exception);
        return await Persist(ToReport(exception, traceId));
    }

    public ValueTask<bool> ReportAsync(ErrorReport report, CancellationToken ct) => Persist(report);

    private static bool ShouldReport(Exception? exception) =>
        exception is not null && !ErrorMarks.IsReported(exception) && !ErrorMarks.IsExpected(exception);

    private static ErrorReport ToReport(Exception exception, string? traceId) =>
        new(ErrorTicketSource.Backend, ErrorTicketRecorder.ServerVersion, exception.ToString(), traceId);

    private async ValueTask<bool> Persist(ErrorReport report)
    {
        if (Environment.TickCount64 >= Interlocked.Read(ref _skipUntilTicks))
        {
            try
            {
                // The request may already be aborted (the client left): the report is still wanted, so it does not use the request token.
                using var budget = new CancellationTokenSource(PersistBudget);
                if (await pipeline.TryAppendAsync([report], budget.Token)) return true;
            }
            catch { /* falls through to the memory queue */ }
            Interlocked.Exchange(ref _skipUntilTicks, Environment.TickCount64 + (long)SkipAfterFailure.TotalMilliseconds);
        }
        try { return queue.TryWrite(report); }
        catch { return false; }
    }
}

/// <summary>
/// Turns the errors the API logs (level Error or Critical with an exception) into tickets, so background work and handled-and-logged
/// failures are reported too, not only the ones that end a request. Skipped on purpose: the log of the exception middleware (the
/// handler reports it), Entity Framework's own log (it logs every failed command, including the unique-key races the code expects
/// and handles; the exception reaches the caller, who decides), and anything already reported or marked as expected.
/// </summary>
[ProviderAlias("ErrorTickets")]
public sealed class ErrorTicketLoggerProvider(ErrorTicketQueue queue) : ILoggerProvider
{
    internal const string OwnCategory = "Trazo.ErrorTickets";
    private const string MiddlewareCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";
    private const string EntityFrameworkCategory = "Microsoft.EntityFrameworkCore";

    public ILogger CreateLogger(string categoryName) => new TicketLogger(categoryName, queue);
    public void Dispose() { }

    private sealed class TicketLogger(string category, ErrorTicketQueue queue) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel is LogLevel.Error or LogLevel.Critical;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // While a ticket is being stored, errors (a database that is down logs one per query) must not become tickets themselves.
            if (!IsEnabled(logLevel) || exception is null || ErrorTicketRecorder.Recording.Value) return;
            if (category == OwnCategory || category.StartsWith(MiddlewareCategory, StringComparison.Ordinal) ||
                category.StartsWith(EntityFrameworkCategory, StringComparison.Ordinal)) return;
            ErrorReporter.Enqueue(queue, exception, null);
        }
    }
}

/// <summary>
/// Last line of defence for requests: reports the exception as a ticket and answers a problem (RFC 9457) with a stable code and the
/// trace id, without the exception text. Not reported: errors caused by the client (malformed requests), aborted requests and the
/// expected operational conditions (<see cref="ErrorMarks.AsExpected{T}"/>), which are answered 503.
/// </summary>
public sealed class ErrorTicketExceptionHandler(IErrorReporter reporter, ILoggerFactory loggers) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested && exception is OperationCanceledException or IOException) return true;
        if (exception is BadHttpRequestException) return false;

        var traceId = ErrorResults.TraceIdOf(context);
        var logger = loggers.CreateLogger(ErrorTicketLoggerProvider.OwnCategory);
        var endpoint = context.GetEndpoint()?.DisplayName ?? "(no endpoint)";
        int status;
        string code, detail;
        if (ErrorMarks.IsExpected(exception))
        {
            (status, code, detail) = (StatusCodes.Status503ServiceUnavailable, "service_unavailable",
                "El servicio no está disponible temporalmente. Inténtalo más tarde.");
            logger.LogWarning("Expected failure of type {ExceptionType} while serving {Endpoint} (trace {TraceId}); no ticket.",
                exception.GetType().FullName, endpoint, traceId);
        }
        else
        {
            (status, code, detail) = (StatusCodes.Status500InternalServerError, ErrorResults.ServerError, "Error inesperado.");
            await reporter.ReportAsync(exception, traceId, CancellationToken.None);
            logger.LogWarning("Unhandled exception of type {ExceptionType} while serving {Endpoint} (trace {TraceId}); reported as an error ticket.",
                exception.GetType().FullName, endpoint, traceId);
        }
        context.Response.StatusCode = status;
        await ErrorResults.Problem(status, code, detail).ExecuteAsync(context);
        return true;
    }
}

public enum ErrorReportResult { Stored, Deferred, Failed }

/// <summary>Builds the ticket of one report and tells the superadmins about new or reopened tickets.</summary>
public sealed class ErrorTicketRecorder(IServiceScopeFactory scopes, TimeProvider clock, ILoggerFactory loggers, R8MappingStore mappings)
{
    public static readonly string? ServerVersion = typeof(ErrorTicketRecorder).Assembly.GetName().Version?.ToString();
    public const int MaxNewTicketsPerHour = 200;
    public const int MaxEmailsPerHour = 10;
    private readonly HourlyBudget _newTickets = new(MaxNewTicketsPerHour, clock);
    private readonly HourlyBudget _emails = new(MaxEmailsPerHour, clock);
    private readonly ILogger _logger = loggers.CreateLogger(ErrorTicketLoggerProvider.OwnCategory);

    /// <summary>True while a report is being stored, so the errors that storing it may log are not reported in turn (an endless loop).</summary>
    internal static readonly AsyncLocal<bool> Recording = new();

    /// <summary>
    /// <see cref="ErrorReportResult.Deferred"/>: a brand-new error while the hourly budget of new tickets is spent. It is not dropped:
    /// the outbox keeps it and a later window stores it (repeats of a known error never need the budget).
    /// </summary>
    public async Task<ErrorReportResult> ProcessAsync(ErrorReport report, CancellationToken ct)
    {
        Recording.Value = true;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            // Retrace Android crashes before scrubbing: the obfuscated frames are needed by R8. Missing version/mapping
            // or an unavailable Retrace tool returns null and leaves the original trace untouched.
            var retracedTrace = report.Source == ErrorTicketSource.Android
                ? await mappings.TryRetraceAsync(report.AppVersion, report.StackTrace, ct)
                : null;
            var trace = retracedTrace ?? report.StackTrace;
            var scrubbedTrace = ErrorTicketService.Scrub(trace);
            var fingerprint = ErrorTicketService.Fingerprint(report.Source, scrubbedTrace);
            // A repeat is cheap and always counted; only a brand-new ticket spends the hourly budget.
            if (!await db.ErrorTickets.AsNoTracking().AnyAsync(x => x.Fingerprint == fingerprint, ct) && !_newTickets.TryTake())
                return ErrorReportResult.Deferred;
            var (outcome, id, type) = await ErrorTicketService.RecordAsync(db, report.Source, report.AppVersion, trace, clock.GetUtcNow(), ct,
                retracedTrace is null ? null : report.StackTrace);
            // Matches the reference the client was given with the ticket (the console log can be searched by trace id).
            _logger.LogInformation("Error ticket {TicketId} {Outcome} for trace {TraceId}.", id, outcome, report.TraceId ?? "(none)");
            if (outcome != ErrorTicketOutcome.Repeated && _emails.TryTake())
                await NotifyAsync(scope.ServiceProvider, report, type, outcome == ErrorTicketOutcome.Reopened, ct);
            return ErrorReportResult.Stored;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Warning, not Error: the provider above must not turn the failure of reporting into one more report.
            _logger.LogWarning("Could not store an error ticket ({ExceptionType}); it stays in the outbox.", ex.GetType().Name);
            return ErrorReportResult.Failed;
        }
        finally { Recording.Value = false; }
    }

    /// <summary>
    /// The notice names the error type only (a type name cannot hold personal data) and never the trace: the trace is read in
    /// the console, where access is limited to the superadmin. A failed notice never undoes the stored ticket.
    /// </summary>
    private async Task NotifyAsync(IServiceProvider services, ErrorReport report, string type, bool reopened, CancellationToken ct)
    {
        try
        {
            var db = services.GetRequiredService<TrazoDbContext>();
            var to = await db.Users.AsNoTracking().Where(u => u.Role == "superadmin" && u.DisabledAt == null && u.EmailVerifiedAt != null)
                .Select(u => u.Email).ToListAsync(ct);
            if (to.Count == 0) return;
            var email = services.GetRequiredService<IEmailSender>();
            var subject = $"TRAZO: {(reopened ? "error reabierto" : "nuevo error")} ({report.Source}): {type}";
            var body = $"Se ha {(reopened ? "reabierto un ticket" : "abierto un ticket")} de error anónimo.\n\nOrigen: {report.Source}\nTipo: {type}\n\n" +
                       "Abre Error tickets en la consola de administración para ver la stacktrace completa.";
            foreach (var recipient in to)
            {
                try { await email.SendAsync(recipient, subject, body, ct); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning("Could not email an error ticket notice ({ExceptionType}); it is still in the console.", ex.GetType().Name);
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("Could not prepare the error ticket notice ({ExceptionType}); it is still in the console.", ex.GetType().Name);
        }
    }
}

/// <summary>Only the type name of this exception is used: it is the subject of the ticket that reports a loss of error reports.</summary>
public sealed class DroppedReportsException : Exception;

/// <summary>
/// The reliable path from a report to a ticket. A report is first appended to the outbox table (PostgreSQL) and removed only when its
/// ticket is stored; the in-memory queue in front of it is only a hand-over point. What can still be lost, and what is done about it:
/// <list type="bullet">
/// <item>Restart or crash: reports reach the outbox within milliseconds; those still in memory are flushed on a normal shutdown.</item>
/// <item>Queue full: the oldest are dropped but counted, and a ticket about the loss is filed (<see cref="DroppedReportsException"/>).</item>
/// <item>Database down: reports wait in memory (bounded) and are appended when it answers again.</item>
/// <item>Failed or throttled storing: the row stays, with a later attempt time; after <see cref="MaxAttempts"/> failures it is discarded with a warning.</item>
/// </list>
/// Deduplication (one ticket per fingerprint) and the hourly budgets stay in <see cref="ErrorTicketRecorder"/>.
/// </summary>
public sealed class ErrorTicketPipeline(
    ErrorTicketQueue queue, ErrorTicketRecorder recorder, IServiceScopeFactory scopes, TimeProvider clock, ILoggerFactory loggers)
{
    public const int BatchSize = 50;
    public const int MaxAttempts = 10;
    public const int MaxUnsaved = 1_000;
    /// <summary>A report that could not be stored for this long (for example because the hourly budget stayed spent) is discarded.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan BudgetRetry = TimeSpan.FromMinutes(5);
    private readonly ILogger _logger = loggers.CreateLogger(ErrorTicketLoggerProvider.OwnCategory);
    private readonly List<ErrorReport> _unsaved = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>True when the last cycle left work behind (rows not yet stored, or reports that could not reach the outbox).</summary>
    public bool HasPendingWork { get; private set; }

    /// <summary>Appends reports to the outbox. False when the database did not take them (the caller keeps them).</summary>
    public async Task<bool> TryAppendAsync(IReadOnlyCollection<ErrorReport> reports, CancellationToken ct)
    {
        if (reports.Count == 0) return true;
        var previous = ErrorTicketRecorder.Recording.Value;
        ErrorTicketRecorder.Recording.Value = true;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var now = clock.GetUtcNow();
            foreach (var r in reports)
                db.ErrorReportOutbox.Add(new ErrorReportOutboxItem
                {
                    Id = Guid.NewGuid(), Source = r.Source, AppVersion = ErrorTicketService.CleanVersion(r.AppVersion),
                    // The API's own traces need no retrace, so they are scrubbed before they are written: the outbox never holds an address that an
                    // exception message quoted. The app's traces stay as sent until the ticket is built (R8 needs the obfuscated names).
                    StackTrace = r.Source == ErrorTicketSource.Backend ? ErrorTicketService.Scrub(r.StackTrace) : r.StackTrace.Length <= 40_000 ? r.StackTrace : r.StackTrace[..40_000],
                    TraceId = r.TraceId is { Length: <= 64 } trace ? trace : null, ReportedAt = now, NextAttemptAt = now
                });
            await db.SaveChangesAsync(ct);
            queue.Signal();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Could not append {Count} error report(s) to the outbox ({ExceptionType}).", reports.Count, ex.GetType().Name);
            return false;
        }
        finally { ErrorTicketRecorder.Recording.Value = previous; }
    }

    /// <summary>
    /// One cycle of the writer: moves what is in memory to the outbox and then stores the rows that are due. Returns false when the
    /// database could not be used (the writer then waits longer before trying again). Safe to call from tests.
    /// </summary>
    public async Task<bool> FlushAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var appended = await MoveMemoryToOutboxAsync(ct);
            var processed = await ProcessDueRowsAsync(ct);
            return appended && processed;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Normal shutdown: whatever is still in memory is put in the outbox, without storing tickets (that is for the next start).</summary>
    public async Task DrainToOutboxAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await MoveMemoryToOutboxAsync(ct); }
        finally { _gate.Release(); }
    }

    private async Task<bool> MoveMemoryToOutboxAsync(CancellationToken ct)
    {
        while (queue.Reader.TryRead(out var report)) _unsaved.Add(report);
        var dropped = queue.TakeDropped();
        if (_unsaved.Count > MaxUnsaved)
        {
            dropped += _unsaved.Count - MaxUnsaved;
            _unsaved.RemoveRange(0, _unsaved.Count - MaxUnsaved);
        }
        if (dropped > 0)
            _unsaved.Add(new ErrorReport(ErrorTicketSource.Backend, ErrorTicketRecorder.ServerVersion,
                $"{typeof(DroppedReportsException).FullName}: {dropped} error report(s) were dropped because the in-memory queue was full.\n" +
                $"   at {typeof(ErrorTicketQueue).FullName}.TryWrite(ErrorReport report)"));
        if (_unsaved.Count == 0) return true;
        if (!await TryAppendAsync(_unsaved, ct))
        {
            HasPendingWork = true;
            return false;
        }
        _unsaved.Clear();
        return true;
    }

    private async Task<bool> ProcessDueRowsAsync(CancellationToken ct)
    {
        var previous = ErrorTicketRecorder.Recording.Value;
        ErrorTicketRecorder.Recording.Value = true;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var now = clock.GetUtcNow();
            var due = await db.ErrorReportOutbox.Where(x => x.NextAttemptAt <= now).OrderBy(x => x.NextAttemptAt).Take(BatchSize).ToListAsync(ct);
            var allStored = true;
            var left = due.Count == BatchSize;
            foreach (var row in due)
            {
                var result = await recorder.ProcessAsync(new ErrorReport(row.Source, row.AppVersion, row.StackTrace, row.TraceId), ct);
                now = clock.GetUtcNow();
                switch (result)
                {
                    case ErrorReportResult.Stored:
                        db.ErrorReportOutbox.Remove(row);
                        break;
                    case ErrorReportResult.Deferred when now - row.ReportedAt >= MaxAge:
                        _logger.LogWarning("An error report was discarded: the hourly budget of new tickets stayed spent for {Hours} hours.", MaxAge.TotalHours);
                        db.ErrorReportOutbox.Remove(row);
                        break;
                    case ErrorReportResult.Deferred:
                        row.NextAttemptAt = now + BudgetRetry;
                        left = true;
                        break;
                    default:
                        allStored = false;
                        row.Attempts++;
                        if (row.Attempts >= MaxAttempts)
                        {
                            _logger.LogWarning("An error report was discarded after {Attempts} failed attempts to store it.", row.Attempts);
                            db.ErrorReportOutbox.Remove(row);
                        }
                        else
                        {
                            row.NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(900, 5 * Math.Pow(2, row.Attempts)));
                            left = true;
                        }
                        break;
                }
            }
            await db.SaveChangesAsync(ct);
            // Rows that are not due yet (a deferred one, a retry) are still work: the writer keeps polling for them.
            HasPendingWork = left || await db.ErrorReportOutbox.AnyAsync(ct);
            return allStored;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("The error outbox could not be processed ({ExceptionType}).", ex.GetType().Name);
            HasPendingWork = true;
            return false;
        }
        finally { ErrorTicketRecorder.Recording.Value = previous; }
    }
}

/// <summary>
/// Drains the queue into the outbox and the outbox into tickets, off the request path. It never stops because of a failure: a cycle
/// that fails is retried after a growing delay, so a database that is down for a while delays tickets but does not lose them.
/// </summary>
public sealed class ErrorTicketWriter(ErrorTicketQueue queue, ErrorTicketPipeline pipeline, ILoggerFactory loggers) : BackgroundService
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(5);
    private readonly ILogger _logger = loggers.CreateLogger(ErrorTicketLoggerProvider.OwnCategory);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var ok = false;
            try { ok = await pipeline.FlushAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                // Warning, not Error: a failing writer must not report through itself.
                _logger.LogWarning("The error ticket writer failed a cycle ({ExceptionType}); it keeps running.", ex.GetType().Name);
            }
            failures = ok ? 0 : Math.Min(failures + 1, 6);
            var wait = !ok ? TimeSpan.FromSeconds(2 * Math.Pow(2, failures)) : pipeline.HasPendingWork ? BusyWait : IdleWait;
            await queue.WaitForWorkAsync(wait, stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(TimeSpan.FromSeconds(5));
            await pipeline.DrainToOutboxAsync(limit.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { /* out of time: nothing more can be done */ }
    }
}

public static class ErrorTicketExtensions
{
    public static IServiceCollection AddErrorTickets(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddSingleton<ErrorTicketQueue>();
        services.AddSingleton<R8MappingStore>();
        services.AddSingleton<ErrorTicketRecorder>();
        services.AddSingleton<ErrorTicketPipeline>();
        services.AddSingleton<IErrorReporter, ErrorReporter>();
        services.AddExceptionHandler<ErrorTicketExceptionHandler>();
        services.AddHostedService<ErrorTicketWriter>();
        services.AddBackgroundWork();
        services.AddSingleton<ILoggerProvider>(sp => new ErrorTicketLoggerProvider(sp.GetRequiredService<ErrorTicketQueue>()));
        return services;
    }
}
