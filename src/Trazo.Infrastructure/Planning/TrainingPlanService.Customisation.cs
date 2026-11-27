using Microsoft.EntityFrameworkCore;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Training;

namespace Trazo.Infrastructure.Planning;

/// <summary>The user's own changes to a generated plan (docs/plan-customisation.md).</summary>
public sealed partial class TrainingPlanService
{
    private const string EditKind = "user_edit";
    private const string ReappliedKind = "user_edit_reapplied";
    private const string DurationKind = "session_duration";
    private const string FixedKeptNote = "Se han respetado los valores que fijaste tú.";
    private const string ManualEditorMessage = "Este plan se edita desde el editor de planes manuales.";

    public async Task<PlanResult<SessionView>> EditSessionAsync(Guid userId, Guid sessionId, ManualDay wanted, bool fromNowOn, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<SessionView>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        if (plan.Origin != TrainingPlanOrigin.Generated) return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, ManualEditorMessage);
        if (await IsLockedAsync(plan, ct)) return PlanResult<SessionView>.Fail(PlanFailure.PremiumRequired, LockedMessage);
        var session = await db.PlannedSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.PlanId == plan.Id, ct);
        if (session is null) return PlanResult<SessionView>.Fail(PlanFailure.SessionNotFound, "La sesión no existe.");
        if (session.Status != PlannedSessionStatus.Planned)
            return PlanResult<SessionView>.Fail(PlanFailure.NotAllowed, "Solo se pueden editar sesiones pendientes.");
        // An app that cannot show techniques would save the session without them.
        if (!client.Techniques && (wanted.Exercises.Any(e => e.Technique is not null) || wanted.Supersets is { Count: > 0 }
                                   || PrescriptionJson.HasTechniques(session.PrescriptionJson)))
            return PlanResult<SessionView>.Fail(PlanFailure.AppUpdateRequired, UpdateAppMessage);

        var candidates = (await LoadCandidatesAsync(ct)).Candidates;
        var catalog = candidates.ToDictionary(c => c.ExerciseId, StringComparer.OrdinalIgnoreCase);
        var current = PrescriptionJson.Read(session.PrescriptionJson);
        var excluded = ExcludedFor(plan, candidates);
        var errors = new Dictionary<string, string[]>();
        for (var i = 0; i < wanted.Exercises.Count; i++)
        {
            var exercise = wanted.Exercises[i];
            var already = current.Any(e => string.Equals(e.ExerciseId, exercise.ExerciseId, StringComparison.OrdinalIgnoreCase));
            if (!catalog.TryGetValue(exercise.ExerciseId, out var candidate))
            {
                // What the session already has stays, even if the catalog no longer offers it.
                if (!already) errors[$"exercises.{i + 1}"] = ["El ejercicio no existe en el catálogo."];
            }
            // The user chooses freely, but never something the profile rules out (injuries or exercises they excluded).
            else if (!already && excluded.Contains(candidate.ExerciseId))
                errors[$"exercises.{i + 1}"] = ["Ese ejercicio no es compatible con tu perfil."];
            else if (exercise.Technique is { } technique && !(candidate.AllowedTechniques?.Contains(technique.Type) ?? false))
                errors[$"exercises.{i + 1}"] = [TechniqueRules.NotAdmitted];
        }
        if (errors.Count > 0)
            return PlanResult<SessionView>.Fail(PlanFailure.InvalidRequest, errors.Values.First()[0], errors);

        var change = UserAdjustments.Between(session.PrescriptionJson, wanted);
        if (change.IsEmpty) return PlanResult<SessionView>.Ok(ToSessionView(session, null));

        var now = clock.GetUtcNow();
        var targets = new List<PlannedSession> { session };
        var layer = change;
        if (fromNowOn)
        {
            var title = session.Title;
            targets.AddRange(await db.PlannedSessions.Where(s => s.PlanId == plan.Id && s.Title == title && s.Id != session.Id
                && s.Status == PlannedSessionStatus.Planned && s.SequenceIndex > session.SequenceIndex).ToListAsync(ct));
            var stored = await db.PlanSessionAdjustments.FirstOrDefaultAsync(a => a.PlanId == plan.Id && a.SessionTitle == title, ct);
            var merged = UserAdjustments.Merge(
                stored is null ? SessionAdjustment.None : UserAdjustments.Read(stored.AdjustmentJson), change,
                wanted.Exercises.Select(e => e.ExerciseId).ToList());
            if (stored is null)
                db.PlanSessionAdjustments.Add(new PlanSessionAdjustment
                {
                    Id = Guid.NewGuid(), PlanId = plan.Id, SessionTitle = title, AdjustmentJson = UserAdjustments.Serialize(merged),
                    CreatedAt = now, UpdatedAt = now
                });
            else
            {
                stored.AdjustmentJson = UserAdjustments.Serialize(merged);
                stored.UpdatedAt = now;
            }
        }

        var weeks = plan.Mesocycles.ToDictionary(m => m.Id);
        var changes = new List<SessionChange>();
        foreach (var target in targets)
        {
            // The session being edited takes the change as asked; the later ones keep their easier weeks easy.
            var light = target.Id != session.Id && weeks.TryGetValue(target.MesocycleId, out var block) && IsLight(block, target);
            Apply(target, layer, candidates, excluded, light, now, changes);
        }
        if (changes.Count == 0) return PlanResult<SessionView>.Ok(ToSessionView(session, null));
        db.PlanAdaptations.Add(Traced(new PlanAdaptation
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Kind = EditKind,
            ExerciseIds = change.Removed.Concat(change.Pins.Select(p => p.ExerciseId)).Distinct().ToArray(),
            SessionsChanged = changes.Count, AppliedAt = now,
            Explanation = fromNowOn ? "El usuario editó esta sesión y sus próximas repeticiones." : "El usuario editó esta sesión."
        }, PrescriptionSources.User, changes));
        if (!await SaveUnlessConflictAsync(ct)) return PlanResult<SessionView>.Fail(PlanFailure.ConcurrentModification, ConcurrentModificationMessage);
        return PlanResult<SessionView>.Ok(ToSessionView(session, null));
    }

    public async Task<PlanResult<PlanView>> UpdateSessionMinutesAsync(Guid userId, int minutes, CancellationToken ct)
    {
        await UseUserClockAsync(userId, ct);
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<PlanView>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        if (plan.Origin != TrainingPlanOrigin.Generated) return PlanResult<PlanView>.Fail(PlanFailure.NotAllowed, ManualEditorMessage);
        if (await IsLockedAsync(plan, ct)) return PlanResult<PlanView>.Fail(PlanFailure.PremiumRequired, LockedMessage);
        if (minutes is < 35 or > 180)
            return PlanResult<PlanView>.Fail(PlanFailure.InvalidRequest, "La duración máxima de sesión debe estar entre 35 y 180 minutos.",
                new Dictionary<string, string[]> { ["minutes"] = ["La duración máxima de sesión debe estar entre 35 y 180 minutos."] });
        if (plan.MaxSessionMinutes != minutes)
        {
            // The block must still be possible to generate with the new time, or the plan would stop at the next one.
            var candidates = (await LoadCandidatesAsync(ct)).Candidates;
            var slot = SlotsOf(plan).FirstOrDefault(s => plan.Mesocycles.All(m => m.Sequence != s.Sequence)) ?? SlotsOf(plan).Last();
            var check = MesocycleGenerator.Generate(ProfileOf(plan) with { MaxSessionMinutes = minutes }, plan.Seed, slot, candidates,
                await LastBlockExerciseIdsAsync(plan, ct));
            if (!check.Succeeded)
                return PlanResult<PlanView>.Fail(PlanFailure.GenerationFailed, "No se pudo generar un plan con ese perfil.",
                    new Dictionary<string, string[]> { ["plan"] = check.Errors.ToArray() });
            plan.MaxSessionMinutes = minutes;
            plan.UpdatedAt = clock.GetUtcNow();
            db.PlanAdaptations.Add(Traced(new PlanAdaptation
            {
                Id = Guid.NewGuid(), PlanId = plan.Id, Kind = DurationKind, ExerciseIds = [], SessionsChanged = 0, AppliedAt = plan.UpdatedAt,
                Explanation = "El usuario cambió la duración máxima de las sesiones."
            }, PrescriptionSources.User, []));
            if (!await SaveUnlessConflictAsync(ct)) return PlanResult<PlanView>.Fail(PlanFailure.ConcurrentModification, ConcurrentModificationMessage);
        }
        return PlanResult<PlanView>.Ok((await GetCurrentAsync(userId, ct))!);
    }

    public async Task<PlanResult<bool>> ClearAdjustmentsAsync(Guid userId, CancellationToken ct)
    {
        var plan = await ActivePlanAsync(userId, ct);
        if (plan is null) return PlanResult<bool>.Fail(PlanFailure.NoActivePlan, "No tienes un plan activo.");
        // Sessions already written keep what they have: only the blocks generated from now on go back to the engine's proposal.
        db.PlanSessionAdjustments.RemoveRange(await db.PlanSessionAdjustments.Where(a => a.PlanId == plan.Id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        return PlanResult<bool>.Ok(true);
    }

    private static HashSet<string> ExcludedFor(TrainingPlan plan, IReadOnlyCollection<TrainingExerciseCandidate> candidates) =>
        plan.ExcludedExerciseIds.Concat(LimitationRules.ExcludedExerciseIds(plan.Limitations, candidates))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool IsLight(TrainingMesocycle block, PlannedSession session) =>
        block.IsDeload || block.PhaseType == PreparationPhase.PhaseType || PrescriptionJson.IsAdaptationDeload(session.PrescriptionJson);

    private static void Apply(
        PlannedSession session, SessionAdjustment adjustment, IReadOnlyCollection<TrainingExerciseCandidate> candidates,
        IReadOnlySet<string> excluded, bool light, DateTimeOffset now, List<SessionChange> changes)
    {
        var adjusted = UserAdjustments.Apply(session.PrescriptionJson, adjustment, candidates, excluded, light);
        if (adjusted.Json == session.PrescriptionJson) return;
        var before = session.PrescriptionJson;
        session.PrescriptionJson = adjusted.Json;
        session.EstimatedDurationMinutes = Math.Clamp(
            session.EstimatedDurationMinutes + (int)Math.Round(adjusted.ExtraSeconds / 60.0), 10, 240);
        session.UpdatedAt = now;
        changes.Add(Modified(session, before, adjusted.Json));
    }

    /// <summary>
    /// Puts the user's lasting changes on a block that has just been generated. The engine's own proposal stays as the original
    /// prescription of each session, and the change is recorded as the user's.
    /// </summary>
    private async Task ApplyAdjustmentsAsync(
        TrainingPlan plan, TrainingMesocycle block, IReadOnlyCollection<TrainingExerciseCandidate> candidates, DateTimeOffset now, CancellationToken ct)
    {
        var stored = await db.PlanSessionAdjustments.AsNoTracking().Where(a => a.PlanId == plan.Id).ToListAsync(ct);
        if (stored.Count == 0) return;
        var excluded = ExcludedFor(plan, candidates);
        var changes = new List<SessionChange>();
        var exercises = new HashSet<string>(StringComparer.Ordinal);
        foreach (var session in block.Sessions)
        {
            if (stored.FirstOrDefault(a => a.SessionTitle == session.Title) is not { } own) continue;
            var adjustment = UserAdjustments.Read(own.AdjustmentJson);
            var count = changes.Count;
            Apply(session, adjustment, candidates, excluded, IsLight(block, session), now, changes);
            if (changes.Count > count) exercises.UnionWith(adjustment.Removed.Concat(adjustment.Pins.Select(p => p.ExerciseId)));
        }
        if (changes.Count == 0) return;
        db.PlanAdaptations.Add(Traced(new PlanAdaptation
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, Kind = ReappliedKind, ExerciseIds = exercises.Order(StringComparer.Ordinal).ToArray(),
            SessionsChanged = changes.Count, AppliedAt = now, Explanation = "Se aplicaron tus cambios al bloque nuevo."
        }, PrescriptionSources.User, changes));
    }
}
