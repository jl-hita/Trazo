using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The user's decisions over generated sessions: what is derived from an edit and how a later block takes it.</summary>
public sealed class UserAdjustmentsTests
{
    private static readonly IReadOnlyCollection<TrainingExerciseCandidate> Catalog = RealCatalog.Candidates();
    private static readonly HashSet<string> None = new(StringComparer.OrdinalIgnoreCase);

    private static string Session(params (string Id, int Sets)[] exercises) =>
        PrescriptionJson.Serialize(exercises.Select((e, i) =>
        {
            var c = Catalog.Single(x => x.ExerciseId == e.Id);
            return new TrainingExercisePrescription(c.ExerciseId, c.CatalogVersion, c.Name, i + 1, e.Sets, 8, 12, 2m, 90,
                TrainingProgressionRule.DoubleProgression, "x", c.PrimaryMuscleGroups, c.SecondaryMuscleGroups, TargetLoadKg: 40m);
        }));

    private static ManualDay Wanted(params ManualExercise[] exercises) => new(null, "x", exercises, []);

    private static ManualExercise Straight(string id, int sets = 3) => new(id, sets, 8, 12, 2m, 90);

    private static string[] Ids(string json) => PrescriptionJson.Read(json).OrderBy(e => e.Order).Select(e => e.ExerciseId).ToArray();

    [Fact]
    public void AnUnchangedSessionIsNoDecision()
    {
        var json = Session(("bench-press", 3), ("barbell-curl", 3));
        Assert.True(UserAdjustments.Between(json, Wanted(Straight("bench-press"), Straight("barbell-curl"))).IsEmpty);
    }

    [Fact]
    public void AReplacementSurvivesABlockThatChoseTheOldExerciseAgain()
    {
        var json = Session(("bench-press", 3), ("barbell-curl", 3));
        var change = UserAdjustments.Between(json, Wanted(Straight("db-bench-press"), Straight("barbell-curl")));
        Assert.Equal(["bench-press"], change.Removed);

        var next = UserAdjustments.Apply(Session(("bench-press", 4), ("barbell-curl", 2)), change, Catalog, None);
        Assert.Equal(["db-bench-press", "barbell-curl"], Ids(next.Json));
        // The dose the user did not touch stays the new block's, and so do the other exercises.
        Assert.Equal(2, PrescriptionJson.Read(next.Json)[1].Sets);
    }

    [Fact]
    public void AFixedDoseIsAppliedAndMarked_ButNotOnADeload_AndTheLoadStays()
    {
        var json = Session(("bench-press", 3));
        var change = UserAdjustments.Between(json, Wanted(Straight("bench-press", 5)));
        var normal = UserAdjustments.Apply(Session(("bench-press", 3)), change, Catalog, None);
        var exercise = Assert.Single(PrescriptionJson.Read(normal.Json));
        Assert.Equal((5, 40m), (exercise.Sets, exercise.TargetLoadKg!.Value));
        Assert.Contains(UserAdjustments.FixedMark, normal.Json);
        Assert.True(normal.ExtraSeconds > 0);

        var light = UserAdjustments.Apply(Session(("bench-press", 2)), change, Catalog, None, lightBlock: true);
        Assert.Equal(2, Assert.Single(PrescriptionJson.Read(light.Json)).Sets);
    }

    [Fact]
    public void SafetyComesBeforeTheUser()
    {
        var change = UserAdjustments.Between(Session(("bench-press", 3)), Wanted(Straight("db-bench-press")));
        var next = UserAdjustments.Apply(
            Session(("bench-press", 3), ("barbell-curl", 3)), change, Catalog, new HashSet<string> { "db-bench-press" });
        // The excluded choice is not brought back; the removal still holds.
        Assert.Equal(["barbell-curl"], Ids(next.Json));
    }

    [Fact]
    public void ASessionIsNeverLeftEmpty()
    {
        var change = new SessionAdjustment(["bench-press"], []);
        Assert.Equal(["bench-press"], Ids(UserAdjustments.Apply(Session(("bench-press", 3)), change, Catalog, None).Json));
    }

    [Fact]
    public void ASupersetIsKeptWithItsExercisesTogether()
    {
        var json = Session(("barbell-curl", 3), ("bench-press", 3), ("db-fly", 3));
        var wanted = new ManualDay(null, "x",
            [Straight("barbell-curl") with { SupersetGroup = "A" }, Straight("db-fly") with { SupersetGroup = "A" }, Straight("bench-press")],
            [new TrainingSupersetPrescription("A", 3, 15, 90)]);
        var change = UserAdjustments.Between(json, wanted);
        var next = UserAdjustments.Apply(json, change, Catalog, None).Json;
        Assert.Equal(["barbell-curl", "db-fly", "bench-press"], Ids(next));
        Assert.Equal("A", PrescriptionJson.SupersetGroup(next, "db-fly"));
        Assert.Equal(3, Assert.Single(PrescriptionJson.Supersets(next)).Rounds);

        // Without one of the two there is no superset.
        var without = UserAdjustments.Apply(Session(("barbell-curl", 3), ("bench-press", 3)), change with { Pins = [] }, Catalog, None).Json;
        Assert.Null(PrescriptionJson.SupersetGroup(without, "barbell-curl"));
        Assert.Empty(PrescriptionJson.Supersets(without));
    }

    [Fact]
    public void ALaterEditWinsAndPutsBackWhatWasRemoved()
    {
        var older = new SessionAdjustment(["barbell-curl"], [new ExercisePin("bench-press", Sets: 5, RestSeconds: 120)]);
        var newer = new SessionAdjustment([], [new ExercisePin("barbell-curl", true, 3), new ExercisePin("bench-press", Sets: 4)]);
        var merged = UserAdjustments.Merge(older, newer, ["bench-press", "barbell-curl"]);
        Assert.Empty(merged.Removed);
        var bench = merged.Pins.Single(p => p.ExerciseId == "bench-press");
        Assert.Equal((4, 120), (bench.Sets!.Value, bench.RestSeconds!.Value));
        Assert.Equal(UserAdjustments.Serialize(merged), UserAdjustments.Serialize(UserAdjustments.Read(UserAdjustments.Serialize(merged))));
    }

    [Fact]
    public void AutomaticChangesLeaveFixedValuesAlone_ExceptWithPain()
    {
        var change = UserAdjustments.Between(Session(("bench-press", 3)), Wanted(Straight("bench-press", 5)));
        var fixedJson = UserAdjustments.Apply(Session(("bench-press", 3)), change, Catalog, None).Json;
        Assert.True(TrainingAdaptationPrescriptionAdjuster.TryReduceSets(fixedJson, new HashSet<string> { "bench-press" }, out var fewer, out _));

        var kept = UserAdjustments.KeepFixed(fixedJson, fewer, _ => false, out var held);
        Assert.Equal(["bench-press"], held);
        Assert.Equal(5, PrescriptionJson.Read(kept)[0].Sets);

        var painful = UserAdjustments.KeepFixed(fixedJson, fewer, _ => true, out var none);
        Assert.Empty(none);
        Assert.Equal(4, PrescriptionJson.Read(painful)[0].Sets);
    }
}
