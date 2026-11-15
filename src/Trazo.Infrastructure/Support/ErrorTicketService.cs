using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Support;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Support;

public enum ErrorTicketOutcome { Created, Reopened, Repeated }

/// <summary>
/// Stores anonymous error tickets. The report never carries a user: the stack trace is scrubbed here, in one place, for both
/// the API's own exceptions and the traces sent by the app, so what reaches the database does not identify anyone.
/// </summary>
public static partial class ErrorTicketService
{
    public const int MaxStackTraceLength = 16_000;
    public const int MaxErrorTypeLength = 200;
    private const int FingerprintFrames = 6;

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}")] private static partial Regex Emails();
    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*")] private static partial Regex Jwts();
    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._~+/=\-]+")] private static partial Regex Bearers();
    [GeneratedRegex(@"(https?://[^\s?#""')>]+)[?#][^\s""')>]*")] private static partial Regex UrlQueries();
    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")] private static partial Regex Guids();
    [GeneratedRegex(@"\b\d{1,3}(\.\d{1,3}){3}\b")] private static partial Regex Ipv4();
    [GeneratedRegex(@"\b[0-9a-fA-F]{32,}\b")] private static partial Regex LongHex();
    [GeneratedRegex(@"[A-Za-z0-9+/_\-]{40,}={0,2}")] private static partial Regex LongSecrets();
    [GeneratedRegex(@"(?i)([\\/]Users[\\/]|/home/)[^\\/\s]+")] private static partial Regex HomeFolders();
    [GeneratedRegex(@"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")] private static partial Regex ControlChars();
    [GeneratedRegex(@"^[A-Za-z0-9_.$<>`+\[\],\-]+")] private static partial Regex TypeName();
    [GeneratedRegex(@"\sin\s.*$")] private static partial Regex FileSuffix();
    [GeneratedRegex(@":\d+\)")] private static partial Regex LineNumber();

    /// <summary>Removes what could identify a person: emails, tokens, ids, addresses, URL queries and home folders.</summary>
    public static string Scrub(string trace)
    {
        var s = ControlChars().Replace(trace, "");
        s = Jwts().Replace(s, "[token]");
        s = Bearers().Replace(s, "Bearer [token]");
        s = Emails().Replace(s, "[email]");
        s = UrlQueries().Replace(s, "$1?[query]");
        s = Guids().Replace(s, "[id]");
        s = Ipv4().Replace(s, "[ip]");
        s = LongHex().Replace(s, "[hex]");
        s = LongSecrets().Replace(s, "[secret]");
        s = HomeFolders().Replace(s, "$1[user]");
        s = s.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return s.Length <= MaxStackTraceLength ? s : s[..MaxStackTraceLength];
    }

    /// <summary>The exception type of the first line (<c>java.lang.IllegalStateException: msg</c> gives <c>java.lang.IllegalStateException</c>).</summary>
    public static string ErrorTypeOf(string trace)
    {
        var first = trace.Split('\n', 2)[0].Trim();
        var match = TypeName().Match(first);
        var type = match.Success && match.Length > 0 ? match.Value : (first.Length > 0 ? first : "UnknownError");
        return type.Length <= MaxErrorTypeLength ? type : type[..MaxErrorTypeLength];
    }

    /// <summary>
    /// Groups equal errors: the source, the outer and the innermost exception type and the first frames without line numbers or
    /// file paths, so a new build that moves a line does not split a ticket in two.
    /// </summary>
    public static string Fingerprint(string source, string scrubbedTrace)
    {
        var lines = scrubbedTrace.Split('\n').Select(l => l.Trim()).ToList();
        var frames = lines.Where(l => l.StartsWith("at ", StringComparison.Ordinal))
            .Take(FingerprintFrames).Select(f => LineNumber().Replace(FileSuffix().Replace(f, ""), ")"));
        var root = lines.Where(l => l.StartsWith("Caused by:", StringComparison.Ordinal) || l.StartsWith("--->", StringComparison.Ordinal))
            .Select(l => ErrorTypeOf(l.StartsWith("Caused by:", StringComparison.Ordinal) ? l["Caused by:".Length..].Trim() : l["--->".Length..].Trim()))
            .LastOrDefault() ?? "";
        var material = string.Join('\n', new[] { source, ErrorTypeOf(scrubbedTrace), root }.Concat(frames));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    /// <summary>A version string safe to store and show: letters, digits and <c>. - + _</c> only.</summary>
    public static string? CleanVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var v = version.Trim();
        return v.Length <= 40 && v.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '+' or '_') ? v : null;
    }

    /// <summary>
    /// Records one occurrence: opens a ticket for a new error, counts a repeat, and reopens a resolved ticket. Two requests that
    /// race to create the same ticket end up as one ticket with two occurrences.
    /// </summary>
    public static async Task<(ErrorTicketOutcome Outcome, Guid Id, string ErrorType)> RecordAsync(
        TrazoDbContext db, string source, string? appVersion, string stackTrace, DateTimeOffset now, CancellationToken ct)
    {
        var trace = Scrub(stackTrace);
        var fingerprint = Fingerprint(source, trace);
        for (var attempt = 0; ; attempt++)
        {
            var ticket = await db.ErrorTickets.FirstOrDefaultAsync(x => x.Fingerprint == fingerprint, ct);
            if (ticket is null)
            {
                ticket = new ErrorTicket
                {
                    Id = Guid.NewGuid(), Fingerprint = fingerprint, Source = source, AppVersion = CleanVersion(appVersion),
                    ErrorType = ErrorTypeOf(trace), StackTrace = trace, Occurrences = 1, FirstSeenAt = now, LastSeenAt = now,
                    Status = ErrorTicketStatus.New
                };
                db.ErrorTickets.Add(ticket);
                try { await db.SaveChangesAsync(ct); return (ErrorTicketOutcome.Created, ticket.Id, ticket.ErrorType); }
                catch (DbUpdateException) when (attempt == 0) { db.Entry(ticket).State = EntityState.Detached; continue; }
            }
            var reopened = ticket.Status == ErrorTicketStatus.Resolved;
            ticket.Occurrences++;
            ticket.LastSeenAt = now;
            if (reopened) ticket.Status = ErrorTicketStatus.New;
            if (CleanVersion(appVersion) is { } version) ticket.AppVersion = version;
            await db.SaveChangesAsync(ct);
            return (reopened ? ErrorTicketOutcome.Reopened : ErrorTicketOutcome.Repeated, ticket.Id, ticket.ErrorType);
        }
    }
}
