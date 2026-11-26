using System.Text.RegularExpressions;
using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// What the engine and the planning rules know about an exercise comes from its catalog metadata, never from its identifier:
/// a new exercise takes part in selection, volume and injury exclusions without any code change.
/// </summary>
public sealed class ExerciseMetadataDrivenTests
{
    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trazo.sln"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public async Task NoProductionCodeNamesASeedExerciseOrDeducesSomethingFromAnId()
    {
        var seed = await Trazo.Infrastructure.Seeding.ExerciseSeed.LoadAsync(
            Path.Combine(Root(), "src", "Trazo.Infrastructure", "Seeds"));
        // Single words ("squat", "plank") are ordinary vocabulary too; an id with a separator is unmistakably an exercise.
        var ids = seed.Entries.Select(e => e.Id).Where(id => id.Contains('-')).ToHashSet(StringComparer.Ordinal);
        var literal = new Regex("\"([a-z0-9]+(?:-[a-z0-9]+)+)\"");
        // Searching ids for what a user typed is fine; comparing an id with a fixed text is not.
        var onTheId = new Regex(@"(ExerciseId|\.Id)\??\.(Contains|StartsWith|EndsWith)\(""");

        var offences = Directory.EnumerateFiles(Path.Combine(Root(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                // Equipment keys and their labels share a few names with exercises.
                !path.EndsWith("ExerciseCatalogVocabulary.cs", StringComparison.Ordinal) &&
                !path.EndsWith("ExerciseCatalogEndpoints.cs", StringComparison.Ordinal))
            .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, number: index + 1)))
            .Where(x => onTheId.IsMatch(x.line) || literal.Matches(x.line).Any(m => ids.Contains(m.Groups[1].Value)))
            .Select(x => $"{Path.GetFileName(x.path)}:{x.number}: {x.line.Trim()}")
            .ToList();

        Assert.True(offences.Count == 0, "Put this in the exercise metadata instead:\n" + string.Join("\n", offences));
    }

    private static TrainingExerciseCandidate New(
        string id, string[] secondary, string[] effective, string[]? areas = null, string[]? emphasis = null, string primary = "Deltoids")
    {
        var item = ExerciseCandidateMapperTests.Item(id, "Accessory", primary: [primary], secondary: secondary, equipment: ["cable"]);
        item.EffectiveSecondaryMuscles = effective;
        item.LimitationAreas = areas ?? [];
        item.Emphasis = emphasis ?? [];
        return ExerciseCandidateMapper.Map([item]).Candidates.Single();
    }

    [Fact]
    public void ANewExerciseCountsTheVolumeItsMetadataSays()
    {
        var candidate = New("brand-new-movement", ["UpperBack", "Triceps"], ["UpperBack"]);

        Assert.Equal(new[] { TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Deltoids }.Order().ToArray(), candidate.StimulatedMuscles().Order().ToArray());
    }

    [Fact]
    public void ANewExerciseIsExcludedForTheAreasItDeclares_WhateverItIsCalled()
    {
        var declared = New("gentle-name", [], [], areas: ["Shoulder"]);
        var scaryName = New("overhead-dip-press-raise", [], []);

        var excluded = LimitationRules.ExcludedExerciseIds(["Shoulder"], [declared, scaryName]);

        Assert.Equal(["gentle-name"], excluded);
        Assert.Empty(LimitationRules.ExcludedExerciseIds(["Knee"], [declared, scaryName]));
    }

    [Fact]
    public void AnExerciseNobodyReviewedIsNotPlannedAndAnyLimitationAvoidsIt()
    {
        var item = ExerciseCandidateMapperTests.Item("unreviewed-movement");
        item.MetadataReviewed = false;
        var candidate = ExerciseCandidateMapper.Map([item]).Candidates.Single();

        Assert.False(candidate.IsReviewed);
        Assert.Equal(["unreviewed-movement"], LimitationRules.ExcludedExerciseIds(["Wrist"], [candidate]));
        Assert.Empty(LimitationRules.ExcludedExerciseIds([], [candidate]));
    }

    [Fact]
    public void ASessionStoredBeforeTheFieldIsCompletedFromTheCatalog_WithoutChangingWhatAlreadySaysIt()
    {
        const string old = """{"exercises":[{"exerciseId":"row","sets":3,"primaryMuscleGroups":["Lats"],"secondaryMuscleGroups":["Biceps","Forearms"],"supersetGroup":"A"}]}""";
        var catalog = new Dictionary<string, IReadOnlyList<string>> { ["row"] = ["Biceps", "Triceps"] };

        var completed = PrescriptionJson.CompleteStimulus(old, catalog);

        // Only secondary muscles the stored session already listed can count, and everything else stays.
        Assert.Contains("\"effectiveSecondaryMuscleGroups\":[\"Biceps\"]", completed);
        Assert.Contains("\"supersetGroup\":\"A\"", completed);
        Assert.Equal(completed, PrescriptionJson.CompleteStimulus(completed, new Dictionary<string, IReadOnlyList<string>> { ["row"] = [] }));
    }
}
