using System.Security.Cryptography;
using System.Text;
using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Pins the classical (calendar) plans: a grid of profiles is generated and every session (title, week, day, date, duration and the
/// complete prescription) plus the quality warnings is summarised into one hash per case, kept in <c>Golden/classical-plans.txt</c>.
/// Refactoring the engine must not change a classical plan by accident: when a difference is intended, regenerate the file with
/// <c>TRAZO_UPDATE_GOLDEN=1</c> and review the diff in the pull request. When a hash differs, the full text of the case is written next
/// to the golden file (<c>*.actual.txt</c>) so the difference can be read.
/// </summary>
public sealed class ClassicalPlanGoldenTests
{
    private static readonly (TrainingProgramSplit Split, int Days)[] Layouts =
    [
        (TrainingProgramSplit.Automatic, 2), (TrainingProgramSplit.Automatic, 3), (TrainingProgramSplit.Automatic, 4),
        (TrainingProgramSplit.FullBody, 2), (TrainingProgramSplit.FullBody, 3), (TrainingProgramSplit.FullBody, 4),
        (TrainingProgramSplit.UpperLower, 2), (TrainingProgramSplit.UpperLower, 4),
        (TrainingProgramSplit.PushPullLegs, 3), (TrainingProgramSplit.PushPullLegs, 5), (TrainingProgramSplit.PushPullLegs, 6)
    ];

    private static readonly string[][] Equipment =
    [
        [],
        ["barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar"],
        ["dumbbell", "bench"]
    ];

    private static string WithoutStimulusField(string json) =>
        System.Text.RegularExpressions.Regex.Replace(json, ",\"effectiveSecondaryMuscleGroups\":\\[[^\\]]*\\]", "");

    private static IEnumerable<(string Name, string Text)> Cases()
    {
        var catalog = PlanTestCatalog.Candidates();
        foreach (var goal in Enum.GetValues<TrainingGoal>())
        foreach (var experience in Enum.GetValues<TrainingExperience>())
        foreach (var (split, days) in Layouts)
        for (var equipmentIndex = 0; equipmentIndex < Equipment.Length; equipmentIndex++)
        foreach (var variant in new[] { "block", "deload", "second-block", "limited" })
        {
            var profile = PlanTestCatalog.Profile(
                goal, experience, days, Equipment[equipmentIndex], null, variant == "limited" ? ["Knee", "Shoulder"] : null, 12, 75, split);
            var slot = variant switch
            {
                "deload" => new MesocycleSlot(2, 2, "deload", 5, 1),
                "second-block" => new MesocycleSlot(2, 1, "accumulation", 5, 4),
                _ => new MesocycleSlot(1, 1, "accumulation", 1, 4)
            };
            var result = MesocycleGenerator.Generate(
                profile, 7, slot, catalog,
                previousBlockExerciseIds: variant == "second-block" ? ["back-squat", "bench-press"] : null,
                continuesPreviousBlock: variant == "second-block");
            var name = $"{goal}|{experience}|{split}|{days}|eq{equipmentIndex}|{variant}";
            var text = new StringBuilder();
            if (!result.Succeeded) text.Append("ERRORS " + string.Join(" || ", result.Errors)).Append((char)10);
            foreach (var session in result.Sessions)
                text.Append($"{session.WeekNumber}|{session.Date:yyyy-MM-dd}|{session.Day}|{session.Title}|{session.EstimatedDurationMinutes}|{WithoutStimulusField(session.PrescriptionJson)}").Append((char)10);
            if (result.Quality is { } quality) text.Append("WARN " + string.Join(" || ", quality.Warnings)).Append((char)10);
            yield return (name, text.ToString());
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    [Fact]
    public void ClassicalPlansAreUnchanged()
    {
        // The engine's warnings format decimals with the current culture; the golden is recorded with the invariant one so it is the same everywhere.
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var current = Cases().Select(c => (c.Name, Hash: Hash(c.Text), c.Text)).ToList();
        var path = ResolveGoldenPath();
        if (Environment.GetEnvironmentVariable("TRAZO_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllLines(path, current.Select(c => $"{c.Name} => {c.Hash}"));
            return;
        }

        var golden = File.ReadAllLines(path).Select(line => line.Split(" => ")).ToDictionary(p => p[0], p => p[1]);
        Assert.Equal(golden.Count, current.Count);
        var different = current.Where(c => !golden.TryGetValue(c.Name, out var hash) || hash != c.Hash).ToList();
        if (different.Count > 0)
            File.WriteAllText(path.Replace(".txt", ".actual.txt"), string.Join("\n=====\n", different.Select(c => $"## {c.Name}\n{c.Text}")));
        Assert.True(different.Count == 0, $"{different.Count} classical plans changed, first: {different.FirstOrDefault().Name}");
    }

    private static string ResolveGoldenPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Trazo.UnitTests.csproj"))) directory = directory.Parent;
        return Path.Combine(directory!.FullName, "Planning", "Golden", "classical-plans.txt");
    }
}
