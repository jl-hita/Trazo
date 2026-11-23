using System.Text.Json;
using System.Text.RegularExpressions;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Guards the findings of the exercise catalog audit (docs/exercise-catalog-audit.md). Only the metadata the engine does
/// not consume (equipment, bodyweight, unilateral flags) and the wording are checked here; the generator's own behaviour
/// lives in the planning tests.
/// </summary>
public sealed class ExerciseCatalogAuditTests
{
    private static List<JsonElement> Entries()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "src", "Trazo.Infrastructure", "Seeds", "exercise-catalog.json")))
            directory = directory.Parent;
        using var stream = File.OpenRead(Path.Combine(directory!.FullName, "src", "Trazo.Infrastructure", "Seeds", "exercise-catalog.json"));
        return JsonDocument.Parse(stream).RootElement.EnumerateArray().Select(x => x.Clone()).ToList();
    }

    private static string Id(JsonElement x) => x.GetProperty("id").GetString()!;

    [Fact]
    public void SingleLegPatternExercisesAreFlaggedUnilateral()
    {
        var offenders = Entries().Where(x => x.GetProperty("movementPattern").GetString() == "SingleLeg" && !x.GetProperty("isUnilateral").GetBoolean()).Select(Id);
        Assert.Empty(offenders);
    }

    [Fact]
    public void ExercisesThatNeedAnImplementDeclareTheEquipmentToo()
    {
        var offenders = Entries()
            .Where(x => x.GetProperty("requiredEquipment").GetArrayLength() > 0 && x.GetProperty("equipment").ValueKind == JsonValueKind.Null)
            .Select(Id);
        Assert.Empty(offenders);
    }

    [Fact]
    public void BarePullUpBarMovementsAreBodyweight()
    {
        foreach (var id in new[] { "pull-up", "chin-ups", "hanging-knee-raise", "hanging-leg-raise", "negative-pull-up" })
            Assert.True(Entries().Single(x => Id(x) == id).GetProperty("isBodyweight").GetBoolean(), id);
    }

    [Fact]
    public void SpanishTextUsesTheSpanishGymRegister()
    {
        // Latin-American forms the catalog used to mix with Spain Spanish ("acuéstate", "párate", "jalar", English "kettlebell"/"cable").
        var banned = new Regex(@"\b(acuéstate|recuéstate|párate|jala|jalas|jalando|jalar|acostad[oa]|kettlebells?|pantorrillas)\b|máquina de cable", RegexOptions.IgnoreCase);
        var offenders = new List<string>();
        foreach (var entry in Entries())
        {
            var text = new[] { entry.GetProperty("nameEs").GetString()!, entry.GetProperty("descriptionEs").GetString()! }
                .Concat(entry.GetProperty("instructionsEs").EnumerateArray().Select(x => x.GetString()!))
                .Concat(entry.GetProperty("tipsEs").EnumerateArray().Select(x => x.GetString()!));
            // Calves of the body are fine in an exercise that works the ankle flexors or hamstrings from the ball/TRX.
            if (Id(entry) is "ball-leg-curl" or "trx-hamstring-curl") continue;
            offenders.AddRange(text.Where(t => banned.IsMatch(t)).Select(t => $"{Id(entry)}: {t}"));
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void EnglishNamesSpellBicepsAndTricepsInTheirPlural()
    {
        var offenders = Entries().Where(x => Regex.IsMatch(x.GetProperty("nameEn").GetString()!, @"\b(Bicep|Tricep)\b")).Select(Id);
        Assert.Empty(offenders);
    }

    [Fact]
    public void StrengthStyleLiftsDoNotOutrankTheCanonicalHypertrophyChoice()
    {
        int Priority(string id) => Entries().Single(x => Id(x) == id).GetProperty("selectionPriority").GetInt32();
        Assert.True(Priority("pendlay-row") < Priority("seated-cable-row"));
        Assert.True(Priority("rack-pull") < Priority("romanian-deadlift"));
        Assert.True(Priority("weighted-pull-up") < Priority("lat-pulldown"));
        // Evidence-led accessory order: overhead triceps work and seated curls over their weaker siblings.
        Assert.True(Priority("cable-overhead-tricep-extension") > Priority("tricep-pushdown"));
        Assert.True(Priority("seated-leg-curl") > Priority("leg-curl"));
        // Loadable abdominal work ranks above holds, which only serve general-fitness plans.
        Assert.True(Priority("cable-crunch") > Priority("plank"));
    }

    [Fact]
    public void DumbbellOnlyProfileGetsACompletePlanForEveryLevel()
    {
        // The catalog used to have no horizontal pull without a bench or a cable, so these profiles failed to generate.
        var catalog = RealCatalog.Candidates();
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        {
            var result = TrainingProgramGenerator.Generate(new(TrainingGoal.Hypertrophy, experience, new DateOnly(2026, 11, 23), 4,
                [DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday], 60, ["dumbbell"], []), catalog);
            Assert.True(result.Prescription is not null, $"{experience}: {string.Join("; ", result.Errors)}");
            Assert.Contains(result.Prescription!.Sessions.SelectMany(s => s.Exercises), e => e.ExerciseId == "dumbbell-bent-over-row");
        }
    }
}
