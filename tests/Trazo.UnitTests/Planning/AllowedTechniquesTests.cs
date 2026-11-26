using System.Text.Json;
using Trazo.Application.Catalog;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Which set techniques each seed exercise admits (docs/exercise-review.md): the limits of docs/prescription-techniques.md hold on
/// the whole seed, so a later edit cannot quietly allow a technique where it was ruled out.
/// </summary>
public sealed class AllowedTechniquesTests
{
    private sealed record Entry(
        string Id, string MovementPattern, string Mechanic, string BodyPart, bool IsBodyweight, string[] RequiredEquipment, string[] AllowedTechniques);

    private static readonly Entry[] Seed = Load();

    private static Entry[] Load()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Trazo.sln"))) root = root.Parent!;
        var json = File.ReadAllText(Path.Combine(root.FullName, "src", "Trazo.Infrastructure", "Seeds", "exercise-catalog.json"));
        return JsonSerializer.Deserialize<Entry[]>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static bool Admits(Entry e) => e.AllowedTechniques.Length > 0;

    [Fact]
    public void Every_seed_exercise_declares_known_techniques()
    {
        Assert.All(Seed, e => Assert.NotNull(e.AllowedTechniques));
        Assert.All(Seed.SelectMany(e => e.AllowedTechniques), t => Assert.Contains(t, ExerciseCatalogVocabulary.Techniques));
        Assert.Contains(Seed, Admits);
    }

    [Fact]
    public void Hinges_cardio_and_single_leg_work_admit_none() =>
        Assert.DoesNotContain(Seed, e => e.MovementPattern is "Hinge" or "Cardio" or "SingleLeg" && Admits(e));

    [Fact]
    public void Squat_patterns_admit_none_except_the_machines_that_carry_the_load_for_the_user()
    {
        var admitted = Seed.Where(e => e.MovementPattern == "Squat" && Admits(e)).ToList();
        Assert.All(admitted, e => Assert.Contains(e.RequiredEquipment.Single(), new[] { "leg-press-machine", "hack-squat-machine" }));
        // Never the techniques that restart from near failure with an activation set.
        Assert.All(admitted, e => Assert.DoesNotContain(e.AllowedTechniques, t => t is "MyoReps" or "MyoRepMatch"));
    }

    [Fact]
    public void Barbell_presses_rows_and_overhead_work_admit_none() =>
        Assert.DoesNotContain(Seed, e => e.Mechanic == "compound" && e.RequiredEquipment.Contains("barbell")
            && !e.Id.Contains("inverted-row", StringComparison.Ordinal) && Admits(e));

    [Fact]
    public void A_drop_set_needs_a_load_that_can_be_lowered_at_once() =>
        Assert.DoesNotContain(Seed, e => e.AllowedTechniques.Contains("DropSet")
            && (e.IsBodyweight || e.RequiredEquipment.Length == 0 || e.RequiredEquipment.Any(q => q is "barbell" or "ez-bar" or "resistance-band" or "assisted-pull-up-machine")));

    [Fact]
    public void The_validator_rejects_an_unknown_technique_and_a_drop_set_on_bodyweight()
    {
        var press = new ExerciseMetadata("test-push-up", "Flexión", "Push-up", "Una flexión.", ["Uno.", "Dos.", "Tres."], [], "push", "compound",
            "beginner", null, "chest", 5, false, true, "HorizontalPush", ["Chest"], ["Triceps"], "Beginner", ["Hypertrophy"], [], 50,
            AllowedTechniques: ["restpause"]);
        var valid = ExerciseMetadataValidator.Validate(press);
        Assert.True(valid.IsValid, valid.Describe());
        Assert.Equal(["RestPause"], valid.Metadata.AllowedTechniques!);

        foreach (var wrong in new[] { "Cluster", "DropSet" })
            Assert.Contains("allowedTechniques", ExerciseMetadataValidator.Validate(press with { AllowedTechniques = [wrong] }).Errors.Keys);
    }
}
