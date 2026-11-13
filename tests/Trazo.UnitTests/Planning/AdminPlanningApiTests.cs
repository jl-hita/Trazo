using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The administration preview of the planning engine, run against a small catalog.</summary>
public sealed class AdminPlanningApiTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public AdminPlanningApiTests()
    {
        using (var db = _fixture.NewContext())
        {
            ExerciseTestSchema.Create(db);
            db.ExerciseCatalog.AddRange(Catalog());
            db.SaveChanges();
        }
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static IEnumerable<Trazo.Domain.Training.ExerciseCatalogItem> Catalog() =>
    [
        ExerciseCandidateMapperTests.Item("squat", "Squat", primary: ["Quadriceps", "Glutes"], secondary: ["Hamstrings"], equipment: ["barbell"]),
        ExerciseCandidateMapperTests.Item("goblet-squat", "Squat", primary: ["Quadriceps"], secondary: ["Glutes"], equipment: ["dumbbell"]),
        ExerciseCandidateMapperTests.Item("deadlift", "Hinge", primary: ["Hamstrings", "Glutes"], secondary: ["ErectorSpinae"], equipment: ["barbell"]),
        ExerciseCandidateMapperTests.Item("rdl", "Hinge", primary: ["Hamstrings"], secondary: ["Glutes"], equipment: ["dumbbell"]),
        ExerciseCandidateMapperTests.Item("bench-press", "HorizontalPush", primary: ["Chest"], secondary: ["Triceps", "Deltoids"], equipment: ["barbell", "bench"]),
        ExerciseCandidateMapperTests.Item("db-bench-press", "HorizontalPush", primary: ["Chest"], secondary: ["Triceps"], equipment: ["dumbbell", "bench"]),
        ExerciseCandidateMapperTests.Item("ohp", "VerticalPush", primary: ["Deltoids"], secondary: ["Triceps"], equipment: ["barbell"]),
        ExerciseCandidateMapperTests.Item("db-shoulder-press", "VerticalPush", primary: ["Deltoids"], secondary: ["Triceps"], equipment: ["dumbbell"]),
        ExerciseCandidateMapperTests.Item("barbell-row", "HorizontalPull", primary: ["UpperBack", "Lats"], secondary: ["Biceps"], equipment: ["barbell"]),
        ExerciseCandidateMapperTests.Item("db-row", "HorizontalPull", primary: ["Lats", "UpperBack"], secondary: ["Biceps"], equipment: ["dumbbell"]),
        ExerciseCandidateMapperTests.Item("chest-supported-row", "HorizontalPull", primary: ["UpperBack", "Lats"], secondary: ["Biceps"], equipment: ["dumbbell", "bench"]),
        ExerciseCandidateMapperTests.Item("lat-pulldown", "VerticalPull", primary: ["Lats"], secondary: ["Biceps"], equipment: ["cable"]),
        ExerciseCandidateMapperTests.Item("pull-up", "VerticalPull", primary: ["Lats"], secondary: ["Biceps"], equipment: ["pull-up-bar"]),
        ExerciseCandidateMapperTests.Item("lunge", "SingleLeg", primary: ["Quadriceps", "Glutes"], secondary: ["Hamstrings"], equipment: ["dumbbell"]),
        ExerciseCandidateMapperTests.Item("plank", "Core", primary: ["Abdominals"], secondary: ["Obliques"], equipment: []),
        ExerciseCandidateMapperTests.Item("crunch", "Core", primary: ["Abdominals"], secondary: [], equipment: []),
        ExerciseCandidateMapperTests.Item("curl", "Accessory", primary: ["Biceps"], secondary: ["Forearms"], equipment: ["dumbbell"]),
        ExerciseCandidateMapperTests.Item("triceps-extension", "Accessory", primary: ["Triceps"], secondary: [], equipment: ["cable"]),
        ExerciseCandidateMapperTests.Item("lateral-raise", "Accessory", primary: ["Deltoids"], secondary: [], equipment: ["dumbbell"]),
        ExerciseCandidateMapperTests.Item("calf-raise", "Accessory", primary: ["Calves"], secondary: [], equipment: []),
        ExerciseCandidateMapperTests.Item("leg-curl", "Accessory", primary: ["Hamstrings"], secondary: [], equipment: ["machine"]),
        ExerciseCandidateMapperTests.Item("rowing", "Cardio")
    ];

    private async Task<HttpClient> ClientAsync(string? role)
    {
        var client = _factory.CreateClient();
        if (role is null) return client;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = $"{role}@example.com", NormalizedEmail = $"{role}@example.com".ToUpperInvariant(),
            DisplayName = role, Role = role, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private static object Profile(object? overrides = null) => new
    {
        goal = "GeneralFitness", experience = "Beginner", startDate = "2026-11-16", durationWeeks = 4,
        trainingDays = new[] { "Monday", "Thursday" }, maxSessionMinutes = 60,
        availableEquipment = Array.Empty<string>(), excludedExerciseIds = Array.Empty<string>()
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Fact]
    public async Task Endpoints_RequireTheSuperadminRole()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ClientAsync(null)).PostAsJsonAsync("/api/v1/admin/planning/preview", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientAsync("user")).PostAsJsonAsync("/api/v1/admin/planning/preview", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientAsync("admin")).GetAsync("/api/v1/admin/planning/catalog-readiness")).StatusCode);
    }

    [Fact]
    public async Task Preview_GeneratesAProgramFromTheCatalog_AndReportsSkippedExercises()
    {
        var response = await (await ClientAsync("superadmin")).PostAsJsonAsync("/api/v1/admin/planning/preview", Profile());

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        var program = body.GetProperty("program");
        Assert.Equal(8, program.GetProperty("sessions").GetArrayLength()); // 2 days x 4 weeks
        var first = program.GetProperty("sessions")[0];
        Assert.Equal("Monday", first.GetProperty("day").GetString()); // enums travel as names
        Assert.True(first.GetProperty("exercises").GetArrayLength() > 0);
        Assert.Equal(21, body.GetProperty("usableExercises").GetInt32());
        Assert.Equal("rowing", body.GetProperty("skipped")[0].GetProperty("exerciseId").GetString());
        Assert.True(body.GetProperty("program").TryGetProperty("qualityReport", out _));
    }

    [Fact]
    public async Task Preview_HonoursEquipmentAndExclusions_AndAddsAMacrocycleDraftOnRequest()
    {
        var client = await ClientAsync("superadmin");
        var response = await client.PostAsJsonAsync("/api/v1/admin/planning/preview", new
        {
            goal = "Hypertrophy", experience = "Beginner", startDate = "2026-11-16", durationWeeks = 4,
            trainingDays = new[] { "Monday", "Wednesday", "Friday" }, maxSessionMinutes = 75,
            availableEquipment = new[] { "dumbbell", "bench" }, excludedExerciseIds = new[] { "db-row" },
            macrocycleWeeks = 12
        });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        var used = body.GetProperty("program").GetProperty("sessions").EnumerateArray()
            .SelectMany(s => s.GetProperty("exercises").EnumerateArray()).Select(e => e.GetProperty("exerciseId").GetString()).Distinct().ToList();
        Assert.DoesNotContain("db-row", used);
        Assert.DoesNotContain("barbell-row", used); // needs a barbell, which is not available
        Assert.True(body.GetProperty("macrocycle").GetProperty("endDate").GetDateTime() > body.GetProperty("macrocycle").GetProperty("startDate").GetDateTime());
        Assert.True(body.GetProperty("macrocycle").GetProperty("phases").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Preview_RejectsInvalidInput_WithReadableErrors()
    {
        var client = await ClientAsync("superadmin");

        var badGoal = await client.PostAsJsonAsync("/api/v1/admin/planning/preview", new { goal = "Flying", experience = "Beginner", trainingDays = new[] { "Monday" } });
        Assert.Equal(HttpStatusCode.BadRequest, badGoal.StatusCode);
        Assert.NotEmpty((await JsonAsync(badGoal)).GetProperty("errors").EnumerateArray());

        var oneDay = await client.PostAsJsonAsync("/api/v1/admin/planning/preview", new
        {
            goal = "Strength", experience = "Beginner", startDate = "2026-11-16", durationWeeks = 4,
            trainingDays = new[] { "Monday" }, maxSessionMinutes = 60, availableEquipment = Array.Empty<string>(), excludedExerciseIds = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, oneDay.StatusCode);
        Assert.NotEmpty((await JsonAsync(oneDay)).GetProperty("errors").EnumerateArray());
    }

    [Fact]
    public async Task CatalogReadiness_CountsWhatTheEngineCanUse()
    {
        var readiness = await JsonAsync(await (await ClientAsync("superadmin")).GetAsync("/api/v1/admin/planning/catalog-readiness"));

        Assert.Equal(21, readiness.GetProperty("usableExercises").GetInt32());
        Assert.Equal("Squat", readiness.GetProperty("byPattern").EnumerateArray().First(g => g.GetProperty("name").GetString() == "Squat").GetProperty("name").GetString());
        Assert.Equal(21, readiness.GetProperty("byGoal").EnumerateArray().First(g => g.GetProperty("name").GetString() == "Strength").GetProperty("count").GetInt32());
    }
}
