namespace Trazo.Domain.Support;

public static class ErrorTicketStatus
{
    public const string New = "new";
    public const string Seen = "seen";
    public const string Resolved = "resolved";
    public static readonly string[] All = [New, Seen, Resolved];
}

public static class ErrorTicketSource
{
    public const string Backend = "backend";
    public const string Android = "android";
    public static readonly string[] All = [Backend, Android];
}

/// <summary>
/// An anonymous report of an unexpected error, from the API itself or from the Android app. Equal errors share one ticket
/// (same <see cref="Fingerprint"/>) and only bump <see cref="Occurrences"/>. It is deliberately not linked to
/// <c>TrazoUser</c>: nothing identifies who hit the error, and the stack trace is scrubbed of emails, tokens, ids and
/// addresses before it is stored (see docs/error-tickets.md).
/// </summary>
public sealed class ErrorTicket
{
    public Guid Id { get; set; }
    /// <summary>SHA-256 (hex) of the source, the exception types and the first frames of the trace.</summary>
    public required string Fingerprint { get; set; }
    /// <summary>"backend" or "android".</summary>
    public required string Source { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>The exception type of the first line, e.g. <c>System.InvalidOperationException</c>.</summary>
    public required string ErrorType { get; set; }
    /// <summary>The complete, scrubbed stack trace of the first occurrence.</summary>
    public required string StackTrace { get; set; }
    /// <summary>The scrubbed original obfuscated trace, retained only when a readable R8-retraced trace was produced.</summary>
    public string? OriginalStackTrace { get; set; }
    public int Occurrences { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    /// <summary>"new", "seen" or "resolved". A resolved ticket that happens again becomes "new" again.</summary>
    public required string Status { get; set; }
}
