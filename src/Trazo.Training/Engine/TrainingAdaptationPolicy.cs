namespace Trazo.Training;

/// <summary>
/// Deterministic, conservative decision policy for interpreting training feedback.
/// It returns a recommendation for future sessions; it never edits a plan or diagnoses an injury.
/// Feedback must be supplied oldest-to-newest; the latest item represents the most recent session.
/// </summary>
public static class TrainingAdaptationPolicy
{
    public static TrainingAdaptationDecision Evaluate(IReadOnlyList<TrainingSessionFeedback> recentFeedback)
    {
        ArgumentNullException.ThrowIfNull(recentFeedback);

        if (recentFeedback.Count == 0)
            return Decision(TrainingAdaptationAction.InsufficientData,
                "Todavía no hay registros suficientes para orientar cambios.");

        if (recentFeedback.Any(HasInvalidValues))
            return Decision(TrainingAdaptationAction.InvalidFeedback,
                "Hay datos de entrenamiento no válidos. Corrige los registros antes de evaluar cambios.");

        // Keep a pain report as a safety blocker throughout the recent feedback window.
        // A later record with no pain value must not silently clear it.
        if (recentFeedback.Any(x => x.PainSignal is TrainingPainSignal.NewOrWorsening))
            return Decision(TrainingAdaptationAction.PauseAffectedExerciseAndReview,
                "Se ha registrado dolor nuevo o que empeora. No progreses el entrenamiento y solicita valoración profesional antes de reanudar el ejercicio implicado.");

        // Reactive deload proposal: only when the last three sessions each show a stress signal AND
        // at least two different kinds of signal appear (low recovery, very high session RPE, many missed
        // sets). This policy only emits the proposal; TrainingAutoAdaptationApplier may apply it after
        // its 28-day cooldown. This is reactive, not a calendar-based deload schedule.
        // Window and thresholds are internal product rules, not ACSM prescriptions.
        var stressWindow = recentFeedback.TakeLast(3).ToArray();
        if (stressWindow.Length == 3 && stressWindow.All(IsStressed))
        {
            var kinds = (stressWindow.Any(x => x.Recovery is TrainingRecoveryRating.Insufficient) ? 1 : 0)
                + (stressWindow.Any(x => x.SessionRpe is >= 9) ? 1 : 0)
                + (stressWindow.Any(x => x.SetCompletionRatio is < 0.8m) ? 1 : 0);
            if (kinds >= 2)
                return Decision(TrainingAdaptationAction.ProposeDeload,
                    "Las tres últimas sesiones muestran señales de estrés combinadas (recuperación insuficiente, esfuerzo muy alto o series sin completar). Valora con tu profesional una semana de descarga; no se aplica automáticamente.");
        }

        var latest = recentFeedback[^1];
        if (latest.SetCompletionRatio is < 0.8m)
            return Decision(TrainingAdaptationAction.HoldProgressionAndReview,
                "Se completó menos del 80 % de las series planificadas. No aumentes la carga; revisa con tu profesional si influyeron fatiga, técnica, dolor, descansos o una planificación excesiva.");

        var recent = recentFeedback.TakeLast(3).ToArray();
        var insufficientRecoveryCount = recent.Count(x =>
            x.Recovery is TrainingRecoveryRating.Insufficient);

        if (insufficientRecoveryCount >= 2)
            return Decision(TrainingAdaptationAction.ReviewAndReduceTrainingStress,
                "La recuperación insuficiente se repite. Revisa con tu profesional el volumen, la intensidad y la separación entre sesiones futuras.");

        if (((latest.Outcome is TrainingSessionOutcome.Partial or TrainingSessionOutcome.NotCompleted) &&
             (latest.NonCompletionReason is TrainingNonCompletionReason.Fatigue or TrainingNonCompletionReason.Illness)) ||
            latest.Recovery is TrainingRecoveryRating.Insufficient)
            return Decision(TrainingAdaptationAction.HoldProgressionAndReview,
                "No aumentes automáticamente la exigencia. Revisa el contexto y confirma la recuperación con tu profesional antes de progresar.");

        if (latest.Outcome is TrainingSessionOutcome.Partial or TrainingSessionOutcome.NotCompleted &&
            latest.NonCompletionReason is TrainingNonCompletionReason.Time)
            return Decision(TrainingAdaptationAction.ReorganizeFutureSessions,
                "La sesión se completó solo parcialmente o se omitió por falta de tiempo; tu profesional puede reorganizar solo las sesiones futuras si el calendario y la recuperación lo permiten.");

        // Progression is deliberately not inferred from session-level RPE or a client-supplied
        // boolean. Exercise progression requires per-set load/reps/RIR and is evaluated by
        // TrainingExerciseProgressionEngine against that exercise's prescription and history.
        if (latest.SessionRpe is >= 9)
            return Decision(TrainingAdaptationAction.HoldProgressionAndReview,
                "El esfuerzo registrado fue muy alto. Mantén la progresión en pausa hasta revisar rendimiento y recuperación con tu profesional.");

        return Decision(TrainingAdaptationAction.NoChange,
            "Los datos no justifican un cambio. Mantén la planificación actual y sigue registrando la respuesta.");

        static TrainingAdaptationDecision Decision(TrainingAdaptationAction action, string explanation) =>
            new(action, explanation);
    }

    private static bool IsStressed(TrainingSessionFeedback feedback) =>
        feedback.Recovery is TrainingRecoveryRating.Insufficient || feedback.SessionRpe is >= 9 || feedback.SetCompletionRatio is < 0.8m;

    private static bool HasInvalidValues(TrainingSessionFeedback feedback) =>
        !Enum.IsDefined(feedback.Outcome) ||
        !Enum.IsDefined(feedback.NonCompletionReason) ||
        !Enum.IsDefined(feedback.Recovery) ||
        !Enum.IsDefined(feedback.PainSignal) ||
        feedback.SessionRpe is < 0 or > 10 ||
        feedback.SetCompletionRatio is < 0 or > 1;
}

public enum TrainingSessionOutcome
{
    Completed,
    Partial,
    NotCompleted
}

public enum TrainingNonCompletionReason
{
    None,
    Time,
    Fatigue,
    Equipment,
    Motivation,
    Illness,
    Other
}

public enum TrainingRecoveryRating
{
    Unknown,
    Good,
    Partial,
    Insufficient
}

public enum TrainingPainSignal
{
    None,
    NewOrWorsening
}

public enum TrainingAdaptationAction
{
    InsufficientData,
    InvalidFeedback,
    NoChange,
    ReorganizeFutureSessions,
    HoldProgressionAndReview,
    ReviewAndReduceTrainingStress,
    // Retained for compatibility with previously persisted proposals; new decisions use
    // TrainingExerciseProgressionEngine and never emit this session-level action.
    ProgressAccordingToPrescription,
    PauseAffectedExerciseAndReview,
    ProposeDeload
}

/// <param name="Outcome">What happened in the session.</param>
/// <param name="NonCompletionReason">Why it was partial or not completed, if applicable.</param>
/// <param name="Recovery">Self-reported recovery before the next session.</param>
/// <param name="PainSignal">A safety signal, not a diagnosis.</param>
/// <param name="SessionRpe">Optional whole-session effort rating from 0 to 10.</param>
/// <param name="ProgressionTargetAchieved">True only when the existing prescription's progression criteria were met.</param>
/// <param name="SetCompletionRatio">Ratio of completed working sets to prescribed working sets, when known.</param>
public sealed record TrainingSessionFeedback(
    TrainingSessionOutcome Outcome,
    TrainingNonCompletionReason NonCompletionReason = TrainingNonCompletionReason.None,
    TrainingRecoveryRating Recovery = TrainingRecoveryRating.Unknown,
    TrainingPainSignal PainSignal = TrainingPainSignal.None,
    decimal? SessionRpe = null,
    bool? ProgressionTargetAchieved = null,
    decimal? SetCompletionRatio = null);

public sealed record TrainingAdaptationDecision(
    TrainingAdaptationAction Action,
    string Explanation);
