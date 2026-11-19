using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    private static object Tight(Dictionary<string, string> priorities) => new
    {
        goal = "Hypertrophy", experience = "Intermediate", totalWeeks = 12,
        trainingDays = new[] { "Monday", "Wednesday", "Friday" }, maxSessionMinutes = 45,
        availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>(), musclePriorities = priorities
    };

    [Fact]
    public async Task ThePlanSaysWhatEachPriorityAskedForAndWhatTheBlockReallyHolds()
    {
        // Three emphasised muscles in three short sessions: the generator cannot give all of them what was asked for, and must say so.
        var (user, client, _) = await StartPlanAsync(profile: Tight(new()
            { ["Chest"] = "Emphasize", ["Lats"] = "Emphasize", ["Quadriceps"] = "Emphasize", ["Triceps"] = "Maintain" }));
        var plan = await JsonAsync(await client.GetAsync("/api/v1/plans/current"));
        var report = plan.GetProperty("priorityDelivery").EnumerateArray().ToDictionary(i => i.GetProperty("muscle").GetString()!);
        Assert.Equal(4, report.Count);

        // What it reports as assigned is what the stored sessions of the week really hold.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Persistence.TrazoDbContext>();
        var week = plan.GetProperty("currentWeek").GetInt32();
        var stored = (await db.PlannedSessions.Where(s => s.WeekNumber == week).ToListAsync()).Select(s => PrescriptionJson.Read(s.PrescriptionJson));
        var real = Trazo.Application.Planning.PriorityDelivery.WeeklySets(stored);
        foreach (var (name, item) in report)
            Assert.Equal(real.GetValueOrDefault(Enum.Parse<TrainingMuscleGroup>(name)), item.GetProperty("assignedSets").GetDecimal());

        // A muscle below its target says why; one that holds it says nothing. The target is never invented upwards to look better.
        Assert.All(report.Values, item =>
        {
            var status = item.GetProperty("status").GetString();
            var note = item.GetProperty("note");
            if (status == "Met") Assert.Equal(System.Text.Json.JsonValueKind.Null, note.ValueKind);
            else Assert.False(string.IsNullOrWhiteSpace(note.GetString()));
            if (status == "Below") Assert.True(item.GetProperty("assignedSets").GetDecimal() < item.GetProperty("targetSets").GetDecimal());
        });
        Assert.Contains(report.Values, item => item.GetProperty("status").GetString() != "Met");
    }

    [Fact]
    public async Task WithThreeShortSessionsTheAllocatorSharesWhatFitsAndSaysWhatDidNotFit()
    {
        // Three emphasised and three maintained muscles in three sessions of 45 minutes, with every normal limit in force.
        var priorities = new Dictionary<string, string>
        {
            ["Chest"] = "Emphasize", ["Lats"] = "Emphasize", ["Quadriceps"] = "Emphasize",
            ["Biceps"] = "Maintain", ["Triceps"] = "Maintain", ["Calves"] = "Maintain"
        };
        // Both plans are created with the SAME seed, so the only difference between them is the priorities.
        var equipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" };
        var typed = priorities.ToDictionary(p => Enum.Parse<TrainingMuscleGroup>(p.Key), p => Enum.Parse<TrainingMusclePriority>(p.Value));
        async Task<HttpClient> CreateAsync(string email, PlanProfile profile)
        {
            var u = await AddUserAsync(email);
            using var scope = _factory.Services.CreateScope();
            var created = await scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Planning.ITrainingPlanService>().CreateAsync(u.Id, profile, 42, CancellationToken.None);
            Assert.True(created.Succeeded, created.Message);
            return ClientFor(u);
        }
        var baseProfile = PlanTestCatalog.Profile(days: 3, minutes: 45, equipment: equipment);
        var client = await CreateAsync("tight@example.com", baseProfile with { MusclePriorities = typed });
        var plainClient = await CreateAsync("plain@example.com", baseProfile);

        async Task<(JsonElement Plan, Dictionary<TrainingMuscleGroup, decimal> Sets, List<(int Minutes, int Sets)> Sessions)> ReadAsync(HttpClient c)
        {
            var plan = await JsonAsync(await c.GetAsync("/api/v1/plans/current"));
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Persistence.TrazoDbContext>();
            var week = plan.GetProperty("currentWeek").GetInt32();
            var sessions = await db.PlannedSessions.Where(s => s.WeekNumber == week && s.PlanId == Guid.Parse(plan.GetProperty("id").GetString()!)).ToListAsync();
            var prescribed = sessions.Select(s => PrescriptionJson.Read(s.PrescriptionJson)).ToList();
            return (plan, PriorityDelivery.WeeklySets(prescribed).ToDictionary(p => p.Key, p => p.Value),
                sessions.Select(s => (s.EstimatedDurationMinutes, PrescriptionJson.Read(s.PrescriptionJson).Sum(e => e.Sets))).ToList());
        }

        var (plan, sets, sessions) = await ReadAsync(client);
        var (_, plainSets, _) = await ReadAsync(plainClient);
        var report = plan.GetProperty("priorityDelivery").EnumerateArray().ToDictionary(i => Enum.Parse<TrainingMuscleGroup>(i.GetProperty("muscle").GetString()!));
        Assert.Equal(6, report.Count);

        // 1. It shares the volume: the emphasised muscles get at least what they get without any priority, and none is starved.
        foreach (var m in new[] { TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.Quadriceps })
            Assert.True(sets.GetValueOrDefault(m) >= plainSets.GetValueOrDefault(m), $"{m}: {sets.GetValueOrDefault(m)} emphasised against {plainSets.GetValueOrDefault(m)} plain");
        // 2. Maintenance is a lower dose, not a sacrifice: a maintained muscle is "Below" only where the same plan without priorities gives it
        //    no more (calves do not fit three 45-minute sessions at all), and it can hold MORE than its target when an emphasised lift trains
        //    it indirectly: that is counted and the report says so (status Above) instead of hiding it.
        foreach (var m in new[] { TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Calves })
        {
            var status = report[m].GetProperty("status").GetString();
            if (status == "Below") Assert.True(plainSets.GetValueOrDefault(m) <= sets.GetValueOrDefault(m), $"{m} was sacrificed: {sets.GetValueOrDefault(m)} against {plainSets.GetValueOrDefault(m)} without priorities");
            if (sets.GetValueOrDefault(m) > report[m].GetProperty("targetSets").GetDecimal() + 2m) Assert.Equal("Above", status);
        }
        // 3. No unreal session and no volume past the limits: sessions fit the 45 minutes, no muscle passes the absolute weekly ceiling.
        Assert.Equal(3, sessions.Count);
        Assert.All(sessions, s => Assert.True(s.Minutes <= 45, $"a session of {s.Minutes} minutes"));
        Assert.All(sets, p => Assert.True(p.Value <= 20m, $"{p.Key}: {p.Value}"));
        // 4 and 5. It does not promise what it did not schedule: every item reports the real assigned sets and explains anything not met.
        foreach (var (muscle, item) in report)
        {
            Assert.Equal(sets.GetValueOrDefault(muscle), item.GetProperty("assignedSets").GetDecimal());   // 6. indirect work counted as everywhere else
            var status = item.GetProperty("status").GetString();
            var target = item.GetProperty("targetSets").GetDecimal();
            var assigned = item.GetProperty("assignedSets").GetDecimal();
            if (status == "Below") Assert.True(assigned < target - 2m);
            if (status == "Met") Assert.InRange(assigned, target - 2m, target + 2m);
            Assert.Equal(status == "Met", item.GetProperty("note").ValueKind == JsonValueKind.Null);
        }
        // The targets themselves are the planner's, not inflated to look reachable.
        var baselines = MesocycleGenerator.Baselines(baseProfile with { MusclePriorities = typed });
        foreach (var b in baselines.Where(b => b.Priority != TrainingMusclePriority.Grow))
            Assert.Equal(b.Target, report[b.Muscle].GetProperty("targetSets").GetDecimal());
    }

    [Fact]
    public async Task ChangingThePrioritiesPromisesNothingUntilTheNextBlockHasThem()
    {
        var (_, client, _) = await StartPlanAsync();
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await JsonAsync(await client.GetAsync("/api/v1/plans/current"))).GetProperty("priorityDelivery").ValueKind);
        var set = await client.PutAsJsonAsync("/api/v1/plans/current/muscle-priorities",
            new { musclePriorities = new Dictionary<string, string> { ["Chest"] = "Emphasize" } });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        // The block in progress was generated before the change: it is not compared with a target it was not built for.
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await JsonAsync(await client.GetAsync("/api/v1/plans/current"))).GetProperty("priorityDelivery").ValueKind);
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
