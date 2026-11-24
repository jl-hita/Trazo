using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>Any session of the user can be read by id, with its log, even after its plan was archived.</summary>
public sealed class SessionDetailApiTests() : PlansApiBase(enforced: true)
{
    private static readonly object Manual = new
    {
        name = "Dos empujes", totalWeeks = 4, days = new object[]
        {
            new { day = "Monday", title = "Empuje A", exercises = new object[] { new { exerciseId = "bench-press", sets = 3 } } },
            new { day = "Thursday", title = "Pierna", exercises = new object[] { new { exerciseId = "squat", sets = 3 } } }
        }
    };

    private async Task<(HttpClient Client, string SessionId)> StartAsync(string email)
    {
        var client = ClientFor(await AddUserAsync(email));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", Manual)).StatusCode);
        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        var first = block.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .OrderBy(s => s.GetProperty("sequenceIndex").GetInt32()).First();
        return (client, first.GetProperty("id").GetString()!);
    }

    private static Task<HttpResponseMessage> LogAsync(HttpClient client, string id) =>
        client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{id}/log", new
        {
            outcome = "Completed",
            sets = Enumerable.Range(1, 3).Select(n => new { exerciseId = "bench-press", setNumber = n, weightKg = 62.5m, repetitions = 8, rir = 2, completed = true }).ToArray()
        });

    [Fact]
    public async Task ItNeedsASignedInUser_AndAnUnknownSessionIsNotFound()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync($"/api/v1/plans/sessions/{Guid.NewGuid()}")).StatusCode);
        var (client, _) = await StartAsync("detail-unknown@example.com");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/plans/sessions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ALoggedSessionComesWithItsSets_AndAPendingOneWithoutLog()
    {
        var (client, id) = await StartAsync("detail-logged@example.com");
        var pending = await JsonAsync(await client.GetAsync($"/api/v1/plans/sessions/{id}"));
        Assert.Equal(id, pending.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, pending.GetProperty("log").ValueKind);

        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, id)).StatusCode);
        var detail = await JsonAsync(await client.GetAsync($"/api/v1/plans/sessions/{id}"));
        var sets = detail.GetProperty("log").GetProperty("sets").EnumerateArray().ToList();
        Assert.Equal(3, sets.Count);
        Assert.Equal(62.5m, sets[0].GetProperty("weightKg").GetDecimal());
        Assert.Equal(8, sets[0].GetProperty("repetitions").GetInt32());
        Assert.Equal(2m, sets[0].GetProperty("rir").GetDecimal());
        Assert.True(sets[0].GetProperty("completed").GetBoolean());
    }

    [Fact]
    public async Task ASessionOfAnArchivedPlanIsStillReadable()
    {
        var (client, id) = await StartAsync("detail-archived@example.com");
        Assert.Equal(HttpStatusCode.OK, (await LogAsync(client, id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/plans/current/archive", null)).StatusCode);

        var response = await client.GetAsync($"/api/v1/plans/sessions/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await JsonAsync(response)).GetProperty("log").GetProperty("sets").GetArrayLength());
    }

    [Fact]
    public async Task ASessionOfAnotherUserIsNotFound()
    {
        var (_, id) = await StartAsync("detail-owner@example.com");
        var intruder = ClientFor(await AddUserAsync("detail-intruder@example.com"));
        var response = await intruder.GetAsync($"/api/v1/plans/sessions/{id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("session_not_found", await response.Content.ReadAsStringAsync());
    }
}
