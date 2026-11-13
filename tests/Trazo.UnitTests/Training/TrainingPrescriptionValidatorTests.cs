using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

public sealed class TrainingPrescriptionValidatorTests
{
    [Fact]
    public void ValidStructuredPrescription_HasNoValidationErrors()
    {
        var errors = TrainingPrescriptionValidator.Validate(ValidPlan());

        Assert.Empty(errors);
    }

    [Fact]
    public void NullPlan_IsRejected()
    {
        var errors = TrainingPrescriptionValidator.Validate(null);

        Assert.Contains("El plan es obligatorio.", errors);
    }

    [Fact]
    public void EmptySessions_AreRejected()
    {
        var errors = TrainingPrescriptionValidator.Validate(ValidPlan() with { Sessions = [] });

        Assert.Contains("El plan debe contener al menos una sesión.", errors);
    }

    [Fact]
    public void SessionOutsideProgramDuration_IsRejected()
    {
        var plan = ValidPlan() with
        {
            DurationWeeks = 2,
            Sessions = [ValidSession() with { WeekNumber = 3 }]
        };

        Assert.Contains(TrainingPrescriptionValidator.Validate(plan),
            error => error.Contains("la semana debe estar dentro", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateDayInSameWeek_IsRejected()
    {
        var plan = ValidPlan() with
        {
            Sessions =
            [
                ValidSession(),
                ValidSession() with { Title = "Segunda sesión el mismo día" }
            ]
        };

        Assert.Contains(TrainingPrescriptionValidator.Validate(plan),
            error => error.Contains("más de una sesión asignada", StringComparison.Ordinal));
    }

    [Fact]
    public void SameDayAcrossDifferentWeeks_IsAllowed()
    {
        var plan = ValidPlan() with
        {
            Sessions =
            [
                ValidSession(),
                ValidSession() with { WeekNumber = 2 }
            ]
        };

        Assert.DoesNotContain(TrainingPrescriptionValidator.Validate(plan),
            error => error.Contains("más de una sesión asignada", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(11, 8)]
    [InlineData(4, 31)]
    [InlineData(5, 3)]
    public void InvalidSetsOrRepetitionRange_IsRejected(int sets, int maxRepetitions)
    {
        var exercise = ValidExercise() with { Sets = sets, MaxRepetitions = maxRepetitions };
        var plan = ValidPlan() with
        {
            Sessions = [ValidSession() with { Exercises = [exercise] }]
        };

        Assert.NotEmpty(TrainingPrescriptionValidator.Validate(plan));
    }

    [Fact]
    public void MissingStableExerciseIdentityOrCatalogVersion_IsRejected()
    {
        var exercise = ValidExercise() with { ExerciseId = " ", CatalogVersion = "" };
        var plan = ValidPlan() with
        {
            Sessions = [ValidSession() with { Exercises = [exercise] }]
        };

        var errors = TrainingPrescriptionValidator.Validate(plan);
        Assert.Contains(errors, error => error.Contains("identificador estable", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("versión del catálogo", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidEffortRestAndProgression_AreRejected()
    {
        var exercise = ValidExercise() with
        {
            TargetRepetitionsInReserve = 6,
            RestSeconds = 601,
            ProgressionRule = (TrainingProgressionRule)999
        };
        var plan = ValidPlan() with
        {
            Sessions = [ValidSession() with { Exercises = [exercise] }]
        };

        var errors = TrainingPrescriptionValidator.Validate(plan);
        Assert.Contains(errors, error => error.Contains("RIR objetivo", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("descanso", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("regla de progresión", StringComparison.Ordinal));
    }

    [Fact]
    public void InvalidExerciseSpecificLoadProgressionLimits_AreRejected()
    {
        var exercise = ValidExercise() with
        {
            SmallestLoadIncrementKg = 0,
            MaximumLoadIncreasePercent = 10.1m
        };
        var plan = ValidPlan() with
        {
            Sessions = [ValidSession() with { Exercises = [exercise] }]
        };

        var errors = TrainingPrescriptionValidator.Validate(plan);
        Assert.Contains(errors, error => error.Contains("incremento mínimo de carga", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("incremento máximo de carga", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingOrContradictoryMuscleMetadata_IsRejected()
    {
        var exercise = ValidExercise() with
        {
            PrimaryMuscleGroups = [],
            SecondaryMuscleGroups = [TrainingMuscleGroup.Chest, TrainingMuscleGroup.Chest]
        };
        var plan = ValidPlan() with
        {
            Sessions = [ValidSession() with { Exercises = [exercise] }]
        };

        var errors = TrainingPrescriptionValidator.Validate(plan);
        Assert.Contains(errors, error => error.Contains("al menos un grupo muscular principal", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("repite grupos musculares secundarios", StringComparison.Ordinal));
    }

    [Fact]
    public void AGroupCannotBeBothPrimaryAndSecondary()
    {
        var exercise = ValidExercise() with
        {
            SecondaryMuscleGroups = [TrainingMuscleGroup.Chest]
        };
        var plan = ValidPlan() with
        {
            Sessions = [ValidSession() with { Exercises = [exercise] }]
        };

        Assert.Contains(TrainingPrescriptionValidator.Validate(plan),
            error => error.Contains("principal y secundario", StringComparison.Ordinal));
    }

    [Fact]
    public void HistoricalExerciseNameSnapshotAndSelectionRationale_AreRequired()
    {
        var exercise = ValidExercise() with { ExerciseNameSnapshot = "", SelectionRationale = " " };
        var plan = ValidPlan() with
        {
            Sessions = [ValidSession() with { Exercises = [exercise] }]
        };

        var errors = TrainingPrescriptionValidator.Validate(plan);
        Assert.Contains(errors, error => error.Contains("nombre histórico", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("motivo de selección", StringComparison.Ordinal));
    }

    private static TrainingProgramPrescription ValidPlan() => new(
        "0.1.0",
        "Fuerza general",
        new DateOnly(2026, 10, 26),
        4,
        [ValidSession()]);

    private static TrainingSessionPrescription ValidSession() => new(
        1,
        DayOfWeek.Monday,
        "Sesión A",
        60,
        [ValidExercise()]);

    private static TrainingExercisePrescription ValidExercise() => new(
        "exercise-push-up",
        "catalog-2026-10",
        "Flexión de brazos",
        1,
        3,
        8,
        12,
        2,
        120,
        TrainingProgressionRule.DoubleProgression,
        "Patrón de empuje horizontal con material corporal disponible.",
        [TrainingMuscleGroup.Chest],
        [TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Deltoids]);
}
