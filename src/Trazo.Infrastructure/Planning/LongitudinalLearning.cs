using Microsoft.EntityFrameworkCore;
using Trazo.Application.Planning;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;

namespace Trazo.Infrastructure.Planning;

/// <summary>What the longitudinal layer hands to the generator for one block, and the audit rows to store with that block.</summary>
public sealed record LongitudinalProposal(
    IReadOnlyDictionary<TrainingMuscleGroup, decimal> LearnedTargets, IReadOnlyList<LongitudinalDecision> Decisions)
{
    public static readonly LongitudinalProposal None = new(new Dictionary<TrainingMuscleGroup, decimal>(), []);
}

/// <summary>
/// Connects the pure longitudinal engine to the database, only when a block is generated (never per request):
/// 1. refreshes the observations whose logs changed since they were built (idempotent: a period is rebuilt from its logs and its row is
///    updated, so a session is never counted twice and a late registration only makes its own period stale);
/// 2. reads, for each muscle, how the finished blocks of THIS plan went (falling performance with fatigue or not);
/// 3. decides the weekly target of each muscle for the new block: it can only lower it, or give a reduction back, never raise it.
/// It adds the observation and decision rows to the context but does not save: the caller saves them together with the new block, in
/// the same transaction, so a block and the decisions behind it are stored or lost together. Data of one user is never read for another.
/// </summary>
public static class LongitudinalLearning
{
    public static async Task<LongitudinalProposal> ProposeAsync(
        TrazoDbContext db, TrainingPlan plan, MesocycleSlot slot, PlanProfile profile, DateTimeOffset now, CancellationToken ct)
    {
        // Only generated hypertrophy plans learn, and a deload block is a pause and a preparation block is a deliberate low volume: nothing is learned or changed for them.
        if (plan.Origin != TrainingPlanOrigin.Generated || profile.Goal != TrainingGoal.Hypertrophy || slot.IsDeload || slot.IsPreparation)
            return LongitudinalProposal.None;
        var baselines = MesocycleGenerator.Baselines(profile);
        if (baselines.Count == 0) return LongitudinalProposal.None;

        // The rows just built are tracked but not saved yet, so they come from the refresh, not from a new query.
        var observations = await RefreshObservationsAsync(db, plan.UserId, now, ct);
        // Only this plan's own earlier decisions count: a plan never inherits a decision taken in another one.
        var applied = await db.LongitudinalDecisions.AsNoTracking().Where(d => d.PlanId == plan.Id).ToListAsync(ct);
        var result = LongitudinalEngine.Propose(
            plan.Id, slot.Sequence, baselines, observations,
            applied.Select(d => new AppliedTarget(d.MesocycleSequence, d.Muscle, d.ProposedSets)).ToList());

        var decisions = new List<LongitudinalDecision>();
        foreach (var decision in result.Decisions)
        {
            if (applied.Any(d => d.Muscle == decision.Muscle && d.MesocycleSequence == slot.Sequence)) continue;
            decisions.Add(new LongitudinalDecision
            {
                Id = Guid.NewGuid(), UserId = plan.UserId, PlanId = plan.Id, MesocycleSequence = slot.Sequence, Muscle = decision.Muscle,
                Action = decision.Action.ToString().ToLowerInvariant(), BaselineSets = decision.BaselineSets,
                PreviousSets = decision.PreviousSets, ProposedSets = decision.ProposedSets,
                AnalyzedFromPeriod = decision.AnalyzedFromPeriod, AnalyzedToPeriod = decision.AnalyzedToPeriod,
                SignalsJson = decision.SignalsJson, Reason = Truncate(decision.Reason, 600), PolicyVersion = LongitudinalPolicy.Version, CreatedAt = now
            });
        }
        return new LongitudinalProposal(result.LearnedTargets, decisions);
    }

    /// <summary>
    /// Rebuilds the observations of every period of the user's plans whose logs are newer than what the stored rows were built from, plus
    /// the periods that fell between two trained ones with no workout at all (a skipped week is evidence of irregularity). Rows are
    /// updated in place; the context is left with the changes tracked and the full list of rows is returned.
    /// </summary>
    private static async Task<List<MuscleWeekObservation>> RefreshObservationsAsync(TrazoDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var stored = await db.MuscleWeekObservations.Where(o => o.UserId == userId).ToListAsync(ct);
        var watermark = stored.Count == 0 ? DateTimeOffset.MinValue : stored.Max(o => o.ProcessedThrough);
        var heads = await db.WorkoutLogs.AsNoTracking().Where(l => l.UserId == userId)
            .Select(l => new { l.Id, l.PlanId, l.PlannedSessionId, l.LoggedAt }).ToListAsync(ct);
        if (heads.Count == 0) return stored;
        var dirtyBySession = heads.Where(h => h.LoggedAt > watermark).Select(h => h.PlannedSessionId).ToHashSet();

        var plansWithLogs = heads.Select(h => h.PlanId).Distinct().ToList();
        List<WorkoutLog>? allLogs = null;
        foreach (var planId in plansWithLogs)
        {
            var sessions = await db.PlannedSessions.AsNoTracking().Where(s => s.PlanId == planId).ToListAsync(ct);
            var loggedSessionIds = heads.Where(h => h.PlanId == planId).Select(h => h.PlannedSessionId).ToHashSet();
            var loggedPeriods = sessions.Where(s => loggedSessionIds.Contains(s.Id)).Select(s => s.WeekNumber).ToHashSet();
            if (loggedPeriods.Count == 0) continue;
            var dirty = sessions.Where(s => dirtyBySession.Contains(s.Id)).Select(s => s.WeekNumber).ToHashSet();
            var haveRows = stored.Where(o => o.PlanId == planId).Select(o => o.Period).ToHashSet();
            // Periods up to the last trained one that have no row yet (including weeks with no workout at all).
            var last = loggedPeriods.Max();
            var missing = sessions.Select(s => s.WeekNumber).Where(week => week <= last && !haveRows.Contains(week));
            var periods = dirty.Concat(missing).Distinct().OrderBy(p => p).ToList();
            if (periods.Count == 0) continue;

            allLogs ??= await db.WorkoutLogs.AsNoTracking().Include(l => l.Sets).Where(l => l.UserId == userId).ToListAsync(ct);
            var mesocycles = await db.TrainingMesocycles.AsNoTracking().Where(m => m.PlanId == planId)
                .ToDictionaryAsync(m => m.Id, m => (m.Sequence, m.IsDeload), ct);
            var preparation = (await db.TrainingMesocycles.AsNoTracking().Where(m => m.PlanId == planId && m.PhaseType == PreparationPhase.PhaseType)
                .Select(m => m.Sequence).ToListAsync(ct)).ToHashSet();
            var built = MuscleWeekObserver.Build(new ObserverInput(userId, planId, sessions, allLogs, mesocycles, periods, now, preparation));
            foreach (var row in built)
            {
                var existing = stored.FirstOrDefault(o => o.PlanId == row.PlanId && o.Period == row.Period && o.Muscle == row.Muscle);
                if (existing is null)
                {
                    db.MuscleWeekObservations.Add(row);
                    stored.Add(row);
                }
                else existing.CopyFrom(row);
            }
        }
        return stored;
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];
}
