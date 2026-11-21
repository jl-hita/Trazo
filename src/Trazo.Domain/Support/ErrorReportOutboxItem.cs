namespace Trazo.Domain.Support;

/// <summary>
/// An error report that was accepted (by the API's own handlers or by the app's report endpoint) and is waiting to become, or to
/// count towards, an <see cref="ErrorTicket"/>. It is the outbox of the ticket system: a report is written here first and removed
/// only once its ticket is stored, so a restart, a crash of the processor or a database that is down for a while do not lose it.
/// Like a ticket it carries no user: the stack trace is scrubbed when the ticket is built, and this row is short-lived.
/// </summary>
public sealed class ErrorReportOutboxItem
{
    public Guid Id { get; set; }
    public required string Source { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>The raw trace as reported (the app's obfuscated trace must reach R8 retrace before it is scrubbed).</summary>
    public required string StackTrace { get; set; }
    /// <summary>The request trace id the client was given, so an incident reference can be matched with the logs. Never user data.</summary>
    public string? TraceId { get; set; }
    public DateTimeOffset ReportedAt { get; set; }
    /// <summary>Failed attempts to store it (budget deferrals do not count).</summary>
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
}
