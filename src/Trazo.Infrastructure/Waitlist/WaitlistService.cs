using System.Net.Mail;
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
    public static readonly string[] Languages = ["es", "en"];

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

    /// <summary>Erasure helper: removes the entry of an address (used when a person asks to be forgotten).</summary>
    public static Task<int> DeleteByEmailAsync(TrazoDbContext db, string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        return db.Set<WaitlistEntry>().Where(x => x.Email == normalized).ExecuteDeleteAsync(ct);
    }
}
