using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingProgramGeneratorTests
{
    [Fact]
    public void GeneratesDeterministicPlanForAllRequestedWeeksAndDays()
    {
        var request = Request();
        var first = TrainingProgramGenerator.Generate(request, Catalog());
        var second = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.True(first.Succeeded, string.Join("; ", first.Errors));
        Assert.NotNull(first.Prescription);
        Assert.Equal(8, first.Prescription.Sessions.Count);
        Assert.Equal(
            first.Prescription.Sessions.Select(SessionSignature),
            second.Prescription!.Sessions.Select(SessionSignature));
        Assert.Empty(TrainingPrescriptionValidator.Validate(first.Prescription));
    }

    [Fact]
    public void GeneratorRespectsRequestedDaysAndSessionDuration()
    {
        var request = Request() with
        {
            TrainingDays = [DayOfWeek.Tuesday, DayOfWeek.Thursday],
            MaximumSessionDurationMinutes = 35
        };

        var result = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.All(result.Prescription!.Sessions, session =>
        {
            Assert.Contains(session.Day, new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday });
            Assert.InRange(session.EstimatedDurationMinutes, 1, 35);
        });
        Assert.Equal(8, result.Prescription.Sessions.Count);
    }

    [Fact]
    public void TwoDayFullBodySplitBalancesTwoSquatExposuresWithOneHipHingeExposure()
    {
        var result = TrainingProgramGenerator.Generate(
            Request() with { DurationWeeks = 1 }, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var exercises = result.Prescription!.Sessions
            .SelectMany(session => session.Exercises)
            .ToArray();

        Assert.Equal(2, exercises.Count(exercise => exercise.ExerciseId == "squat-basic"));
        Assert.Single(exercises, exercise => exercise.ExerciseId == "exercise-hinge");
        Assert.Single(exercises, exercise => exercise.ExerciseId == "exercise-singleleg");
    }

    [Fact]
    public void SessionDurationEstimateAccountsForGoalRestAndWorkInsteadOfFixedPatternCount()
    {
        var general = TrainingProgramGenerator.Generate(
            Request() with { Goal = TrainingGoal.GeneralFitness, Experience = TrainingExperience.Advanced }, Catalog());
        var strength = TrainingProgramGenerator.Generate(
            Request() with { Goal = TrainingGoal.Strength, Experience = TrainingExperience.Advanced }, Catalog());

        Assert.True(general.Succeeded, string.Join("; ", general.Errors));
        Assert.True(strength.Succeeded, string.Join("; ", strength.Errors));
        Assert.True(
            strength.Prescription!.Sessions.Max(x => x.EstimatedDurationMinutes) >
            general.Prescription!.Sessions.Max(x => x.EstimatedDurationMinutes));
        Assert.All(strength.Prescription.Sessions, session => Assert.InRange(session.EstimatedDurationMinutes, 1, 60));
    }

    [Fact]
    public void AdvancedHypertrophyKeepsBuildWeekPrescriptionAndReportsHighVolumeForReview()
    {
        foreach (var days in new[]
        {
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }
        })
        {
            var result = TrainingProgramGenerator.Generate(Request() with
            {
                Goal = TrainingGoal.Hypertrophy,
                Experience = TrainingExperience.Advanced,
                TrainingDays = days
            }, Catalog());

            Assert.True(result.Succeeded, string.Join("; ", result.Errors));
            var plan = result.Prescription!;
            var buildWeekLowerBody = plan.Sessions
                .Where(session => session.WeekNumber is 2 or 3)
                .SelectMany(session => session.Exercises)
                .Where(exercise => exercise.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Glutes))
                .ToArray();

            Assert.NotEmpty(buildWeekLowerBody);
            var gluteVolume = plan.QualityReport!.WeeklyMuscleVolumes
                .Where(week => week.WeekNumber is 2 or 3)
                .SelectMany(week => week.Muscles)
                .Where(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Glutes)
                .ToArray();
            Assert.All(gluteVolume, muscle =>
            {
                // Advanced hypertrophy uses a 12–20 effective-set range; the central
                // target is 16, with no requirement to reach its upper edge.
                Assert.InRange(muscle.EstimatedEffectiveSets, 12m, 20m);
                Assert.True(muscle.EstimatedEffectiveSets >= muscle.PrimarySets);
            });
            Assert.NotEmpty(plan.QualityReport.WeeklyMuscleVolumes);
        }
    }

    [Fact]
    public void GeneratorKeepsExerciseSelectionAcrossMesocycleWeeksWhenCompatible()
    {
        var alternative = new TrainingExerciseCandidate(
            "squat-alternative", "catalog-2026-10", "Sentadilla alternativa",
            TrainingMovementPattern.Squat, [],
            [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner, 40, true,
            [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes],
            [TrainingMuscleGroup.Hamstrings]);
        var result = TrainingProgramGenerator.Generate(
            Request() with { DurationWeeks = 4 },
            Catalog().Append(alternative).ToArray());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var squatByWeek = result.Prescription!.Sessions
            .SelectMany(session => session.Exercises.Select(exercise => new { session.WeekNumber, Exercise = exercise }))
            .Where(item => item.Exercise.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.Quadriceps))
            .GroupBy(item => item.WeekNumber)
            .ToDictionary(group => group.Key, group => group.First().Exercise.ExerciseId);

        Assert.Equal("squat-basic", squatByWeek[1]);
        Assert.All(squatByWeek.Values, exerciseId => Assert.Equal(squatByWeek[1], exerciseId));
    }

    [Fact]
    public void GeneratorUsesDifferentHorizontalPullExercisesWithinWeekWhenAlternativesExist()
    {
        var alternative = new TrainingExerciseCandidate(
            "horizontal-pull-alternative", "catalog-2026-10", "Remo alternativo",
            TrainingMovementPattern.HorizontalPull, [],
            [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
            TrainingExperience.Beginner, 40, true,
            [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Lats],
            [TrainingMuscleGroup.Biceps]);
        var result = TrainingProgramGenerator.Generate(
            Request() with
            {
                DurationWeeks = 4,
                TrainingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday]
            },
            Catalog().Append(alternative).ToArray());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var weeklyPulls = result.Prescription!.Sessions
            .Where(session => session.WeekNumber == 1)
            .SelectMany(session => session.Exercises)
            .Where(exercise => exercise.PrimaryMuscleGroups.Contains(TrainingMuscleGroup.UpperBack))
            .Select(exercise => exercise.ExerciseId)
            .ToArray();

        Assert.Equal(2, weeklyPulls.Length);
        Assert.Equal(2, weeklyPulls.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MissingPatternErrorReportsCandidateFilteringStages()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.HorizontalPull
                ? exercise with { IsReviewed = false }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Errors);
        Assert.Contains("patrón 'HorizontalPull'", error);
        Assert.Contains("revisados/propios=", error);
        Assert.Contains("objetivo=", error);
        Assert.Contains("tras exclusiones=", error);
    }

    [Fact]
    public void DirectCoreExercisesAreExcludedByDefaultButIndirectCoreWorkRemainsEligible()
    {
        var catalog = Catalog()
            .Select(exercise => exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with
                {
                    SecondaryMuscleGroups = [TrainingMuscleGroup.Abdominals, TrainingMuscleGroup.ErectorSpinae]
                }
                : exercise)
            .Append(new TrainingExerciseCandidate(
                "direct-ab-crunch", "catalog-2026-10", "Crunch abdominal",
                TrainingMovementPattern.Accessory, [],
                [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
                TrainingExperience.Beginner, 100, true,
                [TrainingMuscleGroup.Abdominals], []))
            .ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { DurationWeeks = 1 }, catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var exercises = result.Prescription!.Sessions.SelectMany(session => session.Exercises).ToArray();

        Assert.DoesNotContain(exercises, exercise => exercise.ExerciseId == "exercise-core");
        Assert.DoesNotContain(exercises, exercise => exercise.ExerciseId == "direct-ab-crunch");
        Assert.Contains(exercises, exercise =>
            exercise.ExerciseId == "squat-basic" &&
            exercise.SecondaryMuscleGroups.Contains(TrainingMuscleGroup.Abdominals));
    }

    [Fact]
    public void ExplicitProfessionalCorePriorityRestoresDirectCoreExercises()
    {
        var result = TrainingProgramGenerator.Generate(
            Request() with
            {
                DurationWeeks = 1,
                MuscleVolumeTargets =
                [
                    new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Abdominals, Priority: 3)
                ]
            },
            Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "exercise-core");
    }

    [Fact]
    public void ExcludedExerciseIsNeverSelected()
    {
        var result = TrainingProgramGenerator.Generate(
            Request() with { ExcludedExerciseIds = ["squat-basic"] },
            Catalog().Append(new TrainingExerciseCandidate(
                "squat-alternative", "catalog-2026-10", "Sentadilla alternativa",
                TrainingMovementPattern.Squat, [],
                [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
                TrainingExperience.Beginner, 40, true,
                [TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes],
                [TrainingMuscleGroup.ErectorSpinae])).ToArray());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.DoesNotContain(result.Prescription!.Sessions.SelectMany(x => x.Exercises),
            exercise => exercise.ExerciseId == "squat-basic");
    }

    [Fact]
    public void UnreviewedOrEquipmentIncompatibleExercisesAreNotSelected()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { IsReviewed = false }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No hay ejercicios compatibles para el patrón", StringComparison.Ordinal));
    }

    [Fact]
    public void OwnerCanGenerateWithTheirOwnPendingExerciseButNotAnUnownedPendingExercise()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { IsReviewed = false, IsOwnedByCurrentProfessional = true }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Prescription!.Sessions.SelectMany(x => x.Exercises), exercise => exercise.ExerciseId == "squat-basic");

        var notOwned = catalog.Select(exercise => exercise with { IsOwnedByCurrentProfessional = false }).ToArray();
        var denied = TrainingProgramGenerator.Generate(Request(), notOwned);
        Assert.False(denied.Succeeded);
        Assert.Contains(denied.Errors, error => error.Contains("No hay ejercicios compatibles para el patrón", StringComparison.Ordinal));
    }

    [Fact]
    public void ExerciseCatalogRequiresReviewedMuscleGroups()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { PrimaryMuscleGroups = [] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("al menos un grupo muscular principal", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratedPlanSnapshotsPrimaryAndSecondaryMuscleGroups()
    {
        var result = TrainingProgramGenerator.Generate(Request(), Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var push = result.Prescription!.Sessions.SelectMany(session => session.Exercises)
            .First(exercise => exercise.ExerciseId == "exercise-horizontalpush");
        Assert.Contains(TrainingMuscleGroup.Chest, push.PrimaryMuscleGroups);
        Assert.Contains(TrainingMuscleGroup.Triceps, push.SecondaryMuscleGroups);
    }

    [Fact]
    public void InvalidAvailabilityIsRejectedBeforeGeneration()
    {
        var request = Request() with
        {
            TrainingDays = [DayOfWeek.Monday, DayOfWeek.Monday]
        };

        var result = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No se puede repetir un día", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateCatalogIdsAreRejected()
    {
        var catalog = Catalog().Append(Catalog()[0]).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("ID duplicado", StringComparison.Ordinal));
    }

    [Fact]
    public void UndefinedSuitableGoalIsRejected()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { SuitableGoals = [(TrainingGoal)999] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("objetivo compatible no válido", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateSuitableGoalsAreRejected()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { SuitableGoals = [TrainingGoal.Hypertrophy, TrainingGoal.Hypertrophy] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("repite objetivos compatibles", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyEquipmentSelectionMeansNoEquipmentRestriction()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.HorizontalPull
                ? exercise with { RequiredEquipment = ["cable-machine"] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = [] },
            catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "exercise-horizontalpull");
    }

    [Fact]
    public void CandidateSelectionHonorsExplicitEquipmentRestrictionsAndGoal()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with
                {
                    ExerciseId = "squat-gym-only",
                    RequiredEquipment = ["barbell"],
                    SuitableGoals = [TrainingGoal.Strength]
                }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { Goal = TrainingGoal.Hypertrophy, AvailableEquipment = ["dumbbell"] },
            catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No hay ejercicios compatibles para el patrón", StringComparison.Ordinal));
    }

    [Fact]
    public void EquipmentAliasesWithUnderscoresMatchCanonicalRequirements()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.HorizontalPull
                ? exercise with { RequiredEquipment = ["pull-up-bar"] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = ["pull_up_bar"] },
            catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "exercise-horizontalpull");
    }

    [Fact]
    public void GenericMachineEquipmentMatchesSpecializedMachineRequirements()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { RequiredEquipment = ["leg-press-machine"] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = ["machine"] },
            catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.Contains(result.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "squat-basic");
    }

    [Fact]
    public void OneSpecificMachineDoesNotSatisfyAnotherSpecificMachineRequirement()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { RequiredEquipment = ["leg-press-machine"] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = ["hack-squat-machine"] },
            catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No hay ejercicios compatibles para el patrón", StringComparison.Ordinal));
    }

    [Fact]
    public void GenericMachineDoesNotSatisfyBenchOrOtherNonMachineRequirements()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { RequiredEquipment = ["bench", "kettlebell"] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = ["machine", "kettlebell"] },
            catalog);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("No hay ejercicios compatibles para el patrón", StringComparison.Ordinal));
    }

    [Fact]
    public void SpecificMachineEquipmentSatisfiesGenericMachineRequirements()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { RequiredEquipment = ["machine"] }
                : exercise).ToArray();

        var result = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = ["leg-press-machine"] },
            catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
    }

    [Fact]
    public void WeeklyVolumeAllocationDoesNotImposeAnUnconditionalCalendarDeload()
    {
        var request = Request() with { DurationWeeks = 8 };
        var result = TrainingProgramGenerator.Generate(request, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var sessions = result.Prescription!.Sessions;
        var signatureByWeek = sessions
            .GroupBy(session => session.WeekNumber)
            .ToDictionary(group => group.Key, group => group
                .SelectMany(session => session.Exercises)
                .OrderBy(exercise => exercise.ExerciseId, StringComparer.Ordinal)
                .Select(exercise => (exercise.ExerciseId, exercise.Sets))
                .ToArray());

        // Weeks 1-2 are the ramp-in and the last week the peak (see TrainingBlockProgressionTests);
        // the weeks in between keep the allocated dose.
        Assert.Equal(signatureByWeek[3], signatureByWeek[4]);
        Assert.Equal(signatureByWeek[3], signatureByWeek[7]);
        Assert.All(signatureByWeek[8].Zip(signatureByWeek[3]), pair =>
            Assert.InRange(pair.First.Sets - pair.Second.Sets, 0, 1));
        Assert.All(sessions.SelectMany(session => session.Exercises), exercise =>
            Assert.InRange(exercise.TargetRepetitionsInReserve!.Value, 0m, 5m));
        Assert.Empty(TrainingPrescriptionValidator.Validate(result.Prescription));
    }

    [Fact]
    public void MainBilateralPatternsKeepAtLeastTwoBaseSetsWhileAccessoriesStayCompact()
    {
        var catalog = Catalog();
        var patterns = catalog.ToDictionary(candidate => candidate.ExerciseId, candidate => candidate.MovementPattern,
            StringComparer.Ordinal);
        var result = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = ["machine"], MaximumSessionDurationMinutes = 90 }, catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        foreach (var exercise in result.Prescription!.Sessions.SelectMany(session => session.Exercises))
        {
            var pattern = patterns[exercise.ExerciseId];
            if (pattern is TrainingMovementPattern.Squat or TrainingMovementPattern.Hinge
                or TrainingMovementPattern.HorizontalPush or TrainingMovementPattern.VerticalPush
                or TrainingMovementPattern.HorizontalPull or TrainingMovementPattern.VerticalPull)
                Assert.True(exercise.Sets >= 2, $"{exercise.ExerciseNameSnapshot} should retain its two-set base.");
        }
    }

    [Fact]
    public void AccessorySlotsScaleDownForShortSessionsToProtectCoreMovementPatterns()
    {
        var catalog = Catalog();
        var accessoryIds = catalog.Where(candidate => candidate.MovementPattern == TrainingMovementPattern.Accessory)
            .Select(candidate => candidate.ExerciseId)
            .ToHashSet(StringComparer.Ordinal);

        var shortFullBody = TrainingProgramGenerator.Generate(
            Request() with { MaximumSessionDurationMinutes = 35 }, catalog);
        Assert.True(shortFullBody.Succeeded, string.Join("; ", shortFullBody.Errors));
        Assert.All(shortFullBody.Prescription!.Sessions, session =>
            Assert.InRange(session.Exercises.Count(exercise => accessoryIds.Contains(exercise.ExerciseId)), 0, 1));

        var longFullBody = TrainingProgramGenerator.Generate(
            Request() with { MaximumSessionDurationMinutes = 90 }, catalog);
        Assert.True(longFullBody.Succeeded, string.Join("; ", longFullBody.Errors));
        Assert.All(longFullBody.Prescription!.Sessions, session =>
            Assert.InRange(session.Exercises.Count(exercise => accessoryIds.Contains(exercise.ExerciseId)), 0, 4));
    }

    [Fact]
    public void QualityReportCalculatesEffectiveVolumeAndFrequencyByWeek()
    {
        var result = TrainingProgramGenerator.Generate(Request() with { DurationWeeks = 4 }, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var report = result.Prescription!.QualityReport;
        Assert.NotNull(report);
        Assert.Equal(4, report.WeeklyMuscleVolumes.Count);

        var weekOneQuads = report.WeeklyMuscleVolumes.Single(week => week.WeekNumber == 1)
            .Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Quadriceps);
        var weekTwoQuads = report.WeeklyMuscleVolumes.Single(week => week.WeekNumber == 2)
            .Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Quadriceps);
        var weekFourQuads = report.WeeklyMuscleVolumes.Single(week => week.WeekNumber == 4)
            .Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Quadriceps);

        // The two-day full-body split includes a squat exposure on both days.
        // Count the audited effective dose, not only the provider's primary label.
        var weekThreeQuads = report.WeeklyMuscleVolumes.Single(week => week.WeekNumber == 3)
            .Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Quadriceps);
        Assert.InRange(weekThreeQuads.EstimatedEffectiveSets, 8m, 12m);
        Assert.True(weekOneQuads.EstimatedEffectiveSets <= weekTwoQuads.EstimatedEffectiveSets);
        Assert.True(weekTwoQuads.EstimatedEffectiveSets <= weekThreeQuads.EstimatedEffectiveSets);
        Assert.Equal(2, weekOneQuads.PrimaryFrequencySessions);
        // The ramp adds at most one set per muscle in the last week.
        Assert.InRange(weekFourQuads.EstimatedEffectiveSets - weekThreeQuads.EstimatedEffectiveSets, 0m, 1m);
        Assert.DoesNotContain(report.Warnings, warning =>
            warning.Contains("Quadriceps alcanza", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Warnings, warning =>
            warning.Contains("volumen efectivo", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(report.Warnings, warning => warning.Contains("Adductors", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Warnings, warning => warning.Contains("RotatorCuff", StringComparison.Ordinal));
    }

    [Fact]
    public void QualityReportKeepsUncoveredAccessoryMusclesVisibleWithoutCallingThemAutomaticDefects()
    {
        var result = TrainingProgramGenerator.Generate(Request(), Catalog());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        var planWithoutCalfCoverage = result.Prescription! with
        {
            Sessions = result.Prescription.Sessions.Select(session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    PrimaryMuscleGroups = exercise.PrimaryMuscleGroups.Where(muscle => muscle != TrainingMuscleGroup.Calves).ToArray(),
                    SecondaryMuscleGroups = exercise.SecondaryMuscleGroups.Where(muscle => muscle != TrainingMuscleGroup.Calves).ToArray()
                }).ToArray()
            }).ToArray(),
            QualityReport = null
        };

        var report = TrainingProgramQualityAnalyzer.Analyze(planWithoutCalfCoverage);
        var calves = report.WeeklyMuscleVolumes[0].Muscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Calves);
        Assert.Equal(0, calves.PrimarySets);
        Assert.Equal(0, calves.SecondarySets);
        Assert.DoesNotContain(report.Warnings, warning => warning.Contains("Calves", StringComparison.Ordinal));
    }

    [Fact]
    public void QualityReportFlagsVeryHighEffectiveSetVolumeWithoutRejectingThePlan()
    {
        var result = TrainingProgramGenerator.Generate(Request(), Catalog());
        Assert.True(result.Succeeded, string.Join("; ", result.Errors));

        var exaggeratedPlan = result.Prescription! with
        {
            Sessions = result.Prescription.Sessions.Select(session => session with
            {
                Exercises = session.Exercises.Select(exercise => exercise with
                {
                    Sets = 6,
                    PrimaryMuscleGroups = [TrainingMuscleGroup.Quadriceps],
                    SecondaryMuscleGroups = []
                }).ToArray()
            }).ToArray(),
            QualityReport = null
        };

        var report = TrainingProgramQualityAnalyzer.Analyze(exaggeratedPlan);
        Assert.Contains(report.Warnings, warning => warning.Contains("series con estímulo efectivo", StringComparison.Ordinal));
    }

    [Fact]
    public void GeneratorMaintainsCoreInvariantsAcrossGoalExperienceDurationAndAvailabilityMatrix()
    {
        var dayOptions = new[]
        {
            new[] { DayOfWeek.Monday, DayOfWeek.Thursday },
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday }
        };
        var durations = new[] { 1, 2, 3, 4, 5, 8 };

        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in dayOptions)
        foreach (var weeks in durations)
        {
            var request = Request() with
            {
                Goal = goal,
                Experience = experience,
                DurationWeeks = weeks,
                TrainingDays = days,
                MaximumSessionDurationMinutes = 60
            };

            var result = TrainingProgramGenerator.Generate(request, Catalog());

            Assert.True(result.Succeeded,
                $"Goal={goal}, experiencia={experience}, días={days.Length}, semanas={weeks}: {string.Join("; ", result.Errors)}");
            var plan = result.Prescription!;
            // High-volume diagnostics are advisory and must not invalidate a structurally valid plan.
            Assert.NotNull(plan.QualityReport);
            Assert.Equal(weeks * days.Length, plan.Sessions.Count);
            Assert.Equal(weeks, plan.QualityReport!.WeeklyMuscleVolumes.Count);
            Assert.Empty(TrainingPrescriptionValidator.Validate(plan));
            Assert.Equal(
                plan.Sessions.Count,
                plan.Sessions.Select(session => (session.WeekNumber, session.Day)).Distinct().Count());
            Assert.All(plan.Sessions, session =>
            {
                Assert.InRange(session.WeekNumber, 1, weeks);
                Assert.Contains(session.Day, days);
                Assert.InRange(session.EstimatedDurationMinutes, 1, request.MaximumSessionDurationMinutes);
                Assert.NotEmpty(session.Exercises);
                Assert.All(session.Exercises, exercise =>
                {
                    Assert.Contains(exercise.ExerciseId, Catalog().Select(candidate => candidate.ExerciseId));
                    Assert.NotEmpty(exercise.PrimaryMuscleGroups);
                    Assert.InRange(exercise.Sets, 1, 5);
                    Assert.InRange(exercise.MinRepetitions, 1, exercise.MaxRepetitions);
                    Assert.NotNull(exercise.TargetRepetitionsInReserve);
                    Assert.InRange(exercise.TargetRepetitionsInReserve!.Value, 0m, 5m);
                    Assert.True(exercise.RestSeconds > 0);
                });
            });
        }
    }

    [Fact]
    public void GeneratorAddsOptionalAccessoryForAnUncoveredPrimaryMuscle()
    {
        var generated = TrainingProgramGenerator.Generate(Request(), Catalog());

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        Assert.Contains(generated.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "exercise-accessory");
    }

    [Fact]
    public void GeneratorAddsOneDirectHamstringAccessoryWhenWeeklyCoverageIsMissingAndTimeAllows()
    {
        var catalog = Catalog().Append(new TrainingExerciseCandidate(
            "seated-leg-curl", "test", "Curl femoral sentado", TrainingMovementPattern.Accessory,
            ["leg-curl-machine"], [TrainingGoal.Hypertrophy, TrainingGoal.GeneralFitness],
            TrainingExperience.Beginner, 10, true,
            [TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.Calves])).ToArray();

        var generated = TrainingProgramGenerator.Generate(
            Request() with { MaximumSessionDurationMinutes = 90 }, catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var weekOneHamstringAccessories = generated.Prescription!.Sessions
            .Where(session => session.WeekNumber == 1)
            .SelectMany(session => session.Exercises)
            .Where(exercise => exercise.ExerciseId == "seated-leg-curl")
            .ToArray();
        Assert.Single(weekOneHamstringAccessories);
        Assert.All(generated.Prescription.Sessions, session =>
            Assert.InRange(session.EstimatedDurationMinutes, 1, 90));
    }

    [Fact]
    public void GeneratorUsesBandHamstringCurlWhenOnlyHomeBandEquipmentIsAvailable()
    {
        var catalog = Catalog().Append(new TrainingExerciseCandidate(
            "banded-standing-curl", "test", "Curl de pierna de pie con banda", TrainingMovementPattern.Accessory,
            ["resistance-band"], [TrainingGoal.Hypertrophy, TrainingGoal.GeneralFitness],
            TrainingExperience.Beginner, 42, true,
            [TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.Calves])).ToArray();

        var generated = TrainingProgramGenerator.Generate(
            Request() with
            {
                AvailableEquipment = ["resistance-band"],
                MaximumSessionDurationMinutes = 90
            },
            catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var weekOneHamstringCurls = generated.Prescription!.Sessions
            .Where(session => session.WeekNumber == 1)
            .SelectMany(session => session.Exercises)
            .Where(exercise => exercise.ExerciseId == "banded-standing-curl")
            .ToArray();
        Assert.Single(weekOneHamstringCurls);
        Assert.All(weekOneHamstringCurls, exercise =>
            Assert.Equal(new[] { TrainingMuscleGroup.Hamstrings }, exercise.PrimaryMuscleGroups));
    }

    [Fact]
    public void GeneratorUsesReverseNordicAsDirectQuadAccessoryWhenNoExtensionMachineIsAvailable()
    {
        var catalog = Catalog().Append(new TrainingExerciseCandidate(
            "reverse-nordic-curl", "test", "Nordic Curl Inverso", TrainingMovementPattern.Accessory,
            [], [TrainingGoal.Hypertrophy, TrainingGoal.GeneralFitness],
            TrainingExperience.Intermediate, 58, true,
            [TrainingMuscleGroup.Quadriceps],
            [TrainingMuscleGroup.HipFlexors, TrainingMuscleGroup.Abdominals])).ToArray();

        var generated = TrainingProgramGenerator.Generate(
            Request() with
            {
                Experience = TrainingExperience.Intermediate,
                AvailableEquipment = ["barbell", "dumbbell", "bench", "bodyweight"],
                MaximumSessionDurationMinutes = 90,
                MuscleVolumeTargets =
                [
                    new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Glutes, TargetWeeklyPrimarySets: 2m),
                    new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Quadriceps, TargetWeeklyPrimarySets: 6m)
                ]
            },
            catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var weekOneSessions = generated.Prescription!.Sessions
            .Where(session => session.WeekNumber == 1)
            .ToArray();
        Assert.Equal(2, weekOneSessions.Length);
        var reverseNordics = weekOneSessions.SelectMany(session => session.Exercises)
            .Where(exercise => exercise.ExerciseId == "reverse-nordic-curl")
            .ToArray();
        Assert.Single(reverseNordics);
        Assert.Contains(TrainingMuscleGroup.Quadriceps, reverseNordics[0].PrimaryMuscleGroups);
        Assert.Contains(generated.Prescription.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "squat-basic");
    }

    [Fact]
    public void GeneratorPrioritizesDirectQuadricepsAccessoryWhenGlutesAreAlreadyAtTarget()
    {
        var catalog = Catalog().Append(new TrainingExerciseCandidate(
            "leg-extension", "test", "Extensión de cuádriceps", TrainingMovementPattern.Accessory,
            ["leg-extension-machine"], [TrainingGoal.Hypertrophy],
            TrainingExperience.Beginner, 1, true,
            [TrainingMuscleGroup.Quadriceps], [])).ToArray();

        var generated = TrainingProgramGenerator.Generate(
            Request() with
            {
                MaximumSessionDurationMinutes = 90,
                MuscleVolumeTargets =
                [
                    new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Glutes, TargetWeeklyPrimarySets: 2m),
                    new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Quadriceps, TargetWeeklyPrimarySets: 6m)
                ]
            },
            catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var weekOneSessions = generated.Prescription!.Sessions
            .Where(session => session.WeekNumber == 1)
            .ToArray();
        Assert.Equal(2, weekOneSessions.Length);
        var directQuadAccessories = weekOneSessions.SelectMany(session => session.Exercises)
            .Where(exercise => exercise.ExerciseId == "leg-extension")
            .ToArray();
        Assert.Single(directQuadAccessories);
        Assert.Equal(new[] { TrainingMuscleGroup.Quadriceps }, directQuadAccessories[0].PrimaryMuscleGroups);
    }

    [Fact]
    public void GeneratorDoesNotForceHamstringAccessoryWhenTargetIsAlreadyMetOrEquipmentIsUnavailable()
    {
        var catalog = Catalog().Append(new TrainingExerciseCandidate(
            "seated-leg-curl", "test", "Curl femoral sentado", TrainingMovementPattern.Accessory,
            ["leg-curl-machine"], [TrainingGoal.Hypertrophy, TrainingGoal.GeneralFitness],
            TrainingExperience.Beginner, 10, true,
            [TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.Calves])).ToArray();

        var targetAlreadyMet = TrainingProgramGenerator.Generate(
            Request() with
            {
                MuscleVolumeTargets = [new TrainingMuscleVolumeTarget(TrainingMuscleGroup.Hamstrings, TargetWeeklyPrimarySets: 1)]
            },
            catalog);
        Assert.True(targetAlreadyMet.Succeeded, string.Join("; ", targetAlreadyMet.Errors));
        Assert.DoesNotContain(targetAlreadyMet.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "seated-leg-curl");

        var noCompatibleMachine = TrainingProgramGenerator.Generate(
            Request() with { AvailableEquipment = ["barbell"] },
            catalog);
        Assert.True(noCompatibleMachine.Succeeded, string.Join("; ", noCompatibleMachine.Errors));
        Assert.DoesNotContain(noCompatibleMachine.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "seated-leg-curl");
    }

    [Fact]
    public void GeneratorDistributesAccessoriesAcrossSplitDaysWithoutRepeatingCoveredWeeklyVolume()
    {
        var catalog = Catalog()
            .Where(exercise => exercise.MovementPattern != TrainingMovementPattern.Accessory)
            .Append(new TrainingExerciseCandidate(
                "biceps-curl", "test", "Curl de bíceps", TrainingMovementPattern.Accessory,
                [], [TrainingGoal.Hypertrophy], TrainingExperience.Beginner, 55, true,
                [TrainingMuscleGroup.Biceps], []))
            .Append(new TrainingExerciseCandidate(
                "leg-extension", "test", "Extensión de cuádriceps", TrainingMovementPattern.Accessory,
                [], [TrainingGoal.Hypertrophy], TrainingExperience.Beginner, 55, true,
                [TrainingMuscleGroup.Quadriceps], []))
            .ToArray();
        var request = Request() with
        {
            Goal = TrainingGoal.Strength,
            TrainingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday],
            MaximumSessionDurationMinutes = 35
        };

        var generated = TrainingProgramGenerator.Generate(request, catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var weekOne = generated.Prescription!.Sessions.Where(session => session.WeekNumber == 1).ToArray();
        var upperSessions = weekOne.Where(session => session.Title.StartsWith("Tren superior", StringComparison.Ordinal)).ToArray();
        var lowerSessions = weekOne.Where(session => session.Title.StartsWith("Tren inferior", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, upperSessions.Length);
        Assert.Equal(2, lowerSessions.Length);
        // Accessory slots address remaining weekly primary-volume deficits. Once an earlier
        // split session has brought a muscle to its target, repeating the same isolation work
        // in every later session would add unnecessary volume rather than improve coverage.
        Assert.Contains(upperSessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "biceps-curl");
        Assert.Contains(lowerSessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "leg-extension");
    }

    [Fact]
    public void StrengthPlansDoNotBorrowHypertrophyOnlyPrimaryPatternExercises()
    {
        var catalog = Catalog().Select(exercise =>
            exercise.MovementPattern == TrainingMovementPattern.Squat
                ? exercise with { SuitableGoals = [TrainingGoal.Hypertrophy] }
                : exercise).ToArray();

        var generated = TrainingProgramGenerator.Generate(
            Request() with { Goal = TrainingGoal.Strength },
            catalog);

        Assert.False(generated.Succeeded);
    }

    [Fact]
    public void GeneratorStillWorksWhenNoAccessoryIsAvailable()
    {
        var catalog = Catalog().Where(exercise => exercise.MovementPattern != TrainingMovementPattern.Accessory).ToArray();

        var generated = TrainingProgramGenerator.Generate(Request(), catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        Assert.DoesNotContain(generated.Prescription!.Sessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "exercise-accessory");
    }

    [Fact]
    public void ShortPlanDoesNotReceiveAnUnconditionalCalendarDeload()
    {
        var result = TrainingProgramGenerator.Generate(Request() with { DurationWeeks = 3 }, Catalog());

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var signatures = result.Prescription!.Sessions
            .GroupBy(session => session.WeekNumber)
            .ToDictionary(group => group.Key, group => group
                .SelectMany(session => session.Exercises)
                .OrderBy(exercise => exercise.ExerciseId, StringComparer.Ordinal)
                .Select(exercise => (exercise.ExerciseId, exercise.Sets))
                .ToArray());

        Assert.Equal(signatures[1], signatures[3]);
    }

    [Fact]
    public void ExplicitDeloadMustBeExactlyOneWeek()
    {
        var result = TrainingProgramGenerator.Generate(
            Request() with { DurationWeeks = 2, IsDeload = true }, Catalog());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Contains("exactamente una semana", StringComparison.Ordinal));
    }

    [Fact]
    public void ExplicitDeloadReducesWeeklySetsAndIncreasesReserveWithoutChangingExerciseSelection()
    {
        var normal = TrainingProgramGenerator.Generate(Request() with { DurationWeeks = 1 }, Catalog());
        var deload = TrainingProgramGenerator.Generate(Request() with { DurationWeeks = 1, IsDeload = true }, Catalog());

        Assert.True(normal.Succeeded, string.Join("; ", normal.Errors));
        Assert.True(deload.Succeeded, string.Join("; ", deload.Errors));

        var normalExercises = normal.Prescription!.Sessions
            .OrderBy(session => session.Day)
            .SelectMany(session => session.Exercises.OrderBy(exercise => exercise.Order))
            .ToArray();
        var deloadExercises = deload.Prescription!.Sessions
            .OrderBy(session => session.Day)
            .SelectMany(session => session.Exercises.OrderBy(exercise => exercise.Order))
            .ToArray();

        Assert.Equal(normalExercises.Select(exercise => exercise.ExerciseId), deloadExercises.Select(exercise => exercise.ExerciseId));
        Assert.All(normalExercises, exercise =>
            Assert.Equal(TrainingProgressionRule.DoubleProgression, exercise.ProgressionRule));
        Assert.All(deloadExercises, exercise =>
            Assert.Equal(TrainingProgressionRule.None, exercise.ProgressionRule));
        Assert.True(deloadExercises.Sum(exercise => exercise.Sets) < normalExercises.Sum(exercise => exercise.Sets));
        Assert.Contains(deloadExercises.Zip(normalExercises), pair => pair.First.Sets < pair.Second.Sets);
        Assert.All(deloadExercises.Zip(normalExercises), pair =>
        {
            Assert.Equal(Math.Max(1, (int)Math.Ceiling(pair.Second.Sets * 0.5m)), pair.First.Sets);
            Assert.Equal(Math.Min(5m, pair.Second.TargetRepetitionsInReserve!.Value + 2m), pair.First.TargetRepetitionsInReserve);
        });
        Assert.Contains(deload.Prescription.QualityReport!.Warnings, warning => warning.Contains("descarga activa", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void ManualVolumeUsesTheSameEffectiveStimulusAuditAsGeneratedPrograms()
    {
        var candidate = new TrainingExerciseCandidate(
            "bench-press", "test", "Press banca", TrainingMovementPattern.HorizontalPush,
            [], [TrainingGoal.Hypertrophy], TrainingExperience.Beginner, 50, true,
            [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps]);
        var manual = TrainingProgramQualityAnalyzer.AnalyzeManual(
            [candidate],
            [new TrainingManualVolumeSessionInput(
                DayOfWeek.Monday, "Torso",
                [new TrainingManualVolumeExerciseInput("bench-press", "Press banca", 3)])]);
        var generated = TrainingProgramQualityAnalyzer.Analyze(
            new TrainingProgramPrescription(
                "test", "Hypertrophy", new DateOnly(2026, 11, 2), 1,
                [new TrainingSessionPrescription(
                    1, DayOfWeek.Monday, "Torso", 60,
                    [new TrainingExercisePrescription(
                        "bench-press", "test", "Press banca", 1, 3, 8, 12, 2m, 120,
                        TrainingProgressionRule.DoubleProgression, "Test",
                        [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps])])]));

        var manualMuscles = manual.QualityReport.WeeklyMuscleVolumes.Single().Muscles;
        var generatedMuscles = generated.WeeklyMuscleVolumes.Single().Muscles;
        Assert.Equal(
            generatedMuscles.Select(muscle => (muscle.MuscleGroup, muscle.EstimatedEffectiveSets)),
            manualMuscles.Select(muscle => (muscle.MuscleGroup, muscle.EstimatedEffectiveSets)));
        Assert.Equal(3, manualMuscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Chest).EstimatedEffectiveSets);
        Assert.Equal(3, manualMuscles.Single(muscle => muscle.MuscleGroup == TrainingMuscleGroup.Triceps).EstimatedEffectiveSets);
        Assert.Empty(manual.UnclassifiedExerciseNames);
    }

    [Fact]
    public void FullBodyAccessorySlotsPrioritizeMissingLatsAndAvoidDuplicatePrimaryMuscleAccessories()
    {
        var catalog = Catalog()
            .Where(candidate => candidate.MovementPattern != TrainingMovementPattern.Accessory)
            .Concat(new[]
            {
                new TrainingExerciseCandidate("straight-arm-pulldown", "catalog-2026-10", "Jalón con brazos rectos",
                    TrainingMovementPattern.Accessory, ["cable"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 58, true, [TrainingMuscleGroup.Lats], []),
                new TrainingExerciseCandidate("cable-fly", "catalog-2026-10", "Aperturas en polea",
                    TrainingMovementPattern.Accessory, ["cable"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Deltoids]),
                new TrainingExerciseCandidate("cable-lateral-raise", "catalog-2026-10", "Elevación lateral en polea",
                    TrainingMovementPattern.Accessory, ["cable"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.UpperBack]),
                new TrainingExerciseCandidate("lateral-raise", "catalog-2026-10", "Elevación lateral",
                    TrainingMovementPattern.Accessory, ["dumbbell"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Deltoids], []),
                new TrainingExerciseCandidate("machine-calf-raise", "catalog-2026-10", "Gemelo en máquina",
                    TrainingMovementPattern.Accessory, ["machine"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Calves], []),
                new TrainingExerciseCandidate("leg-extension", "catalog-2026-10", "Extensión de cuádriceps",
                    TrainingMovementPattern.Accessory, ["machine"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Quadriceps], []),
                new TrainingExerciseCandidate("shrug", "catalog-2026-10", "Encogimientos",
                    TrainingMovementPattern.Accessory, ["dumbbell"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.UpperBack], [])
            })
            .ToArray();
        var request = new TrainingProgramGenerationRequest(
            TrainingGoal.Strength, TrainingExperience.Advanced, new DateOnly(2026, 10, 26), 4,
            [DayOfWeek.Monday, DayOfWeek.Thursday], 90, ["barbell", "dumbbell", "machine", "cable"], [])
        {
            Split = TrainingProgramSplit.FullBody
        };

        var result = TrainingProgramGenerator.Generate(request, catalog);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        var generatedSessions = result.Prescription!.Sessions;
        Assert.Contains(generatedSessions.SelectMany(session => session.Exercises),
            exercise => exercise.ExerciseId == "straight-arm-pulldown");

        var accessoryIds = catalog
            .Where(candidate => candidate.MovementPattern == TrainingMovementPattern.Accessory)
            .ToDictionary(candidate => candidate.ExerciseId, candidate => candidate);
        foreach (var session in generatedSessions)
        {
            var accessoryPrimaryMuscles = session.Exercises
                .Where(exercise => accessoryIds.ContainsKey(exercise.ExerciseId))
                .SelectMany(exercise => accessoryIds[exercise.ExerciseId].PrimaryMuscleGroups)
                .ToArray();
            Assert.All(accessoryPrimaryMuscles.GroupBy(muscle => muscle),
                group => Assert.Single(group));
        }
    }

    [Fact]
    public void UpperLowerSplitKeepsAccessoryExercisesWithinTheirSessionMuscleGroups()
    {
        var catalog = Catalog()
            .Where(candidate => candidate.MovementPattern != TrainingMovementPattern.Accessory)
            .Concat(new[]
            {
                new TrainingExerciseCandidate("straight-arm-pulldown", "catalog-2026-10", "Jalón con brazos rectos",
                    TrainingMovementPattern.Accessory, ["cable"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 58, true, [TrainingMuscleGroup.Lats], []),
                new TrainingExerciseCandidate("cable-fly", "catalog-2026-10", "Aperturas en polea",
                    TrainingMovementPattern.Accessory, ["cable"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Deltoids]),
                new TrainingExerciseCandidate("lateral-raise", "catalog-2026-10", "Elevación lateral",
                    TrainingMovementPattern.Accessory, ["dumbbell"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Deltoids], []),
                new TrainingExerciseCandidate("machine-calf-raise", "catalog-2026-10", "Gemelo en máquina",
                    TrainingMovementPattern.Accessory, ["machine"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Calves], []),
                new TrainingExerciseCandidate("leg-extension", "catalog-2026-10", "Extensión de cuádriceps",
                    TrainingMovementPattern.Accessory, ["machine"], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 72, true, [TrainingMuscleGroup.Quadriceps], [])
            })
            .ToArray();

        var generated = TrainingProgramGenerator.Generate(
            Request() with
            {
                TrainingDays = [DayOfWeek.Monday, DayOfWeek.Thursday],
                Split = TrainingProgramSplit.UpperLower,
                DurationWeeks = 1,
                MaximumSessionDurationMinutes = 90,
                AvailableEquipment = ["barbell", "dumbbell", "machine", "cable"]
            },
            catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var generatedSessions = generated.Prescription!.Sessions;
        var upper = Assert.Single(generatedSessions, session =>
            session.Title.StartsWith("Tren superior", StringComparison.Ordinal));
        var lower = Assert.Single(generatedSessions, session =>
            session.Title.StartsWith("Tren inferior", StringComparison.Ordinal));

        Assert.Contains(upper.Exercises, exercise =>
            exercise.ExerciseId is "cable-fly" or "lateral-raise" or "straight-arm-pulldown");
        Assert.Contains(lower.Exercises, exercise =>
            exercise.ExerciseId is "leg-extension" or "machine-calf-raise");
        Assert.DoesNotContain(upper.Exercises, exercise =>
            exercise.ExerciseId is "leg-extension" or "machine-calf-raise");
        Assert.DoesNotContain(lower.Exercises, exercise =>
            exercise.ExerciseId is "cable-fly" or "lateral-raise" or "straight-arm-pulldown");
    }

    [Fact]
    public void UpperLowerBSessionsLeadWithTheGroupTrainedSecondInASessions()
    {
        var generated = TrainingProgramGenerator.Generate(
            Request() with
            {
                TrainingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday],
                Split = TrainingProgramSplit.UpperLower,
                DurationWeeks = 1,
                MaximumSessionDurationMinutes = 90
            },
            Catalog());

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        string First(string title) => generated.Prescription!.Sessions
            .Single(session => session.Title == title).Exercises.OrderBy(x => x.Order).First().ExerciseId;

        Assert.Equal("exercise-horizontalpush", First("Tren superior A"));
        Assert.Equal("exercise-verticalpull", First("Tren superior B"));
        Assert.Equal("squat-basic", First("Tren inferior A"));
        Assert.Equal("exercise-hinge", First("Tren inferior B"));
    }

    [Fact]
    public void HypertrophyUpperSessionsReceiveDirectLateralDeltoidBicepsAndTricepsWork()
    {
        var catalog = Catalog()
            .Where(candidate => candidate.MovementPattern != TrainingMovementPattern.Accessory)
            .Concat(new[]
            {
                new TrainingExerciseCandidate("lateral-raise", "catalog-2026-10", "Elevación lateral",
                    TrainingMovementPattern.Accessory, [], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 50, true, [TrainingMuscleGroup.Deltoids], []),
                new TrainingExerciseCandidate("cable-curl", "catalog-2026-10", "Curl en polea",
                    TrainingMovementPattern.Accessory, [], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 50, true, [TrainingMuscleGroup.Biceps], []),
                new TrainingExerciseCandidate("tricep-pushdown", "catalog-2026-10", "Extensión de tríceps en polea",
                    TrainingMovementPattern.Accessory, [], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 50, true, [TrainingMuscleGroup.Triceps], []),
                new TrainingExerciseCandidate("machine-calf-raise", "catalog-2026-10", "Gemelo en máquina",
                    TrainingMovementPattern.Accessory, [], [TrainingGoal.Hypertrophy],
                    TrainingExperience.Beginner, 50, true, [TrainingMuscleGroup.Calves], [])
            })
            .ToArray();

        var generated = TrainingProgramGenerator.Generate(
            Request() with
            {
                TrainingDays = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Friday],
                Split = TrainingProgramSplit.UpperLower,
                DurationWeeks = 1,
                MaximumSessionDurationMinutes = 60
            },
            catalog);

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var sessions = generated.Prescription!.Sessions;
        var upper = sessions.Where(session => session.Title.StartsWith("Tren superior", StringComparison.Ordinal)).ToArray();
        var lower = sessions.Where(session => session.Title.StartsWith("Tren inferior", StringComparison.Ordinal)).ToArray();

        foreach (var id in new[] { "lateral-raise", "cable-curl", "tricep-pushdown" })
        {
            var exposures = upper.SelectMany(session => session.Exercises).Where(x => x.ExerciseId == id).ToArray();
            Assert.Equal(2, exposures.Length);
            Assert.All(exposures, exercise => Assert.True(exercise.Sets >= 2, $"{id} debe tener al menos 2 series directas."));
            Assert.DoesNotContain(lower.SelectMany(session => session.Exercises), x => x.ExerciseId == id);
        }
        Assert.All(upper.SelectMany(session => session.Exercises).Where(x => x.ExerciseId == "lateral-raise"),
            exercise => Assert.True(exercise.Sets >= 3));
    }

    [Fact]
    public void ManualVolumeReportsExercisesWithoutReviewedCatalogMetadataInsteadOfGuessing()
    {
        var result = TrainingProgramQualityAnalyzer.AnalyzeManual(
            Catalog(),
            [new TrainingManualVolumeSessionInput(
                DayOfWeek.Wednesday, "Sesión manual",
                [new TrainingManualVolumeExerciseInput("not-reviewed", "Ejercicio sin revisar", 4)])]);

        Assert.Contains("Ejercicio sin revisar", result.UnclassifiedExerciseNames);
        Assert.All(result.QualityReport.WeeklyMuscleVolumes.Single().Muscles,
            muscle => Assert.Equal(0, muscle.EstimatedEffectiveSets));
    }

    private static string SessionSignature(TrainingSessionPrescription session) =>
        $"{session.WeekNumber}|{session.Day}|{session.Title}|" +
        string.Join(",", session.Exercises.Select(exercise =>
            $"{exercise.ExerciseId}:{exercise.Order}:{exercise.Sets}:{exercise.MinRepetitions}-{exercise.MaxRepetitions}"));

    private static TrainingProgramGenerationRequest Request() => new(
        TrainingGoal.Hypertrophy,
        TrainingExperience.Beginner,
        new DateOnly(2026, 10, 26),
        4,
        [DayOfWeek.Monday, DayOfWeek.Thursday],
        60,
        [],
        []);

    private static TrainingExerciseCandidate[] Catalog()
    {
        return Enum.GetValues<TrainingMovementPattern>().Select(pattern =>
        {
            var (primary, secondary) = MuscleMetadata(pattern);
            return new TrainingExerciseCandidate(
                pattern == TrainingMovementPattern.Squat ? "squat-basic" : $"exercise-{pattern.ToString().ToLowerInvariant()}",
                "catalog-2026-10",
                $"Ejercicio {pattern}",
                pattern,
                [],
                [TrainingGoal.GeneralFitness, TrainingGoal.Hypertrophy, TrainingGoal.Strength],
                TrainingExperience.Beginner,
                50,
                true,
                primary,
                secondary);
        }).ToArray();
    }

    private static (IReadOnlyList<TrainingMuscleGroup> Primary, IReadOnlyList<TrainingMuscleGroup> Secondary)
        MuscleMetadata(TrainingMovementPattern pattern) => pattern switch
        {
            TrainingMovementPattern.Squat => ([TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.ErectorSpinae]),
            TrainingMovementPattern.Hinge => ([TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Quadriceps]),
            TrainingMovementPattern.HorizontalPush => ([TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Deltoids]),
            TrainingMovementPattern.VerticalPush => ([TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Chest]),
            TrainingMovementPattern.HorizontalPull => ([TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Forearms]),
            TrainingMovementPattern.VerticalPull => ([TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.UpperBack]),
            TrainingMovementPattern.SingleLeg => ([TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Glutes], [TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Calves]),
            TrainingMovementPattern.Core => ([TrainingMuscleGroup.Abdominals], [TrainingMuscleGroup.Obliques, TrainingMuscleGroup.ErectorSpinae]),
            TrainingMovementPattern.Accessory => ([TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.Triceps]),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern))
        };

    [Theory]
    [InlineData(TrainingProgramSplit.FullBody, 2, "Cuerpo completo")]
    [InlineData(TrainingProgramSplit.FullBody, 3, "Cuerpo completo")]
    [InlineData(TrainingProgramSplit.FullBody, 4, "Cuerpo completo")]
    [InlineData(TrainingProgramSplit.UpperLower, 2, "Tren superior")]
    [InlineData(TrainingProgramSplit.UpperLower, 4, "Tren superior")]
    [InlineData(TrainingProgramSplit.PushPullLegs, 3, "Empuje")]
    [InlineData(TrainingProgramSplit.PushPullLegs, 6, "Empuje")]
    [InlineData(TrainingProgramSplit.PushPullLegs, 5, "Empuje")]
    public void GeneratorHonorsSelectedWeeklySplit(TrainingProgramSplit split, int dayCount, string expectedTitlePrefix)
    {
        var days = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
        }.Take(dayCount).ToArray();
        var generated = TrainingProgramGenerator.Generate(
            Request() with { TrainingDays = days, Split = split, DurationWeeks = 1, MaximumSessionDurationMinutes = 90 },
            Catalog());

        Assert.True(generated.Succeeded, string.Join("; ", generated.Errors));
        var sessions = generated.Prescription!.Sessions.Where(session => session.WeekNumber == 1).ToArray();
        Assert.Equal(dayCount, sessions.Length);
        Assert.StartsWith(expectedTitlePrefix, sessions[0].Title, StringComparison.Ordinal);
        if (split == TrainingProgramSplit.PushPullLegs)
        {
            Assert.Contains(sessions, session => session.Title.StartsWith("Tracción", StringComparison.Ordinal));
            Assert.Contains(sessions, session => session.Title.StartsWith("Pierna", StringComparison.Ordinal));
        }
        if (split == TrainingProgramSplit.UpperLower)
        {
            Assert.Equal(dayCount / 2, sessions.Count(session => session.Title.StartsWith("Tren superior", StringComparison.Ordinal)));
            Assert.Equal(dayCount / 2, sessions.Count(session => session.Title.StartsWith("Tren inferior", StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData(TrainingProgramSplit.FullBody, 5)]
    [InlineData(TrainingProgramSplit.UpperLower, 3)]
    [InlineData(TrainingProgramSplit.PushPullLegs, 4)]
    [InlineData(TrainingProgramSplit.Automatic, 5)]
    public void GeneratorRejectsUnsupportedSplitAndDayCombinations(TrainingProgramSplit split, int dayCount)
    {
        var days = new[]
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
            DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday
        }.Take(dayCount).ToArray();
        var generated = TrainingProgramGenerator.Generate(
            Request() with { TrainingDays = days, Split = split },
            Catalog());

        Assert.False(generated.Succeeded);
        Assert.Contains(generated.Errors, error => error.Contains("distribución", StringComparison.OrdinalIgnoreCase));
    }
}
