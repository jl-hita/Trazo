using Trazo.Application.Planning;
using Trazo.Domain.Training;
using Trazo.Training;

namespace Trazo.UnitTests.Planning;

/// <summary>A catalog with enough variety (patterns, levels, goals, equipment) to exercise the engine realistically.</summary>
internal static class PlanTestCatalog
{
    private static ExerciseCatalogItem I(string id, string pattern, string[] primary, string[] secondary, string[] equipment,
        string experience = "Beginner", int priority = 70, string[]? goals = null) =>
        ExerciseCandidateMapperTests.Item(id, pattern, experience, goals, primary, secondary, equipment, priority: priority);

    public static IReadOnlyList<ExerciseCatalogItem> Items() =>
    [
        I("squat", "Squat", ["Quadriceps", "Glutes"], ["Hamstrings"], ["barbell", "squat-rack"], "Intermediate", 90),
        I("goblet-squat", "Squat", ["Quadriceps"], ["Glutes"], ["dumbbell"], "Beginner", 80),
        I("leg-press", "Squat", ["Quadriceps", "Glutes"], [], ["machine"], "Beginner", 85),
        I("bodyweight-squat", "Squat", ["Quadriceps", "Glutes"], [], [], "Beginner", 60),
        I("deadlift", "Hinge", ["Hamstrings", "Glutes"], ["ErectorSpinae"], ["barbell"], "Intermediate", 90),
        I("romanian-deadlift", "Hinge", ["Hamstrings", "Glutes"], ["ErectorSpinae"], ["barbell"], "Intermediate", 85),
        I("dumbbell-romanian-deadlift", "Hinge", ["Hamstrings", "Glutes"], [], ["dumbbell"], "Beginner", 80),
        I("glute-bridge", "Hinge", ["Glutes"], ["Hamstrings"], [], "Beginner", 60),
        I("bench-press", "HorizontalPush", ["Chest"], ["Triceps", "Deltoids"], ["barbell", "bench"], "Beginner", 90),
        I("db-bench-press", "HorizontalPush", ["Chest"], ["Triceps"], ["dumbbell", "bench"], "Beginner", 85),
        I("push-up", "HorizontalPush", ["Chest"], ["Triceps"], [], "Beginner", 70),
        I("ohp", "VerticalPush", ["Deltoids"], ["Triceps"], ["barbell"], "Intermediate", 90),
        I("db-shoulder-press", "VerticalPush", ["Deltoids"], ["Triceps"], ["dumbbell"], "Beginner", 85),
        I("machine-shoulder-press", "VerticalPush", ["Deltoids"], ["Triceps"], ["machine"], "Beginner", 80),
        I("barbell-row", "HorizontalPull", ["UpperBack", "Lats"], ["Biceps"], ["barbell"], "Intermediate", 90),
        I("db-row", "HorizontalPull", ["Lats", "UpperBack"], ["Biceps"], ["dumbbell"], "Beginner", 85),
        I("seated-cable-row", "HorizontalPull", ["Lats", "UpperBack"], ["Biceps"], ["cable"], "Beginner", 80),
        I("band-row", "HorizontalPull", ["Lats", "UpperBack"], ["Biceps"], ["resistance-band"], "Beginner", 60),
        I("lat-pulldown", "VerticalPull", ["Lats"], ["Biceps"], ["cable"], "Beginner", 90),
        I("pull-up", "VerticalPull", ["Lats"], ["Biceps"], ["pull-up-bar"], "Intermediate", 85),
        I("lunge", "SingleLeg", ["Quadriceps", "Glutes"], ["Hamstrings"], ["dumbbell"], "Beginner", 80),
        I("bulgarian-split-squat", "SingleLeg", ["Quadriceps", "Glutes"], ["Hamstrings"], ["dumbbell", "bench"], "Intermediate", 85),
        I("step-up", "SingleLeg", ["Quadriceps", "Glutes"], [], ["dumbbell", "bench"], "Beginner", 70),
        I("single-leg-press", "SingleLeg", ["Quadriceps", "Glutes"], [], ["machine"], "Beginner", 60),
        I("plank", "Core", ["Abdominals"], ["Obliques"], [], "Beginner", 70),
        I("cable-crunch", "Core", ["Abdominals"], [], ["cable"], "Beginner", 70),
        I("dumbbell-curl", "Accessory", ["Biceps"], ["Forearms"], ["dumbbell"], "Beginner", 80),
        I("barbell-curl", "Accessory", ["Biceps"], ["Forearms"], ["barbell"], "Beginner", 75),
        I("tricep-pushdown", "Accessory", ["Triceps"], [], ["cable"], "Beginner", 80),
        I("overhead-tricep-extension", "Accessory", ["Triceps"], [], ["dumbbell"], "Beginner", 75),
        I("lateral-raise", "Accessory", ["Deltoids"], [], ["dumbbell"], "Beginner", 80),
        I("calf-raise", "Accessory", ["Calves"], [], [], "Beginner", 70),
        I("standing-calf-raise", "Accessory", ["Calves"], [], ["machine"], "Beginner", 75),
        I("leg-curl", "Accessory", ["Hamstrings"], [], ["machine"], "Beginner", 80),
        I("leg-extension", "Accessory", ["Quadriceps"], [], ["machine"], "Beginner", 75),
        I("face-pull", "Accessory", ["Deltoids", "UpperBack"], [], ["cable"], "Beginner", 70),
        I("cable-fly", "Accessory", ["Chest"], [], ["cable"], "Beginner", 70),
        I("rowing-machine", "Cardio", ["Quadriceps"], [], ["machine"])
    ];

    public static IReadOnlyList<TrainingExerciseCandidate> Candidates() => ExerciseCandidateMapper.Map(Items()).Candidates;

    public static PlanProfile Profile(
        TrainingGoal goal = TrainingGoal.Hypertrophy, TrainingExperience experience = TrainingExperience.Intermediate,
        int days = 3, string[]? equipment = null, string[]? excluded = null, string[]? limitations = null,
        int weeks = 12, int minutes = 75, TrainingProgramSplit split = TrainingProgramSplit.Automatic) =>
        new(goal, experience, new DateOnly(2026, 11, 16),
            weeks, Enumerable.Range(0, days).Select(i => new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }[i]).ToList(),
            minutes, equipment ?? [], excluded ?? [], limitations ?? [], split, true, 4);
}
