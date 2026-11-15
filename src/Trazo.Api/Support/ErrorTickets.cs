using System.Threading.Channels;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Domain.Support;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Support;

namespace Trazo.Api.Support;

/// <summary>An error waiting to become (or to count towards) a ticket. It carries no user, token or request data.</summary>
public sealed record ErrorReport(string Source, string? AppVersion, string StackTrace);

/// <summary>
/// Bounded in-memory queue between the places that find an error (any thread; must never block or fail) and the writer.
/// When it is full the oldest reports are dropped.
/// </summary>
public sealed class ErrorTicketQueue
{
    public const int Capacity = 1_000;
    private readonly Channel<ErrorReport> _channel = Channel.CreateBounded<ErrorReport>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public ChannelReader<ErrorReport> Reader => _channel.Reader;
    public bool TryWrite(ErrorReport report) => _channel.Writer.TryWrite(report);
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
/// Turns every unexpected error the API logs (level Error or Critical with an exception) into a ticket, so background
/// work and handled-and-logged failures are reported too, not only the ones that end a request. The unhandled-exception
/// log of the exception middleware is skipped: <see cref="ErrorTicketExceptionHandler"/> reports those itself.
/// </summary>
[ProviderAlias("ErrorTickets")]
public sealed class ErrorTicketLoggerProvider(ErrorTicketQueue queue, string? version) : ILoggerProvider
{
    internal const string OwnCategory = "Trazo.ErrorTickets";
    private const string MiddlewareCategory = "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware";

    public ILogger CreateLogger(string categoryName) => new TicketLogger(categoryName, queue, version);
    public void Dispose() { }

    private sealed class TicketLogger(string category, ErrorTicketQueue queue, string? version) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel is LogLevel.Error or LogLevel.Critical;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // While a ticket is being stored, errors (a database that is down logs one per query) must not become tickets themselves.
            if (!IsEnabled(logLevel) || exception is null || ErrorTicketRecorder.Recording.Value) return;
            if (category == OwnCategory || category.StartsWith(MiddlewareCategory, StringComparison.Ordinal)) return;
            try { queue.TryWrite(new ErrorReport(ErrorTicketSource.Backend, version, exception.ToString())); }
            catch { /* reporting an error must never fail the code that found it */ }
        }
    }
}

/// <summary>
/// Last line of defence for requests: reports the exception as a ticket and answers a plain 500 problem, without the exception
/// text. Errors caused by the client (malformed requests) and aborted requests are not reported.
/// </summary>
public sealed class ErrorTicketExceptionHandler(ErrorTicketQueue queue, ILoggerFactory loggers) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (context.RequestAborted.IsCancellationRequested && exception is OperationCanceledException or IOException) return true;
        if (exception is BadHttpRequestException) return false;

        try { queue.TryWrite(new ErrorReport(ErrorTicketSource.Backend, ErrorTicketRecorder.ServerVersion, exception.ToString())); }
        catch { /* never mask the original failure */ }
        loggers.CreateLogger(ErrorTicketLoggerProvider.OwnCategory)
            .LogWarning("Unhandled exception of type {ExceptionType} while serving {Endpoint}; reported as an error ticket.",
                exception.GetType().FullName, context.GetEndpoint()?.DisplayName ?? "(no endpoint)");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(title: "server_error", detail: "Unexpected error.", statusCode: StatusCodes.Status500InternalServerError)
            .ExecuteAsync(context);
        return true;
    }
}

/// <summary>Stores queued reports and tells the superadmins about new or reopened tickets.</summary>
public sealed class ErrorTicketRecorder(IServiceScopeFactory scopes, TimeProvider clock, ILoggerFactory loggers)
{
    public static readonly string? ServerVersion = typeof(ErrorTicketRecorder).Assembly.GetName().Version?.ToString();
    public const int MaxNewTicketsPerHour = 200;
    public const int MaxEmailsPerHour = 10;
    private readonly HourlyBudget _newTickets = new(MaxNewTicketsPerHour, clock);
    private readonly HourlyBudget _emails = new(MaxEmailsPerHour, clock);
    private readonly ILogger _logger = loggers.CreateLogger(ErrorTicketLoggerProvider.OwnCategory);

    /// <summary>True while a report is being stored, so the errors that storing it may log are not reported in turn (an endless loop).</summary>
    internal static readonly AsyncLocal<bool> Recording = new();

    public async Task ProcessAsync(ErrorReport report, CancellationToken ct)
    {
        Recording.Value = true;
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var trace = ErrorTicketService.Scrub(report.StackTrace);
            var fingerprint = ErrorTicketService.Fingerprint(report.Source, trace);
            // A repeat is cheap and always counted; only a brand-new ticket spends the hourly budget.
            if (!await db.ErrorTickets.AsNoTracking().AnyAsync(x => x.Fingerprint == fingerprint, ct) && !_newTickets.TryTake()) return;
            var (outcome, id, type) = await ErrorTicketService.RecordAsync(db, report.Source, report.AppVersion, report.StackTrace, clock.GetUtcNow(), ct);
            if (outcome == ErrorTicketOutcome.Repeated || !_emails.TryTake()) return;
            await NotifyAsync(scope.ServiceProvider, report, type, outcome == ErrorTicketOutcome.Reopened, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Warning, not Error: the provider above must not turn the failure of reporting into one more report.
            _logger.LogWarning("Could not store an error ticket ({ExceptionType}).", ex.GetType().Name);
        }
        finally { Recording.Value = false; }
    }

    /// <summary>
    /// The notice names the error type only (a type name cannot hold personal data) and never the trace: the trace is read in
    /// the console, where access is limited to the superadmin.
    /// </summary>
    private async Task NotifyAsync(IServiceProvider services, ErrorReport report, string type, bool reopened, CancellationToken ct)
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
}

/// <summary>Drains the queue into tickets, one report at a time, off the request path.</summary>
public sealed class ErrorTicketWriter(ErrorTicketQueue queue, ErrorTicketRecorder recorder) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var report in queue.Reader.ReadAllAsync(stoppingToken))
                await recorder.ProcessAsync(report, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }
}

public static class ErrorTicketExtensions
{
    public static IServiceCollection AddErrorTickets(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddSingleton<ErrorTicketQueue>();
        services.AddSingleton<ErrorTicketRecorder>();
        services.AddExceptionHandler<ErrorTicketExceptionHandler>();
        services.AddHostedService<ErrorTicketWriter>();
        services.AddSingleton<ILoggerProvider>(sp => new ErrorTicketLoggerProvider(sp.GetRequiredService<ErrorTicketQueue>(), ErrorTicketRecorder.ServerVersion));
        return services;
    }
}
