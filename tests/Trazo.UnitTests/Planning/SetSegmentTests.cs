using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The single reading of sets with segments: what counts as a set, as volume, as performance and as pain.</summary>
public sealed class LoggedSetsTests
{
    private static WorkoutSetLog Set(int number, int segment = 0, bool completed = true, decimal kg = 50m, int reps = 10, bool pain = false,
        string? skipped = null, string exercise = "curl") => new()
        {
            ExerciseId = exercise, SetNumber = number, SegmentIndex = segment, WeightKg = kg, Repetitions = reps, Completed = completed,
            PainReported = pain, SkippedReason = skipped
        };

    [Fact]
    public void Without_segments_every_completed_set_counts_one() =>
        Assert.Equal(2m, LoggedSets.EffectiveCount([Set(1), Set(2), Set(3, completed: false)]));

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void All_the_segments_of_a_set_add_half_a_set_together(int segments) =>
        Assert.Equal(2.5m, LoggedSets.EffectiveCount([Set(1), Set(2), .. Enumerable.Range(1, segments).Select(n => Set(2, n))]));

    [Fact]
    public void Segments_of_a_set_that_was_not_done_add_nothing_and_neither_do_unfinished_segments()
    {
        Assert.Equal(0m, LoggedSets.EffectiveCount([Set(1, completed: false), Set(1, 1)]));
        Assert.Equal(1m, LoggedSets.EffectiveCount([Set(1), Set(1, 1, completed: false)]));
    }

    [Fact]
    public void Each_exercise_counts_its_own_sets() =>
        Assert.Equal(2m, LoggedSets.EffectiveCount([Set(1), Set(1, exercise: "row")]));

    [Fact]
    public void Progression_reads_the_main_sets_only()
    {
        var records = LoggedSets.ProgressionRecords([Set(2), Set(1), Set(2, 1, kg: 30m, reps: 4, skipped: "fatigue", completed: false)]);
        Assert.Equal(2, records.Length);
        Assert.All(records, r => Assert.Equal((50m, 10, true, false), (r.WeightKg!.Value, r.Repetitions!.Value, r.Completed, r.Pain)));
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, "pain")]
    public void Pain_in_a_segment_belongs_to_its_set(bool reported, string? skipped)
    {
        var records = LoggedSets.ProgressionRecords([Set(1), Set(2), Set(2, 1, pain: reported, skipped: skipped, completed: skipped is null)]);
        Assert.Equal([false, true], records.Select(r => r.Pain));
    }

    [Fact]
    public void The_limit_of_sets_counts_main_sets_and_segments_have_their_own_room()
    {
        static bool TooMany(int mains, int segmentsEach) => LogSessionValidator.Parse(new LogSessionRequest("Completed", null, null, null, null, null,
            Enumerable.Range(0, mains).SelectMany(i => Enumerable.Range(0, segmentsEach + 1)
                .Select(s => new LogSetRequest($"e{i / 12}", i % 12 + 1, 10m, 5, null, true, null, null, s))).ToArray())).Errors?.ContainsKey("sets") == true;

        Assert.False(TooMany(60, 2));
        Assert.True(TooMany(61, 0));
        Assert.True(TooMany(41, 4));
    }

    [Fact]
    public void Progress_counts_sets_and_volume_once_keeps_records_to_the_main_set_and_adds_every_kilo()
    {
        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        var log = Guid.NewGuid();
        var info = new Dictionary<string, ProgressExerciseInfo> { ["curl"] = new("Curl", ["Biceps"], ["Forearms"]) };
        var view = ProgressCalculator.Build(
            [
                new("curl", log, now, 20m, 10, 1), new("curl", log, now, 20m, 10, 2),
                // A heavier segment must not become the record: only the main set is a performance.
                new("curl", log, now, 25m, 12, 2, 1), new("curl", log, now, 10m, 5, 2, 2)
            ], info, TimeZoneInfo.Utc, DateOnly.FromDateTime(now.UtcDateTime), 3);

        Assert.Equal(2, view.Summary.TotalSets);
        Assert.Equal(750m, view.Summary.TotalVolumeKg);
        Assert.Equal(2.5m, view.Muscles.Single(m => m.Muscle == "Biceps").SetsByWeek[^1]);
        Assert.Equal(2.5m, view.Muscles.Single(m => m.Muscle == "Forearms").SetsByWeek[^1]);
        var point = Assert.Single(view.Exercises.Single().Points);
        Assert.Equal((20m, 10, 2, 750m), (point.TopWeightKg, point.RepsAtTop, point.Sets, point.VolumeKg));
        Assert.Equal(ProgressCalculator.EstimatedOneRepMax(20m, 10), point.EstimatedOneRepMaxKg);
    }

    [Fact]
    public void A_completed_segment_of_an_unfinished_set_does_not_break_the_progress_view()
    {
        var now = new DateTimeOffset(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
        var view = ProgressCalculator.Build([new("curl", Guid.NewGuid(), now, 20m, 5, 1, 1)],
            new Dictionary<string, ProgressExerciseInfo> { ["curl"] = new("Curl", ["Biceps"], []) }, TimeZoneInfo.Utc,
            DateOnly.FromDateTime(now.UtcDateTime), 3);
        Assert.Equal(0, view.Summary.TotalSets);
        Assert.Equal(100m, view.Summary.TotalVolumeKg);
    }
}

/// <summary>Logging sets with segments through the API, on the real service and database.</summary>
public abstract class SetSegmentScenarios(string? postgres) : PlansApiBase(enforced: false, postgres)
{
    private static string LogUrl(JsonElement session) => $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log";

    private static object Row(string exercise, int set, int? segment = null, decimal kg = 60m, int reps = 8, bool completed = true) =>
        new { exerciseId = exercise, setNumber = set, segment, weightKg = kg, repetitions = reps, rir = 2, completed };

    /// <summary>The first session done in full, optionally with two light, short segments after the last set of every exercise.</summary>
    private static object[] Rows(JsonElement session, bool withSegments) => session.GetProperty("exercises").EnumerateArray().SelectMany(e =>
    {
        var id = e.GetProperty("exerciseId").GetString()!;
        var sets = e.GetProperty("sets").GetInt32();
        var rows = Enumerable.Range(1, sets).Select(n => Row(id, n, reps: e.GetProperty("maxRepetitions").GetInt32()));
        return withSegments ? rows.Concat([Row(id, sets, 1, 40m, 3), Row(id, sets, 2, 30m, 2)]) : rows;
    }).ToArray();

    [ScenarioFact]
    public async Task Segments_are_stored_returned_and_replayed_without_duplicates()
    {
        var (user, client, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];
        var body = new { clientRequestId = Guid.NewGuid(), outcome = "Completed", sets = Rows(session, withSegments: true) };

        var first = await client.PostAsJsonAsync(LogUrl(session), body);
        Assert.True(first.IsSuccessStatusCode, await first.Content.ReadAsStringAsync());
        var replay = await client.PostAsJsonAsync(LogUrl(session), body);
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var stored = await db.WorkoutSetLogs.ToListAsync();
        Assert.Equal(body.sets.Length, stored.Count);
        Assert.Equal(session.GetProperty("exercises").GetArrayLength() * 2, stored.Count(s => s.SegmentIndex > 0));

        var export = await (await client.GetAsync("/api/v1/account/export")).Content.ReadAsStringAsync();
        Assert.Contains("\"segment\":2", export.Replace(" ", string.Empty));
    }

    [ScenarioFact]
    public async Task Light_segments_never_change_what_progression_decides_and_do_not_count_as_sets()
    {
        // Two users: the same session done in full, one of them with two light, short segments on every exercise.
        var (_, plain, plainWeek) = await StartPlanAsync("plain@example.com");
        var (_, segmented, segmentedWeek) = await StartPlanAsync("segmented@example.com");
        var one = plainWeek.GetProperty("sessions")[0];
        var two = segmentedWeek.GetProperty("sessions")[0];

        var without = await JsonAsync(await plain.PostAsJsonAsync(LogUrl(one), new { outcome = "Completed", sets = Rows(one, false) }));
        var with = await JsonAsync(await segmented.PostAsJsonAsync(LogUrl(two), new { outcome = "Completed", sets = Rows(two, true) }));

        static Dictionary<string, string> Decisions(JsonElement report) => report.GetProperty("adaptation").GetProperty("exercises").EnumerateArray()
            .ToDictionary(e => e.GetProperty("exerciseId").GetString()!, e => e.GetRawText());
        var expected = Decisions(without);
        var actual = Decisions(with);
        var shared = expected.Keys.Intersect(actual.Keys).ToList();
        Assert.NotEmpty(shared); // the two plans need not pick the same exercises
        Assert.All(shared, id => Assert.Equal(expected[id], actual[id]));
        Assert.Single(actual.Values.Select(v => JsonDocument.Parse(v).RootElement.GetProperty("action").GetString()).Distinct());

        var summary = (await JsonAsync(await segmented.GetAsync("/api/v1/plans/progress"))).GetProperty("summary");
        Assert.Equal(two.GetProperty("exercises").EnumerateArray().Sum(e => e.GetProperty("sets").GetInt32()), summary.GetProperty("totalSets").GetInt32());
    }

    [ScenarioFact]
    public async Task A_segment_needs_its_set_a_valid_number_and_cannot_repeat()
    {
        var (_, client, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];
        var exercise = session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString()!;

        foreach (var rows in new[]
        {
            new[] { Row(exercise, 1, 1) },
            new[] { Row(exercise, 1), Row(exercise, 1, 11) },
            new[] { Row(exercise, 1), Row(exercise, 1, -1) },
            new[] { Row(exercise, 1), Row(exercise, 1, 1), Row(exercise, 1, 1) },
            new[] { Row(exercise, 1), Row(exercise, 1, 0) }
        })
        {
            var response = await client.PostAsJsonAsync(LogUrl(session), new { outcome = "Partial", sets = rows });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}

public sealed class SetSegmentApiTests() : SetSegmentScenarios(null);

[Trait("Category", "Postgres")]
public sealed class PostgresSetSegmentApiTests() : SetSegmentScenarios(PostgresSettings.ConnectionString);
