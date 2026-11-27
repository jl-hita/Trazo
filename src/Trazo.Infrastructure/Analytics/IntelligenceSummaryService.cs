using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Analytics;

public sealed record IntelligencePeriod(DateTimeOffset From, DateTimeOffset To)
{
    public static IntelligencePeriod Resolve(DateTimeOffset? from, DateTimeOffset? to, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var end = to ?? clock.GetUtcNow();
        var start = from ?? end.AddDays(-90);
        if (start >= end || end - start > TimeSpan.FromDays(731))
            throw new ArgumentException("El periodo debe ser válido y no superar dos años.");
        return new IntelligencePeriod(start, end);
    }
}

public sealed record IntelligencePlanSummary(int Total, int Generated, int Manual, string[] EngineVersions);
public sealed record IntelligencePrescriptionSummary(int Sessions, int WithOriginalSnapshot, int WithoutOriginalSnapshot);
public sealed record IntelligenceWorkoutSummary(int Total, int Completed, int Partial, int NotCompleted);
public sealed record IntelligenceAdaptationSummary(string Kind, string? Source, int Count);
public sealed record IntelligenceObservationSummary(
    string Muscle, string Quality, int Count, decimal PrescribedSets, decimal PerformedSets,
    int PerformanceSamples, int FatigueSignals, double? AveragePerformanceChange);
public sealed record IntelligenceDecisionSummary(string Action, string PolicyVersion, int Count);

public sealed record IntelligenceSummary(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset GeneratedAt,
    IntelligencePlanSummary Plans,
    IntelligencePrescriptionSummary Prescriptions,
    IntelligenceWorkoutSummary Workouts,
    IReadOnlyList<IntelligenceAdaptationSummary> Adaptations,
    IReadOnlyList<IntelligenceObservationSummary> Observations,
    IReadOnlyList<IntelligenceDecisionSummary> Decisions,
    IReadOnlyList<IntelligenceEngineComparison>? EngineComparison = null);

/// <summary>
/// What people did with the blocks of one engine version, aggregated, from consenting users only; a version with too few of
/// them is not shown. Descriptive: the groups differ in more than the version, so a difference is not an effect.
/// </summary>
public sealed record IntelligenceEngineComparison(
    string EngineVersion, int Users, int SessionsPlanned, int Workouts, decimal CompletedShare, decimal PainShare, decimal UserChangedShare);

public interface IIntelligenceSummaryService
{
    Task<IntelligenceSummary> GetSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

/// <summary>
/// Owns the Intelligence summary query and its aggregation rules. API endpoints remain transport adapters only.
/// </summary>
public sealed class IntelligenceSummaryService(TrazoDbContext db, TimeProvider clock) : IIntelligenceSummaryService
{
    /// <summary>The fewest people a group needs to be shown.</summary>
    public const int MinimumUsersPerGroup = 5;

    public async Task<IntelligenceSummary> GetSummaryAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var period = IntelligencePeriod.Resolve(from, to, clock);
        var start = period.From;
        var end = period.To;

        var planOriginCounts = await db.TrainingPlans.AsNoTracking()
            .Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => x.Origin)
            .Select(g => new { Origin = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var engineVersions = await db.TrainingPlans.AsNoTracking()
            .Where(x => x.CreatedAt >= start && x.CreatedAt < end && x.EngineVersion != null && x.EngineVersion != "")
            .Select(x => x.EngineVersion!)
            .Distinct()
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken);

        // PlannedSession has no immutable creation timestamp. Use a defensible period cohort:
        // sessions from plans created in-range, plus sessions actually trained in-range.
        var createdPlanIds = db.TrainingPlans.AsNoTracking()
            .Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .Select(x => x.Id);
        var trainedSessionIds = db.WorkoutLogs.AsNoTracking()
            .Where(x => x.PerformedAt >= start && x.PerformedAt < end)
            .Select(x => x.PlannedSessionId)
            .Distinct();
        var prescriptionCounts = await db.PlannedSessions.AsNoTracking()
            .Where(x => createdPlanIds.Contains(x.PlanId) || trainedSessionIds.Contains(x.Id))
            .GroupBy(x => x.OriginalPrescriptionJson != null)
            .Select(g => new { HasOriginalSnapshot = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var workoutCounts = await db.WorkoutLogs.AsNoTracking()
            .Where(x => x.PerformedAt >= start && x.PerformedAt < end)
            .GroupBy(x => x.Outcome)
            .Select(g => new { Outcome = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var adaptations = await db.PlanAdaptations.AsNoTracking()
            .Where(x => x.AppliedAt >= start && x.AppliedAt < end)
            .GroupBy(x => new { x.Kind, x.Source })
            .Select(g => new IntelligenceAdaptationSummary(g.Key.Kind, g.Key.Source, g.Count()))
            .ToListAsync(cancellationToken);

        var observations = await db.MuscleWeekObservations.AsNoTracking()
            .Where(x => x.UpdatedAt >= start && x.UpdatedAt < end)
            .GroupBy(x => new { x.Muscle, x.Quality })
            .Select(g => new IntelligenceObservationSummary(
                g.Key.Muscle,
                g.Key.Quality,
                g.Count(),
                g.Sum(x => x.PrescribedSets),
                g.Sum(x => x.PerformedSets),
                g.Sum(x => x.PerformanceSamples),
                g.Sum(x => x.FatigueSignals),
                g.Where(x => x.PerformanceChangePercent != null)
                    .Average(x => (double?)x.PerformanceChangePercent)))
            .ToListAsync(cancellationToken);

        var decisions = await db.LongitudinalDecisions.AsNoTracking()
            .Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => new { x.Action, x.PolicyVersion })
            .Select(g => new IntelligenceDecisionSummary(g.Key.Action, g.Key.PolicyVersion, g.Count()))
            .ToListAsync(cancellationToken);

        return new IntelligenceSummary(
            start,
            end,
            clock.GetUtcNow(),
            new IntelligencePlanSummary(
                planOriginCounts.Sum(x => x.Count),
                planOriginCounts.Where(x => x.Origin == TrainingPlanOrigin.Generated).Sum(x => x.Count),
                planOriginCounts.Where(x => x.Origin == TrainingPlanOrigin.Manual).Sum(x => x.Count),
                engineVersions),
            new IntelligencePrescriptionSummary(
                prescriptionCounts.Sum(x => x.Count),
                prescriptionCounts.Where(x => x.HasOriginalSnapshot).Sum(x => x.Count),
                prescriptionCounts.Where(x => !x.HasOriginalSnapshot).Sum(x => x.Count)),
            new IntelligenceWorkoutSummary(
                workoutCounts.Sum(x => x.Count),
                workoutCounts.Where(x => x.Outcome == WorkoutOutcome.Completed).Sum(x => x.Count),
                workoutCounts.Where(x => x.Outcome == WorkoutOutcome.Partial).Sum(x => x.Count),
                workoutCounts.Where(x => x.Outcome == WorkoutOutcome.NotCompleted).Sum(x => x.Count)),
            adaptations,
            observations,
            decisions,
            await CompareEnginesAsync(start, end, cancellationToken));
    }

    private async Task<IReadOnlyList<IntelligenceEngineComparison>> CompareEnginesAsync(
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var rows = await (from block in db.TrainingMesocycles.AsNoTracking()
                          join plan in db.TrainingPlans.AsNoTracking() on block.PlanId equals plan.Id
                          join user in db.Users.AsNoTracking() on plan.UserId equals user.Id
                          join session in db.PlannedSessions.AsNoTracking() on block.Id equals session.MesocycleId
                          where user.ResearchConsent && block.EngineVersion != null && block.CreatedAt >= start && block.CreatedAt < end
                          select new
                          {
                              Version = block.EngineVersion!, plan.UserId, SessionId = session.Id,
                              Changed = session.PrescriptionJson.Contains(Trazo.Application.Planning.UserAdjustments.FixedMark)
                          }).ToListAsync(ct);
        var sessionIds = rows.Select(r => r.SessionId).ToList();
        var logs = (await db.WorkoutLogs.AsNoTracking().Where(l => sessionIds.Contains(l.PlannedSessionId))
                .Select(l => new { l.PlannedSessionId, l.Outcome, l.PainReported }).ToListAsync(ct))
            .ToDictionary(l => l.PlannedSessionId);
        return rows.GroupBy(r => r.Version)
            .Where(g => g.Select(r => r.UserId).Distinct().Count() >= MinimumUsersPerGroup)
            .Select(g =>
            {
                var done = g.Where(r => logs.ContainsKey(r.SessionId)).Select(r => logs[r.SessionId]).ToList();
                static decimal Share(int part, int whole) => whole == 0 ? 0m : Math.Round(part / (decimal)whole, 3);
                return new IntelligenceEngineComparison(
                    g.Key, g.Select(r => r.UserId).Distinct().Count(), g.Count(), done.Count,
                    Share(done.Count(l => l.Outcome == WorkoutOutcome.Completed), done.Count),
                    Share(done.Count(l => l.PainReported), done.Count), Share(g.Count(r => r.Changed), g.Count()));
            }).OrderBy(c => c.EngineVersion, StringComparer.Ordinal).ToList();
    }
}
