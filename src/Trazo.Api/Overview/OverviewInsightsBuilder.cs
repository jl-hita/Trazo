using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Billing;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Overview;

/// <summary>
/// Aggregate-only product insights: activity per day, activation funnel, subscriptions to watch and engine behaviour.
/// Everything is a count or a percentage; groups smaller than <see cref="MinimumGroup"/> are hidden or merged so a
/// figure cannot point at one person (health data: pain, reasons for not training).
/// </summary>
public sealed class OverviewInsightsBuilder(TrazoDbContext db)
{
    public const int MinimumGroup = 5;
    public const int RecurringWorkouts = 3;
    public const int ChartDays = 14;
    private static readonly string[] Reasons = ["Time", "Fatigue", "Equipment", "Motivation", "Illness", "Other"];

    public async Task<OverviewInsights> BuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var activity = await BuildActivityAsync(now, ct);
        var funnel = await BuildFunnelAsync(ct);
        var subscriptions = await BuildSubscriptionsAsync(now, ct);
        var engine = await BuildEngineAsync(now, ct);
        return new OverviewInsights(activity, funnel, subscriptions, engine);
    }

    private async Task<OverviewActivity> BuildActivityAsync(DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var firstDay = today.AddDays(-(ChartDays - 1));
        var from = new DateTimeOffset(firstDay.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var signups = Bucket(await db.Users.AsNoTracking().Where(x => x.CreatedAt >= from).Select(x => x.CreatedAt).ToListAsync(ct));
        var plans = Bucket(await db.TrainingPlans.AsNoTracking().Where(x => x.CreatedAt >= from).Select(x => x.CreatedAt).ToListAsync(ct));
        var workouts = Bucket(await db.WorkoutLogs.AsNoTracking().Where(x => x.LoggedAt >= from).Select(x => x.LoggedAt).ToListAsync(ct));
        var days = Enumerable.Range(0, ChartDays).Select(i => firstDay.AddDays(i))
            .Select(d => new OverviewDay(d, signups.GetValueOrDefault(d), plans.GetValueOrDefault(d), workouts.GetValueOrDefault(d))).ToList();

        var since7 = now.AddDays(-7);
        var since30 = now.AddDays(-30);
        var cutoff = now.AddDays(-3);
        var active7 = await db.Users.AsNoTracking().CountAsync(x => x.LastLoginAt >= since7, ct);
        var active30 = await db.Users.AsNoTracking().CountAsync(x => x.LastLoginAt >= since30, ct);
        var unverified = await db.Users.AsNoTracking()
            .CountAsync(x => x.EmailVerifiedAt == null && x.GoogleSubject == null && x.CreatedAt < cutoff, ct);
        return new OverviewActivity(days, active7, active30, unverified);
    }

    private static Dictionary<DateOnly, int> Bucket(IEnumerable<DateTimeOffset> times) =>
        times.GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime)).ToDictionary(g => g.Key, g => g.Count());

    private async Task<OverviewFunnel> BuildFunnelAsync(CancellationToken ct)
    {
        var registered = await db.Users.AsNoTracking().CountAsync(ct);
        var verified = await db.Users.AsNoTracking().CountAsync(x => x.EmailVerifiedAt != null || x.GoogleSubject != null, ct);
        var withPlan = await db.TrainingPlans.AsNoTracking().Select(x => x.UserId).Distinct().CountAsync(ct);
        var perUser = await db.WorkoutLogs.AsNoTracking().GroupBy(x => x.UserId).Select(g => g.Count()).ToListAsync(ct);
        return new OverviewFunnel(registered, verified, withPlan, perUser.Count, perUser.Count(c => c >= RecurringWorkouts));
    }

    private async Task<OverviewSubscriptionWatch> BuildSubscriptionsAsync(DateTimeOffset now, CancellationToken ct)
    {
        var soon = now.AddDays(7);
        var real = db.Subscriptions.AsNoTracking().Where(x => !x.IsTestPurchase);
        var trials = await real.CountAsync(x => x.Status == SubscriptionStatus.Trial && x.ExpiresAt > now && x.ExpiresAt <= soon, ct);
        var canceling = await real.CountAsync(x => x.Status == SubscriptionStatus.Canceled && x.ExpiresAt > now && x.ExpiresAt <= soon, ct);
        var issues = await real.CountAsync(x => x.Status == SubscriptionStatus.OnHold || x.Status == SubscriptionStatus.GracePeriod, ct);
        var tests = await db.Subscriptions.AsNoTracking().CountAsync(x => x.IsTestPurchase, ct);
        return new OverviewSubscriptionWatch(trials, canceling, issues, tests);
    }

    private async Task<OverviewEngine> BuildEngineAsync(DateTimeOffset now, CancellationToken ct)
    {
        var since = now.AddDays(-30);
        var logs = await db.WorkoutLogs.AsNoTracking().Where(x => x.LoggedAt >= since)
            .Select(x => new { x.Outcome, x.NonCompletionReason, x.PainReported }).ToListAsync(ct);
        var total = logs.Count;
        int? pain = total >= MinimumGroup ? (int)Math.Round(100.0 * logs.Count(x => x.PainReported) / total) : null;

        var notDone = logs.Where(x => x.Outcome != WorkoutOutcome.Completed && x.NonCompletionReason != "None")
            .GroupBy(x => Reasons.Contains(x.NonCompletionReason) ? x.NonCompletionReason : "Other")
            .Select(g => (Name: g.Key, Count: g.Count())).ToList();
        var shown = notDone.Where(x => x.Count >= MinimumGroup).OrderByDescending(x => x.Count)
            .Select(x => new OverviewCount(x.Name, x.Count)).ToList();
        var hidden = notDone.Where(x => x.Count < MinimumGroup).Sum(x => x.Count);
        if (hidden > 0) shown.Add(new OverviewCount("Other", hidden));

        var adaptations = (await db.PlanAdaptations.AsNoTracking().Where(x => x.AppliedAt >= since)
            .GroupBy(x => x.Kind).Select(g => new OverviewCount(g.Key, g.Count())).ToListAsync(ct))
            .OrderByDescending(x => x.Count).ToList();
        return new OverviewEngine(total, logs.Count(x => x.Outcome == WorkoutOutcome.Completed),
            logs.Count(x => x.Outcome == WorkoutOutcome.Partial), logs.Count(x => x.Outcome == WorkoutOutcome.NotCompleted), pain,
            adaptations.Sum(x => x.Count), adaptations, shown);
    }
}
