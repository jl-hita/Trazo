using Trazo.Application.Localization;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Waitlist;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Waitlist;

/// <summary>
/// The waitlist store. Nothing here logs: the address is personal data and must never reach the application log.
/// </summary>
public static class WaitlistService
{
    public const int MaxEmailLength = 254;
    /// <summary>The languages the waitlist can write in: those the API has texts for.</summary>
    public static readonly string[] Languages = Trazo.Application.Localization.LanguageNegotiation.Supported.Select(l => l.Code()).ToArray();

    /// <summary>The only stored form of an address: trimmed and lower-cased.</summary>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var value = email.Trim();
        if (value.Length > MaxEmailLength || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)) return false;
        if (!MailAddress.TryCreate(value, out var parsed) || parsed.Address != value || !string.IsNullOrEmpty(parsed.DisplayName)) return false;
        var domain = parsed.Host;
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.') && !domain.Contains("..");
    }

    /// <summary>
    /// Adds the address unless it is already there. Uses a single INSERT ... ON CONFLICT DO NOTHING so a duplicate (or a
    /// race between two requests) is not an exception: the database error text would otherwise quote the address and be
    /// logged. Returns whether a row was added; callers must not let that reach the client.
    /// </summary>
    public static async Task<bool> AddAsync(TrazoDbContext db, string email, string language, DateTimeOffset now, CancellationToken ct)
    {
        var id = Guid.NewGuid();
        var normalized = Normalize(email);
        // Raw SQL skips value converters, so apply the model's own (none on PostgreSQL) to the date.
        var converter = db.Model.FindEntityType(typeof(WaitlistEntry))!.FindProperty(nameof(WaitlistEntry.CreatedAt))!.GetValueConverter();
        object stamp = converter?.ConvertToProvider(now) ?? now;
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO waitlist_entries ("Id", "Email", "Language", "ConsentAt", "CreatedAt")
            VALUES ({id}, {normalized}, {language}, {stamp}, {stamp})
            ON CONFLICT ("Email") DO NOTHING
            """, ct);
        return rows > 0;
    }

    /// <summary>How long a confirmation link works.</summary>
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromHours(48);

    public static string HashToken(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));

    /// <summary>
    /// Starts (or restarts) the double opt-in of an address that is still unconfirmed: stores the hash of a fresh token and
    /// returns the token to mail. Returns null when the address is unknown or already confirmed, so nothing is sent to it.
    /// </summary>
    public static async Task<string?> IssueConfirmationAsync(TrazoDbContext db, string email, DateTimeOffset now, CancellationToken ct)
    {
        var normalized = Normalize(email);
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = HashToken(raw);
        var expires = now.Add(ConfirmationLifetime);
        var rows = await db.Set<WaitlistEntry>().Where(x => x.Email == normalized && x.ConfirmedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConfirmTokenHash, hash).SetProperty(x => x.ConfirmTokenExpiresAt, expires), ct);
        return rows == 1 ? raw : null;
    }

    /// <summary>Confirms the address that owns the token. The token works once and only until it expires.</summary>
    public static async Task<bool> ConfirmAsync(TrazoDbContext db, string? rawToken, DateTimeOffset now, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length > 200) return false;
        var hash = HashToken(rawToken);
        var rows = await db.Set<WaitlistEntry>().Where(x => x.ConfirmTokenHash == hash && x.ConfirmTokenExpiresAt > now && x.ConfirmedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConfirmedAt, now)
                .SetProperty(x => x.ConfirmTokenHash, (string?)null).SetProperty(x => x.ConfirmTokenExpiresAt, (DateTimeOffset?)null), ct);
        return rows == 1;
    }

    /// <summary>Removes one entry by id (the person's own "unsubscribe" link and the console's delete).</summary>
    public static Task<int> DeleteByIdAsync(TrazoDbContext db, Guid id, CancellationToken ct) =>
        db.Set<WaitlistEntry>().Where(x => x.Id == id).ExecuteDeleteAsync(ct);

    /// <summary>Erasure helper: removes the entry of an address (used when a person asks to be forgotten).</summary>
    public static Task<int> DeleteByEmailAsync(TrazoDbContext db, string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        return db.Set<WaitlistEntry>().Where(x => x.Email == normalized).ExecuteDeleteAsync(ct);
    }
}
