using Trazo.Application.Planning;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Domain.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning.Longitudinal;

/// <summary>
/// What each kind of workout is allowed to teach. The observations are rebuilt from the real logs of a simulated plan (real generator and
/// observer), the logs are damaged the way real life damages them, and the rebuilt rows must not carry the damage into the evidence.
/// </summary>
public sealed class ObservationQualityTests
{
    private const TrainingMuscleGroup Chest = TrainingMuscleGroup.Chest;
    private const TrainingMuscleGroup Triceps = TrainingMuscleGroup.Triceps;

    private static PlanSimulator Trained(Behavior behavior, int blocks = 1, bool deloadLast = false)
    {
        var s = new PlanSimulator(PlanTestCatalog.Profile(days: 4));
        for (var i = 0; i < blocks; i++) s.RunBlock(behavior, learn: false);
        if (deloadLast) s.RunBlock(new Behavior(), deload: true, learn: false);
        return s;
    }

    private static List<MuscleWeekObservation> Rebuild(PlanSimulator s)
    {
        s.Observations.Clear();
        s.RefreshObservations(PlanSimulator.Start.AddDays(7 * s.NextWeek + 30));
        return s.Observations.ToList();
    }

    private static MuscleWeekObservation Row(IEnumerable<MuscleWeekObservation> rows, int period, TrainingMuscleGroup muscle) =>
        rows.Single(o => o.Period == period && o.Muscle == muscle.ToString());

    [Fact]
    public void A_deload_block_is_never_evidence()
    {
        var s = Trained(new Behavior(), deloadLast: true);
        var rows = Rebuild(s);
        var deloadWeek = s.NextWeek - 1;
        Assert.All(rows.Where(o => o.Period == deloadWeek), o => { Assert.Equal("excluded", o.Quality); Assert.Equal("deload", o.Reason); Assert.True(o.IsDeload); });
        // and the engine does not read it: only non-deload blocks are summarised
        var blocks = new[] { new BlockEvidence(s.PlanId, 2, true, rows.Where(o => o.Period == deloadWeek && o.Muscle == "Chest").ToList()) };
        Assert.Empty(BlockAssessor.Summaries(blocks));
    }

    [Fact]
    public void A_workout_registered_more_than_14_days_late_is_not_evidence_and_not_a_reference_for_the_next()
    {
        var s = Trained(new Behavior());
        foreach (var log in s.Logs.Where(l => PeriodOf(s, l) == 2))
            log.LoggedAt = log.PerformedAt.AddDays(LongitudinalPolicy.LateRegistrationDays + 1);
        var rows = Rebuild(s);
        var late = Row(rows, 2, Chest);
        Assert.Equal("excluded", late.Quality);
        Assert.Equal("date_unreliable", late.Reason);
        Assert.Equal(0, late.Exposures);
        // exactly at the limit it still counts
        foreach (var log in s.Logs.Where(l => PeriodOf(s, l) == 2)) log.LoggedAt = log.PerformedAt.AddDays(LongitudinalPolicy.LateRegistrationDays);
        Assert.Equal("valid", Row(Rebuild(s), 2, Chest).Quality);
        // the week after a late one is compared with the last RELIABLE exposure, not with the late one
        foreach (var log in s.Logs.Where(l => PeriodOf(s, l) == 2)) log.LoggedAt = log.PerformedAt.AddDays(30);
        var week3 = Row(Rebuild(s), 3, Chest);
        Assert.NotNull(week3.PerformanceChangePercent);
    }

    [Fact]
    public void A_session_with_pain_is_excluded_and_reported_never_learned_from()
    {
        var s = Trained(new Behavior { Pain = (period, _) => period == 2 });
        var rows = Rebuild(s);
        var week2 = Row(rows, 2, Chest);
        Assert.Equal("excluded", week2.Quality);
        Assert.Equal("pain", week2.Reason);
        Assert.True(week2.PainSessions > 0);
        Assert.Null(week2.PerformanceChangePercent);
        Assert.All(rows.Where(o => o.Period != 2 && o.Quality == "valid"), o => Assert.Equal(0, o.PainSessions));
        // the next block reads the pain: no reduction is given back while it is recent
        var summary = BlockAssessor.Summarize(new BlockEvidence(s.PlanId, 1, false, rows.Where(o => o.Muscle == "Chest").ToList()));
        Assert.True(summary.Pain);
    }

    [Fact]
    public void The_first_workout_after_a_long_absence_counts_for_volume_but_not_for_performance_or_fatigue()
    {
        // Trains in week 1, nothing in weeks 2 and 3 (more than 14 days), and comes back in week 4.
        var s = Trained(new Behavior { Attends = (period, _) => period is 1 or 4 });
        var rows = Rebuild(s);
        var back = Row(rows, 4, Chest);
        Assert.True(back.ReturnFromAbsence);
        Assert.Contains(rows.Where(o => o.Period is 2 or 3 && o.Muscle == "Chest"), o => o.Quality == "excluded");
        Assert.True(back.Exposures < 4, "the re-entry session is not an exposure");
    }

    [Fact]
    public void A_duplicated_registration_of_the_same_planned_session_counts_once()
    {
        var s = Trained(new Behavior());
        var before = Rebuild(s).ToDictionary(o => (o.Period, o.Muscle), o => (o.PerformedSets, o.Exposures, o.PerformanceChangePercent, o.FatigueSignals));
        foreach (var original in s.Logs.ToList())
        {
            var copy = new WorkoutLog
            {
                Id = Guid.NewGuid(), UserId = original.UserId, PlanId = original.PlanId, PlannedSessionId = original.PlannedSessionId,
                LoggedAt = original.LoggedAt.AddHours(2), PerformedAt = original.PerformedAt, PerformedOn = original.PerformedOn, TimeZoneId = "UTC",
                Outcome = WorkoutOutcome.Completed, Recovery = "Insufficient", SessionRpe = 10m, PainReported = true
            };
            foreach (var set in original.Sets)
                copy.Sets.Add(new WorkoutSetLog
                {
                    Id = Guid.NewGuid(), WorkoutLogId = copy.Id, ExerciseId = set.ExerciseId, SetNumber = set.SetNumber, WeightKg = (set.WeightKg ?? 0) * 2,
                    Repetitions = set.Repetitions, Rir = 0m, Completed = true, PainReported = true
                });
            s.Logs.Add(copy);
        }
        var after = Rebuild(s).ToDictionary(o => (o.Period, o.Muscle), o => (o.PerformedSets, o.Exposures, o.PerformanceChangePercent, o.FatigueSignals));
        Assert.Equal(before, after); // the second, painful, doubled-load copy changes nothing
    }

    [Fact]
    public void The_performance_of_a_lift_is_attributed_to_the_muscles_it_trains_as_primary_not_to_its_synergists()
    {
        // The chest lifts get worse, every other lift gets better. The triceps work with the presses (secondary) and have lifts of their own.
        var s = Trained(new Behavior { MuscleResponse = (_, muscle, _) => muscle == Chest ? Response.Poor : Response.Good }, blocks: 1);
        var rows = Rebuild(s);
        var chest = rows.Where(o => o.Muscle == "Chest" && o.PerformanceChangePercent is not null).ToList();
        var triceps = rows.Where(o => o.Muscle == "Triceps" && o.PerformanceChangePercent is not null).ToList();
        Assert.NotEmpty(chest);
        Assert.NotEmpty(triceps);
        Assert.All(chest, o => Assert.True(o.PerformanceChangePercent < 0, $"chest week {o.Period}: {o.PerformanceChangePercent}"));
        Assert.All(triceps, o => Assert.True(o.PerformanceChangePercent > 0, $"triceps week {o.Period}: {o.PerformanceChangePercent} (it must not inherit the bench press)"));
    }

    [Fact]
    public void Missing_feedback_leaves_no_trace_of_a_negative_answer()
    {
        var s = Trained(new Behavior { Feedback = (_, _) => null });
        Assert.All(Rebuild(s), o => { Assert.Equal(0, o.FeedbackAnswers); Assert.Equal(0, o.LowStimulusAnswers); Assert.Equal(0, o.DiscomfortAnswers); });
    }

    private static int PeriodOf(PlanSimulator s, WorkoutLog log) => s.Sessions.Single(x => x.Id == log.PlannedSessionId).WeekNumber;
}
