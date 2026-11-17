using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Infrastructure.Growth;

public sealed record ReferralStats(
    int Invited, int Pending, int Converted, int Rewards, int DaysEarned, int DaysPending, int DaysDeferred);

public sealed record ReferralInvitationView(
    Guid Id, DateTimeOffset RegisteredAt, string Status, DateTimeOffset? ConvertedAt, string? Plan, int? RewardDays, string? RewardState);

public sealed record PremiumGrantView(
    Guid Id, string Kind, int Days, string Status, DateTimeOffset GrantedAt, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    DateTimeOffset? GoogleExpiryAfter);

public interface IReferralService
{
    /// <summary>The user's permanent code, created on first use.</summary>
    Task<ReferralCode> GetOrCreateCodeAsync(Guid userId, CancellationToken ct);

    /// <summary>
    /// Links a newly created account to the person who invited them. Only called when the account is created, never later, so an
    /// existing account can never become a referral. Returns false (and changes nothing) for an unknown code, a disabled or the
    /// same person, a repeated attribution, or when the programme is off. The code never decides anything economic by itself.
    /// </summary>
    Task<bool> AttributeAsync(Guid newUserId, string newUserEmail, string? code, string? source, string? appVersion, CancellationToken ct);

    Task<ReferralStats> StatsAsync(Guid referrerId, CancellationToken ct);
    Task<IReadOnlyList<ReferralInvitationView>> InvitationsAsync(Guid referrerId, string? status, CancellationToken ct);
    Task<IReadOnlyList<PremiumGrantView>> GrantsAsync(Guid userId, CancellationToken ct);
}

public sealed class ReferralService(TrazoDbContext db, IRuntimeSettings settings, IGrowthEvents events, TimeProvider clock,
    ILogger<ReferralService> logger) : IReferralService
{
    /// <summary>No 0/O or 1/I/L: codes are read aloud and typed by hand.</summary>
    public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
    public const int CodeLength = 8;

    public static string NewCode()
    {
        Span<char> chars = stackalloc char[CodeLength];
        for (var i = 0; i < CodeLength; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>Upper-cases and strips spaces and dashes; null when the text cannot be a code.</summary>
    public static string? NormalizeCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 20) return null;
        var code = new string(raw.Where(c => c is not (' ' or '-' or '_')).Select(char.ToUpperInvariant).ToArray());
        return code.Length == CodeLength && code.All(c => Alphabet.Contains(c)) ? code : null;
    }

    /// <summary>
    /// The address with the usual aliases removed (dots and "+tag" on Gmail, "+tag" elsewhere), to detect the same person
    /// inviting themselves from a second account. It is a proportionate check, not a guarantee.
    /// </summary>
    public static string CanonicalEmail(string email)
    {
        var text = email.Trim().ToLowerInvariant();
        var at = text.LastIndexOf('@');
        if (at <= 0) return text;
        var local = text[..at];
        var domain = text[(at + 1)..];
        var plus = local.IndexOf('+');
        if (plus > 0) local = local[..plus];
        if (domain is "gmail.com" or "googlemail.com") { local = local.Replace(".", ""); domain = "gmail.com"; }
        return local + "@" + domain;
    }

    public async Task<ReferralCode> GetOrCreateCodeAsync(Guid userId, CancellationToken ct)
    {
        var existing = await db.ReferralCodes.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
        if (existing is not null) return existing;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var row = new ReferralCode { Id = Guid.NewGuid(), UserId = userId, Code = NewCode(), CreatedAt = clock.GetUtcNow() };
            db.ReferralCodes.Add(row);
            try
            {
                await db.SaveChangesAsync(ct);
                await events.RecordAsync(GrowthEventNames.ReferralCodeCreated, userId, new GrowthEventInfo(Reference: row.Code, DedupeKey: $"code:{userId:N}"), ct);
                return row;
            }
            catch (DbUpdateException)
            {
                // Either another request created this user's code (unique user) or the random code collided (unique code).
                db.Entry(row).State = EntityState.Detached;
                existing = await db.ReferralCodes.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId, ct);
                if (existing is not null) return existing;
            }
        }
        throw new InvalidOperationException("Could not generate a unique referral code.");
    }

    public async Task<bool> AttributeAsync(Guid newUserId, string newUserEmail, string? code, string? source, string? appVersion, CancellationToken ct)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null) return false;
        if (!(await settings.GetPromotionsAsync(ct)).ReferralsEnabled) return false;

        var owner = await db.ReferralCodes.AsNoTracking().SingleOrDefaultAsync(x => x.Code == normalized, ct);
        if (owner is null || owner.UserId == newUserId) return false;
        var referrer = await db.Users.AsNoTracking().Where(u => u.Id == owner.UserId)
            .Select(u => new { u.Email, u.DisabledAt }).SingleOrDefaultAsync(ct);
        if (referrer is null || referrer.DisabledAt is not null) return false;
        if (CanonicalEmail(referrer.Email) == CanonicalEmail(newUserEmail))
        {
            logger.LogInformation("A referral code was ignored because both accounts use the same mailbox.");
            return false;
        }
        // Defence in depth: attribution belongs to account creation. A user older than a few minutes is an existing account.
        var createdAt = await db.Users.AsNoTracking().Where(u => u.Id == newUserId).Select(u => u.CreatedAt).SingleOrDefaultAsync(ct);
        if (createdAt == default || clock.GetUtcNow() - createdAt > TimeSpan.FromMinutes(10)) return false;

        var now = clock.GetUtcNow();
        var row = new ReferralRelationship
        {
            Id = Guid.NewGuid(), ReferrerUserId = owner.UserId, ReferredUserId = newUserId, Code = normalized,
            Source = ReferralSources.Normalize(source), Status = ReferralStatuses.Registered, AttributedAt = now,
            AppVersion = string.IsNullOrWhiteSpace(appVersion) ? null : appVersion.Trim()[..Math.Min(appVersion.Trim().Length, 40)], UpdatedAt = now
        };
        db.ReferralRelationships.Add(row);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached; // already attributed: the first referrer stays
            return false;
        }
        await events.RecordAsync(GrowthEventNames.ReferralAttributed, newUserId,
            new GrowthEventInfo(Channel: "referral", Reference: normalized, AppVersion: row.AppVersion, DedupeKey: $"attributed:{newUserId:N}"), ct);
        return true;
    }

    public async Task<ReferralStats> StatsAsync(Guid referrerId, CancellationToken ct)
    {
        var rels = await db.ReferralRelationships.AsNoTracking().Where(r => r.ReferrerUserId == referrerId).ToListAsync(ct);
        var grants = await db.PremiumGrants.AsNoTracking()
            .Where(g => g.UserId == referrerId && g.Kind == PremiumGrantKinds.ReferralReward).ToListAsync(ct);
        int Sum(string s) => grants.Where(g => g.Status == s).Sum(g => g.Days);
        return new ReferralStats(
            rels.Count, rels.Count(r => r.Status == ReferralStatuses.Registered), rels.Count(r => r.Status == ReferralStatuses.Rewarded),
            grants.Count(g => g.Status != PremiumGrantStatuses.Voided),
            Sum(PremiumGrantStatuses.Active) + Sum(PremiumGrantStatuses.Deferred), Sum(PremiumGrantStatuses.Pending), Sum(PremiumGrantStatuses.Deferred));
    }

    public async Task<IReadOnlyList<ReferralInvitationView>> InvitationsAsync(Guid referrerId, string? status, CancellationToken ct)
    {
        var rels = await db.ReferralRelationships.AsNoTracking().Where(r => r.ReferrerUserId == referrerId).ToListAsync(ct);
        var grantIds = rels.Where(r => r.RewardGrantId is not null).Select(r => r.RewardGrantId!.Value).ToList();
        var grants = await db.PremiumGrants.AsNoTracking().Where(g => grantIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        // Deliberately no name, e-mail or any identity of the invited person: only what the inviter needs to understand their rewards.
        return rels.Where(r => status is null || r.Status == status).OrderByDescending(r => r.AttributedAt)
            .Select(r => new ReferralInvitationView(r.Id, r.AttributedAt, r.Status, r.ConvertedAt, PlanKind(r.ConversionPlan), r.RewardDays,
                r.RewardGrantId is { } id && grants.TryGetValue(id, out var g) ? g.Status : null)).ToList();
    }

    public async Task<IReadOnlyList<PremiumGrantView>> GrantsAsync(Guid userId, CancellationToken ct) =>
        (await db.PremiumGrants.AsNoTracking().Where(g => g.UserId == userId).ToListAsync(ct))
        .OrderByDescending(g => g.GrantedAt)
        .Select(g => new PremiumGrantView(g.Id, g.Kind, g.Days, g.Status, g.GrantedAt, g.StartsAt, g.EndsAt, g.GoogleExpiryAfter)).ToList();

    public static string? PlanKind(string? stored) => stored is "monthly" or "annual" ? stored : null;

    /// <summary>
    /// Monthly or annual, from the length of the purchase (Google reports both plans under one subscription id, and the
    /// base-plan id is not stored): a purchase spanning 300+ days from its start is the annual plan.
    /// </summary>
    public static string PlanOf(Trazo.Domain.Billing.Subscription row) =>
        (row.ExpiresAt - row.StartedAt).TotalDays >= 300 ? "annual" : "monthly";
}
