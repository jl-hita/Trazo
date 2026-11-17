using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Growth;

public sealed record FunnelStage(string Name, int Users, double? RateOfPrevious, double? RateOfCohort);
public sealed record FunnelGroup(string Key, int Users, int Paid);
public sealed record CohortRow(string Week, int Signups, int Paid, int Renewed);

/// <summary>The signup-to-retention funnel of a cohort of accounts created in a period. Works for every account and for referred ones alike.</summary>
public sealed record FunnelReport(
    DateTimeOffset From, DateTimeOffset To, int Cohort, IReadOnlyList<FunnelStage> Stages,
    double? TrialToPaid, double? SignupToPaid, double? SignupToTrial, double? RetainedAfterSeveralCycles,
    IReadOnlyList<FunnelGroup> ByPlan, IReadOnlyList<FunnelGroup> ByAppVersion, IReadOnlyList<FunnelGroup> ByReference,
    IReadOnlyList<CohortRow> Cohorts, int MinGroupSize);

public sealed record ReferralReport(
    DateTimeOffset From, DateTimeOffset To,
    int CodesCreated, int Shares, int LinkOpens, int InstallsAttributed, int Attributed, int Rewarded, int Voided, int Skipped,
    int RewardDaysGranted, int RewardsPending, int RewardDaysPending, int RewardsDeferred, int RewardsPromo, int IncidentCount,
    double? LinkToRegistered, double? RegisteredToPaid, int PaidMonthly, int PaidAnnual, int RenewedAtLeastOnce, int RenewedAtLeastThree,
    IReadOnlyList<FunnelGroup> TopCodes, int MinGroupSize);

public interface IGrowthAnalytics
{
    Task<FunnelReport> FunnelAsync(DateTimeOffset from, DateTimeOffset to, string? channel, string? reference, CancellationToken ct);
    Task<ReferralReport> ReferralsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

public sealed class GrowthAnalytics(TrazoDbContext db) : IGrowthAnalytics
{
    /// <summary>Breakdowns with fewer users than this are folded into "other": small groups could identify people.</summary>
    public const int MinGroup = 3;

    private static double? Rate(int part, int whole) => whole == 0 ? null : Math.Round(part / (double)whole, 4);

    public async Task<FunnelReport> FunnelAsync(DateTimeOffset from, DateTimeOffset to, string? channel, string? reference, CancellationToken ct)
    {
        var signups = (await db.GrowthEvents.AsNoTracking().Where(e => e.Name == GrowthEventNames.Signup && e.UserId != null).ToListAsync(ct))
            .Where(e => e.OccurredAt >= from && e.OccurredAt < to).ToList();
        var codes = await db.ReferralRelationships.AsNoTracking().Select(r => new { r.ReferredUserId, r.Code }).ToListAsync(ct);
        var codeOf = codes.ToDictionary(x => x.ReferredUserId, x => x.Code);

        var cohortEvents = signups.Where(e =>
            (string.IsNullOrEmpty(channel) || (channel == "referral") == codeOf.ContainsKey(e.UserId!.Value)) &&
            (string.IsNullOrEmpty(reference) || (codeOf.TryGetValue(e.UserId!.Value, out var c) && c == reference))).ToList();
        var cohort = cohortEvents.Select(e => e.UserId!.Value).ToHashSet();

        var events = (await db.GrowthEvents.AsNoTracking().Where(e => e.UserId != null).ToListAsync(ct))
            .Where(e => cohort.Contains(e.UserId!.Value)).ToList();
        HashSet<Guid> Users(string name) => events.Where(e => e.Name == name).Select(e => e.UserId!.Value).ToHashSet();
        HashSet<Guid> AtLeast(string name, int n) =>
            events.Where(e => e.Name == name).GroupBy(e => e.UserId!.Value).Where(g => g.Count() >= n).Select(g => g.Key).ToHashSet();

        // Derived from the data itself (plans and logged workouts), so it also covers accounts created before events existed.
        var planUsers = (await db.TrainingPlans.AsNoTracking().Select(p => p.UserId).Distinct().ToListAsync(ct)).Where(cohort.Contains).ToHashSet();
        var workoutUsers = (await db.WorkoutLogs.AsNoTracking().Select(l => l.UserId).Distinct().ToListAsync(ct)).Where(cohort.Contains).ToHashSet();
        var stagesDef = new (string Name, HashSet<Guid> Set)[]
        {
            (GrowthEventNames.Signup, cohort),
            (GrowthEventNames.EmailVerified, Users(GrowthEventNames.EmailVerified)),
            (GrowthEventNames.OnboardingCompleted, Users(GrowthEventNames.OnboardingCompleted)),
            (GrowthEventNames.FirstPlan, planUsers),
            (GrowthEventNames.FirstWorkout, workoutUsers),
            (GrowthEventNames.TrialStarted, Users(GrowthEventNames.TrialStarted)),
            (GrowthEventNames.FirstPurchase, Users(GrowthEventNames.FirstPurchase)),
            (GrowthEventNames.Renewed, AtLeast(GrowthEventNames.Renewed, 1)),
            (GrowthEventNames.Canceled, Users(GrowthEventNames.Canceled)),
            (GrowthEventNames.Refunded, Users(GrowthEventNames.Refunded))
        };
        var stages = new List<FunnelStage>();
        var previous = cohort.Count;
        foreach (var (name, set) in stagesDef)
        {
            stages.Add(new FunnelStage(name, set.Count, Rate(set.Count, previous), Rate(set.Count, cohort.Count)));
            if (name is not (GrowthEventNames.Canceled or GrowthEventNames.Refunded)) previous = Math.Max(set.Count, 0);
        }

        var paid = Users(GrowthEventNames.FirstPurchase);
        var trial = Users(GrowthEventNames.TrialStarted);
        var trialAndPaid = trial.Count(paid.Contains);
        var byPlan = events.Where(e => e.Name == GrowthEventNames.FirstPurchase && e.Plan != null)
            .GroupBy(e => e.Plan!).Select(g => new FunnelGroup(g.Key, g.Select(e => e.UserId).Distinct().Count(), g.Select(e => e.UserId).Distinct().Count())).ToList();
        var byVersion = Fold(cohortEvents.GroupBy(e => e.AppVersion ?? "unknown")
            .Select(g => new FunnelGroup(g.Key, g.Count(), g.Count(e => paid.Contains(e.UserId!.Value)))));
        var byRef = Fold(cohortEvents.Where(e => codeOf.ContainsKey(e.UserId!.Value)).GroupBy(e => codeOf[e.UserId!.Value])
            .Select(g => new FunnelGroup(g.Key, g.Count(), g.Count(e => paid.Contains(e.UserId!.Value)))));
        var renewed = AtLeast(GrowthEventNames.Renewed, 1);
        var weekly = cohortEvents.GroupBy(e => Week(e.OccurredAt)).OrderBy(g => g.Key)
            .Select(g => new CohortRow(g.Key, g.Count(), g.Count(e => paid.Contains(e.UserId!.Value)), g.Count(e => renewed.Contains(e.UserId!.Value)))).ToList();

        return new FunnelReport(from, to, cohort.Count, stages, Rate(trialAndPaid, trial.Count), Rate(paid.Count, cohort.Count),
            Rate(trial.Count, cohort.Count), Rate(AtLeast(GrowthEventNames.Renewed, 3).Count, paid.Count),
            byPlan, byVersion, byRef, weekly, MinGroup);
    }

    public async Task<ReferralReport> ReferralsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var events = (await db.GrowthEvents.AsNoTracking().Where(e => e.Name.StartsWith("referral_")).ToListAsync(ct))
            .Where(e => e.OccurredAt >= from && e.OccurredAt < to).ToList();
        int Count(string name) => events.Count(e => e.Name == name);

        var rels = (await db.ReferralRelationships.AsNoTracking().ToListAsync(ct)).Where(r => r.AttributedAt >= from && r.AttributedAt < to).ToList();
        var grants = (await db.PremiumGrants.AsNoTracking().Where(g => g.Kind == PremiumGrantKinds.ReferralReward).ToListAsync(ct))
            .Where(g => g.GrantedAt >= from && g.GrantedAt < to).ToList();
        var referred = rels.Select(r => r.ReferredUserId).ToHashSet();
        var renewals = (await db.GrowthEvents.AsNoTracking().Where(e => e.Name == GrowthEventNames.Renewed && e.UserId != null).ToListAsync(ct))
            .Where(e => referred.Contains(e.UserId!.Value)).GroupBy(e => e.UserId!.Value).ToDictionary(g => g.Key, g => g.Count());
        var trials = (await db.GrowthEvents.AsNoTracking().Where(e => e.Name == GrowthEventNames.TrialStarted && e.UserId != null).ToListAsync(ct))
            .Count(e => referred.Contains(e.UserId!.Value));

        var converted = rels.Where(r => r.ConvertedAt != null).ToList();
        var top = Fold(rels.GroupBy(r => r.Code).Select(g => new FunnelGroup(g.Key, g.Count(), g.Count(r => r.ConvertedAt != null)))
            .OrderByDescending(g => g.Users));
        _ = trials;
        return new ReferralReport(from, to,
            Count(GrowthEventNames.ReferralCodeCreated), Count(GrowthEventNames.ReferralShared), Count(GrowthEventNames.ReferralLinkOpened),
            Count(GrowthEventNames.ReferralInstallAttributed), rels.Count,
            rels.Count(r => r.Status == ReferralStatuses.Rewarded), rels.Count(r => r.Status == ReferralStatuses.Voided),
            rels.Count(r => r.Status == ReferralStatuses.Skipped),
            grants.Where(g => g.Status != PremiumGrantStatuses.Voided).Sum(g => g.Days),
            grants.Count(g => g.Status == PremiumGrantStatuses.Pending), grants.Where(g => g.Status == PremiumGrantStatuses.Pending).Sum(g => g.Days),
            grants.Count(g => g.Status == PremiumGrantStatuses.Deferred), grants.Count(g => g.Status == PremiumGrantStatuses.Active),
            rels.Count(r => r.Incident != null),
            Rate(rels.Count, Count(GrowthEventNames.ReferralLinkOpened)), Rate(converted.Count, rels.Count),
            converted.Count(r => r.ConversionPlan == "monthly"), converted.Count(r => r.ConversionPlan == "annual"),
            renewals.Count(kv => kv.Value >= 1), renewals.Count(kv => kv.Value >= 3), top, MinGroup);
    }

    private static List<FunnelGroup> Fold(IEnumerable<FunnelGroup> groups)
    {
        var list = groups.ToList();
        var big = list.Where(g => g.Users >= MinGroup).ToList();
        var small = list.Where(g => g.Users < MinGroup).ToList();
        if (small.Count > 0) big.Add(new FunnelGroup("other", small.Sum(g => g.Users), small.Sum(g => g.Paid)));
        return big;
    }

    private static string Week(DateTimeOffset when)
    {
        var date = when.UtcDateTime.Date;
        var monday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return monday.ToString("yyyy-MM-dd");
    }
}
