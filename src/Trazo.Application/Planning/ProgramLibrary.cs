using Trazo.Domain.Planning;
using Trazo.Training;

namespace Trazo.Application.Planning;

/// <summary>
/// A prebuilt program: a ready-made profile for the one generator (there is no second engine). The user's equipment,
/// limitations and exclusions still decide the exercises, and the plan is customised like any generated one.
/// </summary>
public sealed record ProgramRecipe(
    string Id, string Name, string Description, TrainingExperience MinimumExperience, TrainingProgramSplit Split,
    PlanSchedulingMode SchedulingMode, int SessionsPerWeek, int SessionMinutes, int TotalWeeks,
    IReadOnlyList<TrainingMuscleGroup> Emphasised)
{
    /// <summary>The training days proposed when the user chooses none.</summary>
    public IReadOnlyList<DayOfWeek> DefaultDays => SessionsPerWeek switch
    {
        2 => [DayOfWeek.Monday, DayOfWeek.Thursday],
        3 => [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        4 => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        5 => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday],
        _ => [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday]
    };
}

/// <summary>
/// The ten programs (docs/programs.md). All are hypertrophy plans and Premium, like every generated plan. Durations, levels
/// and priorities are product choices, not demonstrated recommendations.
/// </summary>
public static class ProgramLibrary
{
    private const string FullBody = "Cada sesión trabaja todo el cuerpo: cada músculo se entrena varias veces por semana.";
    private const string UpperLower = "Alterna sesiones de torso y de pierna: cada músculo se entrena unas dos veces por semana.";
    private const string PushPullLegs = "Reparte la semana en empuje, tirón y pierna.";

    public static IReadOnlyList<ProgramRecipe> All { get; } =
    [
        Recipe("full-body-3", "Cuerpo completo · 3 días", FullBody, TrainingExperience.Beginner, TrainingProgramSplit.FullBody, 3, 60),
        Recipe("full-body-4", "Cuerpo completo · 4 días", FullBody, TrainingExperience.Intermediate, TrainingProgramSplit.FullBody, 4, 60),
        Recipe("upper-lower-4", "Torso/pierna · 4 días", UpperLower, TrainingExperience.Intermediate, TrainingProgramSplit.UpperLower, 4, 60),
        Recipe("upper-lower-5", "Torso/pierna · 5 días", "Torso y pierna en rotación, sin días fijos: la semana siguiente continúa donde lo dejaste.",
            TrainingExperience.Intermediate, TrainingProgramSplit.UpperLower, 5, 60, mode: PlanSchedulingMode.Sequence),
        Recipe("push-pull-legs-3", "Empuje/tirón/pierna · 3 días", PushPullLegs, TrainingExperience.Beginner, TrainingProgramSplit.PushPullLegs, 3, 60),
        Recipe("push-pull-legs-6", "Empuje/tirón/pierna · 6 días", PushPullLegs, TrainingExperience.Intermediate, TrainingProgramSplit.PushPullLegs, 6, 60),
        Recipe("chest-focus", "Especialización en pecho", "Torso/pierna con más series para el pecho.",
            TrainingExperience.Intermediate, TrainingProgramSplit.UpperLower, 4, 75, [TrainingMuscleGroup.Chest]),
        Recipe("back-focus", "Especialización en espalda", "Torso/pierna con más series para la espalda.",
            TrainingExperience.Intermediate, TrainingProgramSplit.UpperLower, 4, 75, [TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack]),
        Recipe("arms-shoulders-focus", "Especialización en brazos y hombros", "Torso/pierna con más series para brazos y hombros.",
            TrainingExperience.Intermediate, TrainingProgramSplit.UpperLower, 4, 75,
            [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Deltoids]),
        Recipe("advanced-hybrid-5", "Hipertrofia avanzada · 5 días", "Torso y pierna, y después empuje, pierna y tirón: cinco sesiones distintas.",
            TrainingExperience.Advanced, TrainingProgramSplit.Hybrid, 5, 75)
    ];

    private static ProgramRecipe Recipe(
        string id, string name, string description, TrainingExperience minimum, TrainingProgramSplit split, int sessions, int minutes,
        TrainingMuscleGroup[]? emphasised = null, PlanSchedulingMode mode = PlanSchedulingMode.Calendar) =>
        new(id, name, description, minimum, split, mode, sessions, minutes, 12, emphasised ?? []);

    public static ProgramRecipe? Find(string id) => All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public const string NotFound = "El programa no existe.";
    public const string MoreExperience = "Este programa requiere más experiencia.";
    public const string DaysMustMatch = "Elige tantos días como sesiones tiene el programa.";

    /// <summary>
    /// The profile request of a program: the recipe fixes what makes it that program; the person's level, days, time, equipment,
    /// limitations and start stay theirs. Returns the field and message of what does not fit.
    /// </summary>
    public static (PlanProfileInput Input, (string Field, string Message)? Problem) Request(ProgramRecipe recipe, PlanProfileInput own)
    {
        var experience = own.Experience;
        if (string.IsNullOrWhiteSpace(experience)) experience = recipe.MinimumExperience.ToString();
        (string, string)? problem = null;
        if (Enum.TryParse<TrainingExperience>(experience, true, out var level) && level < recipe.MinimumExperience)
            problem = ("experience", MoreExperience);
        var sequence = recipe.SchedulingMode == PlanSchedulingMode.Sequence;
        var days = sequence ? null
            : own.TrainingDays is { Length: > 0 } chosen ? chosen
            : recipe.DefaultDays.Select(d => d.ToString()).ToArray();
        if (days is not null && days.Distinct(StringComparer.OrdinalIgnoreCase).Count() != recipe.SessionsPerWeek)
            problem ??= ("trainingDays", DaysMustMatch);
        return (own with
        {
            Goal = nameof(TrainingGoal.Hypertrophy), Experience = experience, Split = recipe.Split.ToString(),
            SchedulingMode = recipe.SchedulingMode.ToString(), SessionsPerPeriod = sequence ? recipe.SessionsPerWeek : null,
            TrainingDays = days, MaxSessionMinutes = own.MaxSessionMinutes ?? recipe.SessionMinutes,
            TotalWeeks = own.TotalWeeks ?? recipe.TotalWeeks,
            MusclePriorities = recipe.Emphasised.ToDictionary(m => m.ToString(), _ => "emphasize")
        }, problem);
    }
}

/// <summary>A program as listed. <c>Locked</c> says the person needs Premium to start it.</summary>
public sealed record ProgramView(
    string Id, string Name, string Description, string MinimumExperience, int SessionsPerWeek, string SchedulingMode, string Split,
    int SessionMinutes, int TotalWeeks, IReadOnlyList<string> EmphasisedMuscles, bool Locked);

public sealed record ProgramMuscleSets(string Muscle, decimal WeeklySets);

public sealed record ProgramSampleExercise(string ExerciseId, string Name, int Sets, int MinRepetitions, int MaxRepetitions);

public sealed record ProgramSampleSession(string Title, int EstimatedDurationMinutes, IReadOnlyList<ProgramSampleExercise> Exercises);

/// <summary>
/// What a program looks like before starting it, for the person's equipment: the equipment it would use, the weekly sets per
/// muscle of a normal week and one sample session. Nothing is stored and no other session is served.
/// </summary>
public sealed record ProgramSheetView(
    ProgramView Program, IReadOnlyList<string> Equipment, IReadOnlyList<ProgramMuscleSets> WeeklySets, ProgramSampleSession SampleSession);
