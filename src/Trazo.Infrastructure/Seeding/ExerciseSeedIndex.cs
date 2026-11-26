namespace Trazo.Infrastructure.Seeding;

/// <summary>
/// Which exercises belong to the seed shipped with this version of the API. The seed governs their metadata and texts, so
/// nothing else may change them. Read once: the file only changes with a new deployment.
/// </summary>
public sealed class ExerciseSeedIndex
{
    private readonly Lazy<IReadOnlySet<string>> ids = new(() => ExerciseSeed.LoadIds(ExerciseSeed.DefaultFolder));

    public bool Contains(string exerciseId) => ids.Value.Contains(exerciseId);
}
