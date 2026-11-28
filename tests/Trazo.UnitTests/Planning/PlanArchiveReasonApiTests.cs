using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;

namespace Trazo.UnitTests.Planning;

/// <summary>Why a plan was archived: only kept with the research consent, never required, and exported as stable codes.</summary>
public sealed class PlanArchiveReasonApiTests() : PlansApiBase(enforced: false)
{
    private const string Archive = "/api/v1/plans/current/archive";

    private async Task ConsentAsync(HttpClient client, string kind, bool consent = true) =>
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/v1/account/{kind}-consent", new { consent })).StatusCode);

    private async Task<List<TrainingPlan>> PlansOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().TrainingPlans.AsNoTracking()
            .Where(p => p.UserId == userId).ToListAsync();
    }

    private async Task<(Guid UserId, HttpClient Client)> PersonAsync(string email, bool research = true, bool health = false)
    {
        var user = await AddUserAsync(email, healthConsent: false);
        var client = ClientFor(user);
        (await client.PostAsJsonAsync("/api/v1/plans", Profile())).EnsureSuccessStatusCode();
        if (research) await ConsentAsync(client, "research");
        if (health) await ConsentAsync(client, "health");
        return (user.Id, client);
    }

    [Theory]
    [InlineData("PlanCompleted")]
    [InlineData("WantNewPlan")]
    [InlineData("NoTime")]
    [InlineData("TooDemanding")]
    [InlineData("TooEasy")]
    [InlineData("GoalsChanged")]
    [InlineData("NotConvinced")]
    [InlineData("Other")]
    public async Task WithTheResearchConsentTheReasonIsKeptWithTheArchivedPlan(string reason)
    {
        var (userId, client) = await PersonAsync("ana@example.com");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Archive, new { reason })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);

        var plans = await PlansOfAsync(userId);
        var archived = Assert.Single(plans, p => p.Status == TrainingPlanStatus.Archived);
        Assert.Equal(reason, archived.ArchiveReason.ToString());
        Assert.Null(Assert.Single(plans, p => p.Status == TrainingPlanStatus.Active).ArchiveReason);
    }

    [Fact]
    public async Task WithoutTheResearchConsentThePlanIsArchivedAndNoReasonIsKept()
    {
        var (never, client) = await PersonAsync("never@example.com", research: false);
        var (revoked, other) = await PersonAsync("revoked@example.com");
        await ConsentAsync(other, "research", false);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Archive, new { reason = "NoTime" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync(Archive, new { reason = "NoTime" })).StatusCode);

        foreach (var id in new[] { never, revoked })
        {
            var plan = Assert.Single(await PlansOfAsync(id));
            Assert.Equal(TrainingPlanStatus.Archived, plan.Status);
            Assert.Null(plan.ArchiveReason);
        }
    }

    [Fact]
    public async Task ClientsThatSendNoBodyOrNoReasonStillArchive()
    {
        foreach (var (email, body) in new (string, HttpContent?)[]
        {
            ("none@example.com", null),
            ("empty@example.com", new StringContent("{}", Encoding.UTF8, "application/json")),
            ("null@example.com", new StringContent("""{"reason":null,"planId":null}""", Encoding.UTF8, "application/json"))
        })
        {
            var (userId, client) = await PersonAsync(email);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(Archive, body)).StatusCode);
            var plan = Assert.Single(await PlansOfAsync(userId));
            Assert.Equal(TrainingPlanStatus.Archived, plan.Status);
            Assert.Null(plan.ArchiveReason);
        }
    }

    [Theory]
    [InlineData("Illness")]
    [InlineData("notime")]
    [InlineData("2")]
    [InlineData("NoTime,TooEasy")]
    [InlineData("")]
    [InlineData("I was tired of it")]
    public async Task AnUnknownReasonIsRejectedAndThePlanStaysActive(string reason)
    {
        var (userId, client) = await PersonAsync("ana@example.com");

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Archive, new { reason })).StatusCode);

        var plan = Assert.Single(await PlansOfAsync(userId));
        Assert.Equal(TrainingPlanStatus.Active, plan.Status);
        Assert.Null(plan.ArchiveReason);
    }

    [Fact]
    public async Task AReasonForAnotherPlanArchivesNothing()
    {
        var (userId, client) = await PersonAsync("ana@example.com");
        var first = Assert.Single(await PlansOfAsync(userId)).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Archive, new { reason = "NoTime", planId = first })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);

        // A late retry of the first request.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(Archive, new { reason = "TooEasy", planId = first })).StatusCode);

        var plans = await PlansOfAsync(userId);
        Assert.Equal(PlanArchiveReason.NoTime, plans.Single(p => p.Id == first).ArchiveReason);
        var current = plans.Single(p => p.Id != first);
        Assert.Equal(TrainingPlanStatus.Active, current.Status);
        Assert.Null(current.ArchiveReason);
    }

    [Fact]
    public async Task DiscomfortNeedsTheHealthConsentAndIsErasedWhenItIsWithdrawn()
    {
        var (without, plain) = await PersonAsync("plain@example.com");
        var (with, consented) = await PersonAsync("health@example.com", health: true);

        Assert.Equal(HttpStatusCode.NoContent, (await plain.PostAsJsonAsync(Archive, new { reason = "Discomfort" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await consented.PostAsJsonAsync(Archive, new { reason = "Discomfort" })).StatusCode);
        Assert.Equal(PlanArchiveReason.Other, Assert.Single(await PlansOfAsync(without)).ArchiveReason);
        Assert.Equal(PlanArchiveReason.Discomfort, Assert.Single(await PlansOfAsync(with)).ArchiveReason);

        await ConsentAsync(consented, "health", false);

        Assert.Equal(PlanArchiveReason.Other, Assert.Single(await PlansOfAsync(with)).ArchiveReason);
    }

    [Fact]
    public async Task WithdrawingTheResearchConsentErasesTheReasonsOfThatUserOnly()
    {
        var (ana, client) = await PersonAsync("ana@example.com");
        var (eve, other) = await PersonAsync("eve@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Archive, new { reason = "NoTime" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await other.PostAsJsonAsync(Archive, new { reason = "TooEasy" })).StatusCode);
        var archivedAt = Assert.Single(await PlansOfAsync(ana)).UpdatedAt;

        await ConsentAsync(client, "research", false);
        await ConsentAsync(client, "research");

        var plan = Assert.Single(await PlansOfAsync(ana));
        Assert.Null(plan.ArchiveReason);
        Assert.Equal(TrainingPlanStatus.Archived, plan.Status);
        Assert.Equal(archivedAt, plan.UpdatedAt);
        Assert.Equal(PlanArchiveReason.TooEasy, Assert.Single(await PlansOfAsync(eve)).ArchiveReason);
    }

    [Fact]
    public async Task TheExportCarriesTheCodesOfThoseWhoConsentAndLeavesOutThoseWhoRevoked()
    {
        var (yes, consenting) = await PersonAsync("yes@example.com");
        var (_, revoking) = await PersonAsync("revoked@example.com");
        var (_, historic) = await PersonAsync("old@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await consenting.PostAsJsonAsync(Archive, new { reason = "TooDemanding" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await revoking.PostAsJsonAsync(Archive, new { reason = "TooEasy" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await historic.PostAsync(Archive, null)).StatusCode);

        var admin = ClientFor(await AddUserAsync("admin@trazo.test", "superadmin"));
        var (json, csv) = await ExportAsync(admin);
        Assert.Equal(new string?[] { null, "TooDemanding", "TooEasy" }, Reasons(json));
        Assert.Contains("\"ArchiveReason\"", csv.Split('\n')[0]);
        Assert.Contains("\"TooDemanding\"", csv);

        await ConsentAsync(revoking, "research", false);

        (json, csv) = await ExportAsync(admin);
        Assert.Equal(new string?[] { null, "TooDemanding" }, Reasons(json));
        Assert.DoesNotContain("TooEasy", csv);
        foreach (var secret in new[] { yes.ToString(), "yes@example.com" })
        {
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, csv, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task APlanArchivedInThePeriodIsExportedEvenIfItWasCreatedBefore()
    {
        var (userId, client) = await PersonAsync("ana@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Archive, new { reason = "NoTime" })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var plan = await db.TrainingPlans.SingleAsync(p => p.UserId == userId);
            plan.CreatedAt = plan.UpdatedAt.AddYears(-1);
            await db.SaveChangesAsync();
        }

        var (json, _) = await ExportAsync(ClientFor(await AddUserAsync("admin@trazo.test", "superadmin")));

        Assert.Equal(new string?[] { "NoTime" }, Reasons(json));
    }

    [Fact]
    public async Task ThePersonsOwnDataExportIncludesTheReason()
    {
        var (_, client) = await PersonAsync("ana@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Archive, new { reason = "GoalsChanged" })).StatusCode);

        var export = await JsonAsync(await client.GetAsync("/api/v1/account/export"));

        Assert.Equal("GoalsChanged", export.GetProperty("plans")[0].GetProperty("archiveReason").GetString());
    }

    /// <summary>The export of a period around the clock of the tests.</summary>
    private async Task<(string Json, string Csv)> ExportAsync(HttpClient admin)
    {
        using var scope = _factory.Services.CreateScope();
        var now = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().TrainingPlans.AsNoTracking().Select(p => p.UpdatedAt).ToListAsync();
        var from = now.Min().AddMonths(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var until = now.Max().AddMonths(1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var response = await admin.GetAsync($"/api/v1/admin/intelligence/export?from={from}&to={until}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var archive = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()), ZipArchiveMode.Read);
        async Task<string> TextAsync(string name)
        {
            using var reader = new StreamReader(archive.GetEntry(name)!.Open());
            return await reader.ReadToEndAsync();
        }
        return (await TextAsync("research.json"), await TextAsync("plans.csv"));
    }

    private static string?[] Reasons(string research)
    {
        using var document = JsonDocument.Parse(research);
        return document.RootElement.GetProperty("plans").EnumerateArray().Select(p => p.GetProperty("archiveReason").GetString())
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}
