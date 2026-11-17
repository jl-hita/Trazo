using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Growth;

public sealed record GrowthEventInfo(
    string? Channel = null, string? Reference = null, string? Plan = null, string? AppVersion = null,
    string? Variant = null, string? DedupeKey = null);

/// <summary>Writes funnel events (see <see cref="GrowthEventNames"/>). Never throws for a duplicate and never blocks the business operation.</summary>
public interface IGrowthEvents
{
    /// <summary>Records the event once per dedupe key. Returns false when it already existed or could not be written.</summary>
    Task<bool> RecordAsync(string name, Guid? userId, GrowthEventInfo? info, CancellationToken ct);
}

public sealed class GrowthEventRecorder(TrazoDbContext db, TimeProvider clock, ILogger<GrowthEventRecorder> logger) : IGrowthEvents
{
    public async Task<bool> RecordAsync(string name, Guid? userId, GrowthEventInfo? info, CancellationToken ct)
    {
        info ??= new GrowthEventInfo();
        try
        {
            if (info.DedupeKey is not null && await db.GrowthEvents.AnyAsync(x => x.DedupeKey == info.DedupeKey, ct)) return false;
            var channel = info.Channel;
            var reference = info.Reference;
            if (userId is not null && channel is null && reference is null)
            {
                // Referral attribution travels with the user, so every later event of a referred user carries it.
                var attribution = await ReferralAttribution.LookupAsync(db, userId.Value, ct);
                if (attribution is not null) (channel, reference) = ("referral", attribution);
            }
            db.GrowthEvents.Add(new GrowthEvent
            {
                Id = Guid.NewGuid(), Name = name, OccurredAt = clock.GetUtcNow(), UserId = userId, Channel = Cut(channel, 30),
                Reference = Cut(reference, 40), Plan = Cut(info.Plan, 200), AppVersion = Cut(info.AppVersion, 40),
                Variant = Cut(info.Variant, 40), DedupeKey = Cut(info.DedupeKey, 120)
            });
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex)
        {
            // Duplicate dedupe key raced with another writer, or the user no longer exists: analytics must not break the operation.
            foreach (var entry in db.ChangeTracker.Entries<GrowthEvent>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            logger.LogDebug(ex, "Growth event {Name} was not recorded.", name);
            return false;
        }
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > max ? value[..max] : value;
}

/// <summary>Placeholder until referral relationships exist: no attribution.</summary>
internal static class ReferralAttribution
{
    public static Task<string?> LookupAsync(TrazoDbContext db, Guid userId, CancellationToken ct) => Task.FromResult<string?>(null);
}
