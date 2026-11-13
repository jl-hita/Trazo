using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingManualVolumeAnalysisTests
{
    [Fact]
    public void ManualVolumeReportWarnsWhenEffectiveWeeklyVolumeExceedsTwentySets()
    {
        var benchPress = new TrainingExerciseCandidate(
            "bench-press", "catalog-v1", "Press banca",
            TrainingMovementPattern.HorizontalPush,
            Array.Empty<string>(),
            [TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner,
            100,
            true,
            [TrainingMuscleGroup.Chest],
            [TrainingMuscleGroup.Triceps]);

        var result = TrainingProgramQualityAnalyzer.AnalyzeManual(
            [benchPress],
            [
                new TrainingManualVolumeSessionInput(DayOfWeek.Monday, "Torso A",
                    [new TrainingManualVolumeExerciseInput("bench-press", "Press banca", 7)]),
                new TrainingManualVolumeSessionInput(DayOfWeek.Wednesday, "Torso B",
                    [new TrainingManualVolumeExerciseInput("bench-press", "Press banca", 7)]),
                new TrainingManualVolumeSessionInput(DayOfWeek.Friday, "Torso C",
                    [new TrainingManualVolumeExerciseInput("bench-press", "Press banca", 7)])
            ]);

        Assert.Contains(result.QualityReport.Warnings,
            warning => warning.Contains("Chest acumula 21 series", StringComparison.Ordinal));
        Assert.Contains(result.QualityReport.Warnings,
            warning => warning.Contains("Triceps acumula 21 series", StringComparison.Ordinal));
    }

    [Fact]
    public void ManualVolumeUsesStableIdsAndDoesNotGuessAmbiguousExerciseNames()
    {
        var benchPress = new TrainingExerciseCandidate(
            "bench-press", "catalog-v1", "Press banca",
            TrainingMovementPattern.HorizontalPush,
            Array.Empty<string>(),
            [TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner,
            100,
            true,
            [TrainingMuscleGroup.Chest],
            [TrainingMuscleGroup.Triceps]);
        var ambiguousExercise = new TrainingExerciseCandidate(
            "machine-chest-press", "catalog-v1", "Press banca",
            TrainingMovementPattern.HorizontalPush,
            ["machine"],
            [TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner,
            90,
            true,
            [TrainingMuscleGroup.Deltoids],
            [TrainingMuscleGroup.Chest]);

        var result = TrainingProgramQualityAnalyzer.AnalyzeManual(
            [benchPress, ambiguousExercise],
            [
                new TrainingManualVolumeSessionInput(DayOfWeek.Monday, "Con ID estable",
                    [new TrainingManualVolumeExerciseInput("bench-press", "Press banca", 3)]),
                new TrainingManualVolumeSessionInput(DayOfWeek.Wednesday, "Nombre ambiguo",
                    [new TrainingManualVolumeExerciseInput(null, "Press banca", 4)])
            ]);

        Assert.Contains("Press banca", result.UnclassifiedExerciseNames);
        Assert.Equal(3m, Assert.Single(result.SessionReports[0].QualityReport.WeeklyMuscleVolumes[0].Muscles,
            muscle => muscle.MuscleGroup == TrainingMuscleGroup.Chest).EstimatedEffectiveSets);
        Assert.Equal(0m, Assert.Single(result.SessionReports[1].QualityReport.WeeklyMuscleVolumes[0].Muscles,
            muscle => muscle.MuscleGroup == TrainingMuscleGroup.Chest).EstimatedEffectiveSets);
        Assert.Contains("Press banca", result.SessionReports[1].UnclassifiedExerciseNames);
    }

    [Fact]
    public void ManualVolumeKeepsExercisesWithoutReviewedMuscleMetadataUnclassified()
    {
        var incompleteExercise = new TrainingExerciseCandidate(
            "unknown-muscles", "catalog-v1", "Ejercicio sin músculos revisados",
            TrainingMovementPattern.Accessory,
            Array.Empty<string>(),
            [TrainingGoal.Hypertrophy],
            TrainingExperience.Beginner,
            50,
            false,
            [],
            []);

        var result = TrainingProgramQualityAnalyzer.AnalyzeManual(
            [incompleteExercise],
            [
                new TrainingManualVolumeSessionInput(DayOfWeek.Monday, "Sesión manual",
                    [new TrainingManualVolumeExerciseInput("unknown-muscles", "Ejercicio sin músculos revisados", 3)])
            ]);

        Assert.Contains("Ejercicio sin músculos revisados", result.UnclassifiedExerciseNames);
        Assert.Equal(0m, result.QualityReport.WeeklyMuscleVolumes[0].Muscles.Sum(muscle => muscle.EstimatedEffectiveSets));
    }

    [Fact]
    public void ManualVolumeReportKeepsSessionsSeparateEvenWhenTheyShareTheSameWeekday()
    {
        var benchPress = new TrainingExerciseCandidate(
            "bench-press", "catalog-v1", "Press banca",
            TrainingMovementPattern.HorizontalPush,
            Array.Empty<string>(),
            [TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner,
            100,
            true,
            [TrainingMuscleGroup.Chest],
            [TrainingMuscleGroup.Triceps]);

        var result = TrainingProgramQualityAnalyzer.AnalyzeManual(
            [benchPress],
            [
                new TrainingManualVolumeSessionInput(DayOfWeek.Monday, "Torso A",
                    [new TrainingManualVolumeExerciseInput("bench-press", "Press banca", 3)]),
                new TrainingManualVolumeSessionInput(DayOfWeek.Monday, "Torso B",
                    [new TrainingManualVolumeExerciseInput("bench-press", "Press banca", 2)])
            ]);

        var weekly = Assert.Single(result.QualityReport.WeeklyMuscleVolumes);
        Assert.Equal(5m, Assert.Single(weekly.Muscles, x => x.MuscleGroup == TrainingMuscleGroup.Chest).EstimatedEffectiveSets);
        Assert.Equal(5, Assert.Single(weekly.Muscles, x => x.MuscleGroup == TrainingMuscleGroup.Triceps).EstimatedEffectiveSets);
        Assert.Equal(2, result.SessionReports.Count);
        Assert.Equal("Torso A", result.SessionReports[0].Title);
        Assert.Equal("Torso B", result.SessionReports[1].Title);
        Assert.Equal(3m, Assert.Single(result.SessionReports[0].QualityReport.WeeklyMuscleVolumes[0].Muscles,
            x => x.MuscleGroup == TrainingMuscleGroup.Chest).EstimatedEffectiveSets);
        Assert.Equal(2m, Assert.Single(result.SessionReports[1].QualityReport.WeeklyMuscleVolumes[0].Muscles,
            x => x.MuscleGroup == TrainingMuscleGroup.Chest).EstimatedEffectiveSets);
    }
}
