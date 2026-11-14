using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>Manual plan review: request, queue, assignment, the conversation, handing over and closing.</summary>
public sealed class PlanReviewApiTests() : PlansApiBase(enforced: false)
{
    private const string Route = "/api/v1/plans/current/review-request";
    private const string Admin = "/api/v1/admin/plan-reviews";

    private static async Task<Guid> OpenAsync(HttpClient user, string? note = null)
    {
        var created = await user.PostAsJsonAsync(Route, new { note });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ThreadAsync(HttpClient user) => await JsonAsync(await user.GetAsync(Route + "?messages=true"));
    private static string[] Authors(JsonElement thread) => thread.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("author").GetString()!).ToArray();

    [Fact]
    public async Task NeedsASignedInUserAndAnActivePlan()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).PostAsJsonAsync(Route, new { })).StatusCode);

        var client = ClientFor(await AddUserAsync("noplan@example.com"));
        var response = await client.PostAsJsonAsync(Route, new { });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no_active_plan", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task TheNoteBecomesTheFirstMessage_AndOnlyOneRequestCanBeOpen()
    {
        var (_, client, _) = await StartPlanAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Route, new { note = new string('x', 1001) })).StatusCode);

        await OpenAsync(client, "  Me duele el hombro  ");
        var thread = await ThreadAsync(client);
        Assert.Equal("pending", thread.GetProperty("status").GetString());
        Assert.Equal(["user"], Authors(thread));
        Assert.Equal("Me duele el hombro", thread.GetProperty("messages")[0].GetProperty("body").GetString());

        var again = await client.PostAsJsonAsync(Route, new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("review_already_open", (await JsonAsync(again)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task FullConversation_TakeReplyHandOverAndClose()
    {
        var (_, user, _) = await StartPlanAsync("ana@example.com");
        var id = await OpenAsync(user, "Revisa el volumen");
        var anna = ClientFor(await AddUserAsync("marta@example.com", role: "admin"));
        var ben = ClientFor(await AddUserAsync("ben@example.com", role: "admin"));

        // The queue shows the pending request with the user's note; the count says one is waiting.
        var queue = await JsonAsync(await anna.GetAsync($"{Admin}?status=pending"));
        var item = queue.GetProperty("items")[0];
        Assert.Equal("Revisa el volumen", item.GetProperty("preview").GetString());
        Assert.Equal(1, (await JsonAsync(await anna.GetAsync($"{Admin}/pending-count"))).GetProperty("pending").GetInt32());

        // Marta takes it; Ben cannot take it nor write, but can read it.
        Assert.Equal(HttpStatusCode.NoContent, (await anna.PostAsync($"{Admin}/{id}/claim", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ben.PostAsync($"{Admin}/{id}/claim", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ben.PostAsJsonAsync($"{Admin}/{id}/messages", new { body = "hola" })).StatusCode);
        var asBen = await JsonAsync(await ben.GetAsync($"{Admin}/{id}"));
        Assert.False(asBen.GetProperty("canReply").GetBoolean());
        Assert.Contains(asBen.GetProperty("messages").EnumerateArray(), m => m.GetProperty("author").GetString() == "system");
        Assert.True((await JsonAsync(await anna.GetAsync($"{Admin}/{id}"))).GetProperty("canReply").GetBoolean());

        // Marta answers: the user sees one unread message from "team", and never the audit lines or Marta's name.
        Assert.Equal(HttpStatusCode.NoContent, (await anna.PostAsJsonAsync($"{Admin}/{id}/messages", new { body = "Cuéntame más" })).StatusCode);
        var summary = await JsonAsync(await user.GetAsync(Route));
        Assert.Equal("in_progress", summary.GetProperty("status").GetString());
        Assert.Equal(1, summary.GetProperty("unread").GetInt32());
        var thread = await ThreadAsync(user);
        Assert.Equal(["user", "team"], Authors(thread));
        Assert.DoesNotContain("marta", thread.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, (await JsonAsync(await user.GetAsync(Route))).GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.Created, (await user.PostAsJsonAsync(Route + "/messages", new { body = "Me duele al subir el brazo" })).StatusCode);

        // Marta hands it back with a reason; it is pending and unassigned again, and Ben continues with the whole thread.
        Assert.Equal(HttpStatusCode.Conflict, (await ben.PostAsJsonAsync($"{Admin}/{id}/release", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await anna.PostAsJsonAsync($"{Admin}/{id}/release", new { reason = "Termino mi turno" })).StatusCode);
        Assert.Equal("pending", (await JsonAsync(await user.GetAsync(Route))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await ben.PostAsync($"{Admin}/{id}/claim", null)).StatusCode);
        var detail = await JsonAsync(await ben.GetAsync($"{Admin}/{id}"));
        Assert.Equal(["user", "system", "staff", "user", "system", "system"], Authors2(detail));
        Assert.Contains("Termino mi turno", detail.GetRawText());

        // Ben closes it with a final message; the conversation is then read-only and a new request may be opened.
        Assert.Equal(HttpStatusCode.NoContent, (await ben.PostAsJsonAsync($"{Admin}/{id}/review", new { message = "Todo ajustado" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ben.PostAsJsonAsync($"{Admin}/{id}/review", new { })).StatusCode);
        var closed = await ThreadAsync(user);
        Assert.Equal("reviewed", closed.GetProperty("status").GetString());
        Assert.Equal("Todo ajustado", closed.GetProperty("messages").EnumerateArray().Last().GetProperty("body").GetString());
        var late = await user.PostAsJsonAsync(Route + "/messages", new { body = "gracias" });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Equal("review_closed", (await JsonAsync(late)).GetProperty("title").GetString());
        await OpenAsync(user);
    }

    private static string[] Authors2(JsonElement detail) => detail.GetProperty("messages").EnumerateArray().Select(m => m.GetProperty("author").GetString()!).ToArray();

    [Fact]
    public async Task ASuperadminCanGiveBackARequestSomeoneElseHolds()
    {
        var (_, user, _) = await StartPlanAsync("carl@example.com");
        var id = await OpenAsync(user);
        var admin = ClientFor(await AddUserAsync("admin@example.com", role: "admin"));
        var root = ClientFor(await AddUserAsync("root@example.com", role: "superadmin"));
        await admin.PostAsync($"{Admin}/{id}/claim", null);

        var other = ClientFor(await AddUserAsync("admin2@example.com", role: "admin"));
        Assert.Equal(HttpStatusCode.Conflict, (await other.PostAsJsonAsync($"{Admin}/{id}/release", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await root.PostAsJsonAsync($"{Admin}/{id}/release", new { reason = "De vacaciones" })).StatusCode);
        Assert.Equal("pending", (await JsonAsync(await user.GetAsync(Route))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AUserCannotFloodTheTeam()
    {
        var (_, user, _) = await StartPlanAsync("dora@example.com");
        await OpenAsync(user, "Uno");
        for (var i = 0; i < 4; i++)
            Assert.Equal(HttpStatusCode.Created, (await user.PostAsJsonAsync(Route + "/messages", new { body = $"Mensaje {i}" })).StatusCode);

        var blocked = await user.PostAsJsonAsync(Route + "/messages", new { body = "Otro" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("wait_for_reply", (await JsonAsync(blocked)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync(Route + "/messages", new { body = "  " })).StatusCode);
    }

    [Fact]
    public async Task PermissionsFollowTheRoles()
    {
        var (_, user, _) = await StartPlanAsync("bob@example.com");
        var id = await OpenAsync(user);
        var admin = ClientFor(await AddUserAsync("admin@example.com", role: "admin"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"{Admin}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.PostAsync($"{Admin}/{id}/claim", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"{Admin}/pending-count")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Admin}?status=zzz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Admin}/{Guid.NewGuid()}/claim", null)).StatusCode);
        // An administrator is not a superadmin: the rest of the console stays closed to them.
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/admin/logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/admin/users")).StatusCode);
    }
}
