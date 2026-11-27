using Microsoft.EntityFrameworkCore;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;

namespace Trazo.Infrastructure.Planning;

/// <summary>What is worth reviewing in the active plan (docs/recommendations.md). Read-only.</summary>
public sealed partial class TrainingPlanService
{
    public async Task<RecommendationsView?> GetRecommendationsAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return null;
        if (!await subscriptions.HasPremiumAsync(userId, ct)) return new([], Locked: true);

        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.PlanId == plan.Id).ToListAsync(ct);
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == plan.Id)
            .Select(s => new { s.Id, s.MesocycleId, s.SequenceIndex, s.Status, s.Date, s.WeekNumber, s.PrescriptionJson }).ToListAsync(ct);
        var blocks = plan.Mesocycles.ToDictionary(m => m.Id);
        var training = sessions.ToDictionary(s => s.Id,
            s => blocks.TryGetValue(s.MesocycleId, out var b) && !b.IsDeload && b.PhaseType != PreparationPhase.PhaseType
                 && !PrescriptionJson.IsAdaptationDeload(s.PrescriptionJson));

        var exposures = logs.SelectMany(log => log.Sets.GroupBy(s => s.ExerciseId).Select(g => new ExerciseExposure(
            g.Key, log.PerformedAt,
            g.Where(s => s.Completed && s.SegmentIndex == 0 && s.WeightKg is > 0 && s.Repetitions is not null)
                .Select(s => ProgressCalculator.EstimatedOneRepMax(s.WeightKg!.Value, s.Repetitions!.Value)).Max(),
            g.Any(LoggedSets.HasPain), training.GetValueOrDefault(log.PlannedSessionId)))).ToList();

        var pending = sessions.Where(s => s.Status == PlannedSessionStatus.Planned).OrderBy(s => s.SequenceIndex)
            .Select(s => (s.Id, Exercises: PrescriptionJson.Read(s.PrescriptionJson).Select(e => (e.ExerciseId, e.ExerciseNameSnapshot)).ToList()))
            .ToList();
        var names = pending.SelectMany(s => s.Exercises).GroupBy(e => e.ExerciseId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ExerciseNameSnapshot, StringComparer.OrdinalIgnoreCase);

        var adherence = await RecentAdherenceAsync(plan, logs, sessions.Select(s => (s.WeekNumber, s.PrescriptionJson)).ToList(), ct);
        return new(Recommendations.Build(
            exposures, adherence, T,
            id => NameOf(id, names.GetValueOrDefault(id) ?? id),
            id => pending.Where(s => s.Exercises.Any(e => string.Equals(e.ExerciseId, id, StringComparison.OrdinalIgnoreCase)))
                .Select(s => (Guid?)s.Id).FirstOrDefault()));
    }

    /// <summary>
    /// Asked against done over the last two weeks, in effective sets. Only for somebody who has been training this plan for
    /// longer than that and did train in the window: an absence is handled elsewhere.
    /// </summary>
    private async Task<RecentAdherence?> RecentAdherenceAsync(
        TrainingPlan plan, IReadOnlyList<WorkoutLog> logs, IReadOnlyList<(int Week, string Json)> sessions, CancellationToken ct)
    {
        var since = clock.GetUtcNow().AddDays(-Recommendations.AdherenceDays);
        var recent = logs.Where(l => l.PerformedAt > since).ToList();
        if (recent.Count == 0 || logs.All(l => l.PerformedAt > since)) return null;
        var position = await PositionAsync(plan, ct);
        var slot = SlotsOf(plan).FirstOrDefault(s => s.Contains(position.Week));
        if (slot is null || slot.IsDeload) return null;
        var stored = await StoredStimulusAsync(ct);
        var weekly = sessions.Where(s => slot.Contains(s.Week)).SelectMany(s => PrescribedDose(s.Json, stored)).Sum(e => e.Sets) / slot.Weeks;
        var done = recent.Sum(l => LoggedSets.EffectiveCount(l.Sets));
        return new(Math.Round(weekly * Recommendations.AdherenceDays / 7m, 1), done);
    }
}
