using System.Text.Json;
using System.Text.Json.Serialization;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Training;

/// <summary>
/// Emits a reproducible, complete JSON corpus for human-assisted review. The report is evidence,
/// not a claim that structurally valid plans are physiologically optimal.
/// </summary>
public sealed class TrainingGeneratorEvaluationReportTests
{
    [Fact]
    public void GenerateScenarioMatrixAndWriteFullJsonReport()
    {
        var scenarios = new List<ScenarioReport>();
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var days in DaySets())
        foreach (var durationMinutes in new[] { 35, 60, 90 })
        {
            var request = new TrainingProgramGenerationRequest(
                goal,
                experience,
                new DateOnly(2026, 10, 26),
                4,
                days,
                durationMinutes,
                ["barbell", "dumbbell", "pull_up_bar", "cable", "kettlebell", "ez_bar", "shoulder_press_machine", "ab_wheel", "machine"],
                []);

            var generated = TrainingProgramGenerator.Generate(request, Catalog());
            var hardViolations = new List<string>(generated.Errors);
            var metrics = new PlanMetrics(0, 0, 0, 0, 0, new Dictionary<int, int>(), new Dictionary<string, int>(),
                new Dictionary<string, IReadOnlyDictionary<int, int>>(), new Dictionary<string, IReadOnlyDictionary<int, int>>());

            if (generated.Prescription is { } plan)
            {
                hardViolations.AddRange(TrainingPrescriptionValidator.Validate(plan));
                var expectedSessions = request.DurationWeeks * request.TrainingDays.Count;
                if (plan.Sessions.Count != expectedSessions)
                    hardViolations.Add($"Se esperaban {expectedSessions} sesiones y se generaron {plan.Sessions.Count}.");
                if (plan.Sessions.Any(s => !request.TrainingDays.Contains(s.Day)))
                    hardViolations.Add("Se ha generado una sesión en un día no solicitado.");
                if (plan.Sessions.Any(s => s.EstimatedDurationMinutes > request.MaximumSessionDurationMinutes))
                    hardViolations.Add("Una sesión supera la duración máxima solicitada.");

                var allExercises = plan.Sessions.SelectMany(s => s.Exercises).ToArray();
                var duplicateIdsWithinSession = plan.Sessions.Sum(session =>
                    session.Exercises.Select(x => x.ExerciseId).Count() -
                    session.Exercises.Select(x => x.ExerciseId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                var primaryMuscleSetsPerWeek = plan.Sessions
                    .SelectMany(session => session.Exercises.SelectMany(exercise => exercise.PrimaryMuscleGroups
                        .Select(muscle => new { Muscle = muscle.ToString(), session.WeekNumber, exercise.Sets })))
                    .GroupBy(x => x.Muscle)
                    .ToDictionary(
                        muscle => muscle.Key,
                        muscle => (IReadOnlyDictionary<int, int>)muscle.GroupBy(x => x.WeekNumber)
                            .ToDictionary(week => week.Key, week => week.Sum(x => x.Sets)));

                var secondaryMuscleSetExposurePerWeek = plan.Sessions
                    .SelectMany(session => session.Exercises.SelectMany(exercise => exercise.SecondaryMuscleGroups
                        .Select(muscle => new { Muscle = muscle.ToString(), session.WeekNumber, exercise.Sets })))
                    .GroupBy(x => x.Muscle)
                    .ToDictionary(
                        muscle => muscle.Key,
                        muscle => (IReadOnlyDictionary<int, int>)muscle.GroupBy(x => x.WeekNumber)
                            .ToDictionary(week => week.Key, week => week.Sum(x => x.Sets)));

                metrics = new PlanMetrics(
                    plan.Sessions.Count,
                    allExercises.Length,
                    allExercises.Sum(x => x.Sets),
                    plan.Sessions.Count == 0 ? 0 : plan.Sessions.Max(s => s.EstimatedDurationMinutes),
                    duplicateIdsWithinSession,
                    plan.Sessions.GroupBy(s => s.WeekNumber).ToDictionary(g => g.Key, g => g.Count()),
                    allExercises.GroupBy(x => x.ExerciseId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase),
                    primaryMuscleSetsPerWeek,
                    secondaryMuscleSetExposurePerWeek);
            }

            scenarios.Add(new ScenarioReport(
                $"goal-{goal.ToString().ToLowerInvariant()}-experience-{experience.ToString().ToLowerInvariant()}-days-{days.Count}-minutes-{durationMinutes}",
                request,
                "repdb-free-curated-v1",
                generated.Succeeded,
                hardViolations.Distinct().ToArray(),
                new[]
                {
                    "El catálogo de evaluación contiene ejercicios reales de RepDB seleccionados mediante una lista curada y revisión explícita de patrón, material y grupos musculares; no representa el catálogo completo.",
                    "El informe conserva series primarias y secundarias por separado; las series efectivas usan el mapeo revisado TrainingExerciseStimulus por ID de ejercicio, sin crédito universal de 0,5. Es una regla explícita de programación, no una equivalencia fisiológica validada por ACSM.",
                    "La biblioteca completa se conserva en PostgreSQL con su payload original; solo los ejercicios incluidos en la lista curada se consideran aptos para el generador.",
                    "La duración es la estimación heurística del motor; no está contrastada con sesiones reales.",
                    "La validación estructural no demuestra idoneidad fisiológica individual."
                },
                metrics,
                generated.Prescription));
        }

        var report = new EvaluationReport(
            "training-generator-evaluation-v4",
            DateTimeOffset.UtcNow,
            scenarios.Select(scenario => scenario.Plan?.EngineVersion).FirstOrDefault(version => !string.IsNullOrWhiteSpace(version)) ?? "unknown",
            scenarios.Count,
            scenarios.Count(s => s.GenerationSucceeded),
            scenarios.Sum(s => s.HardViolations.Count),
            "Las advertencias son limitaciones conocidas, no diagnósticos clínicos ni umbrales fisiológicos.",
            scenarios);

        var configuredPath = Environment.GetEnvironmentVariable("TRAINING_GENERATOR_REPORT_PATH");
        var outputPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppContext.BaseDirectory, "training-generator-evaluation.json")
            : configuredPath;
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, JsonOptions));

        Assert.Equal("0.9.0", report.EngineVersion);
        Assert.Equal(81, report.ScenarioCount);
        Assert.Equal(report.ScenarioCount, report.SuccessfulScenarios);
        Assert.Equal(0, report.HardViolationCount);
        var selectedExerciseIds = scenarios.SelectMany(scenario => scenario.Metrics.ExerciseSelectionCounts.Keys)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("leg-curl", selectedExerciseIds);
        Assert.Contains("lateral-raise", selectedExerciseIds);
        Assert.DoesNotContain(scenarios.SelectMany(scenario => scenario.Plan!.QualityReport!.Warnings),
            warning => warning.Contains("estimación de volumen efectivo", StringComparison.OrdinalIgnoreCase));
        Assert.All(scenarios, scenario =>
        {
            Assert.NotNull(scenario.Plan!.QualityReport);
            Assert.Equal(4, scenario.Plan.QualityReport!.WeeklyMuscleVolumes.Count);
            // Quality warnings are advisory; they must not be counted as structural violations.
            Assert.Empty(scenario.HardViolations);
        });
        // The allocator targets direct primary sets and must not create extreme volume
        // simply to satisfy another muscle through a compound exercise.
        Assert.All(scenarios, scenario => Assert.All(
            scenario.Metrics.PrimaryMuscleSetsPerWeek.Values,
            weekly => Assert.All(weekly.Values, sets => Assert.InRange(sets, 0, 20))));
        Assert.All(scenarios, scenario => Assert.All(
            scenario.Plan!.Sessions.SelectMany(session => session.Exercises),
            exercise =>
            {
                Assert.NotEmpty(exercise.PrimaryMuscleGroups);
                Assert.NotNull(exercise.SecondaryMuscleGroups);
            }));
        Assert.Contains(scenarios[0].Metrics.PrimaryMuscleSetsPerWeek,
            muscle => muscle.Key == nameof(TrainingMuscleGroup.Chest) && muscle.Value[1] > 0);
        Assert.Contains(scenarios[0].Metrics.SecondaryMuscleSetExposurePerWeek,
            muscle => muscle.Key == nameof(TrainingMuscleGroup.Triceps) && muscle.Value[1] > 0);
    }

    private static IReadOnlyList<DayOfWeek>[] DaySets() =>
    [
        [DayOfWeek.Monday, DayOfWeek.Thursday],
        [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday],
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday]
    ];

    private static TrainingExerciseCandidate[] Catalog()
    {
        var goals = Enum.GetValues<TrainingGoal>();
        return
        [
            Candidate("squat", "Sentadilla trasera con barra", TrainingMovementPattern.Squat, ["barbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Hamstrings]),
            Candidate("goblet-squat", "Sentadilla goblet", TrainingMovementPattern.Squat, ["kettlebell"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Hamstrings]),
            Candidate("ez-bar-romanian-deadlift", "Peso muerto rumano con barra EZ", TrainingMovementPattern.Hinge, ["ez_bar"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Forearms, TrainingMuscleGroup.UpperBack]),
            Candidate("romanian-deadlift", "Peso muerto rumano", TrainingMovementPattern.Hinge, ["barbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Forearms]),
            Candidate("push-up", "Flexiones", TrainingMovementPattern.HorizontalPush, [], TrainingExperience.Beginner, 100,
                [TrainingMuscleGroup.Chest, TrainingMuscleGroup.Triceps], [TrainingMuscleGroup.Deltoids]),
            Candidate("bench-press", "Press de banca con barra", TrainingMovementPattern.HorizontalPush, ["barbell"], TrainingExperience.Intermediate, 95,
                [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Triceps]),
            Candidate("lat-pulldown", "Jalón al pecho", TrainingMovementPattern.VerticalPull, ["cable"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.UpperBack]),
            Candidate("pull-up", "Dominadas", TrainingMovementPattern.VerticalPull, ["pull_up_bar"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Lats], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.UpperBack]),
            Candidate("seated-cable-row", "Remo sentado en polea", TrainingMovementPattern.HorizontalPull, ["cable"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Deltoids]),
            Candidate("bent-over-db-row", "Remo inclinado con mancuernas", TrainingMovementPattern.HorizontalPull, ["dumbbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack], [TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Deltoids]),
            Candidate("machine-shoulder-press", "Press de hombros en máquina", TrainingMovementPattern.VerticalPush, ["shoulder_press_machine"], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Triceps]),
            Candidate("ohp", "Press militar con barra", TrainingMovementPattern.VerticalPush, ["barbell"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Triceps]),
            Candidate("step-ups", "Subidas a cajón", TrainingMovementPattern.SingleLeg, [], TrainingExperience.Beginner, 95,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Calves, TrainingMuscleGroup.Hamstrings]),
            Candidate("walking-lunge", "Zancadas caminando", TrainingMovementPattern.SingleLeg, [], TrainingExperience.Intermediate, 90,
                [TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Hamstrings, TrainingMuscleGroup.Calves]),
            Candidate("plank", "Plancha", TrainingMovementPattern.Core, [], TrainingExperience.Beginner, 90,
                [TrainingMuscleGroup.Abdominals], [TrainingMuscleGroup.ErectorSpinae, TrainingMuscleGroup.Obliques]),
            Candidate("ab-wheel-rollout", "Rueda abdominal", TrainingMovementPattern.Core, ["ab_wheel"], TrainingExperience.Intermediate, 100,
                [TrainingMuscleGroup.Abdominals], [TrainingMuscleGroup.Deltoids, TrainingMuscleGroup.Lats, TrainingMuscleGroup.Obliques]),
            Candidate("leg-extension", "Extensión de cuádriceps en máquina", TrainingMovementPattern.Accessory, ["machine"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Quadriceps], [TrainingMuscleGroup.Glutes]),
            Candidate("leg-curl", "Curl femoral sentado", TrainingMovementPattern.Accessory, ["machine"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Hamstrings], [TrainingMuscleGroup.Glutes]),
            Candidate("lateral-raise", "Elevación lateral con mancuernas", TrainingMovementPattern.Accessory, ["dumbbell"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Deltoids], [TrainingMuscleGroup.UpperBack]),
            Candidate("biceps-curl", "Curl de bíceps con mancuernas", TrainingMovementPattern.Accessory, ["dumbbell"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Biceps], [TrainingMuscleGroup.Forearms]),
            Candidate("triceps-pushdown", "Extensión de tríceps en polea", TrainingMovementPattern.Accessory, ["cable"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Triceps], [TrainingMuscleGroup.Forearms]),
            Candidate("calf-raise", "Elevación de gemelos en máquina", TrainingMovementPattern.Accessory, ["machine"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Calves], []),
            Candidate("chest-fly", "Aperturas de pecho en polea", TrainingMovementPattern.Accessory, ["cable"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Chest], [TrainingMuscleGroup.Deltoids]),
            Candidate("hip-abduction", "Abducción de cadera en máquina", TrainingMovementPattern.Accessory, ["machine"], TrainingExperience.Beginner, 55,
                [TrainingMuscleGroup.Abductors], [TrainingMuscleGroup.Glutes])
        ];

        TrainingExerciseCandidate Candidate(
            string id, string name, TrainingMovementPattern pattern, string[] equipment,
            TrainingExperience minimumExperience, int priority,
            TrainingMuscleGroup[] primary, TrainingMuscleGroup[] secondary) =>
            new(id, "repdb-free-curated-v1", name, pattern, equipment, goals, minimumExperience, priority, true, primary, secondary);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record EvaluationReport(
        string SchemaVersion,
        DateTimeOffset GeneratedAtUtc,
        string EngineVersion,
        int ScenarioCount,
        int SuccessfulScenarios,
        int HardViolationCount,
        string Interpretation,
        IReadOnlyList<ScenarioReport> Scenarios);

    private sealed record ScenarioReport(
        string ScenarioId,
        TrainingProgramGenerationRequest Request,
        string EvaluationCatalogVersion,
        bool GenerationSucceeded,
        IReadOnlyList<string> HardViolations,
        IReadOnlyList<string> Limitations,
        PlanMetrics Metrics,
        TrainingProgramPrescription? Plan);

    private sealed record PlanMetrics(
        int SessionCount,
        int ExercisePrescriptionCount,
        int TotalPrescribedSets,
        int MaximumEstimatedSessionMinutes,
        int DuplicateExerciseIdsWithinSessions,
        IReadOnlyDictionary<int, int> SessionsPerWeek,
        IReadOnlyDictionary<string, int> ExerciseSelectionCounts,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> PrimaryMuscleSetsPerWeek,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> SecondaryMuscleSetExposurePerWeek);
}
