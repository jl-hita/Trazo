using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// What an adjustment really reaches in a split, where an exercise comes back several sessions later: the sessions that hold it,
/// not the next sessions of the plan.
/// </summary>
public sealed class FeedbackReachTests : PlansApiBase
{
    private static readonly AdjustableClock Clock = new(Now);

    public FeedbackReachTests() : base(enforced: false, clock: Clock) => Clock.Now = Now;

    /// <summary>Four sessions a week with the bench press only on Monday.</summary>
    private static readonly object[] Split =
    [
        Day("Monday", "Press", "bench-press"), Day("Tuesday", "Pierna", "squat"),
        Day("Thursday", "Hombro", "ohp"), Day("Friday", "Pierna B", "squat")
    ];

    private const int Weeks = 4;

    private static object Day(string day, string title, string exercise) =>
        new { day, title, exercises = new object[] { new { exerciseId = exercise, sets = 3, minRepetitions = 6, maxRepetitions = 10, targetRir = 2 } } };

    private async Task<HttpClient> StartAsync(string email, bool healthConsent = true)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: true, healthConsent: healthConsent));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual", new { name = "Dividido", totalWeeks = Weeks, days = Split });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private static async Task<List<JsonElement>> SessionsAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"))).GetProperty("weeks").EnumerateArray()
            .SelectMany(w => w.GetProperty("sessions").EnumerateArray()).ToList();

    private static bool Holds(JsonElement session, string exercise) =>
        session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString() == exercise;

    private static int Sets(JsonElement session) => session.GetProperty("exercises")[0].GetProperty("sets").GetInt32();

    private static bool Pending(JsonElement session) => session.GetProperty("log").ValueKind == JsonValueKind.Null;

    private static async Task<JsonElement> LogAsync(HttpClient client, JsonElement session, int repetitions = 8, int rir = 2, object? answer = null)
    {
        Clock.Now = Clock.Now.AddDays(2);
        var exercise = session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString()!;
        var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Completed",
            sets = Enumerable.Range(1, Sets(session)).Select(n => new
                { exerciseId = exercise, setNumber = n, weightKg = 60, repetitions, rir, completed = true }).ToArray(),
            exerciseFeedback = answer is null ? null : new Dictionary<string, object> { [exercise] = answer }
        });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("adaptation");
    }

    private static List<JsonElement> Applied(JsonElement report, string kind) =>
        report.GetProperty("applied").EnumerateArray().Where(a => a.GetProperty("kind").GetString() == kind).ToList();

    [Fact]
    public async Task AFatigueCutReachesTheNextThreeSessionsThatHoldTheExercise()
    {
        var client = await StartAsync("reach-cut@example.com");
        var sessions = await SessionsAsync(client);
        // Two weeks of every session, the bench press losing repetitions with no reserve left.
        var bench = 0;
        JsonElement last = default;
        foreach (var session in sessions.Take(9))
            last = Holds(session, "bench-press")
                ? await LogAsync(client, session, repetitions: 9 - bench++, rir: 0)
                : await LogAsync(client, session);

        var cut = Assert.Single(Applied(last, "reduction_sets"));
        var later = (await SessionsAsync(client)).Where(Pending).ToList();
        var benchLeft = later.Where(s => Holds(s, "bench-press")).ToList();
        Assert.NotEmpty(benchLeft);
        Assert.Equal(Math.Min(3, benchLeft.Count), cut.GetProperty("sessionsChanged").GetInt32());
        Assert.All(benchLeft.Take(3), s => Assert.Equal(2, Sets(s)));
        Assert.All(later.Where(s => !Holds(s, "bench-press")), s => Assert.Equal(3, Sets(s)));
    }

    [Theory]
    [InlineData(true, "none")]
    // Without the health consent the answer about discomfort is not even stored.
    [InlineData(false, "none")]
    [InlineData(false, null)]
    public async Task AnAddedSetStaysForTheRestOfTheBlock(bool healthConsent, string? discomfort)
    {
        var client = await StartAsync($"reach-add-{healthConsent}-{discomfort}@example.com", healthConsent);
        var sessions = await SessionsAsync(client);
        var answer = new { stimulus = "low", discomfort, preFatigue = "recovered" };
        JsonElement last = default;
        foreach (var session in sessions.Take(5))
            last = await LogAsync(client, session, answer: Holds(session, "bench-press") ? answer : null);

        var added = Assert.Single(Applied(last, "muscle_add_set"));
        var benchLeft = (await SessionsAsync(client)).Where(s => Pending(s) && Holds(s, "bench-press")).ToList();
        Assert.Equal(benchLeft.Count, added.GetProperty("sessionsChanged").GetInt32());
        Assert.True(benchLeft.Count >= 2);
        Assert.All(benchLeft, s => Assert.Equal(4, Sets(s)));
    }

    [Fact]
    public async Task ASetTheAnswersTookAwayIsNotGivenBackInThatBlock()
    {
        var client = await StartAsync("reach-reduce@example.com");
        var bench = (await SessionsAsync(client)).Where(s => Holds(s, "bench-press")).ToList();
        var loaded = new { stimulus = "high", discomfort = "none", preFatigue = "loaded" };
        await LogAsync(client, bench[0], answer: loaded);
        Assert.Single(Applied(await LogAsync(client, bench[1], answer: loaded), "muscle_reduce_set"));

        // Clean sessions at the lower dose, well past the cooldown of the cut.
        foreach (var session in (await SessionsAsync(client)).Where(s => Pending(s) && Holds(s, "bench-press")).ToList())
        {
            Assert.Equal(2, Sets(session));
            Clock.Now = Clock.Now.AddDays(10);
            Assert.Empty(Applied(await LogAsync(client, session), "restore_sets"));
        }
    }

    [Fact]
    public async Task WithTheConsentAnUnansweredDiscomfortStillDoesNotAddASet()
    {
        var client = await StartAsync("reach-unanswered@example.com");
        var answer = new { stimulus = "low", preFatigue = "recovered" };
        foreach (var session in (await SessionsAsync(client)).Take(5))
            Assert.Empty(Applied(await LogAsync(client, session, answer: Holds(session, "bench-press") ? answer : null), "muscle_add_set"));
    }

    [Fact]
    public async Task AChangeThatReachesNoSessionIsNotAnnounced()
    {
        var client = await StartAsync("reach-nothing@example.com");
        var sessions = await SessionsAsync(client);
        var answer = new { stimulus = "low", discomfort = "none", preFatigue = "recovered" };
        // The first and the last bench sessions of the block: nothing pending holds the exercise afterwards.
        var bench = sessions.Where(s => Holds(s, "bench-press")).ToList();
        await LogAsync(client, bench[0], answer: answer);
        var report = await LogAsync(client, bench[^1], answer: answer);

        Assert.Empty(Applied(report, "muscle_add_set"));
        var history = await JsonAsync(await client.GetAsync("/api/v1/plans/current/adaptations"));
        Assert.DoesNotContain(history.GetProperty("entries").EnumerateArray(), e => e.GetProperty("kind").GetString() == "muscle_add_set");
    }

    [Fact]
    public async Task AFatigueCutWithNoSessionLeftIsNotAnnounced()
    {
        var client = await StartAsync("reach-late-cut@example.com");
        var bench = (await SessionsAsync(client)).Where(s => Holds(s, "bench-press")).ToList();
        JsonElement last = default;
        for (var i = 1; i < bench.Count; i++) last = await LogAsync(client, bench[i], repetitions: 10 - i, rir: 0);

        Assert.Empty(Applied(last, "reduction_sets"));
        var history = await JsonAsync(await client.GetAsync("/api/v1/plans/current/adaptations"));
        Assert.DoesNotContain(history.GetProperty("entries").EnumerateArray(), e => e.GetProperty("kind").GetString() == "reduction_sets");
    }

    // ------------------------------------------------------------------ the answers the adapter asks for

    private static TrainingMuscleFeedbackExposure Exposure(int day, string id, string[] primary, string? stimulus, string? discomfort = "none") =>
        new(new DateOnly(2026, 11, day), id, ["Chest", "Triceps"], 3, stimulus, discomfort, stimulus is null ? null : "recovered", true, primary);

    private static IReadOnlyList<TrainingMuscleFeedbackDecision> Evaluate(string? secondaryDiscomfort) =>
        TrainingMuscleFeedbackAdapter.Evaluate(
            new[] { 2, 4 }.SelectMany(day => new[]
            {
                Exposure(day, "pushdown", ["Triceps"], "low"),
                Exposure(day, "bench", ["Chest"], null, secondaryDiscomfort)
            }).ToArray(),
            new Dictionary<string, int> { ["Triceps"] = 6 }, new Dictionary<string, int> { ["Triceps"] = 20 });

    [Fact]
    public void OnlyTheExercisesThatTrainTheMuscleDirectlyHaveToBeAnswered() =>
        Assert.Equal("pushdown", Assert.Single(Evaluate(null)).ExerciseId);

    [Fact]
    public void DiscomfortInAnExerciseThatOnlyHelpsStillStopsTheAddedSet() =>
        Assert.DoesNotContain(Evaluate("mild"), d => d.Action == TrainingMuscleFeedbackAction.AddSet && d.Muscle == "Triceps");
}
