using System.Text.RegularExpressions;
using Trazo.Application.Localization;
using Trazo.Application.Planning.Frequency;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Every text this layer produces for the user exists in Spanish and in English. The texts are produced by the real code (the decision, the
/// detector), translated by the real <see cref="Localizer"/>, and the English result must not contain Spanish.
/// </summary>
public sealed partial class NewTextsInBothLanguagesTests
{
    [GeneratedRegex(@"[áéíóúñ¿¡]|\b(el|la|los|las|de|del|que|se|una|por|con|tu|tus|semanas|volumen|bloque|semanales)\b")]
    private static partial Regex Spanish();

    internal static void AssertEnglish(string? text, string where)
    {
        Assert.False(string.IsNullOrWhiteSpace(text), where);
        var english = Localizer.Translate(text, AppLanguage.En)!;
        Assert.False(Spanish().IsMatch(english), $"{where}: still Spanish in English -> {english}");
    }

    private static BlockSummary Block(BlockVerdict verdict, bool pain = false) =>
        new(Guid.Empty, 1, 4, 0, 0, verdict == BlockVerdict.Unfavorable ? 2 : 0, 0, 10m, 12m, 1, 4, pain, verdict);

    [Fact]
    public void Every_longitudinal_decision_reason_is_available_in_English()
    {
        foreach (var muscle in new[] { TrainingMuscleGroup.Chest, TrainingMuscleGroup.Lats, TrainingMuscleGroup.UpperBack, TrainingMuscleGroup.Deltoids,
                     TrainingMuscleGroup.Biceps, TrainingMuscleGroup.Triceps, TrainingMuscleGroup.Quadriceps, TrainingMuscleGroup.Hamstrings,
                     TrainingMuscleGroup.Glutes, TrainingMuscleGroup.Calves })
        {
            var b = new TrainingMuscleBaseline(muscle, TrainingMusclePriority.Grow, 12m, 7m);
            var decisions = new[]
            {
                LongitudinalVolumePlanner.Decide(b, 12m, []),                                  // abstain
                LongitudinalVolumePlanner.Decide(b, 12m, [Block(BlockVerdict.Unfavorable)]),   // decrease
                LongitudinalVolumePlanner.Decide(b, 7m, [Block(BlockVerdict.Unfavorable)]),    // hold at the floor
                LongitudinalVolumePlanner.Decide(b, 10m, [Block(BlockVerdict.Stable, pain: true)]), // hold with pain
                LongitudinalVolumePlanner.Decide(b, 10m, [Block(BlockVerdict.Stable)]),        // restore
                LongitudinalVolumePlanner.Decide(b, 12m, [Block(BlockVerdict.Stable)])         // hold
            };
            Assert.Equal(4, decisions.Select(d => d.Action).Distinct().Count());
            Assert.Equal(6, decisions.Select(d => d.Reason).Distinct().Count());
            foreach (var d in decisions) AssertEnglish(d.Reason, $"{muscle} {d.Action}");
        }
    }

    [Fact]
    public void Every_frequency_assessment_reason_and_weekday_list_is_available_in_English()
    {
        WeeklyAttendance Week(int index, int sessions) =>
            new(new DateOnly(2026, 11, 16).AddDays(7 * index), Enumerable.Range(0, sessions).Select(i => (DayOfWeek)(((i * 2) + 1) % 7)).ToList(), false);
        var scenarios = new List<FrequencyAssessment>
        {
            FrequencyPatternDetector.Assess(0, null, []),
            FrequencyPatternDetector.Assess(4, null, [Week(0, 0), Week(1, 0), Week(2, 0), Week(3, 4)]),
            FrequencyPatternDetector.Assess(4, null, [Week(0, 4)]),
            FrequencyPatternDetector.Assess(4, null, [Week(0, 4), Week(1, 4), Week(2, 4), Week(3, 4)]),
            FrequencyPatternDetector.Assess(4, null, [Week(0, 3), Week(1, 4), Week(2, 4), Week(3, 4)]),
            FrequencyPatternDetector.Assess(4, null, [Week(0, 3), Week(1, 3), Week(2, 3), Week(3, 3), Week(4, 3)]),
            FrequencyPatternDetector.Assess(3, 4, [Week(0, 4), Week(1, 4), Week(2, 4), Week(3, 4)])
        };
        Assert.Contains(scenarios, a => a.Pattern == FrequencyPattern.PersistentReduction);
        Assert.Contains(scenarios, a => a.Pattern == FrequencyPattern.PersistentReturn);
        Assert.Contains(scenarios, a => a.Pattern == FrequencyPattern.Transient);
        foreach (var a in scenarios) AssertEnglish(a.Reason, a.Pattern.ToString());

        // every combination of weekdays a proposal can name
        for (var mask = 1; mask < 128; mask++)
        {
            var days = Enumerable.Range(0, 7).Where(i => (mask & (1 << i)) != 0).Select(i => (DayOfWeek)((i + 1) % 7)).ToList();
            var es = FrequencyReorganizer.DaysEs(days);
            var en = Localizer.Translate(es, AppLanguage.En)!;
            Assert.False(Spanish().IsMatch(en), $"{es} -> {en}");
            Assert.Equal(days.Count, en.Split(',').Length);
        }
    }
}
