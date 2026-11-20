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
    string Muscle, string Quality, int Count, int PrescribedSets, int PerformedSets,
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
    IReadOnlyList<IntelligenceDecisionSummary> Decisions);

public interface IIntelligenceSummaryService
{
    Task<IntelligenceSummary> GetSummaryAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
}

/// <summary>
/// Owns the Intelligence summary query and its aggregation rules. API endpoints remain transport adapters only.
/// </summary>
public sealed class IntelligenceSummaryService(TrazoDbContext db, TimeProvider clock) : IIntelligenceSummaryService
{
    public async Task<IntelligenceSummary> GetSummaryAsync(
        DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var period = IntelligencePeriod.Resolve(from, to, clock);
        var start = period.From;
        var end = period.To;

        var plans = await db.TrainingPlans.AsNoTracking()
            .Where(x => x.CreatedAt >= start && x.CreatedAt < end)
            .Select(x => new { x.Id, x.Origin, x.EngineVersion })
            .ToListAsync(cancellationToken);

        // PlannedSession has no immutable creation timestamp. Use a defensible period cohort:
        // sessions from plans created in-range, plus sessions actually trained in-range.
        var createdPlanIds = plans.Select(x => x.Id).ToArray();
        var trainedSessionIds = await db.WorkoutLogs.AsNoTracking()
            .Where(x => x.PerformedAt >= start && x.PerformedAt < end)
            .Select(x => x.PlannedSessionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var sessions = await db.PlannedSessions.AsNoTracking()
            .Where(x => createdPlanIds.Contains(x.PlanId) || trainedSessionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.OriginalPrescriptionJson })
            .ToListAsync(cancellationToken);

        var workouts = await db.WorkoutLogs.AsNoTracking()
            .Where(x => x.PerformedAt >= start && x.PerformedAt < end)
            .Select(x => new { x.Outcome })
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
                plans.Count,
                plans.Count(x => x.Origin == TrainingPlanOrigin.Generated),
                plans.Count(x => x.Origin == TrainingPlanOrigin.Manual),
                plans.Select(x => x.EngineVersion)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToArray()!),
            new IntelligencePrescriptionSummary(
                sessions.Count,
                sessions.Count(x => x.OriginalPrescriptionJson != null),
                sessions.Count(x => x.OriginalPrescriptionJson == null)),
            new IntelligenceWorkoutSummary(
                workouts.Count,
                workouts.Count(x => x.Outcome == WorkoutOutcome.Completed),
                workouts.Count(x => x.Outcome == WorkoutOutcome.Partial),
                workouts.Count(x => x.Outcome == WorkoutOutcome.NotCompleted)),
            adaptations,
            observations,
            decisions);
    }
}
