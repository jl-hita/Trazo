using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// The single definition of a valid technique and superset, for manual and generated plans alike
/// (docs/prescription-techniques.md). The default figures are product choices, not demonstrated recommendations.
/// </summary>
public static class TechniqueRules
{
    public const int MaximumRestSeconds = 60;
    public const int MaximumSupersetExercises = 4;
    public const int MaximumSupersetRounds = 10;
    public const int MaximumSupersetRestSeconds = 600;

    public static TrainingTechniquePrescription Default(TrainingTechniqueType type) => type switch
    {
        TrainingTechniqueType.MyoReps => new(type, TrainingTechniqueScope.LastSet, 5, 15, null, 3, 5, 1m),
        TrainingTechniqueType.MyoRepMatch => new(type, TrainingTechniqueScope.AllSets, 5, 15, null, null, null, 1m),
        TrainingTechniqueType.RestPause => new(type, TrainingTechniqueScope.LastSet, 2, 20, null, 2, null, 1m),
        TrainingTechniqueType.DropSet => new(type, TrainingTechniqueScope.LastSet, 1, 10, 20, null, null, 1m),
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    /// <summary>The most segments each technique may ask for; several descending sets are a drop set of two or three.</summary>
    public static int MaximumSegments(TrainingTechniqueType type) => type switch
    {
        TrainingTechniqueType.DropSet => 3,
        TrainingTechniqueType.RestPause => 5,
        _ => LogSessionValidator.MaximumSegment
    };

    public const string InvalidTechnique = "La técnica no es válida.";
    public const string NotAdmitted = "El ejercicio no admite esa técnica.";
    public const string NotInSuperset = "Una superserie no se combina con otra técnica en el mismo ejercicio.";
    public const string MatchNeedsTwoSets = "Myo-rep match necesita al menos dos series.";
    public const string InvalidSuperset = "Las rondas o los descansos de la superserie no son válidos.";

    /// <summary>What is wrong with a technique on an exercise; empty when it can be prescribed.</summary>
    public static IReadOnlyList<string> Validate(
        TrainingTechniquePrescription technique, int sets, IReadOnlyCollection<TrainingTechniqueType>? allowed, bool inSuperset)
    {
        ArgumentNullException.ThrowIfNull(technique);
        if (!Enum.IsDefined(technique.Type) || !Enum.IsDefined(technique.AppliesTo)) return [InvalidTechnique];
        var errors = new List<string>();
        if (allowed is null || !allowed.Contains(technique.Type)) errors.Add(NotAdmitted);
        if (inSuperset) errors.Add(NotInSuperset);
        if (!InRange(technique)) errors.Add(InvalidTechnique);
        if (technique.Type == TrainingTechniqueType.MyoRepMatch && sets < 2) errors.Add(MatchNeedsTwoSets);
        return errors;
    }

    /// <summary>Segments, rest, reserve, load drop (a drop set only) and segment repetitions (myo-reps and rest-pause only).</summary>
    private static bool InRange(TrainingTechniquePrescription technique)
    {
        if (technique.MaxSegments < 1 || technique.MaxSegments > MaximumSegments(technique.Type)) return false;
        if (technique.RestSeconds is < 0 or > MaximumRestSeconds || technique.TargetRir is < 0 or > 5) return false;
        if (technique.Type == TrainingTechniqueType.DropSet ? technique.LoadDropPercent is not (>= 5 and <= 50) : technique.LoadDropPercent is not null)
            return false;
        var (min, max) = (technique.SegmentMinRepetitions, technique.SegmentMaxRepetitions);
        return technique.Type is TrainingTechniqueType.MyoReps or TrainingTechniqueType.RestPause
            ? min is not (< 1 or > 30) && max is not (< 1 or > 30) && !(min > max)
            : min is null && max is null;
    }

    public static IReadOnlyList<string> Validate(TrainingSupersetPrescription superset)
    {
        ArgumentNullException.ThrowIfNull(superset);
        var valid = superset.Group is { Length: 1 } && superset.Group[0] is >= 'A' and <= 'F'
                    && superset.Rounds is >= 1 and <= MaximumSupersetRounds
                    && superset.RestBetweenSeconds is >= 0 and <= MaximumSupersetRestSeconds
                    && superset.RestAfterRoundSeconds is >= 0 and <= MaximumSupersetRestSeconds;
        return valid ? [] : [InvalidSuperset];
    }

    public const int DefaultRestBetweenSeconds = 15;
    public const int DefaultRestAfterRoundSeconds = 90;
    /// <summary>The time a segment takes besides its rest, for the estimated duration of a session. A product estimate.</summary>
    public const int SecondsPerSegment = 20;

    /// <summary>
    /// The rounds a session can still do: never more than the most sets among the exercises of the group, so a session that lost
    /// sets (a deload, an adjustment) does not announce rounds it no longer has. A group with no exercise left is dropped.
    /// </summary>
    public static IReadOnlyList<TrainingSupersetPrescription> WithinSets(
        IReadOnlyList<TrainingSupersetPrescription> supersets, IEnumerable<(string? Group, int Sets)> exercises)
    {
        var most = exercises.Where(e => e.Group is not null).GroupBy(e => e.Group!).ToDictionary(g => g.Key, g => g.Max(e => e.Sets));
        return supersets.Where(s => most.ContainsKey(s.Group)).Select(s => s with { Rounds = Math.Min(s.Rounds, most[s.Group]) }).ToList();
    }

    /// <summary>
    /// The estimated seconds of a session of straight sets, techniques and supersets: 45 s of work per set and its rest. In a
    /// superset with rounds the rests are the ones of the round, not those of each exercise.
    /// </summary>
    public static int SessionSeconds(
        IReadOnlyList<(string? Group, int Sets, int RestSeconds, TrainingTechniquePrescription? Technique)> exercises,
        IReadOnlyList<TrainingSupersetPrescription> supersets)
    {
        var alternated = supersets.ToDictionary(s => s.Group);
        var seconds = exercises.Sum(e => e.Group is not null && alternated.ContainsKey(e.Group)
            ? e.Sets * SecondsPerSet
            : e.Sets * (e.RestSeconds + SecondsPerSet) + ExtraSeconds(e.Technique, e.Sets));
        foreach (var superset in supersets)
        {
            var members = exercises.Where(e => e.Group == superset.Group).ToList();
            if (members.Count == 0) continue;
            var rounds = Math.Min(superset.Rounds, members.Max(m => m.Sets));
            seconds += rounds * ((members.Count - 1) * superset.RestBetweenSeconds + superset.RestAfterRoundSeconds);
        }
        return seconds;
    }

    private const int SecondsPerSet = 45;

    /// <summary>What the segments of an exercise add to the session, at most: every segment of every set that carries the technique.</summary>
    public static int ExtraSeconds(TrainingTechniquePrescription? technique, int sets)
    {
        if (technique is null) return 0;
        var carrying = technique.AppliesTo == TrainingTechniqueScope.LastSet ? 1
            : technique.Type == TrainingTechniqueType.MyoRepMatch ? Math.Max(0, sets - 1) : sets;
        return carrying * technique.MaxSegments * (technique.RestSeconds + SecondsPerSegment);
    }
}
