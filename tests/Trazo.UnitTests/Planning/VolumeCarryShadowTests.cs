using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trazo.Application.Planning.Longitudinal;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The carry between blocks that is worked out and kept, and never applied.</summary>
public sealed class VolumeCarryShadowTests : PlansApiBase
{
    private static readonly AdjustableClock Clock = new(Now);

    public VolumeCarryShadowTests() : base(enforced: false, clock: Clock) => Clock.Now = Now;

    [Fact]
    public void OnlyAGainThatHeldIsProposed_AndNeverMoreThanTwo()
    {
        var carry = VolumeCarryShadow.Compute(1,
            [
                new(["Chest"], 1), new(["Chest"], 1), new(["Chest"], 1),
                new(["Lats", "UpperBack"], 1), new(["Quadriceps"], -1), new(["Biceps"], 1), new(["Biceps"], -1),
                new(["Triceps"], 1), new(["Calves"], 1)
            ],
            [new("Lats", 2, 0), new("UpperBack", 1, 0), new("Triceps", 0, 1)]);

        var byMuscle = carry.Muscles.ToDictionary(m => m.Muscle, m => (m.NetSets, m.ProposedSets));
        Assert.Equal((3, 2), byMuscle["Chest"]);
        Assert.Equal((1, 0), byMuscle["Lats"]);
        Assert.Equal((1, 1), byMuscle["UpperBack"]);
        Assert.Equal((-1, 0), byMuscle["Quadriceps"]);
        Assert.Equal((1, 0), byMuscle["Triceps"]);
        Assert.Equal((1, 1), byMuscle["Calves"]);
        Assert.False(byMuscle.ContainsKey("Biceps"));
    }

    [Fact]
    public async Task ANewTrainingBlockKeepsTheCarryOfTheLastOne()
    {
        var carry = await RunAsync("shadow-answers@example.com");

        Assert.Equal(1, carry.GetProperty("fromSequence").GetInt32());
        var muscles = carry.GetProperty("muscles").EnumerateArray().ToList();
        Assert.NotEmpty(muscles);
        Assert.All(muscles, m => Assert.InRange(m.GetProperty("proposedSets").GetInt32(), 0, VolumeCarryShadow.MaximumCarriedSets));
        Assert.Contains(muscles, m => m.GetProperty("netSets").GetInt32() > 0);
    }

    /// <summary>Two sessions of a first block answered "recovered, little work", then the plan reaches its second training block.</summary>
    private async Task<JsonElement> RunAsync(string email)
    {
        Clock.Now = Now;
        var (_, client, _) = await StartPlanAsync(email);
        // The same sessions of the first two weeks: every muscle is trained at least twice.
        for (var round = 0; round < 2; round++)
        {
            var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
            foreach (var session in block.GetProperty("weeks")[round].GetProperty("sessions").EnumerateArray())
            {
                var muscles = session.GetProperty("exercises").EnumerateArray()
                    .SelectMany(e => e.GetProperty("primaryMuscles").EnumerateArray().Select(m => m.GetString()!)).Distinct()
                    .ToDictionary(m => m, _ => (object)new { recovery = "recovered", workload = "low" });
                var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
                    new { outcome = "Completed", sets = CompleteSets(session), muscleFeedback = muscles });
                Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
                Clock.Now = Clock.Now.AddDays(1);
            }
            Clock.Now = Now.AddDays(7);
        }

        // Week 6: the deload and the next training block are generated as their weeks are read.
        foreach (var day in new[] { 28, 35 })
        {
            Clock.Now = Now.AddDays(day);
            Assert.True((await client.GetAsync("/api/v1/plans/current/week")).IsSuccessStatusCode);
        }
        using var scope = _factory.Services.CreateScope();
        var blocks = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().TrainingMesocycles.AsNoTracking()
            .OrderBy(m => m.Sequence).ToListAsync();
        Assert.Equal(3, blocks.Count);
        Assert.Equal(JsonValueKind.Null, JsonDocument.Parse(blocks[1].GenerationSnapshotJson!).RootElement.GetProperty("shadowCarry").ValueKind);
        var snapshot = JsonDocument.Parse(blocks[2].GenerationSnapshotJson!).RootElement;
        return snapshot.GetProperty("shadowCarry").Clone();
    }
}
