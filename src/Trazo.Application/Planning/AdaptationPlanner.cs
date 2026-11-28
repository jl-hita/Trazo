using Trazo.Domain.Planning;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>One set the user logged, reduced to what the engine needs.</summary>
public sealed record SetRecord(decimal? WeightKg, int? Repetitions, decimal? Rir, bool Completed, string? SkippedReason, bool Pain);

/// <summary>One exercise in one logged session, with the prescription it was done against.</summary>
public sealed record ExerciseExposureRecord(
    DateTimeOffset At,
    string ExerciseId,
    int PlannedSets,
    int MinRepetitions,
    int MaxRepetitions,
    decimal? TargetRir,
    TrainingProgressionRule Rule,
    bool ProgressionEligible,
    IReadOnlyList<SetRecord> Sets,
    WorkoutOutcome Outcome,
    string Recovery,
    int? OriginalPlannedSets = null);

public sealed record SessionRecord(
    DateTimeOffset At, WorkoutOutcome Outcome, string Reason, string Recovery, bool Pain, decimal? Rpe, decimal? CompletionRatio);


public sealed record AdaptationInput(
    IReadOnlyList<ExerciseExposureRecord> ExerciseHistory,
    IReadOnlyList<SessionRecord> SessionHistory,
    IReadOnlyCollection<string> ExercisesInLoggedSession,
    IReadOnlyList<TrainingAutoAdaptationRecord> RecentlyApplied,
    DateTimeOffset Now);

/// <summary>The engine's reading of one exercise after a session. <see cref="NextLoadKg"/> is what the next session should show.</summary>
public sealed record ExerciseAdvice(
    string ExerciseId, string Action, string Explanation, decimal? NextLoadKg, IReadOnlyList<int>? SuggestedRepetitions);

/// <summary>A change the engine decided to make by itself to the upcoming sessions.</summary>
public sealed record AutoAction(TrainingAutoAdaptationKind Kind, IReadOnlyList<string> ExerciseIds, string Explanation);

public sealed record AdaptationOutcome(
    IReadOnlyList<ExerciseAdvice> Exercises,
    string SessionAction,
    string SessionExplanation,
    IReadOnlyList<AutoAction> Actions,
    IReadOnlyList<string> Notes,
    bool PainReviewPending = false,
    IReadOnlyCollection<string>? PainExerciseIds = null)
{
    /// <summary>Exercises whose recent exposures carry a pain signal; empty when none.</summary>
    public IReadOnlyCollection<string> PainExercises => PainExerciseIds ?? [];

    /// <summary>
    /// True when no automatic INCREASE (load, repetitions or sets) may be applied to <paramref name="exerciseId"/>:
    /// either a pain report is pending review for the whole plan (general pain) or the exercise itself carries pain.
    /// </summary>
    public bool BlocksIncreaseFor(string exerciseId) =>
        PainReviewPending || PainExercises.Contains(exerciseId, StringComparer.Ordinal);
}

/// <summary>
/// Reads what the user logged and decides what changes in the upcoming sessions. Pure: it wires the engine's pieces
/// (<see cref="TrainingExerciseProgressionEngine"/>, <see cref="TrainingAdaptationPolicy"/>,
/// <see cref="TrainingExerciseVolumeAdapter"/>, <see cref="TrainingAutoAdaptationPolicy"/>) together and persists nothing.
/// Safety signals (pain) are never turned into automatic changes: they pause progression and are reported.
/// </summary>
public static class AdaptationPlanner
{
    public const int ProgressionHoldDays = 7;

    public static AdaptationOutcome Evaluate(AdaptationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var notes = new List<string>();
        var recent = input.RecentlyApplied.ToList();

        // Session-level reading (recovery, effort, completion, pain).
        var sessionDecision = TrainingAdaptationPolicy.Evaluate(input.SessionHistory.TakeLast(5).Select(ToFeedback).ToArray());

        // Exercise-level volume reading, only for the exercises of the session just logged.
        var volume = TrainingExerciseVolumeAdapter.Evaluate(
                input.ExerciseHistory.Where(e => input.ExercisesInLoggedSession.Contains(e.ExerciseId, StringComparer.Ordinal))
                    .GroupBy(e => e.ExerciseId, StringComparer.Ordinal)
                    .SelectMany(group => group.OrderBy(e => e.At).TakeLast(3))
                    .OrderBy(e => e.ExerciseId, StringComparer.Ordinal).ThenBy(e => e.At)
                    .Select(ToVolumeExposure).ToArray())
            .ToDictionary(r => r.ExerciseId, StringComparer.Ordinal);

        foreach (var recommendation in volume.Values.Where(r =>
                     r.Action is TrainingExerciseVolumeAction.PauseAndReview or TrainingExerciseVolumeAction.ReviewWithoutReduction))
            notes.Add($"{recommendation.ExerciseId}: {recommendation.Explanation}");

        var actions = new List<AutoAction>();
        var holdNow = false;

        // Pain precedence. Pain is a safety signal, never a performance signal. While a pain report is pending review
        // (general pain declared in the recent session window), nothing may increase automatically: not loads, not
        // repetitions, not sets. Pain on a specific exercise (any of its last three exposures) blocks that exercise.
        var painPending = sessionDecision.Action == TrainingAdaptationAction.PauseAffectedExerciseAndReview;
        var painExercises = input.ExerciseHistory
            .GroupBy(e => e.ExerciseId, StringComparer.Ordinal)
            .Where(group => group.OrderBy(e => e.At).TakeLast(3).Any(HasPain))
            .Select(group => group.Key).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        void TryDecide(string proposalAction, IReadOnlyList<string> targets, string explanation)
        {
            var decision = TrainingAutoAdaptationPolicy.Decide(proposalAction, targets, input.Now, recent);
            if (!decision.ShouldApply)
            {
                if (decision.SkipReason is not null) notes.Add(decision.SkipReason);
                return;
            }
            actions.Add(new AutoAction(decision.Kind, decision.ExerciseIds, explanation));
            recent.Add(new TrainingAutoAdaptationRecord(decision.Kind, input.Now));
            if (decision.Kind == TrainingAutoAdaptationKind.HoldProgression) holdNow = true;
        }

        var reduceTargets = volume.Values.Where(r => r.Action == TrainingExerciseVolumeAction.ProposeReduceOneSet)
            .Select(r => r.ExerciseId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        var restoreTargets = volume.Values.Where(r => r.Action == TrainingExerciseVolumeAction.ProposeRestoreOneSet)
            .Select(r => r.ExerciseId).OrderBy(id => id, StringComparer.Ordinal).ToArray();

        // A deload absorbs every other change, so it is decided first.
        switch (sessionDecision.Action)
        {
            case TrainingAdaptationAction.ProposeDeload:
                TryDecide("propose_deload", [], "Descarga conservadora de las próximas sesiones: mitad de series y 2 repeticiones más en reserva.");
                break;
            case TrainingAdaptationAction.ReviewAndReduceTrainingStress:
                TryDecide("review_and_reduce_training_stress", reduceTargets,
                    reduceTargets.Length > 0
                        ? "Una serie menos en los ejercicios con fatiga repetida, en las próximas sesiones."
                        : "Una repetición más en reserva (RIR +1) en las próximas sesiones por recuperación insuficiente repetida.");
                break;
            case TrainingAdaptationAction.HoldProgressionAndReview:
                TryDecide("hold_progression_and_review", [], "Se pausa la progresión de carga durante una semana.");
                break;
            case TrainingAdaptationAction.PauseAffectedExerciseAndReview:
                notes.Add(sessionDecision.Explanation);
                break;
        }

        if (reduceTargets.Length > 0 && actions.All(a => a.Kind != TrainingAutoAdaptationKind.ReduceSets))
            TryDecide("review_and_reduce_training_stress", reduceTargets, "Una serie menos en los ejercicios con fatiga repetida, en las próximas sesiones.");
        if (restoreTargets.Length > 0)
        {
            // Restoring a set is a volume increase: it must not coexist with a pain report in the affected scope.
            var allowedRestores = painPending ? Array.Empty<string>() : restoreTargets.Where(id => !painExercises.Contains(id, StringComparer.Ordinal)).ToArray();
            if (allowedRestores.Length > 0)
                TryDecide("restore_volume", allowedRestores, "Se recupera una serie reducida antes, sin superar la dosis original.");
            else
                notes.Add("No se recupera ninguna serie porque hay dolor pendiente de revisión.");
        }

        // Exercise-level progression.
        var holdActive = holdNow || painPending || input.RecentlyApplied.Any(a => a.Kind == TrainingAutoAdaptationKind.HoldProgression
            && input.Now - a.AppliedAt < TimeSpan.FromDays(ProgressionHoldDays));
        var advice = new List<ExerciseAdvice>();
        foreach (var exerciseId in input.ExercisesInLoggedSession.OrderBy(id => id, StringComparer.Ordinal))
        {
            var history = input.ExerciseHistory.Where(e => string.Equals(e.ExerciseId, exerciseId, StringComparison.Ordinal))
                .OrderBy(e => e.At).ToArray();
            if (history.Length == 0) continue;
            advice.Add(AdviseExercise(history, holdActive, painPending));
        }

        return new AdaptationOutcome(advice, sessionDecision.Action.ToString(), sessionDecision.Explanation, actions, notes, painPending, painExercises);
    }

    private static ExerciseAdvice AdviseExercise(IReadOnlyList<ExerciseExposureRecord> history, bool holdActive, bool painPending)
    {
        var latest = history[^1];
        var lastLoad = WorkingLoad(latest);
        var increment = TrainingLoadEstimator.IncrementFor(lastLoad ?? TrainingLoadEstimator.LightLoadThresholdKg);
        var prescription = new TrainingExerciseProgressionPrescription(
            latest.ExerciseId, latest.PlannedSets, latest.MinRepetitions, latest.MaxRepetitions, latest.TargetRir,
            latest.Rule, lastLoad is null ? null : increment);
        // An exercise can come twice a week with different repetition ranges (a heavy day and a volume day): each range
        // progresses on its own exposures. Pain is read on all of them, whatever the range.
        var exposures = history.Where(e => e.MinRepetitions == latest.MinRepetitions && e.MaxRepetitions == latest.MaxRepetitions)
            .TakeLast(3).Select(ToExposure).ToArray();
        if (history.TakeLast(3).Any(HasPain)) exposures[^1] = exposures[^1] with { PainReported = true };

        var raw = TrainingExerciseProgressionEngine.Evaluate(prescription, exposures);
        var result = TrainingProgressionHold.Apply(raw, holdActive);
        // Say WHY the increase was withheld: a pain report is a different reason from a routine progression pause.
        if (painPending && !ReferenceEquals(result, raw) && result.Action == TrainingExerciseProgressionAction.HoldAndReview)
            result = result with
            {
                Explanation = "No se incrementa la carga ni las repeticiones porque se ha declarado dolor. La progresión automática queda en pausa hasta revisarlo."
            };
        var painNow = HasPain(latest);

        // The next session shows the suggested load; otherwise the load that was actually used, unless pain was reported.
        decimal? next = result.SuggestedLoadKg ?? (painNow ? null : lastLoad);
        return new ExerciseAdvice(latest.ExerciseId, result.Action.ToString(), result.Explanation, next, result.SuggestedRepetitionsBySet);
    }

    /// <summary>A set-level pain signal: the pain flag or a set skipped because of pain.</summary>
    private static bool HasPain(ExerciseExposureRecord e) =>
        e.Sets.Any(s => s.Pain || string.Equals(s.SkippedReason, "pain", StringComparison.OrdinalIgnoreCase));

    /// <summary>The load of most completed sets of the exposure, the reference the engine progresses from.</summary>
    internal static decimal? WorkingLoad(ExerciseExposureRecord exposure) =>
        TrainingExerciseProgressionEngine.MainLoad(exposure.Sets.Select(ToExecuted));

    private static TrainingExecutedSet ToExecuted(SetRecord s) =>
        new("working", s.WeightKg, s.Repetitions, s.Rir, s.Completed, s.SkippedReason);

    private static TrainingExerciseExposure ToExposure(ExerciseExposureRecord e) =>
        new(e.Sets.Select(ToExecuted).ToArray(),
            // Whether THIS exercise was finished is read from its sets: a session left partial elsewhere says nothing about it.
            SessionCompleted: e.Outcome != WorkoutOutcome.NotCompleted,
            RecoveryInsufficient: string.Equals(e.Recovery, "Insufficient", StringComparison.OrdinalIgnoreCase),
            PainReported: HasPain(e),
            ProgressionEligible: e.ProgressionEligible,
            HistoricalPrescription: new TrainingExerciseProgressionPrescriptionSnapshot(e.PlannedSets, e.MinRepetitions, e.MaxRepetitions, e.TargetRir),
            SkippedSetReason: e.Sets.Select(s => s.SkippedReason).FirstOrDefault(r => !string.IsNullOrEmpty(r)));

    private static TrainingExerciseVolumeExposure ToVolumeExposure(ExerciseExposureRecord e)
    {
        var completed = e.Sets.Where(s => s.Completed).ToArray();
        var rirs = completed.Where(s => s.Rir is not null).Select(s => s.Rir!.Value).ToArray();
        return new TrainingExerciseVolumeExposure(
            e.ExerciseId, e.PlannedSets, completed.Length,
            e.Sets.Select(s => s.SkippedReason).FirstOrDefault(r => !string.IsNullOrEmpty(r)),
            SessionCompleted: e.Outcome == WorkoutOutcome.Completed,
            PainReported: HasPain(e),
            TargetRir: e.TargetRir,
            ActualAverageRir: completed.Length > 0 && rirs.Length == completed.Length ? rirs.Average() : null,
            OriginalPlannedSets: e.OriginalPlannedSets);
    }

    private static TrainingSessionFeedback ToFeedback(SessionRecord s) => new(
        Outcome: s.Outcome switch
        {
            WorkoutOutcome.Completed => TrainingSessionOutcome.Completed,
            WorkoutOutcome.Partial => TrainingSessionOutcome.Partial,
            _ => TrainingSessionOutcome.NotCompleted
        },
        NonCompletionReason: Enum.TryParse<TrainingNonCompletionReason>(s.Reason, true, out var reason) ? reason : TrainingNonCompletionReason.None,
        Recovery: Enum.TryParse<TrainingRecoveryRating>(s.Recovery, true, out var recovery) ? recovery : TrainingRecoveryRating.Unknown,
        PainSignal: s.Pain ? TrainingPainSignal.NewOrWorsening : TrainingPainSignal.None,
        SessionRpe: s.Rpe,
        SetCompletionRatio: s.CompletionRatio);
}
