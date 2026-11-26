using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// The single definition of a valid technique and superset, for manual and generated plans alike
/// (docs/prescription-techniques.md). The default figures are product choices, not demonstrated recommendations.
/// </summary>
public static class TechniqueRules
{
    public const int MaximumRestSeconds = 60;
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

    /// <summary>What is wrong with a technique on an exercise; empty when it can be prescribed.</summary>
    public static IReadOnlyList<string> Validate(
        TrainingTechniquePrescription technique, int sets, IReadOnlyCollection<TrainingTechniqueType>? allowed, bool inSuperset)
    {
        ArgumentNullException.ThrowIfNull(technique);
        var errors = new List<string>();
        if (!Enum.IsDefined(technique.Type) || !Enum.IsDefined(technique.AppliesTo)) return ["La técnica no es válida."];
        if (allowed is null || !allowed.Contains(technique.Type)) errors.Add("El ejercicio no admite esa técnica.");
        if (inSuperset) errors.Add("Una superserie no se combina con otra técnica en el mismo ejercicio.");
        var maximum = MaximumSegments(technique.Type);
        if (technique.MaxSegments < 1 || technique.MaxSegments > maximum) errors.Add($"Los segmentos deben estar entre 1 y {maximum}.");
        if (technique.RestSeconds is < 0 or > MaximumRestSeconds) errors.Add($"El descanso entre segmentos debe estar entre 0 y {MaximumRestSeconds} segundos.");
        if (technique.TargetRir is < 0 or > 5) errors.Add("El RIR objetivo debe estar entre 0 y 5.");
        if (technique.Type == TrainingTechniqueType.DropSet)
        {
            if (technique.LoadDropPercent is not (>= 5 and <= 50)) errors.Add("La bajada de carga debe estar entre el 5 y el 50 %.");
        }
        else if (technique.LoadDropPercent is not null) errors.Add("Solo un drop set baja la carga.");
        var (min, max) = (technique.SegmentMinRepetitions, technique.SegmentMaxRepetitions);
        if (technique.Type is TrainingTechniqueType.MyoReps or TrainingTechniqueType.RestPause)
        {
            if (min is < 1 or > 30 || max is < 1 or > 30 || (min is not null && max is not null && min > max))
                errors.Add("Las repeticiones de cada segmento deben estar entre 1 y 30, y el mínimo no puede superar el máximo.");
        }
        else if (min is not null || max is not null) errors.Add("Esta técnica no fija las repeticiones de los segmentos.");
        if (technique.Type == TrainingTechniqueType.MyoRepMatch && sets < 2) errors.Add("Myo-rep match necesita al menos dos series.");
        return errors;
    }

    public static IReadOnlyList<string> Validate(TrainingSupersetPrescription superset)
    {
        ArgumentNullException.ThrowIfNull(superset);
        var errors = new List<string>();
        if (superset.Group is not { Length: 1 } || superset.Group[0] is < 'A' or > 'F') errors.Add("El grupo de superserie debe ser una letra de la A a la F.");
        if (superset.Rounds is < 1 or > MaximumSupersetRounds) errors.Add($"Las rondas deben estar entre 1 y {MaximumSupersetRounds}.");
        if (superset.RestBetweenSeconds is < 0 or > MaximumSupersetRestSeconds || superset.RestAfterRoundSeconds is < 0 or > MaximumSupersetRestSeconds)
            errors.Add($"Los descansos deben estar entre 0 y {MaximumSupersetRestSeconds} segundos.");
        return errors;
    }
}
