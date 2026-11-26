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

/// <summary>Manual plans with techniques and superset rounds: what is stored, what is refused and who may edit them.</summary>
public sealed class ManualTechniqueApiTests() : PlansApiBase(enforced: false)
{
    private const string Url = "/api/v1/plans/manual";
    private const string EditUrl = "/api/v1/plans/current/manual";

    private async Task AdmitAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var curl = await db.ExerciseCatalog.SingleAsync(x => x.Id == "dumbbell-curl");
        curl.AllowedTechniques = Enum.GetNames<TrainingTechniqueType>();
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> UserAsync(string email, bool capable = true)
    {
        await AdmitAsync();
        var client = ClientFor(await AddUserAsync(email));
        if (capable) client.DefaultRequestHeaders.Add(ClientCapabilities.Header, ClientCapabilities.TechniquesName);
        return client;
    }

    private static object Plan(params object[] exercises) => new
    {
        name = "Mi plan", totalWeeks = 2, days = new[] { new { day = "Monday", title = "Brazo", exercises } }
    };

    private static object Curl(object? technique, int sets = 3, string? group = null) =>
        new { exerciseId = "dumbbell-curl", sets, restSeconds = 90, technique, supersetGroup = group };

    private static async Task<JsonElement> FirstSessionAsync(HttpClient client) =>
        (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("sessions")[0];

    [Fact]
    public async Task TheExerciseDetailSaysWhichTechniquesItAdmits()
    {
        var client = await UserAsync("detail@example.com");
        var curl = await JsonAsync(await client.GetAsync("/api/v1/exercises/dumbbell-curl"));
        Assert.Contains("DropSet", curl.GetProperty("allowedTechniques").EnumerateArray().Select(t => t.GetString()));
        var press = await JsonAsync(await client.GetAsync("/api/v1/exercises/ohp"));
        Assert.Equal(0, press.GetProperty("allowedTechniques").GetArrayLength());
    }

    [Fact]
    public async Task ATechniqueTakesItsDefaults_IsServedBack_AndAddsToTheDuration()
    {
        var client = await UserAsync("tech@example.com");
        var created = await client.PostAsJsonAsync(Url, Plan(Curl(new { type = "dropSet", maxSegments = 2 })));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var session = await FirstSessionAsync(client);
        var technique = session.GetProperty("exercises")[0].GetProperty("technique");
        Assert.Equal("DropSet", technique.GetProperty("type").GetString());
        Assert.Equal("LastSet", technique.GetProperty("appliesTo").GetString());
        Assert.Equal(2, technique.GetProperty("maxSegments").GetInt32());
        Assert.Equal(20, technique.GetProperty("loadDropPercent").GetInt32());
        // 5 min of preparation, 3 sets of 135 s and two segments of 30 s.
        Assert.Equal(13, session.GetProperty("estimatedDurationMinutes").GetInt32());
    }

    [Fact]
    public async Task SupersetRoundsAreStored_WithDefaultsForWhatIsLeftOut()
    {
        var client = await UserAsync("rounds@example.com");
        var days = new[]
        {
            new
            {
                day = "Monday", title = "Brazo",
                exercises = new object[] { new { exerciseId = "dumbbell-curl", sets = 4, supersetGroup = "a" }, new { exerciseId = "ohp", sets = 3, supersetGroup = "A" } },
                supersets = new object[] { new { group = "a", restAfterRoundSeconds = 120 } }
            }
        };
        var created = await client.PostAsJsonAsync(Url, new { name = "Mi plan", totalWeeks = 2, days });
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var superset = (await FirstSessionAsync(client)).GetProperty("supersets")[0];
        Assert.Equal(("A", 4, 15, 120), (superset.GetProperty("group").GetString(), superset.GetProperty("rounds").GetInt32(),
            superset.GetProperty("restBetweenSeconds").GetInt32(), superset.GetProperty("restAfterRoundSeconds").GetInt32()));
    }

    [Theory]
    [InlineData("Cluster", null, 3, null)]
    [InlineData("DropSet", 4, 3, null)]
    [InlineData("MyoRepMatch", null, 1, null)]
    [InlineData("DropSet", null, 3, "A")]
    public async Task AnImpossibleTechniqueIsRefused(string type, int? segments, int sets, string? group)
    {
        var client = await UserAsync("bad@example.com");
        var exercises = new List<object> { Curl(new { type, maxSegments = segments }, sets, group) };
        if (group is not null) exercises.Add(new { exerciseId = "ohp", supersetGroup = group });
        var response = await client.PostAsJsonAsync(Url, Plan([.. exercises]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("errors").TryGetProperty("days.1.exercises.1", out _));
    }

    [Fact]
    public async Task AnExerciseThatDoesNotAdmitTheTechniqueRefusesIt_InTheLanguageOfTheRequest()
    {
        var client = await UserAsync("refused@example.com");
        client.DefaultRequestHeaders.Add("Accept-Language", "en");
        var response = await client.PostAsJsonAsync(Url, Plan(new { exerciseId = "ohp", technique = new { type = "DropSet" } }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal("The exercise does not allow that technique.", body.GetProperty("errors").GetProperty("days.1.exercises.1")[0].GetString());
    }

    [Theory]
    [InlineData("""{"name":"X","days":[{"day":"Monday","title":"X","exercises":[{"exerciseId":"ohp"}],"supersets":[{"group":"A"}]}]}""")]
    [InlineData("""{"name":"X","days":[{"day":"Monday","title":"X","exercises":[{"exerciseId":"ohp","supersetGroup":"A"},{"exerciseId":"squat","supersetGroup":"A"}],"supersets":[{"group":"A","rounds":11}]}]}""")]
    public async Task SupersetRoundsNeedTheirExercisesAndSensibleFigures(string json)
    {
        var client = await UserAsync("badrounds@example.com");
        var response = await client.PostAsync(Url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("errors").TryGetProperty("days.1.supersets", out _));
    }

    [Fact]
    public async Task AnAppThatCannotShowTechniquesCannotCreateOrOverwriteThem()
    {
        var old = await UserAsync("old@example.com", capable: false);
        var withDrop = Plan(Curl(new { type = "DropSet" }));
        var refused = await old.PostAsJsonAsync(Url, withDrop);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("app_update_required", (await JsonAsync(refused)).GetProperty("title").GetString());

        // The same user from an updated app creates it; the old one cannot edit it afterwards, even without sending techniques.
        old.DefaultRequestHeaders.Add(ClientCapabilities.Header, ClientCapabilities.TechniquesName);
        Assert.Equal(HttpStatusCode.Created, (await old.PostAsJsonAsync(Url, withDrop)).StatusCode);
        old.DefaultRequestHeaders.Remove(ClientCapabilities.Header);
        Assert.Equal(HttpStatusCode.Conflict, (await old.PutAsJsonAsync(EditUrl, Plan(Curl(null)))).StatusCode);

        // An updated app can take the technique off, and then the old one can edit again.
        old.DefaultRequestHeaders.Add(ClientCapabilities.Header, ClientCapabilities.TechniquesName);
        var removed = await old.PutAsJsonAsync(EditUrl, Plan(Curl(null)));
        Assert.True(removed.IsSuccessStatusCode, await removed.Content.ReadAsStringAsync());
        old.DefaultRequestHeaders.Remove(ClientCapabilities.Header);
        var edit = await old.PutAsJsonAsync(EditUrl, Plan(Curl(null, sets: 4)));
        Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync());
    }
}
