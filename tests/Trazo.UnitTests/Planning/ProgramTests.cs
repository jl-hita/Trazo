using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Trazo.Application.Localization;
using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;
using Xunit.Abstractions;

namespace Trazo.UnitTests.Planning;

/// <summary>The ten programs over the real catalog: every one generates with a full gym, stays in its time and does what it says.</summary>
public sealed class ProgramLibraryTests(ITestOutputHelper output)
{
    private static readonly string[] Gym = ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"];

    private static MesocycleGeneration Generate(ProgramRecipe recipe, string[] equipment)
    {
        var (request, problem) = ProgramLibrary.Request(recipe, new PlanProfileInput(
            null, null, null, null, null, null, equipment, null, null, null, null, null));
        Assert.Null(problem);
        var parsed = PlanProfileParser.Parse(request, new DateOnly(2026, 11, 30));
        Assert.True(parsed.Succeeded, string.Join(" | ", parsed.Errors.SelectMany(e => e.Value)));
        return MesocycleGenerator.Generate(parsed.Profile!, 1, new MesocycleSlot(1, 1, "accumulation", 1, 4), RealCatalog.Candidates());
    }

    [Fact]
    public void ThereAreTenProgramsWithTheirOwnIds() =>
        Assert.Equal(10, ProgramLibrary.All.Select(p => p.Id).Distinct().Count());

    [Fact]
    public void EveryProgramGenerates_InItsTime_WithItsSessions()
    {
        foreach (var recipe in ProgramLibrary.All)
        {
            var generation = Generate(recipe, Gym);
            Assert.True(generation.Succeeded, $"{recipe.Id}: {string.Join(" | ", generation.Errors)}");
            var week = generation.Sessions.Where(s => s.WeekNumber == 2).ToList();
            Assert.Equal(recipe.SessionsPerWeek, week.Count);
            Assert.All(generation.Sessions, s => Assert.True(s.EstimatedDurationMinutes <= recipe.SessionMinutes, $"{recipe.Id}: {s.EstimatedDurationMinutes}"));
            var sets = week.SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson))
                .SelectMany(e => e.StimulatedMuscles().Select(m => (m, e.Sets))).GroupBy(x => x.m)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Sets));
            output.WriteLine($"{recipe.Id}: {string.Join(", ", sets.OrderByDescending(s => s.Value).Select(s => $"{s.Key} {s.Value}"))}");
            foreach (var muscle in new[] { TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Hamstrings })
                Assert.True(sets.GetValueOrDefault(muscle) >= 4, $"{recipe.Id}: {muscle} {sets.GetValueOrDefault(muscle)}");
        }
    }

    [Theory]
    [InlineData("chest-focus", TrainingMuscleGroup.Chest)]
    [InlineData("back-focus", TrainingMuscleGroup.UpperBack)]
    [InlineData("arms-shoulders-focus", TrainingMuscleGroup.Biceps)]
    public void AFocusProgramGivesItsMuscleMoreThanThePlainSplit(string id, TrainingMuscleGroup muscle)
    {
        var focus = ProgramLibrary.Find(id)!;
        int Sets(ProgramRecipe recipe) => Generate(recipe, Gym).Sessions.Where(s => s.WeekNumber == 3)
            .SelectMany(s => PrescriptionJson.Read(s.PrescriptionJson)).Where(e => e.StimulatedMuscles().Contains(muscle)).Sum(e => e.Sets);
        Assert.True(Sets(focus) > Sets(focus with { Emphasised = [] }), $"{Sets(focus)} / {Sets(focus with { Emphasised = [] })}");
    }

    [Fact]
    public void TheNamesAndDescriptionsAreTranslated()
    {
        foreach (var recipe in ProgramLibrary.All)
        {
            Assert.NotEqual(recipe.Name, Localizer.Translate(recipe.Name, AppLanguage.En));
            Assert.NotEqual(recipe.Description, Localizer.Translate(recipe.Description, AppLanguage.En));
        }
    }
}

public sealed class ProgramApiTests() : PlansApiBase(enforced: false)
{
    [Fact]
    public async Task TheListAndASheetCanBeRead_AndAProgramStartsAPlanWithItsName()
    {
        var client = ClientFor(await AddUserAsync("program@example.com"));
        var list = await JsonAsync(await client.GetAsync("/api/v1/plans/programs"));
        Assert.Equal(10, list.GetArrayLength());

        var sheetResponse = await client.PostAsJsonAsync("/api/v1/plans/programs/full-body-3/sheet", new { });
        Assert.True(sheetResponse.IsSuccessStatusCode, await sheetResponse.Content.ReadAsStringAsync());
        var sheet = await JsonAsync(sheetResponse);
        Assert.True(sheet.GetProperty("weeklySets").GetArrayLength() > 0);
        Assert.True(sheet.GetProperty("sampleSession").GetProperty("exercises").GetArrayLength() > 0);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/plans/current")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/v1/plans/programs/nope", new { })).StatusCode);
        var wrongDays = await client.PostAsJsonAsync("/api/v1/plans/programs/full-body-3", new { trainingDays = new[] { "Monday", "Friday" } });
        Assert.Equal(HttpStatusCode.BadRequest, wrongDays.StatusCode);
        var beginner = await client.PostAsJsonAsync("/api/v1/plans/programs/advanced-hybrid-5", new { experience = "Beginner" });
        Assert.Equal(HttpStatusCode.BadRequest, beginner.StatusCode);

        var created = await client.PostAsJsonAsync("/api/v1/plans/programs/full-body-3", new { });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var plan = await JsonAsync(created);
        Assert.Equal("FullBody", plan.GetProperty("split").GetString());
        Assert.Contains("3", plan.GetProperty("macrocycle").GetProperty("name").GetString());
    }
}

public sealed class ProgramSubscriptionTests() : PlansApiBase(enforced: true)
{
    [Fact]
    public async Task WithoutPremiumTheSheetIsReadable_ButTheProgramDoesNotStart()
    {
        var client = ClientFor(await AddUserAsync("free@example.com"));
        var list = await JsonAsync(await client.GetAsync("/api/v1/plans/programs"));
        Assert.All(list.EnumerateArray(), p => Assert.True(p.GetProperty("locked").GetBoolean()));
        Assert.True((await client.PostAsJsonAsync("/api/v1/plans/programs/full-body-3/sheet", new { })).IsSuccessStatusCode);
        var created = await client.PostAsJsonAsync("/api/v1/plans/programs/full-body-3", new { });
        Assert.Equal(HttpStatusCode.PaymentRequired, created.StatusCode);
    }
}
