using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The stored shape of techniques and supersets, what makes a technique valid and what takes it away.</summary>
public sealed class TechniquePrescriptionTests
{
    private static TrainingExercisePrescription Exercise(string id, TrainingTechniquePrescription? technique = null, int sets = 3, int order = 1) =>
        new(id, "1", id, order, sets, 10, 15, 2m, 90, TrainingProgressionRule.DoubleProgression, "x",
            [TrainingMuscleGroup.Biceps], [], EffectiveSecondaryMuscleGroups: [], Technique: technique);

    private static string Stored(params TrainingExercisePrescription[] exercises) =>
        PrescriptionJson.Serialize(exercises, new Dictionary<string, string>(), []);

    private static TrainingTechniquePrescription Drop => TechniqueRules.Default(TrainingTechniqueType.DropSet);

    [Fact]
    public void Straight_sets_are_stored_exactly_as_before()
    {
        var plain = new[] { Exercise("curl") };
        var json = PrescriptionJson.Serialize(plain, new Dictionary<string, string>(), []);
        Assert.Equal(PrescriptionJson.Serialize(plain, new Dictionary<string, string>()), json);
        Assert.DoesNotContain("technique", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prescriptionVersion", json);
        Assert.Empty(PrescriptionJson.Supersets(json));
    }

    [Fact]
    public void Techniques_and_superset_rounds_are_read_back_as_written()
    {
        var superset = new TrainingSupersetPrescription("A", 3, 15, 90);
        var json = PrescriptionJson.Serialize([Exercise("curl", Drop), Exercise("row", order: 2), Exercise("press", order: 3)],
            new Dictionary<string, string> { ["row"] = "A", ["press"] = "A" }, [superset]);
        Assert.Equal(PrescriptionJson.TechniqueVersion, (int)JsonNode.Parse(json)!["prescriptionVersion"]!);
        Assert.Equal("DropSet", (string?)JsonNode.Parse(json)!["exercises"]![0]!["technique"]!["type"]);
        var read = PrescriptionJson.Read(json);
        Assert.Equal(Drop, read[0].Technique);
        Assert.Null(read[1].Technique);
        Assert.Equal(superset, Assert.Single(PrescriptionJson.Supersets(json)));
        Assert.Equal("A", PrescriptionJson.SupersetGroup(json, "row"));
    }

    public static TheoryData<TrainingTechniqueType> Types => new(Enum.GetValues<TrainingTechniqueType>());

    [Theory]
    [MemberData(nameof(Types))]
    public void Every_default_is_valid_where_the_exercise_admits_it(TrainingTechniqueType type) =>
        Assert.Empty(TechniqueRules.Validate(TechniqueRules.Default(type), 3, [type], inSuperset: false));

    [Fact]
    public void A_technique_is_refused_where_it_cannot_be_done()
    {
        TrainingTechniqueType[] all = Enum.GetValues<TrainingTechniqueType>();
        Assert.NotEmpty(TechniqueRules.Validate(Drop, 3, [TrainingTechniqueType.RestPause], false));
        Assert.NotEmpty(TechniqueRules.Validate(Drop, 3, null, false));
        Assert.NotEmpty(TechniqueRules.Validate(Drop, 3, all, inSuperset: true));
        Assert.NotEmpty(TechniqueRules.Validate(Drop with { MaxSegments = 4 }, 3, all, false));
        Assert.NotEmpty(TechniqueRules.Validate(Drop with { LoadDropPercent = null }, 3, all, false));
        Assert.NotEmpty(TechniqueRules.Validate(Drop with { LoadDropPercent = 60 }, 3, all, false));
        Assert.NotEmpty(TechniqueRules.Validate(Drop with { SegmentMinRepetitions = 3 }, 3, all, false));
        Assert.NotEmpty(TechniqueRules.Validate(Drop with { RestSeconds = 61 }, 3, all, false));
        var myo = TechniqueRules.Default(TrainingTechniqueType.MyoReps);
        Assert.NotEmpty(TechniqueRules.Validate(myo with { LoadDropPercent = 20 }, 3, all, false));
        Assert.NotEmpty(TechniqueRules.Validate(myo with { SegmentMinRepetitions = 6, SegmentMaxRepetitions = 5 }, 3, all, false));
        Assert.NotEmpty(TechniqueRules.Validate(TechniqueRules.Default(TrainingTechniqueType.MyoRepMatch), 1, all, false));
    }

    [Theory]
    [InlineData("A", 3, 15, 90, true)]
    [InlineData("G", 3, 15, 90, false)]
    [InlineData("A", 0, 15, 90, false)]
    [InlineData("A", 3, -1, 90, false)]
    [InlineData("A", 3, 15, 601, false)]
    public void Superset_rounds_have_limits(string group, int rounds, int between, int after, bool valid) =>
        Assert.Equal(valid, TechniqueRules.Validate(new TrainingSupersetPrescription(group, rounds, between, after)).Count == 0);

    private static TrainingTechniquePrescription? After(string json, string id = "curl") =>
        PrescriptionJson.Read(json).Single(e => e.ExerciseId == id).Technique;

    [Fact]
    public void Whatever_makes_a_session_lighter_takes_the_technique_away()
    {
        var json = Stored(Exercise("curl", Drop), Exercise("row", Drop, order: 2));
        var curl = new HashSet<string> { "curl" };

        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(json, curl, out var fewer, out _));
        Assert.Null(After(fewer));
        Assert.NotNull(After(fewer, "row"));
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryRestoreSets(fewer, curl, out var restored, out _, out _));
        Assert.Null(After(restored)); // giving the set back does not bring the technique back

        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryRaiseReserve(json, out var reserve, out _));
        Assert.All(PrescriptionJson.Read(reserve), e => Assert.Null(e.Technique));
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryApplyDeload(json, out var deload, out _));
        Assert.All(PrescriptionJson.Read(deload), e => Assert.Null(e.Technique));
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(json, "curl", -1, out var less, out _));
        Assert.Null(After(less));
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryAdjustSets(json, "curl", +1, out var more, out _));
        Assert.NotNull(After(more));
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TrySetLoad(json, "curl", 20m, "x", out var loaded, out _));
        Assert.NotNull(After(loaded));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_swap_keeps_the_technique_only_if_the_replacement_admits_it(bool admits)
    {
        var replacement = RealCatalog.Candidates().First(c => c.ExerciseId == "cable-curl") with
        {
            AllowedTechniques = admits ? [TrainingTechniqueType.DropSet] : [TrainingTechniqueType.RestPause]
        };
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TrySwapExercise(Stored(Exercise("curl", Drop)), "curl", replacement, out var swapped, out _));
        Assert.Equal(admits ? Drop : null, After(swapped, "cable-curl"));
    }

    [Fact]
    public void Recovery_reads_a_set_with_segments_as_close_to_failure()
    {
        static WorkoutSetLog Set(int segment, decimal? rir, bool completed = true) =>
            new() { ExerciseId = "curl", SetNumber = 1, SegmentIndex = segment, Rir = rir, Completed = completed };
        Assert.Equal(3m, LoggedSets.RecoveryRir([Set(0, 3m)]));
        Assert.Null(LoggedSets.RecoveryRir([Set(0, null)]));
        Assert.Equal(1m, LoggedSets.RecoveryRir([Set(0, 3m), Set(1, null)]));
        Assert.Equal(0m, LoggedSets.RecoveryRir([Set(0, 0m), Set(1, null)]));
        Assert.Equal(1m, LoggedSets.RecoveryRir([Set(0, null), Set(1, 4m)]));
        Assert.Equal(3m, LoggedSets.RecoveryRir([Set(0, 3m), Set(1, 0m, completed: false)]));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("techniques-1", true)]
    [InlineData("other, Techniques-1", true)]
    [InlineData("techniques-10", false)]
    public void The_capability_is_declared_by_name(string? header, bool expected)
    {
        var client = new ClientCapabilities();
        client.Read(header);
        Assert.Equal(expected, client.Techniques);
    }
}

/// <summary>What an app is served depending on what it says it can show.</summary>
public sealed class TechniqueCapabilityApiTests() : PlansApiBase(enforced: false)
{
    [Fact]
    public async Task Only_an_app_that_declares_the_capability_sees_techniques_rounds_and_segments()
    {
        var (_, old, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];
        var sessionId = session.GetProperty("id").GetGuid();
        var exercise = session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString()!;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var stored = await db.PlannedSessions.SingleAsync(s => s.Id == sessionId);
            var root = JsonNode.Parse(stored.PrescriptionJson)!.AsObject();
            root["exercises"]![0]!["technique"] = JsonNode.Parse("""{"type":"DropSet","appliesTo":"LastSet","maxSegments":1,"restSeconds":10,"loadDropPercent":20}""");
            foreach (var item in root["exercises"]!.AsArray().Skip(1).Take(2)) item!["supersetGroup"] = "A";
            root["supersets"] = JsonNode.Parse("""[{"group":"A","rounds":1,"restBetweenSeconds":15,"restAfterRoundSeconds":90}]""");
            stored.PrescriptionJson = root.ToJsonString();
            await db.SaveChangesAsync();
        }
        var logged = await old.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessionId}/log", new
        {
            outcome = "Partial",
            sets = new object[]
            {
                new { exerciseId = exercise, setNumber = 1, weightKg = 20m, repetitions = 10, completed = true },
                new { exerciseId = exercise, setNumber = 1, segment = 1, weightKg = 16m, repetitions = 6, completed = true }
            }
        });
        Assert.True(logged.IsSuccessStatusCode, await logged.Content.ReadAsStringAsync());

        async Task<JsonElement> ReadAsync(HttpClient client) => (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week")))
            .GetProperty("sessions").EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == sessionId);

        var plain = await ReadAsync(old);
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("exercises")[0].GetProperty("technique").ValueKind);
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("supersets").ValueKind);
        Assert.Equal(1, plain.GetProperty("log").GetProperty("sets").GetArrayLength());

        old.DefaultRequestHeaders.Add(ClientCapabilities.Header, ClientCapabilities.TechniquesName);
        var capable = await ReadAsync(old);
        Assert.Equal("DropSet", capable.GetProperty("exercises")[0].GetProperty("technique").GetProperty("type").GetString());
        Assert.Equal(1, capable.GetProperty("supersets")[0].GetProperty("rounds").GetInt32());
        Assert.Equal(2, capable.GetProperty("log").GetProperty("sets").GetArrayLength());
    }
}
