namespace Trazo.Training;

/// <summary>The muscle names the engine writes inside its sentences, in the language of those sentences. The code names stay in the data.</summary>
public static class TrainingMuscleNames
{
    public static string InText(TrainingMuscleGroup muscle) => muscle switch
    {
        TrainingMuscleGroup.Chest => "Pecho",
        TrainingMuscleGroup.Lats => "Dorsales",
        TrainingMuscleGroup.UpperBack => "Espalda alta",
        TrainingMuscleGroup.Deltoids => "Hombros",
        TrainingMuscleGroup.Biceps => "Bíceps",
        TrainingMuscleGroup.Triceps => "Tríceps",
        TrainingMuscleGroup.Forearms => "Antebrazos",
        TrainingMuscleGroup.Abdominals => "Abdomen",
        TrainingMuscleGroup.Obliques => "Oblicuos",
        TrainingMuscleGroup.ErectorSpinae => "Lumbares",
        TrainingMuscleGroup.Glutes => "Glúteos",
        TrainingMuscleGroup.Quadriceps => "Cuádriceps",
        TrainingMuscleGroup.Hamstrings => "Isquiotibiales",
        TrainingMuscleGroup.Calves => "Gemelos",
        TrainingMuscleGroup.Adductors => "Aductores",
        TrainingMuscleGroup.Abductors => "Abductores",
        TrainingMuscleGroup.HipFlexors => "Flexores de cadera",
        TrainingMuscleGroup.Serratus => "Serrato",
        TrainingMuscleGroup.TibialisAnterior => "Tibial anterior",
        TrainingMuscleGroup.RotatorCuff => "Manguito rotador",
        _ => muscle.ToString()
    };

    /// <summary>The same for a name as it is stored; a text that is not a muscle comes back as it is.</summary>
    public static string InText(string muscle) =>
        Enum.TryParse<TrainingMuscleGroup>(muscle, true, out var group) && Enum.IsDefined(group) ? InText(group) : muscle;
}
