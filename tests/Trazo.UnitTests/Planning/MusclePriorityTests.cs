using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Per-muscle priorities (Emphasize / Grow / Maintain) from the first block of a plan. The expectations are directional (more, not more,
/// not less) on purpose: the exact sets are internal heuristics that the catalog and the session time legitimately move.
/// </summary>
public sealed class MusclePriorityTests
{
    private static readonly DateOnly Today = new(2026, 11, 16);
    private static readonly IReadOnlyList<TrainingExerciseCandidate> Catalog = PlanTestCatalog.Candidates();

    private static readonly TrainingMuscleGroup[] Muscles = [.. TrainingMusclePriorityPlanner.Configurable];

    private static Dictionary<TrainingMuscleGroup, decimal> WeekTwoVolume(
        TrainingExperience experience, int days, TrainingProgramSplit split, Dictionary<TrainingMuscleGroup, TrainingMusclePriority>? priorities)
    {
        var profile = PlanTestCatalog.Profile(TrainingGoal.Hypertrophy, experience, days, [], split: split) with { MusclePriorities = priorities };
        var block = MesocycleGenerator.Generate(profile, 7, new MesocycleSlot(1, 1, "accumulation", 1, 4), Catalog);
        Assert.True(block.Succeeded, string.Join("; ", block.Errors));
        return block.Quality!.WeeklyMuscleVolumes.First(week => week.WeekNumber == 2).Muscles
            .ToDictionary(m => m.MuscleGroup, m => m.EstimatedEffectiveSets);
    }

    public static IEnumerable<object[]> Plans() =>
    [
        [TrainingExperience.Beginner, 3, TrainingProgramSplit.FullBody],
        [TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower],
        [TrainingExperience.Advanced, 5, TrainingProgramSplit.PushPullLegs]
    ];

    [Theory]
    [MemberData(nameof(Plans))]
    public void EmphasisNeverLowersAMuscle_AndMaintenanceNeverRaisesIt(TrainingExperience experience, int days, TrainingProgramSplit split)
    {
        var grow = WeekTwoVolume(experience, days, split, null);
        foreach (var muscle in Muscles)
        {
            var emphasized = WeekTwoVolume(experience, days, split, new() { [muscle] = TrainingMusclePriority.Emphasize });
            var maintained = WeekTwoVolume(experience, days, split, new() { [muscle] = TrainingMusclePriority.Maintain });
            Assert.True(emphasized[muscle] >= grow[muscle], $"{muscle} emphasized {emphasized[muscle]} < grow {grow[muscle]}");
            Assert.True(maintained[muscle] <= grow[muscle], $"{muscle} maintained {maintained[muscle]} > grow {grow[muscle]}");
            Assert.True(emphasized.Values.Max() <= 20m, $"{muscle} emphasis pushed a muscle above 20 effective sets");
        }
    }

    [Theory]
    [InlineData(TrainingMuscleGroup.Hamstrings, TrainingExperience.Beginner, 3, TrainingProgramSplit.FullBody)]
    [InlineData(TrainingMuscleGroup.UpperBack, TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower)]
    [InlineData(TrainingMuscleGroup.Calves, TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower)]
    public void EmphasisGivesTheMuscleMoreWorkWhereThereIsRoom(TrainingMuscleGroup muscle, TrainingExperience experience, int days, TrainingProgramSplit split)
    {
        var grow = WeekTwoVolume(experience, days, split, null);
        var emphasized = WeekTwoVolume(experience, days, split, new() { [muscle] = TrainingMusclePriority.Emphasize });
        Assert.True(emphasized[muscle] > grow[muscle], $"{muscle}: {grow[muscle]} -> {emphasized[muscle]}");
    }

    [Theory]
    [InlineData(TrainingMuscleGroup.Chest, TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower)]
    [InlineData(TrainingMuscleGroup.Lats, TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower)]
    [InlineData(TrainingMuscleGroup.Biceps, TrainingExperience.Beginner, 3, TrainingProgramSplit.FullBody)]
    [InlineData(TrainingMuscleGroup.Glutes, TrainingExperience.Advanced, 5, TrainingProgramSplit.PushPullLegs)]
    public void MaintenanceTakesWorkAwayFromTheMuscle(TrainingMuscleGroup muscle, TrainingExperience experience, int days, TrainingProgramSplit split)
    {
        var grow = WeekTwoVolume(experience, days, split, null);
        var maintained = WeekTwoVolume(experience, days, split, new() { [muscle] = TrainingMusclePriority.Maintain });
        Assert.True(maintained[muscle] < grow[muscle], $"{muscle}: {grow[muscle]} -> {maintained[muscle]}");
    }

    [Fact]
    public void AMuscleWithNoEntryIsGrow_SoAnEmptyMapChangesNothing()
    {
        var plain = WeekTwoVolume(TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower, null);
        var empty = WeekTwoVolume(TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower, new());
        var explicitGrow = WeekTwoVolume(TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower, new() { [TrainingMuscleGroup.Chest] = TrainingMusclePriority.Grow });
        Assert.Equal(plain, empty);
        Assert.Equal(plain, explicitGrow);
    }

    [Fact]
    public void SamePrioritiesAndSeedGiveTheSamePlan()
    {
        var priorities = new Dictionary<TrainingMuscleGroup, TrainingMusclePriority>
        {
            [TrainingMuscleGroup.Deltoids] = TrainingMusclePriority.Emphasize, [TrainingMuscleGroup.Chest] = TrainingMusclePriority.Maintain
        };
        Assert.Equal(
            WeekTwoVolume(TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower, priorities),
            WeekTwoVolume(TrainingExperience.Intermediate, 4, TrainingProgramSplit.UpperLower, priorities));
    }

    // ---------------------------------------------------------------- planner

    [Fact]
    public void PlannerRejectsMoreThanThreeEmphasizedMuscles()
    {
        var priorities = new Dictionary<TrainingMuscleGroup, TrainingMusclePriority>
        {
            [TrainingMuscleGroup.Chest] = TrainingMusclePriority.Emphasize, [TrainingMuscleGroup.Lats] = TrainingMusclePriority.Emphasize,
            [TrainingMuscleGroup.Biceps] = TrainingMusclePriority.Emphasize, [TrainingMuscleGroup.Triceps] = TrainingMusclePriority.Emphasize
        };
        var result = TrainingMusclePriorityPlanner.Build(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, priorities, 4, 75);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("como máximo 3"));
    }

    [Fact]
    public void PlannerOnlyAppliesToHypertrophy()
    {
        var result = TrainingMusclePriorityPlanner.Build(TrainingGoal.Strength, TrainingExperience.Intermediate,
            new Dictionary<TrainingMuscleGroup, TrainingMusclePriority> { [TrainingMuscleGroup.Chest] = TrainingMusclePriority.Emphasize }, 4, 75);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void PlannerRejectsMusclesWithoutPriority()
    {
        var result = TrainingMusclePriorityPlanner.Build(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate,
            new Dictionary<TrainingMuscleGroup, TrainingMusclePriority> { [TrainingMuscleGroup.Abdominals] = TrainingMusclePriority.Emphasize }, 4, 75);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void PlannerEmphasisStaysWithinTheCeiling_AndMaintenanceKeepsTheFloor()
    {
        var result = TrainingMusclePriorityPlanner.Build(TrainingGoal.Hypertrophy, TrainingExperience.Advanced,
            new Dictionary<TrainingMuscleGroup, TrainingMusclePriority>
            {
                [TrainingMuscleGroup.Biceps] = TrainingMusclePriority.Emphasize, [TrainingMuscleGroup.Calves] = TrainingMusclePriority.Maintain
            }, 5, 90);
        Assert.True(result.Succeeded);
        var biceps = result.Targets.Single(t => t.MuscleGroup == TrainingMuscleGroup.Biceps);
        var calves = result.Targets.Single(t => t.MuscleGroup == TrainingMuscleGroup.Calves);
        Assert.True(biceps.TargetWeeklyPrimarySets <= TrainingMusclePriorityPlanner.EmphasisTargetCeiling);
        Assert.False(biceps.Maintenance);
        Assert.True(calves.Maintenance);
        Assert.True(calves.TargetWeeklyPrimarySets >= TrainingMusclePriorityPlanner.MaintenanceFloorSets);
    }

    [Fact]
    public void PlannerWithNothingChosenReturnsNoTargets()
    {
        var result = TrainingMusclePriorityPlanner.Build(TrainingGoal.Hypertrophy, TrainingExperience.Intermediate, null, 4, 75);
        Assert.True(result.Succeeded);
        Assert.Empty(result.Targets);
    }

    // ---------------------------------------------------------------- parsing

    private static PlanProfileInput Input(Dictionary<string, string>? priorities, string goal = "Hypertrophy") => new(
        goal, "Intermediate", Today, 12, ["Monday", "Tuesday", "Thursday", "Friday"], 75, [], [], [], null, true, 4,
        MusclePriorities: priorities);

    [Fact]
    public void ParserReadsPrioritiesCaseInsensitively_AndDropsGrow()
    {
        var parsed = PlanProfileParser.Parse(Input(new() { ["chest"] = "emphasize", ["Triceps"] = "Maintain", ["Lats"] = "Grow" }), Today);
        Assert.True(parsed.Succeeded, string.Join("; ", parsed.Errors.SelectMany(e => e.Value)));
        var priorities = parsed.Profile!.MusclePriorities!;
        Assert.Equal(TrainingMusclePriority.Emphasize, priorities[TrainingMuscleGroup.Chest]);
        Assert.Equal(TrainingMusclePriority.Maintain, priorities[TrainingMuscleGroup.Triceps]);
        Assert.False(priorities.ContainsKey(TrainingMuscleGroup.Lats));
    }

    [Theory]
    [InlineData("Nonsense", "Emphasize")]
    [InlineData("Chest", "Maximum")]
    [InlineData("Abdominals", "Emphasize")]
    public void ParserReportsInvalidPriorities(string muscle, string priority)
    {
        var parsed = PlanProfileParser.Parse(Input(new() { [muscle] = priority }), Today);
        Assert.False(parsed.Succeeded);
        Assert.Contains("musclePriorities", parsed.Errors.Keys);
    }

    [Fact]
    public void ParserRejectsPrioritiesOnANonHypertrophyPlan()
    {
        var parsed = PlanProfileParser.Parse(Input(new() { ["Chest"] = "Emphasize" }, goal: "Strength"), Today);
        Assert.False(parsed.Succeeded);
        Assert.Contains("musclePriorities", parsed.Errors.Keys);
    }

    [Fact]
    public void ParserAcceptsAMissingMap()
    {
        var parsed = PlanProfileParser.Parse(Input(null), Today);
        Assert.True(parsed.Succeeded);
        Assert.True(parsed.Profile!.MusclePriorities is null || parsed.Profile.MusclePriorities.Count == 0);
    }
}

public sealed class MusclePriorityApiTests() : PlansApiBase(enforced: false)
{
    private static object WithPriorities(Dictionary<string, string> priorities) => new
    {
        goal = "Hypertrophy", experience = "Intermediate", totalWeeks = 12,
        trainingDays = new[] { "Monday", "Tuesday", "Thursday", "Friday" }, maxSessionMinutes = 75,
        availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>(), musclePriorities = priorities
    };

    [Fact]
    public async Task CreatingAPlanKeepsThePrioritiesAndReturnsThem()
    {
        var (_, client, _) = await StartPlanAsync(profile: WithPriorities(new() { ["Deltoids"] = "Emphasize", ["Chest"] = "Maintain" }));
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        var priorities = plan.GetProperty("musclePriorities");
        Assert.Equal("Emphasize", priorities.GetProperty("Deltoids").GetString());
        Assert.Equal("Maintain", priorities.GetProperty("Chest").GetString());
    }

    [Fact]
    public async Task APlanWithoutPrioritiesReturnsAnEmptyMap()
    {
        var (_, client, _) = await StartPlanAsync();
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        Assert.Empty(plan.GetProperty("musclePriorities").EnumerateObject());
    }

    [Fact]
    public async Task InvalidPrioritiesAtCreationAnswerAValidationProblem()
    {
        var user = await AddUserAsync("bad@example.com");
        var response = await ClientFor(user).PostAsJsonAsync("/api/v1/plans", WithPriorities(new() { ["Chest"] = "Maximum" }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("musclePriorities", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PrioritiesCanBeReplacedAndCleared_AndTheChangeIsRecorded()
    {
        var (_, client, _) = await StartPlanAsync();

        var set = await client.PutAsJsonAsync("/api/v1/plans/current/muscle-priorities",
            new { musclePriorities = new Dictionary<string, string> { ["Triceps"] = "Maintain" } });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal("Maintain", (await JsonAsync(set)).GetProperty("musclePriorities").GetProperty("Triceps").GetString());

        var cleared = await client.PutAsJsonAsync("/api/v1/plans/current/muscle-priorities", new { musclePriorities = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        Assert.Empty((await JsonAsync(cleared)).GetProperty("musclePriorities").EnumerateObject());

        var history = await JsonAsync(await client.GetAsync("/api/v1/plans/current/adaptations?scope=plan"));
        Assert.Contains("muscle_priorities", history.GetRawText());
    }

    [Fact]
    public async Task ReplacingWithInvalidPrioritiesChangesNothing()
    {
        var (_, client, _) = await StartPlanAsync(profile: WithPriorities(new() { ["Deltoids"] = "Emphasize" }));
        var response = await client.PutAsJsonAsync("/api/v1/plans/current/muscle-priorities",
            new { musclePriorities = new Dictionary<string, string> { ["Nonsense"] = "Emphasize" } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        Assert.Equal("Emphasize", plan.GetProperty("musclePriorities").GetProperty("Deltoids").GetString());
    }

    [Fact]
    public async Task WithoutAnActivePlanTheAnswerIsNotFound()
    {
        var client = ClientFor(await AddUserAsync("none@example.com"));
        var response = await client.PutAsJsonAsync("/api/v1/plans/current/muscle-priorities", new { musclePriorities = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousCallersAreRejected()
    {
        var response = await ClientFor(null).PutAsJsonAsync("/api/v1/plans/current/muscle-priorities", new { musclePriorities = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
