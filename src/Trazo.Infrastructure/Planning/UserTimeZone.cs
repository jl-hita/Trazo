using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Planning;

/// <summary>
/// Decides which time zone a request is evaluated in, so "today" and "the current week" follow the user and not the
/// server. Order: the zone the client sent with this request (<c>X-Time-Zone</c> header), then the zone saved in the
/// profile, then <see cref="UserTimeZone.DefaultId"/>. An invalid value is never an error: it is skipped.
/// </summary>
public interface IUserTimeZone
{
    /// <summary>Zone sent by the client with the current request; set by the API for the plan routes.</summary>
    string? RequestOverride { get; set; }

    Task<TimeZoneInfo> ResolveAsync(Guid userId, CancellationToken ct);
}

public sealed partial class UserTimeZone(TrazoDbContext db) : IUserTimeZone
{
    public const string DefaultId = "Europe/Madrid";

    // IANA identifiers only (Region/City, UTC): Windows names such as "Romance Standard Time" are not part of the contract.
    [GeneratedRegex(@"^(UTC|[A-Za-z][A-Za-z_]*(/[A-Za-z0-9_+\-]+){1,2})$")]
    private static partial Regex IanaShape();

    public string? RequestOverride { get; set; }

    /// <summary>Validates an IANA identifier against the zones this server knows.</summary>
    public static bool TryParse(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        var value = id?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 64 || !IanaShape().IsMatch(value)) return false;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(value); return true; }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
    }

    public async Task<TimeZoneInfo> ResolveAsync(Guid userId, CancellationToken ct)
    {
        if (TryParse(RequestOverride, out var requested)) return requested;
        var saved = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.TimeZoneId).SingleOrDefaultAsync(ct);
        if (TryParse(saved, out var profile)) return profile;
        return TryParse(DefaultId, out var fallback) ? fallback : TimeZoneInfo.Utc;
    }
}
