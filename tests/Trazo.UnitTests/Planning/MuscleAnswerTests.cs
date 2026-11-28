using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning;
using Trazo.Infrastructure.Persistence;
using Trazo.Training;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The two questions about each muscle: what they change, what stops them and where they are kept.</summary>
public sealed class MuscleAnswerTests : PlansApiBase
{
    private static readonly AdjustableClock Clock = new(Now);

    public MuscleAnswerTests() : base(enforced: false, clock: Clock) => Clock.Now = Now;

    private static object Day(string day) => new
    {
        day, title = day, exercises = new object[] { new { exerciseId = "bench-press", sets = 4, minRepetitions = 6, maxRepetitions = 10, targetRir = 2 } }
    };

    private async Task<HttpClient> StartAsync(string email, bool healthConsent = true)
    {
        var client = ClientFor(await AddUserAsync(email, subscribed: true, healthConsent: healthConsent));
        var created = await client.PostAsJsonAsync("/api/v1/plans/manual",
            new { name = "Dos veces", totalWeeks = 4, days = new[] { Day("Monday"), Day("Thursday") } });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private static async Task<List<JsonElement>> PendingAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"))).GetProperty("weeks").EnumerateArray()
            .SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .Where(s => s.GetProperty("log").ValueKind == JsonValueKind.Null).ToList();

    private static int Sets(JsonElement session) => session.GetProperty("exercises")[0].GetProperty("sets").GetInt32();

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, JsonElement session, object? muscles, int repetitions = 8, int rir = 2, string? discomfort = null)
    {
        Clock.Now = Clock.Now.AddDays(2);
        return await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Completed",
            sets = Enumerable.Range(1, Sets(session)).Select(n => new
                { exerciseId = "bench-press", setNumber = n, weightKg = 60, repetitions, rir, completed = true }).ToArray(),
            muscleFeedback = muscles,
            exerciseFeedback = discomfort is null ? null : new Dictionary<string, object> { ["bench-press"] = new { discomfort } }
        });
    }

    /// <summary>Logs the next pending session and returns the muscle set changes it caused.</summary>
    private static async Task<List<string>> LogNextAsync(
        HttpClient client, string? recovery, string? workload, int repetitions = 8, int rir = 2, string? discomfort = null)
    {
        var session = (await PendingAsync(client))[0];
        var muscles = new Dictionary<string, object> { ["Chest"] = new { recovery, workload } };
        var response = await PostAsync(client, session, muscles, repetitions, rir, discomfort);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("adaptation").GetProperty("applied").EnumerateArray()
            .Select(a => a.GetProperty("kind").GetString()!).Where(k => k.StartsWith("muscle_", StringComparison.Ordinal)).ToList();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RecoveredWithLittleWorkTwiceAddsASetForTheRestOfTheBlock(bool healthConsent)
    {
        var client = await StartAsync($"muscle-add-{healthConsent}@example.com", healthConsent);

        Assert.Empty(await LogNextAsync(client, "recovered", "low"));
        Assert.Equal(["muscle_add_set"], await LogNextAsync(client, "recovered", "low"));
        Assert.All(await PendingAsync(client), s => Assert.Equal(5, Sets(s)));
    }

    [Theory]
    [InlineData("notRecovered", "adequate")]
    [InlineData("justRecovered", "high")]
    public async Task TooMuchTwiceTakesASetAway(string recovery, string workload)
    {
        var client = await StartAsync($"muscle-cut-{recovery}@example.com");

        Assert.Empty(await LogNextAsync(client, recovery, workload));
        Assert.Equal(["muscle_reduce_set"], await LogNextAsync(client, recovery, workload));
        Assert.All(await PendingAsync(client), s => Assert.Equal(3, Sets(s)));
    }

    [Theory]
    [InlineData("justRecovered", "low")]
    [InlineData("recovered", "adequate")]
    [InlineData("recovered", null)]
    [InlineData(null, "low")]
    public async Task OtherAnswersChangeNothing(string? recovery, string? workload)
    {
        var client = await StartAsync($"muscle-keep-{recovery}-{workload}@example.com");
        for (var i = 0; i < 3; i++) Assert.Empty(await LogNextAsync(client, recovery, workload));
    }

    [Fact]
    public async Task AChangeUsesUpItsAnswers_AndTheNextWaitsUnlessPerformanceFallsToo()
    {
        var steady = await StartAsync("muscle-steady@example.com");
        var steadyChanges = new List<string>();
        for (var i = 0; i < 5; i++) steadyChanges.AddRange(await LogNextAsync(steady, "notRecovered", "high"));
        // Inside the pause of the first cut: one change.
        Assert.Single(steadyChanges);

        var falling = await StartAsync("muscle-falling@example.com");
        var changes = new List<int>();
        for (var i = 0; i < 5; i++)
            if ((await LogNextAsync(falling, "notRecovered", "high", repetitions: 10 - i, rir: 1)).Count > 0) changes.Add(i);
        // A second cut needs two new sessions after the first one, and does not wait for the pause.
        Assert.Equal([1, 3], changes);
        Assert.All(await PendingAsync(falling), s => Assert.Equal(2, Sets(s)));
    }

    [Fact]
    public async Task LostPerformanceStopsTheAddedSet()
    {
        var client = await StartAsync("muscle-fell@example.com");
        Assert.Empty(await LogNextAsync(client, "recovered", "low", repetitions: 10));
        Assert.Empty(await LogNextAsync(client, "recovered", "low", repetitions: 7));
    }

    [Fact]
    public async Task ReportedDiscomfortStopsTheAddedSet()
    {
        var client = await StartAsync("muscle-sore@example.com");
        Assert.Empty(await LogNextAsync(client, "recovered", "low"));
        Assert.Empty(await LogNextAsync(client, "recovered", "low", discomfort: "mild"));
    }

    [Fact]
    public async Task TheAnswersAreValidatedStoredAndExported()
    {
        var client = await StartAsync("muscle-store@example.com");
        var session = (await PendingAsync(client))[0];
        foreach (var invalid in new object[]
                 {
                     new Dictionary<string, object> { ["Wings"] = new { recovery = "recovered" } },
                     new Dictionary<string, object> { ["3"] = new { recovery = "recovered" } },
                     new Dictionary<string, object> { ["Chest"] = new { recovery = "fine" } },
                     new Dictionary<string, object> { ["Chest"] = new { workload = "huge" } }
                 })
            Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(client, session, invalid)).StatusCode);

        var saved = await PostAsync(client, session, new Dictionary<string, object>
        {
            ["chest"] = new { recovery = "NotRecovered", workload = "High" }, ["Triceps"] = new { }
        });
        Assert.True(saved.StatusCode == HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var stored = (await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().WorkoutLogs.AsNoTracking().SingleAsync()).MuscleFeedbackJson;
        Assert.Equal("""[{"muscle":"Chest","recovery":"notRecovered","workload":"high"}]""", stored);
        Assert.Equal(["Chest"], MuscleAnswers.NotRecovered(stored));

        var export = await client.GetAsync("/api/v1/account/export");
        Assert.Contains("notRecovered", await export.Content.ReadAsStringAsync());
    }

    // ------------------------------------------------------------------ the adapter

    private static TrainingMuscleSession Session(int order, string muscle, string recovery, string workload, params TrainingMuscleExercise[] exercises) =>
        new(order, muscle, recovery, workload, true, false, false, exercises);

    [Fact]
    public void AnExerciseThatAlsoTrainsAMuscleWithEnoughNeverTakesASet()
    {
        var row = new TrainingMuscleExercise("row", 3, ["Lats", "UpperBack"]);
        var pulldown = new TrainingMuscleExercise("pulldown", 4, ["Lats"]);
        var sessions = new[] { 1, 2 }.SelectMany(order => new[]
        {
            Session(order, "Lats", "recovered", "low", row, pulldown),
            Session(order, "UpperBack", "notRecovered", "high", row)
        }).ToArray();

        var decisions = TrainingMuscleAnswerAdapter.Evaluate(sessions);

        Assert.Equal(("pulldown", TrainingMuscleFeedbackAction.AddSet),
            decisions.Where(d => d.Muscle == "Lats").Select(d => (d.ExerciseId, d.Action)).Single());
        Assert.Equal(TrainingMuscleFeedbackAction.ReduceSet, decisions.Single(d => d.Muscle == "UpperBack").Action);
    }
}
