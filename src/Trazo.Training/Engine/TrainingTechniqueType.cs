namespace Trazo.Training;

/// <summary>
/// The set techniques an exercise can be prescribed with (docs/prescription-techniques.md). Several descending sets are a
/// <see cref="DropSet"/> with more than one drop; supersets join exercises and are not a property of one.
/// </summary>
public enum TrainingTechniqueType
{
    MyoReps,
    MyoRepMatch,
    DropSet,
    RestPause
}

/// <summary>Which working sets of the exercise carry the technique. In a myo-rep match the first set is always the reference.</summary>
public enum TrainingTechniqueScope
{
    LastSet,
    AllSets
}

/// <summary>
/// How the segments that follow a main set are done. <see cref="MaxSegments"/> counts what follows the main set;
/// <see cref="LoadDropPercent"/> belongs to a drop set only, and the segment repetitions are the short target of myo-reps or the
/// floor of a rest-pause. The sets, repetition range and rest of the exercise still describe its main sets.
/// </summary>
public sealed record TrainingTechniquePrescription(
    TrainingTechniqueType Type,
    TrainingTechniqueScope AppliesTo,
    int MaxSegments,
    int RestSeconds,
    int? LoadDropPercent = null,
    int? SegmentMinRepetitions = null,
    int? SegmentMaxRepetitions = null,
    decimal? TargetRir = null,
    // Why the generator prescribed it (the app words it); absent in a technique the user chose.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Reason = null);

/// <summary>
/// How the exercises that share a superset letter are alternated. It lives beside the exercises of a session; a letter without
/// one is done as supersets always were.
/// </summary>
public sealed record TrainingSupersetPrescription(
    string Group, int Rounds, int RestBetweenSeconds, int RestAfterRoundSeconds,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? Reason = null);
