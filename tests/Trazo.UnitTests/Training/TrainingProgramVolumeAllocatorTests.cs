using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingProgramVolumeAllocatorTests
{
    [Fact]
    public void BenchPressCountsMeaningfulTricepsStimulusAsAFullSet()
    {
        var session = Session(new TrainingExercisePrescription(
            "bench-press", "v1", "Press banca", 1, 2, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test",
            [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]));

        var volume = TrainingProgramVolumeAllocator.CalculateEffectiveVolume([session]);

        Assert.Equal(2m, volume[TrainingMuscleGroup.Chest]);
        Assert.Equal(2m, volume[TrainingMuscleGroup.Triceps]);
    }

    [Fact]
    public void RowCountsMeaningfulBicepsAndLatStimulusAsFullSets()
    {
        var session = Session(new TrainingExercisePrescription(
            "barbell-row", "v1", "Remo con barra", 1, 3, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test",
            [TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Lats]));

        var volume = TrainingProgramVolumeAllocator.CalculateEffectiveVolume([session]);

        Assert.Equal(3m, volume[TrainingMuscleGroup.UpperBack]);
        Assert.Equal(3m, volume[TrainingMuscleGroup.Biceps]);
        Assert.Equal(3m, volume[TrainingMuscleGroup.Lats]);
    }

    [Fact]
    public void QualityReportUsesTheSameEffectiveStimulusRulesAsTheAllocator()
    {
        var plan = new TrainingProgramPrescription(
            "test", "Hypertrophy", new DateOnly(2026, 11, 6), 1,
            [Session(new TrainingExercisePrescription(
                "bench-press", "v1", "Press banca", 1, 2, 8, 12, 2m, 120,
                TrainingProgressionRule.DoubleProgression, "test",
                [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]))]);

        var report = TrainingProgramQualityAnalyzer.Analyze(plan);
        var triceps = report.WeeklyMuscleVolumes.Single().Muscles
            .Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Triceps);

        Assert.Equal(0, triceps.PrimarySets);
        Assert.Equal(2, triceps.SecondarySets);
        Assert.Equal(2m, triceps.EstimatedEffectiveSets);
        Assert.DoesNotContain("0,5", report.Methodology);
    }

    [Fact]
    public void IncidentalHamstringCoContractionInSquatsDoesNotCountAsHypertrophyVolume()
    {
        var session = Session(new TrainingExercisePrescription(
            "squat", "v1", "Sentadilla", 1, 3, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test",
            [TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Hamstrings]));

        var volume = TrainingProgramVolumeAllocator.CalculateEffectiveVolume([session]);

        Assert.Equal(3m, volume[TrainingMuscleGroup.Quadriceps]);
        Assert.DoesNotContain(TrainingMuscleGroup.Hamstrings, volume.Keys);
    }

    [Theory]
    [InlineData(TrainingExperience.Beginner, 8, 12, 10, 5)]
    [InlineData(TrainingExperience.Intermediate, 10, 16, 13, 7)]
    [InlineData(TrainingExperience.Advanced, 12, 20, 16, 8)]
    public void HypertrophyDefaultsUseAgreedExperienceRanges(
        TrainingExperience experience, decimal minimum, decimal maximum, decimal target, decimal hamstringTarget)
    {
        var targets = GetWeeklyTargets(TrainingGoal.Hypertrophy, experience, null);

        Assert.Equal(target, targets[TrainingMuscleGroup.Chest]);
        Assert.Equal(target, targets[TrainingMuscleGroup.Biceps]);
        Assert.Equal(target, targets[TrainingMuscleGroup.Triceps]);
        Assert.Equal(hamstringTarget, targets[TrainingMuscleGroup.Hamstrings]);
        Assert.Equal(minimum, target - (experience == TrainingExperience.Beginner ? 2m : experience == TrainingExperience.Intermediate ? 3m : 4m));
        Assert.Equal(maximum, target + (experience == TrainingExperience.Beginner ? 2m : experience == TrainingExperience.Intermediate ? 3m : 4m));
    }

    [Fact]
    public void StrengthAndGeneralFitnessKeepTheirPreviousConservativeSupplementalTargets()
    {
        var strength = GetWeeklyTargets(TrainingGoal.Strength, TrainingExperience.Beginner, null);
        var general = GetWeeklyTargets(TrainingGoal.GeneralFitness, TrainingExperience.Beginner, null);

        Assert.Equal(6m, strength[TrainingMuscleGroup.Chest]);
        Assert.Equal(3m, strength[TrainingMuscleGroup.Biceps]);
        Assert.Equal(3m, strength[TrainingMuscleGroup.Triceps]);
        Assert.Equal(3m, strength[TrainingMuscleGroup.Hamstrings]);
        Assert.DoesNotContain(TrainingMuscleGroup.Abdominals, strength.Keys);
        Assert.Equal(4m, general[TrainingMuscleGroup.Chest]);
        Assert.Equal(2m, general[TrainingMuscleGroup.Biceps]);
        Assert.Equal(2m, general[TrainingMuscleGroup.Hamstrings]);
        Assert.DoesNotContain(TrainingMuscleGroup.Abdominals, general.Keys);
    }

    [Fact]
    public void AdvancedPriorityTargetIsCappedAtTwentyAndUsesNarrowEighteenToTwentyTwoRange()
    {
        var session = Session(new TrainingExercisePrescription(
            "bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test",
            [TrainingMuscleGroup.Chest], []));
        var result = TrainingProgramVolumeAllocator.Allocate(
            [session], TrainingGoal.Hypertrophy, TrainingExperience.Advanced,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 3)], 60);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("rango orientativo 18–22", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitTwentySeriesHypertrophyTargetUsesEighteenToTwentyTwoRange()
    {
        var target = new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, TargetWeeklyPrimarySets: 20m);
        var targets = GetWeeklyTargets(TrainingGoal.Hypertrophy, TrainingExperience.Advanced, [target]);

        Assert.Equal(20m, targets[TrainingMuscleGroup.Chest]);
    }

    [Fact]
    public void PriorityAndExplicitTargetsChangeOnlyTheRequestedMuscleDose()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []),
                new TrainingExercisePrescription("row", "v1", "Remo", 2, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps])),
            new TrainingSessionPrescription(1, DayOfWeek.Wednesday, "Sesión B", 5,
            [
                new TrainingExercisePrescription("incline", "v1", "Press inclinado", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []),
                new TrainingExercisePrescription("db-fly", "v1", "Aperturas", 2, 1, 10, 15, 2m, 90,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], [])
            ])
        };

        var defaultAllocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner, null, 60);
        var priorityAllocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 2)], 60);
        var explicitAllocation = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, TargetWeeklyPrimarySets: 4m)], 60);

        Assert.True(defaultAllocation.Succeeded, string.Join("; ", defaultAllocation.Errors));
        Assert.True(priorityAllocation.Succeeded, string.Join("; ", priorityAllocation.Errors));
        Assert.True(explicitAllocation.Succeeded, string.Join("; ", explicitAllocation.Errors));

        var defaultChest = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(defaultAllocation.Sessions)[TrainingMuscleGroup.Chest];
        var priorityChest = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(priorityAllocation.Sessions)[TrainingMuscleGroup.Chest];
        var explicitChest = TrainingProgramVolumeAllocator.CalculateEffectiveVolume(explicitAllocation.Sessions)[TrainingMuscleGroup.Chest];

        Assert.True(priorityChest > defaultChest);
        Assert.Equal(4m, explicitChest);
    }

    [Fact]
    public void MissingDirectCalfWorkIsReportedInsteadOfDroppingItsDefaultTarget()
    {
        var session = Session(new TrainingExercisePrescription(
            "bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test",
            [TrainingMuscleGroup.Chest], []));

        var result = TrainingProgramVolumeAllocator.Allocate(
            [session], TrainingGoal.Hypertrophy, TrainingExperience.Beginner, null, 60);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("Calves", StringComparison.Ordinal) &&
            warning.Contains("no hay ningún ejercicio disponible", StringComparison.Ordinal));
    }

    [Fact]
    public void DefaultAllocationPrioritizesQuadricepsGlutesAndCalvesOverHamstrings()
    {
        TrainingExercisePrescription Exercise(string id, TrainingMuscleGroup muscle) =>
            new(id, "v1", id, 1, 1, 8, 12, 2m, 90,
                TrainingProgressionRule.DoubleProgression, "test", [muscle], []);

        var sessions = new[]
        {
            Session(
                Exercise("quad-1", TrainingMuscleGroup.Quadriceps),
                Exercise("glute-1", TrainingMuscleGroup.Glutes),
                Exercise("calf-1", TrainingMuscleGroup.Calves),
                Exercise("hamstring-1", TrainingMuscleGroup.Hamstrings)),
            new TrainingSessionPrescription(1, DayOfWeek.Wednesday, "Sesión B", 5,
            [
                Exercise("quad-2", TrainingMuscleGroup.Quadriceps),
                Exercise("glute-2", TrainingMuscleGroup.Glutes),
                Exercise("calf-2", TrainingMuscleGroup.Calves),
                Exercise("hamstring-2", TrainingMuscleGroup.Hamstrings)
            ]),
            new TrainingSessionPrescription(1, DayOfWeek.Friday, "Sesión C", 5,
            [
                Exercise("quad-3", TrainingMuscleGroup.Quadriceps),
                Exercise("glute-3", TrainingMuscleGroup.Glutes),
                Exercise("calf-3", TrainingMuscleGroup.Calves),
                Exercise("hamstring-3", TrainingMuscleGroup.Hamstrings)
            ])
        };

        var defaults = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner, null, 60);
        var explicitHamstrings = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Hamstrings, TargetWeeklyPrimarySets: 8m)], 60);

        Assert.True(defaults.Succeeded, string.Join("; ", defaults.Errors));
        Assert.True(explicitHamstrings.Succeeded, string.Join("; ", explicitHamstrings.Errors));
        var defaultVolume = TrainingProgramVolumeAllocator.CalculatePrimaryVolume(defaults.Sessions);
        var explicitVolume = TrainingProgramVolumeAllocator.CalculatePrimaryVolume(explicitHamstrings.Sessions);
        Assert.Equal(10m, defaultVolume[TrainingMuscleGroup.Quadriceps]);
        Assert.Equal(10m, defaultVolume[TrainingMuscleGroup.Glutes]);
        Assert.Equal(10m, defaultVolume[TrainingMuscleGroup.Calves]);
        Assert.Equal(5m, defaultVolume[TrainingMuscleGroup.Hamstrings]);
        Assert.Equal(8m, explicitVolume[TrainingMuscleGroup.Hamstrings]);
    }

    [Fact]
    public void DefaultWeeklyTargetsFocusOnMainGroupsAndOnlyAddAccessoryGroupsWhenPrioritized()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []),
                new TrainingExercisePrescription("curl", "v1", "Curl de bíceps", 2, 1, 8, 12, 2m, 90,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Biceps], []),
                new TrainingExercisePrescription("knee-raise", "v1", "Elevación de rodillas", 3, 1, 8, 12, 2m, 90,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.HipFlexors], []))
        };

        var defaults = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner, null, 60);
        Assert.True(defaults.Succeeded, string.Join("; ", defaults.Errors));
        Assert.True(defaults.Sessions[0].Exercises.Single(x => x.ExerciseId == "bench").Sets > 1);
        Assert.Equal(5, defaults.Sessions[0].Exercises.Single(x => x.ExerciseId == "curl").Sets);
        Assert.Equal(1, defaults.Sessions[0].Exercises.Single(x => x.ExerciseId == "knee-raise").Sets);
        Assert.DoesNotContain(defaults.Warnings, warning => warning.Contains("Abdominals", StringComparison.Ordinal));

        var prioritized = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.HipFlexors, Priority: 1)], 60);
        Assert.True(prioritized.Succeeded, string.Join("; ", prioritized.Errors));
        Assert.True(prioritized.Sessions[0].Exercises.Single(x => x.ExerciseId == "knee-raise").Sets > 1);

        var explicitCore = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Abdominals, Priority: 1)], 60);
        Assert.True(explicitCore.Succeeded, string.Join("; ", explicitCore.Errors));
        Assert.Contains(explicitCore.Warnings, warning =>
            warning.Contains("Abdominals", StringComparison.Ordinal) &&
            warning.Contains("no hay ningún ejercicio disponible", StringComparison.Ordinal));
    }

    [Fact]
    public void AllocationIsDeterministicAndRespectsPerExerciseSetCeiling()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("squat", "v1", "Sentadilla", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings]),
                new TrainingExercisePrescription("leg-press", "v1", "Prensa", 2, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], []))
        };

        var first = TrainingProgramVolumeAllocator.Allocate(sessions, TrainingGoal.Hypertrophy,
            TrainingExperience.Advanced, null, 60);
        var second = TrainingProgramVolumeAllocator.Allocate(sessions, TrainingGoal.Hypertrophy,
            TrainingExperience.Advanced, null, 60);

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.Equal(
            first.Sessions.SelectMany(x => x.Exercises).Select(x => (x.ExerciseId, x.Sets)),
            second.Sessions.SelectMany(x => x.Exercises).Select(x => (x.ExerciseId, x.Sets)));
        Assert.All(first.Sessions.SelectMany(x => x.Exercises), exercise => Assert.InRange(exercise.Sets, 1, 5));
    }


    [Fact]
    public void AllocationUsesCompoundStimulusAndAcceptsTheLowerEdgeOfAnExplicitTargetRange()
    {
        var sessions = new[]
        {
            Session(
                new TrainingExercisePrescription("squat", "v1", "Sentadilla", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test",
                    [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], []),
                new TrainingExercisePrescription("glute-bridge", "v1", "Puente de glúteos", 2, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test",
                    [TrainingMuscleGroup.Glutes], []))
        };

        var result = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Quadriceps, TargetWeeklyPrimarySets: 4m),
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Glutes, TargetWeeklyPrimarySets: 2m)
            ], 60);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var volume = TrainingProgramVolumeAllocator.CalculatePrimaryVolume(result.Sessions);
        Assert.Equal(3m, volume[TrainingMuscleGroup.Quadriceps]);
        Assert.Equal(4m, volume[TrainingMuscleGroup.Glutes]);
        Assert.DoesNotContain(result.Warnings, warning => warning.Contains("Quadriceps alcanza", StringComparison.Ordinal));
    }

    [Fact]
    public void AllocationDistributesTheSameAccessoryAcrossSessionsBeforeMaxingOneSession()
    {
        var sessions = new[]
        {
            Session(new TrainingExercisePrescription("biceps-curl", "v1", "Curl de bíceps", 1, 1, 10, 15, 2m, 90,
                TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Biceps], [])),
            new TrainingSessionPrescription(1, DayOfWeek.Thursday, "Sesión B", 5,
            [
                new TrainingExercisePrescription("biceps-curl", "v1", "Curl de bíceps", 1, 1, 10, 15, 2m, 90,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Biceps], [])
            ])
        };

        var result = TrainingProgramVolumeAllocator.Allocate(
            sessions, TrainingGoal.Hypertrophy, TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Biceps, TargetWeeklyPrimarySets: 6m)], 60);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Equal(6, result.Sessions.Sum(session => session.Exercises.Single().Sets));
        Assert.Equal(new[] { 3, 3 }, result.Sessions.Select(session => session.Exercises.Single().Sets));
    }

    [Fact]
    public void DurationLimitPreventsUnboundedVolumeAndExplainsUnmetTarget()
    {
        var sessions = new[]
        {
            Session(new TrainingExercisePrescription("squat", "v1", "Sentadilla", 1, 1, 8, 12, 2m, 180,
                TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Quadriceps], []))
        };

        var result = TrainingProgramVolumeAllocator.Allocate(sessions, TrainingGoal.Hypertrophy,
            TrainingExperience.Advanced, null, 35);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.InRange(result.Sessions[0].EstimatedDurationMinutes, 1, 35);
        Assert.Contains(result.Warnings, warning => warning.Contains("Quadriceps", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, warning => warning.Contains("opción compatible", StringComparison.Ordinal));
    }

    [Fact]
    public void FractionalWeeklySetTargetsAreRejected()
    {
        var session = Session(new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []));

        var result = TrainingProgramVolumeAllocator.Allocate([session], TrainingGoal.Hypertrophy,
            TrainingExperience.Beginner,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, TargetWeeklyPrimarySets: 4.5m)], 60);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("entero", StringComparison.Ordinal));
    }

    [Fact]
    public void HypertrophyTargetsRemainEvidenceInformedAndReportTimeLimitedShortfalls()
    {
        var shortSchedule = new[]
        {
            Session(new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
                TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], [])),
            new TrainingSessionPrescription(1, DayOfWeek.Thursday, "Sesión B", 5,
            [
                new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
                    TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], [])
            ])
        };

        var shortBudget = TrainingProgramVolumeAllocator.Allocate(
            shortSchedule, TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, null, 10);
        var fullBudget = TrainingProgramVolumeAllocator.Allocate(
            shortSchedule, TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, null, 90);
        var explicitTarget = TrainingProgramVolumeAllocator.Allocate(
            shortSchedule, TrainingGoal.Hypertrophy, TrainingExperience.Intermediate,
            [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, TargetWeeklyPrimarySets: 8m)], 35);

        Assert.True(shortBudget.Succeeded, string.Join("; ", shortBudget.Errors));
        Assert.True(fullBudget.Succeeded, string.Join("; ", fullBudget.Errors));
        Assert.True(explicitTarget.Succeeded, string.Join("; ", explicitTarget.Errors));
        Assert.Equal(4, shortBudget.Sessions.Sum(session => session.Exercises.Single().Sets));
        Assert.Equal(10, fullBudget.Sessions.Sum(session => session.Exercises.Single().Sets));
        Assert.Equal(8, explicitTarget.Sessions.Sum(session => session.Exercises.Single().Sets));
        Assert.Contains(shortBudget.Warnings, warning => warning.Contains("rango orientativo", StringComparison.Ordinal));
        Assert.DoesNotContain(fullBudget.Warnings, warning => warning.Contains("rango orientativo", StringComparison.Ordinal));
    }

    [Fact]
    public void StrengthAllocationCapsEveryExerciseAtThreeWorkingSets()
    {
        var session = Session(
            new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 5, 3, 6, 2m, 180,
                TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []),
            new TrainingExercisePrescription("row", "v1", "Remo", 2, 5, 3, 6, 2m, 180,
                TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.UpperBack], []));

        var result = TrainingProgramVolumeAllocator.Allocate(
            [session], TrainingGoal.Strength, TrainingExperience.Advanced, null, 90);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.All(result.Sessions.SelectMany(item => item.Exercises),
            exercise => Assert.InRange(exercise.Sets, 1, 3));
        Assert.All(result.Sessions.SelectMany(item => item.Exercises),
            exercise => Assert.Equal(3, exercise.Sets));
    }

    [Fact]
    public void DuplicateOrOutOfRangeMusclePreferencesAreRejected()
    {
        var session = Session(new TrainingExercisePrescription("bench", "v1", "Press banca", 1, 1, 8, 12, 2m, 120,
            TrainingProgressionRule.DoubleProgression, "test", [TrainingMuscleGroup.Chest], []));

        var result = TrainingProgramVolumeAllocator.Allocate([session], TrainingGoal.Hypertrophy,
            TrainingExperience.Beginner,
            [
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 1),
                new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Chest, Priority: 2)
            ], 60);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("repite", StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<TrainingMuscleGroup, decimal> GetWeeklyTargets(
        TrainingGoal goal,
        TrainingExperience experience,
        IReadOnlyList<TrainingMuscleVolumeTarget>? preferences)
    {
        var method = typeof(TrainingProgramVolumeAllocator).GetMethod(
            "GetWeeklyTargets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (IReadOnlyDictionary<TrainingMuscleGroup, decimal>)method.Invoke(
            null, [goal, experience, preferences, 3, 60])!;
    }

    private static TrainingSessionPrescription Session(params TrainingExercisePrescription[] exercises) =>
        new(1, DayOfWeek.Monday, "Sesión", 5, exercises);
}
