using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Trazo.Application.Planning;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The engine's decisions are visible: what it changed (with numbers), what it reviewed and left alone, and the load it proposes.</summary>
public sealed class AdaptationHistoryTests() : PlansApiBase(enforced: true)
{
    private static object Manual() => new
    {
        name = "Rutina", totalWeeks = 4, schedulingMode = "Sequence",
        days = new object[]
        {
            new { title = "A", exercises = new object[] { new { exerciseId = "bench-press", sets = 4, targetRir = 2 } } },
            new { title = "B", exercises = new object[] { new { exerciseId = "squat", sets = 4, targetRir = 2 } } }
        }
    };

    private async Task<HttpClient> StartAsync(string email, bool premium, string? language = null)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: premium));
        if (language is not null) client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(language);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", Manual())).StatusCode);
        return client;
    }

    private static async Task<List<JsonElement>> BlockAsync(HttpClient client)
    {
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        return block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .OrderBy(s => s.GetProperty("sequenceIndex").GetInt32()).ToList();
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, JsonElement session, int reps = 8, decimal weight = 60m, decimal rir = 2m) =>
        client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Completed",
            sets = session.GetProperty("exercises").EnumerateArray().SelectMany(e => Enumerable.Range(1, e.GetProperty("sets").GetInt32())
                .Select(n => new { exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = weight, repetitions = reps, rir, completed = true })).ToArray()
        });

    private static async Task<JsonElement> AdaptationsAsync(HttpClient client) =>
        await JsonAsync(await client.GetAsync("/api/v1/plans/current/adaptations"));

    [Fact]
    public async Task APremiumLogLeavesARecordOfWhatTheEngineDid_OrThatItReviewedAndKeptThePlan()
    {
        var client = await StartAsync("hist-premium@example.com", premium: true);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[1])).StatusCode);

        var history = await AdaptationsAsync(client);
        Assert.True(history.GetProperty("automatic").GetBoolean());
        var entries = history.GetProperty("entries").EnumerateArray().ToList();
        Assert.NotEmpty(entries);
        // Every logged session leaves at least one entry (a change or a review) with its source and a readable summary.
        Assert.Equal(2, entries.Select(e => e.GetProperty("sourceSession").GetString()).Distinct().Count());
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("summary").GetString())));
        var times = entries.Select(e => e.GetProperty("at").GetDateTimeOffset()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.True(history.GetProperty("reviews").GetInt32() + history.GetProperty("changes").GetInt32() >= 2);
    }

    [Fact]
    public async Task AReviewThatChangedNothingSaysSoWithoutCountingAsAChange()
    {
        var client = await StartAsync("hist-review@example.com", premium: true);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        var history = await AdaptationsAsync(client);
        foreach (var review in history.GetProperty("entries").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "review"))
        {
            Assert.False(review.GetProperty("changed").GetBoolean());
            Assert.Equal(0, review.GetProperty("sessionsChanged").GetInt32());
            Assert.StartsWith("Sesión revisada", review.GetProperty("summary").GetString());
        }
    }

    [Fact]
    public async Task OmissionsAreRecordedForEveryoneButAutomaticAdjustmentsOnlyForPremium()
    {
        var client = await StartAsync("hist-free@example.com", premium: false);
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{sessions[1].GetProperty("id").GetString()}/skip", new { reason = "Time" })).StatusCode);

        var history = await AdaptationsAsync(client);
        Assert.False(history.GetProperty("automatic").GetBoolean());
        var entry = Assert.Single(history.GetProperty("entries").EnumerateArray());
        Assert.Equal("skip", entry.GetProperty("kind").GetString());
        Assert.False(entry.GetProperty("changed").GetBoolean());
        Assert.Equal(0, history.GetProperty("changes").GetInt32());
    }

    [Fact]
    public async Task TheSummariesAreInTheLanguageOfTheRequest()
    {
        var client = await StartAsync("hist-en@example.com", premium: true, language: "en");
        var sessions = await BlockAsync(client);
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, sessions[0])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{sessions[1].GetProperty("id").GetString()}/skip", new { reason = "Other" })).StatusCode);

        var text = (await AdaptationsAsync(client)).GetProperty("entries").GetRawText();
        foreach (var spanish in new[] { "Sesión", "sesión", "serie", "carga", "Carga", "revisada", "omitida" })
            Assert.DoesNotContain(spanish, text);
    }

    [Fact]
    public async Task ThereIsNothingToShowWithoutAPlan()
    {
        var client = ClientFor(await AddUserAsync("hist-none@example.com", subscribed: true));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/plans/current/adaptations")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/plans/current/adaptations?take=0")).StatusCode);
    }

    // ------------------------------------------------------------------ load estimation

    private static IReadOnlyList<TrainingExecutedSet> Sets(decimal kg, int reps, decimal rir = 2m, int count = 3) =>
        Enumerable.Range(0, count).Select(_ => new TrainingExecutedSet("working", kg, reps, rir, true)).ToList();

    [Fact]
    public void OneSessionIsNotEnoughToEstimate() =>
        Assert.Null(LoadEstimation.Estimate([Sets(60, 8)], 6, 10, 2m, 60m));

    [Fact]
    public void SteadySessionsKeepTheLoad() =>
        Assert.Equal(60m, LoadEstimation.Estimate([Sets(60, 8), Sets(60, 8), Sets(60, 8)], 6, 10, 2m, 60m));

    [Fact]
    public void OneBadDayDoesNotDragTheLoadDownAsMuchAsRepeatingItWould()
    {
        // The last session was a bad one (6 reps at 57.5 with one in reserve); the two before were at 65 for 8. Repeating would propose 57.5.
        var estimate = LoadEstimation.Estimate([Sets(57.5m, 6, 1m), Sets(65, 8), Sets(65, 8)], 6, 10, 2m, 57.5m);
        Assert.Equal(60m, estimate); // closer to what the history says, and never more than 5 % above the last load
    }

    [Fact]
    public void TheEstimateIsBoundedAroundTheLastLoad()
    {
        // Wildly better history cannot jump more than 5 %; a collapse cannot drop more than 10 %.
        Assert.Equal(62.5m, LoadEstimation.Estimate([Sets(60, 8), Sets(100, 8)], 6, 10, 2m, 60m));
        Assert.Equal(55m, LoadEstimation.Estimate([Sets(60, 8), Sets(20, 8)], 6, 10, 2m, 60m));
    }

    [Fact]
    public void SessionsWithoutUsableSetsAreIgnored() =>
        Assert.Null(LoadEstimation.Estimate([Sets(60, 8), [], []], 6, 10, 2m, 60m));
}
