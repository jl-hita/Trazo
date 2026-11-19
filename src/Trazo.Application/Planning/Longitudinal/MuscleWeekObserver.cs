using System.Text.Json;
using Trazo.Domain.Planning;
using Trazo.Training;

namespace Trazo.Application.Planning.Longitudinal;

/// <summary>What the observer needs; the service loads it, the observer never touches the database.</summary>
public sealed record ObserverInput(
    Guid UserId,
    Guid PlanId,
    IReadOnlyList<PlannedSession> Sessions,
    /// <summary>Every workout of the user (any plan), with its sets: the previous exposure of an exercise may be from an earlier plan.</summary>
    IReadOnlyList<WorkoutLog> Logs,
    IReadOnlyDictionary<Guid, (int Sequence, bool IsDeload)> Mesocycles,
    IReadOnlyCollection<int> Periods,
    DateTimeOffset Now);

/// <summary>
/// Turns the workouts of a plan into one observation per muscle and period (<see cref="MuscleWeekObservation"/>). Pure and deterministic.
///
/// Data quality policy (what each kind of workout is allowed to teach):
/// - complete workout with its performance logged: full evidence;
/// - incomplete workout: the sets really done count, the rest is not invented (a period with under 60 % of the prescribed sets is partial);
/// - workout without feedback: evidence of performance only; a missing answer is never read as a bad one;
/// - workout with pain: excluded from learning and reported (it can only make the next decision more conservative);
/// - workout registered long after it happened: excluded (its date, and so its order, is not reliable);
/// - first workout after a long absence: excluded from performance and fatigue (a re-entry says nothing about the dose);
/// - duplicated registration of the same planned session: only the first one counts;
/// - substituted exercise: the stimulus goes to the muscles of the exercise really done, and performance is only ever compared
///   with earlier exposures of the SAME exercise, so a new exercise starts a new history.
/// </summary>
public static class MuscleWeekObserver
{
    public static IReadOnlyList<MuscleWeekObservation> Build(ObserverInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        // One workout per planned session (the first one registered), in the order they happened.
        var logs = input.Logs.GroupBy(l => l.PlannedSessionId).Select(g => g.OrderBy(l => l.LoggedAt).First())
            .OrderBy(l => l.PerformedAt).ThenBy(l => l.LoggedAt).ToList();
        var flags = new Dictionary<Guid, LogFlags>();
        for (var i = 0; i < logs.Count; i++)
        {
            var log = logs[i];
            flags[log.Id] = new LogFlags(
                Pain: log.PainReported || log.Sets.Any(IsPain),
                Late: (log.LoggedAt - log.PerformedAt).TotalDays > LongitudinalPolicy.LateRegistrationDays,
                Returning: i > 0 && (log.PerformedAt - logs[i - 1].PerformedAt).TotalDays > LongitudinalPolicy.AbsenceDays);
        }

        // Earlier exposures per exercise, only from workouts that can serve as a reference.
        var history = new Dictionary<string, List<(DateTimeOffset At, Indicator Value)>>(StringComparer.OrdinalIgnoreCase);
        var previousOf = new Dictionary<(Guid Log, string Exercise), (DateTimeOffset At, Indicator Value)>();
        foreach (var log in logs)
        {
            var f = flags[log.Id];
            foreach (var group in log.Sets.GroupBy(s => s.ExerciseId, StringComparer.OrdinalIgnoreCase))
            {
                var indicator = IndicatorOf(group);
                if (indicator is null || f.Pain || f.Late) continue;
                if (history.TryGetValue(group.Key, out var earlier) && earlier.Count > 0)
                    previousOf[(log.Id, group.Key.ToLowerInvariant())] = earlier[^1];
                if (!history.ContainsKey(group.Key)) history[group.Key] = [];
                history[group.Key].Add((log.PerformedAt, indicator));
            }
        }

        var logBySession = logs.ToDictionary(l => l.PlannedSessionId);
        var rows = new List<MuscleWeekObservation>();
        foreach (var period in input.Periods.Distinct().OrderBy(p => p))
        {
            var sessions = input.Sessions.Where(s => s.WeekNumber == period).OrderBy(s => s.SequenceIndex).ToList();
            if (sessions.Count == 0) continue;
            var block = input.Mesocycles.TryGetValue(sessions[0].MesocycleId, out var found) ? found : (Sequence: 0, IsDeload: false);
            var acc = new Dictionary<string, Acc>(StringComparer.Ordinal);
            var processed = DateTimeOffset.MinValue;

            foreach (var session in sessions)
            {
                var prescription = PrescriptionJson.Read(session.PrescriptionJson);
                var muscles = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in prescription)
                    muscles.TryAdd(e.ExerciseId, TrainingStimulus.EffectiveMuscles(
                        e.ExerciseId, e.PrimaryMuscleGroups.Select(m => m.ToString()), e.SecondaryMuscleGroups.Select(m => m.ToString())));
                var prescribedBySessionMuscle = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var exercise in prescription)
                    foreach (var muscle in muscles[exercise.ExerciseId])
                    {
                        prescribedBySessionMuscle[muscle] = prescribedBySessionMuscle.GetValueOrDefault(muscle) + exercise.Sets;
                        Get(acc, muscle).Prescribed += exercise.Sets;
                    }
                var meaningful = prescribedBySessionMuscle.Where(p => p.Value >= LongitudinalPolicy.MeaningfulSets).Select(p => p.Key).ToList();
                foreach (var muscle in meaningful) Get(acc, muscle).SessionsPlanned++;

                if (!logBySession.TryGetValue(session.Id, out var log)) continue;
                if (log.LoggedAt > processed) processed = log.LoggedAt;
                var f = flags[log.Id];
                if (f.Late) { foreach (var m in meaningful) Get(acc, m).Late++; continue; }
                if (f.Pain) { foreach (var m in meaningful) Get(acc, m).Pain++; continue; }
                if (f.Returning) { foreach (var m in meaningful) Get(acc, m).Returning = true; continue; }

                var feedback = Feedback(log.ExerciseFeedbackJson);
                var perMuscle = new Dictionary<string, SessionAcc>(StringComparer.Ordinal);
                foreach (var exercise in prescription)
                {
                    var sets = log.Sets.Where(s => string.Equals(s.ExerciseId, exercise.ExerciseId, StringComparison.OrdinalIgnoreCase)).ToList();
                    var done = sets.Where(s => s.Completed && !IsPain(s)).ToList();
                    var skippedForFatigue = sets.Any(s => !s.Completed && string.Equals(s.SkippedReason, "fatigue", StringComparison.OrdinalIgnoreCase));
                    feedback.TryGetValue(exercise.ExerciseId, out var answer);
                    var change = previousOf.TryGetValue((log.Id, exercise.ExerciseId.ToLowerInvariant()), out var previous)
                        ? Change(IndicatorOf(sets), previous, log.PerformedAt) : null;
                    foreach (var muscle in muscles[exercise.ExerciseId])
                    {
                        if (!perMuscle.TryGetValue(muscle, out var s)) perMuscle[muscle] = s = new SessionAcc();
                        s.Sets += done.Count;
                        s.Below += done.Count(x => x.Repetitions is { } r && r < exercise.MinRepetitions);
                        if (skippedForFatigue) s.Fatigue++;
                        if (done.Count > 0)
                        {
                            if (string.Equals(answer.PreFatigue, "loaded", StringComparison.OrdinalIgnoreCase)) s.Fatigue++;
                            if (answer.Stimulus is not null) s.Answers++;
                            if (string.Equals(answer.Stimulus, "low", StringComparison.OrdinalIgnoreCase)) s.LowStimulus++;
                            if (string.Equals(answer.Discomfort, "mild", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(answer.Discomfort, "strong", StringComparison.OrdinalIgnoreCase)) s.Discomfort++;
                            if (change is { } c) { s.PerformanceSum += c * done.Count; s.PerformanceWeight += done.Count; s.Samples++; }
                        }
                    }
                }

                foreach (var muscle in meaningful)
                {
                    var a = Get(acc, muscle);
                    perMuscle.TryGetValue(muscle, out var s);
                    s ??= new SessionAcc();
                    a.Performed += s.Sets;
                    a.Fatigue += s.Fatigue;
                    if (string.Equals(log.Recovery, "Insufficient", StringComparison.OrdinalIgnoreCase)) a.Fatigue++;
                    if (log.SessionRpe is { } rpe && rpe >= LongitudinalPolicy.HighSessionRpe) a.Fatigue++;
                    if (log.Outcome != WorkoutOutcome.Completed && string.Equals(log.NonCompletionReason, "Fatigue", StringComparison.OrdinalIgnoreCase)) a.Fatigue++;
                    if (s.Sets >= LongitudinalPolicy.MeaningfulSets)
                    {
                        a.Exposures++;
                        a.SessionsDone++;
                        a.Completed += s.Sets;
                        a.Below += s.Below;
                        a.Answers += s.Answers;
                        a.LowStimulus += s.LowStimulus;
                        a.Discomfort += s.Discomfort;
                        a.PerformanceSum += s.PerformanceSum;
                        a.PerformanceWeight += s.PerformanceWeight;
                        a.Samples += s.Samples;
                    }
                }
            }

            foreach (var (muscle, a) in acc.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                if (a.Prescribed < LongitudinalPolicy.MeaningfulSets) continue;
                string quality;
                string? reason = null;
                if (block.IsDeload) { quality = "excluded"; reason = "deload"; }
                else if (a.Exposures == 0)
                {
                    quality = "excluded";
                    reason = a.Pain > 0 ? "pain" : a.Returning ? "return_from_absence" : a.Late > 0 ? "date_unreliable" : "no_valid_exposure";
                }
                else if ((decimal)a.Performed / a.Prescribed < LongitudinalPolicy.PartialCompletionRatio) { quality = "partial"; reason = "low_completion"; }
                else quality = "valid";

                rows.Add(new MuscleWeekObservation
                {
                    Id = Guid.NewGuid(), UserId = input.UserId, PlanId = input.PlanId, Period = period,
                    MesocycleSequence = block.Sequence, IsDeload = block.IsDeload, Muscle = muscle, Quality = quality, Reason = reason,
                    PrescribedSets = a.Prescribed, PerformedSets = a.Performed, SessionsPlanned = a.SessionsPlanned,
                    SessionsDone = a.SessionsDone, Exposures = a.Exposures, CompletedSets = a.Completed, BelowRangeSets = a.Below,
                    PerformanceChangePercent = a.PerformanceWeight > 0 ? Math.Round(a.PerformanceSum / a.PerformanceWeight, 2) : null,
                    PerformanceSamples = a.Samples, FatigueSignals = a.Fatigue, FeedbackAnswers = a.Answers,
                    LowStimulusAnswers = a.LowStimulus, DiscomfortAnswers = a.Discomfort, PainSessions = a.Pain,
                    ReturnFromAbsence = a.Returning, ProcessedThrough = processed, UpdatedAt = input.Now
                });
            }
        }
        return rows;
    }

    private static bool IsPain(WorkoutSetLog set) =>
        set.PainReported || string.Equals(set.SkippedReason, "pain", StringComparison.OrdinalIgnoreCase);

    private static Acc Get(Dictionary<string, Acc> acc, string muscle)
    {
        if (!acc.TryGetValue(muscle, out var a)) acc[muscle] = a = new Acc();
        return a;
    }

    /// <summary>
    /// A simple, transparent indicator of how much the user lifted: the best completed set as load x (1 + repetitions / 30) (the Epley form),
    /// or the best repetitions when no load is logged. It is only ever compared with the same exercise, in the same unit, and it is a
    /// signal of performance, not a measure of muscle growth.
    /// </summary>
    private static Indicator? IndicatorOf(IEnumerable<WorkoutSetLog> sets)
    {
        var done = sets.Where(s => s.Completed && !IsPain(s) && s.Repetitions is > 0).ToList();
        if (done.Count == 0) return null;
        var weighted = done.Where(s => s.WeightKg is > 0).ToList();
        return weighted.Count > 0
            ? new Indicator(weighted.Max(s => s.WeightKg!.Value * (1m + s.Repetitions!.Value / 30m)), true)
            : new Indicator(done.Max(s => s.Repetitions!.Value), false);
    }

    private static decimal? Change(Indicator? current, (DateTimeOffset At, Indicator Value) previous, DateTimeOffset at)
    {
        if (current is null || current.Weighted != previous.Value.Weighted || previous.Value.Value <= 0m) return null;
        if ((at - previous.At).TotalDays > LongitudinalPolicy.ComparableGapDays) return null;
        return (current.Value - previous.Value.Value) / previous.Value.Value * 100m;
    }

    private static Dictionary<string, (string? Stimulus, string? Discomfort, string? PreFatigue)> Feedback(string? json)
    {
        var result = new Dictionary<string, (string?, string?, string?)>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                var id = Text(item, "exerciseId");
                if (!string.IsNullOrWhiteSpace(id)) result[id] = (Text(item, "stimulus"), Text(item, "discomfort"), Text(item, "preFatigue"));
            }
        }
        catch (JsonException) { /* Unreadable feedback is missing feedback, never a negative answer. */ }
        return result;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record LogFlags(bool Pain, bool Late, bool Returning);
    private sealed record Indicator(decimal Value, bool Weighted);

    private sealed class SessionAcc
    {
        public int Sets, Below, Fatigue, Answers, LowStimulus, Discomfort, Samples;
        public decimal PerformanceSum, PerformanceWeight;
    }

    private sealed class Acc
    {
        public int Prescribed, SessionsPlanned, SessionsDone, Exposures, Completed, Below, Fatigue, Answers, LowStimulus, Discomfort, Samples, Pain, Late, Performed;
        public bool Returning;
        public decimal PerformanceSum, PerformanceWeight;
    }
}
