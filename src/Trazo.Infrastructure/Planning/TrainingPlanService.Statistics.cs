using Microsoft.EntityFrameworkCore;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;

namespace Trazo.Infrastructure.Planning;

/// <summary>Progress per muscle and per block (docs/progress-statistics.md). Read-only.</summary>
public sealed partial class TrainingPlanService
{
    public async Task<ProgressView> GetProgressAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var zone = await zones.ResolveAsync(userId, ct);
        var rows = await (from set in db.WorkoutSetLogs.AsNoTracking()
                          join log in db.WorkoutLogs.AsNoTracking() on set.WorkoutLogId equals log.Id
                          where log.UserId == userId && set.Completed
                          select new { set.ExerciseId, LogId = log.Id, LoggedAt = log.PerformedAt, set.WeightKg, set.Repetitions, set.SetNumber, set.SegmentIndex }).ToListAsync(ct);
        var ids = rows.Select(r => r.ExerciseId).Distinct().ToList();
        var catalog = await db.ExerciseCatalog.AsNoTracking().Where(e => ids.Contains(e.Id))
            .Select(e => new { e.Id, e.NameEs, e.PrimaryMuscles, e.EffectiveSecondaryMuscles }).ToListAsync(ct);
        var info = catalog.ToDictionary(e => e.Id, e => new ProgressExerciseInfo(e.NameEs, e.PrimaryMuscles, e.EffectiveSecondaryMuscles));
        var plan = await ActivePlanAsync(userId, ct);
        return ProgressCalculator.Build(
            rows.Select(r => new ProgressSetRow(r.ExerciseId, r.LogId, r.LoggedAt, r.WeightKg, r.Repetitions, r.SetNumber, r.SegmentIndex)).ToList(),
            info, zone, Today, plan?.SessionsPerPeriod ?? 0, plan is null ? null : await ProgramOfAsync(plan, ct));
    }

    /// <summary>What the active plan asks of each muscle, to set beside what was done.</summary>
    private async Task<ProgressProgram> ProgramOfAsync(TrainingPlan plan, CancellationToken ct)
    {
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == plan.Id)
            .Select(s => new { s.Date, s.WeekNumber, s.PrescriptionJson }).ToListAsync(ct);
        var stored = await StoredStimulusAsync(ct);
        var dated = sessions.Where(s => s.Date is not null)
            .Select(s => (Date: s.Date!.Value, Exercises: PrescribedDose(s.PrescriptionJson, stored))).ToList();
        var position = await PositionAsync(plan, ct);
        var slot = SlotsOf(plan).FirstOrDefault(s => s.Contains(position.Week));
        var current = slot is null ? [] : sessions.Where(s => slot.Contains(s.WeekNumber)).ToList();
        var perSevenDays = current.Count == 0 ? []
            : LoadMetrics.PrescribedPerSevenDays(current.Select(s => PrescribedDose(s.PrescriptionJson, stored)), slot!.Weeks);
        return new ProgressProgram(dated, perSevenDays,
            DecodePriorities(plan.MusclePriorities).ToDictionary(p => p.Key.ToString(), p => p.Value.ToString()));
    }

    private static IReadOnlyList<DoseExercise> PrescribedDose(string json, IReadOnlyDictionary<string, IReadOnlyList<string>> stored) =>
        PrescriptionJson.Read(PrescriptionJson.CompleteStimulus(json, stored))
            .Select(e => new DoseExercise(e.ExerciseId, e.PrimaryMuscleGroups.Select(m => m.ToString()).ToList(), EffectiveSecondary(e), e.Sets)).ToList();

    public async Task<BlockSummariesView?> GetBlockSummariesAsync(Guid userId, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return null;
        var position = await PositionAsync(plan, ct);
        // A block that has not begun has nothing to summarise, and later weeks are never served.
        var blocks = plan.Mesocycles.Where(m => position.State != PlanTimeline.NotStarted && m.StartWeek <= position.Week)
            .OrderBy(m => m.Sequence).ToList();
        var sessions = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == plan.Id)
            .Select(s => new { s.Id, s.MesocycleId, s.PrescriptionJson }).ToListAsync(ct);
        var logs = await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.PlanId == plan.Id).ToListAsync(ct);
        var logOfSession = logs.ToDictionary(l => l.PlannedSessionId);
        var ids = logs.SelectMany(l => l.Sets).Select(s => s.ExerciseId).Distinct().ToList();
        var catalog = (await db.ExerciseCatalog.AsNoTracking().Where(e => ids.Contains(e.Id))
                .Select(e => new { e.Id, e.NameEs, e.PrimaryMuscles, e.EffectiveSecondaryMuscles }).ToListAsync(ct))
            .ToDictionary(e => e.Id);
        var stored = await StoredStimulusAsync(ct);

        var facts = blocks.Select(block =>
        {
            var own = sessions.Where(s => s.MesocycleId == block.Id).ToList();
            var done = own.Where(s => logOfSession.ContainsKey(s.Id)).Select(s => logOfSession[s.Id])
                .Where(l => l.Sets.Any(s => s.Completed)).ToList();
            var performed = done.Select(log => (IReadOnlyList<DoseExercise>)log.Sets.Where(s => s.Completed && catalog.ContainsKey(s.ExerciseId))
                .GroupBy(s => s.ExerciseId)
                .Select(g => new DoseExercise(g.Key, catalog[g.Key].PrimaryMuscles, catalog[g.Key].EffectiveSecondaryMuscles, LoggedSets.EffectiveCount(g)))
                .ToList()).ToList();
            var sets = done.SelectMany(log => log.Sets.Where(s => s.Completed)
                .Select(s => new ProgressSetRow(s.ExerciseId, log.Id, log.PerformedAt, s.WeightKg, s.Repetitions, s.SetNumber, s.SegmentIndex))).ToList();
            return new BlockFacts(block.Sequence, block.PhaseType, block.IsDeload, block.StartWeek, block.Weeks,
                own.Select(s => PrescribedDose(s.PrescriptionJson, stored)).ToList(), performed, sets);
        }).ToList();
        return new BlockSummariesView(plan.Id, BlockSummaries.Build(facts, id => catalog.TryGetValue(id, out var e) ? e.NameEs : id));
    }
}
