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
