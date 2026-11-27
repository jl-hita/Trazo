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

/// <summary>Changing a generated plan: what the edit does to the session, what stays for later, and what it refuses.</summary>
public sealed class PlanCustomisationApiTests() : PlansApiBase(enforced: false)
{
    private static string Url(Guid session) => $"/api/v1/plans/current/sessions/{session}/exercises";

    private async Task<HttpClient> GeneratedAsync(string email)
    {
        var client = ClientFor(await AddUserAsync(email));
        client.DefaultRequestHeaders.Add(ClientCapabilities.Header, ClientCapabilities.TechniquesName);
        var created = await client.PostAsJsonAsync("/api/v1/plans", Profile());
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        return client;
    }

    private static async Task<List<JsonElement>> SessionsAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("sessions").EnumerateArray().ToList();

    private static List<Dictionary<string, object?>> Exercises(JsonElement session) =>
        session.GetProperty("exercises").EnumerateArray().Select(e => new Dictionary<string, object?>
        {
            ["exerciseId"] = e.GetProperty("exerciseId").GetString(), ["sets"] = e.GetProperty("sets").GetInt32(),
            ["minRepetitions"] = e.GetProperty("minRepetitions").GetInt32(), ["maxRepetitions"] = e.GetProperty("maxRepetitions").GetInt32(),
            ["targetRir"] = e.GetProperty("targetRir").ValueKind == JsonValueKind.Null ? null : e.GetProperty("targetRir").GetDecimal(),
            ["restSeconds"] = e.GetProperty("restSeconds").GetInt32()
        }).ToList();

    private static Guid Id(JsonElement session) => session.GetProperty("id").GetGuid();

    private static string[] Ids(JsonElement session) =>
        session.GetProperty("exercises").EnumerateArray().Select(e => e.GetProperty("exerciseId").GetString()!).ToArray();

    [Fact]
    public async Task AnEditChangesOnlyThatSession_AndMarksItAsTheUsers()
    {
        var client = await GeneratedAsync("edit@example.com");
        var week = await SessionsAsync(client);
        var first = week[0];
        var wanted = Exercises(first);
        var removed = (string)wanted[^1]["exerciseId"]!;
        wanted.RemoveAt(wanted.Count - 1);
        wanted[0]["sets"] = 5;
        wanted.Reverse();

        var response = await client.PutAsJsonAsync(Url(Id(first)), new { exercises = wanted });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var edited = await JsonAsync(response);

        Assert.True(edited.GetProperty("customised").GetBoolean());
        Assert.Equal(wanted.Select(e => (string)e["exerciseId"]!).ToArray(), Ids(edited));
        Assert.DoesNotContain(removed, Ids(edited));
        Assert.Equal(5, edited.GetProperty("exercises").EnumerateArray()
            .Single(e => e.GetProperty("exerciseId").GetString() == Ids(first)[0]).GetProperty("sets").GetInt32());
        var others = await SessionsAsync(client);
        Assert.All(others.Skip(1), s => Assert.False(s.GetProperty("customised").GetBoolean()));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Empty(await db.PlanSessionAdjustments.ToListAsync());
        var stored = await db.PlannedSessions.SingleAsync(s => s.Id == Id(first));
        Assert.NotEqual(stored.OriginalPrescriptionJson, stored.PrescriptionJson);
        Assert.Single(await db.PlanAdaptations.Where(a => a.Kind == "user_edit").ToListAsync());
    }

    [Fact]
    public async Task AChangeKeptForThePlanReachesTheLaterRepetitions_AndCanBeForgotten()
    {
        var client = await GeneratedAsync("keep@example.com");
        var first = (await SessionsAsync(client))[0];
        var title = first.GetProperty("title").GetString();
        var wanted = Exercises(first);
        var kept = (string)wanted[0]["exerciseId"]!;
        wanted[0]["restSeconds"] = 200;
        var response = await client.PutAsJsonAsync(Url(Id(first)), new { scope = "plan", exercises = wanted });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var layer = UserAdjustments.Read((await db.PlanSessionAdjustments.SingleAsync()).AdjustmentJson);
            Assert.Equal((kept, 200), (layer.Pins.Single().ExerciseId, layer.Pins.Single().RestSeconds!.Value));
            var later = (await db.PlannedSessions.Where(s => s.Id != Id(first)).ToListAsync())
                .Where(s => PrescriptionJson.Read(s.PrescriptionJson).Any(e => e.ExerciseId == kept && e.RestSeconds == 200)).ToList();
            Assert.NotEmpty(later);
            Assert.All(later, s => Assert.Contains("userFixed", s.PrescriptionJson));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/v1/plans/current/adjustments")).StatusCode);
        using var after = _factory.Services.CreateScope();
        Assert.Empty(await after.ServiceProvider.GetRequiredService<TrazoDbContext>().PlanSessionAdjustments.ToListAsync());
        Assert.NotNull(title);
    }

    [Fact]
    public async Task AnInvalidEditIsRefused_AndNothingChanges()
    {
        var client = await GeneratedAsync("refused@example.com");
        var first = (await SessionsAsync(client))[0];
        var unknown = Exercises(first);
        unknown[0]["exerciseId"] = "no-such-exercise";
        var response = await client.PutAsJsonAsync(Url(Id(first)), new { exercises = unknown });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var empty = await client.PutAsJsonAsync(Url(Id(first)), new { exercises = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        var scope = await client.PutAsJsonAsync(Url(Id(first)), new { scope = "forever", exercises = Exercises(first) });
        Assert.Equal(HttpStatusCode.BadRequest, scope.StatusCode);
        Assert.Equal(Ids(first), Ids((await SessionsAsync(client))[0]));
    }

    [Fact]
    public async Task TheTimePerSessionCanBeChanged()
    {
        var client = await GeneratedAsync("minutes@example.com");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/v1/plans/current/session-minutes", new { minutes = 10 })).StatusCode);
        var response = await client.PutAsJsonAsync("/api/v1/plans/current/session-minutes", new { minutes = 90 });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(90, (await JsonAsync(response)).GetProperty("maxSessionMinutes").GetInt32());
    }
}
