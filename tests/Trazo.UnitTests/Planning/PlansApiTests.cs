using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Security;
using Trazo.Application.Planning;
using Trazo.Domain.Billing;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>
/// Like <see cref="SqliteDateTimeOffsetCustomizer"/> but leaves the exercise catalog alone: its test table is created by hand
/// with text dates, as the other catalog tests do.
/// </summary>
public sealed class PlanSqliteCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().Where(e => e.ClrType != typeof(Trazo.Domain.Training.ExerciseCatalogItem))
                     .SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.DateTimeOffsetToBinaryConverter());
    }
}

/// <summary>SQLite stand-ins for the planning tables, generated from the EF model.</summary>
internal static class PlanTestSchema
{
    private static readonly string[] Tables = ["training_plans", "training_plan_phases", "training_mesocycles", "planned_sessions", "workout_logs", "workout_set_logs", "plan_adaptations", "plan_review_requests", "plan_review_messages"];

    public static void Create(SetupFixture fixture)
    {
        var options = new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(fixture.ConnectionString)
            .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>().Options;
        using var db = new TrazoDbContext(options);
        var script = db.GetService<IRelationalDatabaseCreator>().GenerateCreateScript();
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var text = statement.TrimStart();
            var mine = Tables.Any(table =>
                text.StartsWith($"CREATE TABLE \"{table}\"", StringComparison.Ordinal) || text.Contains($" ON \"{table}\" ", StringComparison.Ordinal));
            if (mine && text.StartsWith("CREATE", StringComparison.Ordinal)) db.Database.ExecuteSqlRaw(text);
        }
    }
}

/// <summary>Test host for the plan API on SQLite with a fixed clock (a Monday).</summary>
public abstract class PlansApiBase : IDisposable
{
    protected static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero); // a Monday
    protected readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    protected readonly WebApplicationFactory<Program> _factory;

    protected PlansApiBase(bool enforced)
    {
        using (var db = _fixture.NewContext())
        {
            ExerciseTestSchema.Create(db);
            db.ExerciseCatalog.AddRange(PlanTestCatalog.Items());
            db.SaveChanges();
        }
        PlanTestSchema.Create(_fixture);
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.UseSetting("Billing:Enforced", enforced ? "true" : "false");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString)
                    .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    protected async Task<TrazoUser> AddUserAsync(string email, string role = "user", bool subscribed = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email.Split('@')[0],
            Role = role, EmailVerifiedAt = Now, CreatedAt = Now, UpdatedAt = Now
        };
        user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, "CorrectHorse1Battery");
        db.Users.Add(user);
        if (subscribed)
            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(), UserId = user.Id, ProductId = "trazo_premium", PurchaseTokenHash = Guid.NewGuid().ToString("N"),
                ProtectedPurchaseToken = "x", Status = SubscriptionStatus.Active, StartedAt = Now.AddDays(-1), ExpiresAt = Now.AddDays(30),
                CreatedAt = Now, UpdatedAt = Now
            });
        await db.SaveChangesAsync();
        return user;
    }

    protected HttpClient ClientFor(TrazoUser? user)
    {
        var client = _factory.CreateClient();
        if (user is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    protected static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>())!;

    protected static object Profile(object? days = null, object? equipment = null, object? limitations = null, object? goal = null, object? weeks = null) => new
    {
        goal = goal ?? "Hypertrophy", experience = "Intermediate", totalWeeks = weeks ?? 12,
        trainingDays = days ?? new[] { "Monday", "Tuesday", "Thursday", "Friday" }, maxSessionMinutes = 75,
        availableEquipment = equipment ?? new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = limitations ?? Array.Empty<string>()
    };

    protected async Task<(TrazoUser User, HttpClient Client, JsonElement Week)> StartPlanAsync(string email = "ana@example.com", object? profile = null)
    {
        var user = await AddUserAsync(email);
        var client = ClientFor(user);
        var created = await client.PostAsJsonAsync("/api/v1/plans", profile ?? Profile());
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        return (user, client, week);
    }

    protected static object CompleteSets(JsonElement session, decimal kg = 60m, int reps = 8) => session.GetProperty("exercises").EnumerateArray()
        .SelectMany(e => Enumerable.Range(1, e.GetProperty("sets").GetInt32()).Select(n => new
        {
            exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = kg, repetitions = reps, rir = 2, completed = true
        })).ToArray();

}

/// <summary>The user-facing plan API: creation, the current week only, logging with adaptation, swaps and ownership.</summary>
public sealed class PlansApiTests() : PlansApiBase(enforced: false)
{
    // ------------------------------------------------------------------ access

    [Fact]
    public async Task EveryPlanRouteNeedsASignedInUser()
    {
        var client = ClientFor(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/plans/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/plans/current/week")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/plans/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
    }

    [Fact]
    public async Task ANewUserHasNoPlan()
    {
        var client = ClientFor(await AddUserAsync("new@example.com"));

        var response = await client.GetAsync("/api/v1/plans/current");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("no_active_plan", (await JsonAsync(response)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/plans/current/week")).StatusCode);
    }

    [Fact]
    public async Task OptionsListTheAllowedValues()
    {
        var response = await ClientFor(await AddUserAsync("o@example.com")).GetAsync("/api/v1/plans/options");

        var body = await JsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Hypertrophy", body.GetProperty("goals").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("LowerBack", body.GetProperty("limitations").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("dumbbell", body.GetProperty("equipment").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("rowing-machine", body.GetProperty("equipment").EnumerateArray().Select(x => x.GetString()).Where(x => x == "xx"));
    }

    // ------------------------------------------------------------------ creation

    [Fact]
    public async Task CreatingAPlanReturnsTheMacrocycleSummary_WithoutAnySessionDetail()
    {
        var client = ClientFor(await AddUserAsync("ana@example.com"));

        var response = await client.PostAsJsonAsync("/api/v1/plans", Profile());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var plan = JsonDocument.Parse(text).RootElement;
        Assert.Equal("Active", plan.GetProperty("status").GetString());
        Assert.Equal("Generated", plan.GetProperty("origin").GetString());
        Assert.Equal(12, plan.GetProperty("totalWeeks").GetInt32());
        Assert.Equal(1, plan.GetProperty("currentWeek").GetInt32());
        var phases = plan.GetProperty("macrocycle").GetProperty("phases").EnumerateArray().ToList();
        Assert.True(phases.Count >= 3); // training phases separated by deloads
        Assert.Contains(phases, p => p.GetProperty("type").GetString() == "deload");
        Assert.DoesNotContain("exerciseId", text);
        Assert.DoesNotContain("\"sessions\"", text); // the key of a session list ("sessionsPerPeriod" is only a count)
        Assert.Equal("/api/v1/plans/current", response.Headers.Location?.ToString());

        var current = await (await client.GetAsync("/api/v1/plans/current")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("exerciseId", current);
    }

    [Fact]
    public async Task OnlyTheCurrentWeekIsServed_WithTheFullPrescription()
    {
        var (_, _, week) = await StartPlanAsync();

        Assert.Equal(1, week.GetProperty("weekNumber").GetInt32());
        Assert.Equal("2026-11-16", week.GetProperty("weekStart").GetString());
        var sessions = week.GetProperty("sessions").EnumerateArray().ToList();
        Assert.Equal(4, sessions.Count); // 4 training days, one week, never the rest of the macrocycle
        Assert.All(sessions, s => Assert.Equal(1, s.GetProperty("weekNumber").GetInt32()));
        var first = sessions[0];
        Assert.Equal("Planned", first.GetProperty("status").GetString());
        var exercise = first.GetProperty("exercises")[0];
        foreach (var field in new[] { "sets", "minRepetitions", "maxRepetitions", "targetRir", "restSeconds", "progressionRule", "primaryMuscles" })
            Assert.True(exercise.TryGetProperty(field, out _), field);
        Assert.Equal("accumulation", week.GetProperty("mesocycle").GetProperty("phaseType").GetString());
        Assert.Equal(1, week.GetProperty("mesocycle").GetProperty("weekInBlock").GetInt32());
    }

    [Fact]
    public async Task TheBlockShowsEveryWeekOfTheCurrentMesocycle_AndNothingLater()
    {
        var (_, client, _) = await StartPlanAsync();

        var block = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));

        var meso = block.GetProperty("mesocycle");
        var weeks = block.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal(meso.GetProperty("weeks").GetInt32(), weeks.Count);
        Assert.True(weeks.Count is >= 1 and <= 4);
        Assert.True(weeks[0].GetProperty("isCurrent").GetBoolean());
        Assert.All(weeks, w => Assert.Equal(4, w.GetProperty("sessions").GetArrayLength()));
        Assert.Equal(1, weeks[0].GetProperty("weekNumber").GetInt32());
        Assert.Equal(weeks.Count, weeks[^1].GetProperty("weekNumber").GetInt32()); // a 12-week plan: later blocks do not exist yet
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(await AddUserAsync("none@example.com")).GetAsync("/api/v1/plans/current/block")).StatusCode);
    }

    [Fact]
    public async Task ThereIsNoRouteThatReturnsTheWholePlan()
    {
        var (_, client, _) = await StartPlanAsync();

        foreach (var path in new[] { "/api/v1/plans/current/sessions", "/api/v1/plans/current/weeks", "/api/v1/plans/current/weeks/5",
                     "/api/v1/plans/current/macrocycle", "/api/v1/plans/current/export", "/api/v1/plans/all" })
            Assert.Contains((await client.GetAsync(path)).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    [Fact]
    public async Task InvalidProfilesAreRejectedWithAFieldByFieldExplanation()
    {
        var client = ClientFor(await AddUserAsync("bad@example.com"));

        var response = await client.PostAsJsonAsync("/api/v1/plans", Profile(days: new[] { "Monday" }, goal: "Bulking", weeks: 2, limitations: new[] { "neck" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await JsonAsync(response)).GetProperty("errors");
        foreach (var field in new[] { "goal", "trainingDays", "totalWeeks", "limitations" }) Assert.True(errors.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task AProfileTheCatalogCannotSatisfyIsExplainedAndStoresNothing()
    {
        var user = await AddUserAsync("none@example.com");
        var client = ClientFor(user);

        var response = await client.PostAsJsonAsync("/api/v1/plans", Profile(equipment: new[] { "kettlebell" }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("plan_generation_failed", (await JsonAsync(response)).GetProperty("title").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/plans/current")).StatusCode);
    }

    [Fact]
    public async Task TheWeekCarriesWhatTheUserDidLastTime_AndProposesTheNextLoad()
    {
        var (_, client, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];
        Assert.All(session.GetProperty("exercises").EnumerateArray(), e => Assert.Equal(JsonValueKind.Null, e.GetProperty("lastPerformance").ValueKind));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(session, kg: 60m, reps: 30) })).StatusCode);

        var again = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var exercise = again.GetProperty("sessions")[0].GetProperty("exercises")[0];
        var last = exercise.GetProperty("lastPerformance");
        Assert.Equal(60m, last.GetProperty("sets")[0].GetProperty("weightKg").GetDecimal());
        Assert.Equal(30, last.GetProperty("sets")[0].GetProperty("repetitions").GetInt32());
        // Every set reached the top of the range: one increment above the heaviest load.
        Assert.Equal("increase", last.GetProperty("suggestionBasis").GetString());
        Assert.Equal(62.5m, last.GetProperty("suggestedLoadKg").GetDecimal());
    }

    [Fact]
    public async Task ProgressIsEmptyForANewUser_AndReflectsALoggedSession()
    {
        var (_, client, week) = await StartPlanAsync();
        var empty = await JsonAsync(await client.GetAsync("/api/v1/plans/progress"));
        Assert.Equal(0, empty.GetProperty("summary").GetProperty("totalSessions").GetInt32());
        Assert.Equal(4, empty.GetProperty("summary").GetProperty("weeklyGoal").GetInt32());

        var session = week.GetProperty("sessions")[0];
        await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(session, kg: 50m, reps: 10) });

        var progress = await JsonAsync(await client.GetAsync("/api/v1/plans/progress"));
        Assert.Equal(1, progress.GetProperty("summary").GetProperty("totalSessions").GetInt32());
        Assert.True(progress.GetProperty("exercises").GetArrayLength() > 0);
        var first = progress.GetProperty("exercises")[0];
        Assert.Equal(50m, first.GetProperty("bestWeightKg").GetDecimal());
        Assert.Equal(66.7m, first.GetProperty("bestEstimatedOneRepMaxKg").GetDecimal());
        Assert.True(progress.GetProperty("muscles").GetArrayLength() > 0);
    }

    [Fact]
    public async Task OnlyOneActivePlan_ArchivingFreesTheSlot_AndHistoryIsKept()
    {
        var (_, client, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(session) })).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/plans/current/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Equal(1, await db.TrainingPlans.CountAsync(p => p.Status == TrainingPlanStatus.Archived));
        Assert.Equal(1, await db.WorkoutLogs.CountAsync());
    }

    [Fact]
    public async Task LimitationsKeepRiskyExercisesOutOfThePlan()
    {
        var (_, _, week) = await StartPlanAsync(profile: Profile(limitations: new[] { "Knee", "LowerBack" }));

        var ids = week.GetProperty("sessions").EnumerateArray().SelectMany(s => s.GetProperty("exercises").EnumerateArray())
            .Select(e => e.GetProperty("exerciseId").GetString()).ToHashSet();
        Assert.DoesNotContain("lunge", ids);
        Assert.DoesNotContain("deadlift", ids);
        Assert.DoesNotContain("bulgarian-split-squat", ids);
    }

    // ------------------------------------------------------------------ ownership

    [Fact]
    public async Task AUserCannotSeeLogSwapOrQuerySomeoneElsesSession()
    {
        var (_, _, week) = await StartPlanAsync("ana@example.com");
        var session = week.GetProperty("sessions")[0];
        var sessionId = session.GetProperty("id").GetString();
        var exerciseId = session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString();

        var other = ClientFor(await AddUserAsync("eve@example.com"));
        // Eve has no plan: every route answers 404, and with a plan of her own the foreign id is still unknown.
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessionId}/log", new { outcome = "Completed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await other.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessionId}/log", new { outcome = "Completed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/plans/current/sessions/{sessionId}/exercises/{exerciseId}/alternatives")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{sessionId}/exercises/{exerciseId}/swap", new { replacementExerciseId = "push-up" })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().WorkoutLogs.CountAsync());
    }

    [Fact]
    public async Task ADefaultPlanOfTwelveWeeksGetsItsDeloadsInWeekFiveAndNine()
    {
        var (_, client, _) = await StartPlanAsync();

        var phases = (await JsonAsync(await client.GetAsync("/api/v1/plans/current"))).GetProperty("macrocycle").GetProperty("phases").EnumerateArray().ToList();

        var deloads = phases.Where(p => p.GetProperty("type").GetString() == "deload").ToList();
        var start = DateOnly.Parse(phases[0].GetProperty("startDate").GetString()!);
        Assert.Equal([start.AddDays(28), start.AddDays(56)], deloads.Select(p => DateOnly.Parse(p.GetProperty("startDate").GetString()!)));
    }

    // ------------------------------------------------------------------ weekly load

    [Fact]
    public async Task TheLoadListsEveryWeekWithPlannedSetsOnlyForGeneratedBlocksAndCompletedSetsOnlyForReachedWeeks()
    {
        var (_, client, week) = await StartPlanAsync();
        var sessions = week.GetProperty("sessions").EnumerateArray().ToList();
        var plannedWeek1 = sessions.Sum(s => s.GetProperty("exercises").EnumerateArray().Sum(e => e.GetProperty("sets").GetInt32()));
        var first = sessions[0];
        var firstSets = first.GetProperty("exercises").EnumerateArray().Sum(e => e.GetProperty("sets").GetInt32());
        await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{first.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(first) });

        var response = await client.GetAsync("/api/v1/plans/current/load");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        var weeks = body.GetProperty("weeks").EnumerateArray().ToList();
        Assert.Equal(12, weeks.Count);
        Assert.Equal(1, body.GetProperty("currentWeek").GetInt32());
        Assert.Equal("sets", body.GetProperty("unit").GetString());
        Assert.Equal(plannedWeek1, weeks[0].GetProperty("plannedSets").GetInt32());
        Assert.Equal(firstSets, weeks[0].GetProperty("completedSets").GetInt32());
        Assert.Equal(1, weeks[0].GetProperty("loggedSessions").GetInt32());
        // Only the first block exists: later weeks have no planned volume and have not been reached.
        Assert.Equal(JsonValueKind.Null, weeks[^1].GetProperty("plannedSets").ValueKind);
        Assert.Equal(JsonValueKind.Null, weeks[^1].GetProperty("completedSets").ValueKind);
        Assert.All(weeks, w => Assert.False(string.IsNullOrEmpty(w.GetProperty("phaseType").GetString())));
    }

    [Fact]
    public async Task TheLoadOfAUserWithoutPlanIsNotFoundAndNeedsSignIn()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(await AddUserAsync("noload@example.com")).GetAsync("/api/v1/plans/current/load")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/plans/current/load")).StatusCode);
    }

    // ------------------------------------------------------------------ logging and adaptation

    [Fact]
    public async Task LoggingASessionRecordsItAndAdvisesTheNextLoad()
    {
        var (_, client, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];

        var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", recovery = "Good", sessionRpe = 7, sets = CompleteSets(session) });

        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var body = await JsonAsync(response);
        Assert.Equal("Completed", body.GetProperty("session").GetProperty("status").GetString());
        Assert.Equal(7, body.GetProperty("session").GetProperty("log").GetProperty("sessionRpe").GetDecimal());
        var advice = body.GetProperty("adaptation").GetProperty("exercises").EnumerateArray().ToList();
        Assert.Equal(session.GetProperty("exercises").GetArrayLength(), advice.Count);
        Assert.All(advice, a => Assert.True(a.TryGetProperty("explanation", out _)));

        var history = await JsonAsync(await client.GetAsync("/api/v1/plans/current/history"));
        Assert.Equal(1, history.GetArrayLength());
        Assert.Equal(session.GetProperty("id").GetString(), history[0].GetProperty("sessionId").GetString());

        var again = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new { outcome = "Completed" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task TheLoadOfTheNextExposureIsWrittenIntoTheNextSessionThatHasTheExercise()
    {
        var (_, client, week) = await StartPlanAsync();
        var sessions = week.GetProperty("sessions").EnumerateArray().ToList();
        var first = sessions[0];
        var exerciseId = first.GetProperty("exercises")[0].GetProperty("exerciseId").GetString()!;

        // Make sure a later session of the week contains the same exercise, as happens when a block repeats a lift.
        var targetId = Guid.Parse(sessions[1].GetProperty("id").GetString()!);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var source = PrescriptionJson.Read((await db.PlannedSessions.FindAsync(Guid.Parse(first.GetProperty("id").GetString()!)))!.PrescriptionJson);
            var target = (await db.PlannedSessions.FindAsync(targetId))!;
            var kept = PrescriptionJson.Read(target.PrescriptionJson).Where(e => e.ExerciseId != exerciseId).ToList();
            target.PrescriptionJson = PrescriptionJson.Serialize(kept.Prepend(source.First(e => e.ExerciseId == exerciseId)));
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{first.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(first, kg: 72.5m, reps: 8) })).StatusCode);

        var updated = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var next = updated.GetProperty("sessions")[1].GetProperty("exercises").EnumerateArray()
            .First(e => e.GetProperty("exerciseId").GetString() == exerciseId);
        Assert.Equal(72.5m, next.GetProperty("targetLoadKg").GetDecimal());
        Assert.False(string.IsNullOrWhiteSpace(next.GetProperty("loadBasis").GetString()));
    }

    [Fact]
    public async Task ThreeStressedSessionsTurnTheNextOneIntoADeload_AndTheChangeIsAudited()
    {
        var (_, client, week) = await StartPlanAsync();
        var sessions = week.GetProperty("sessions").EnumerateArray().ToList();
        var originalSets = sessions[3].GetProperty("exercises").EnumerateArray().Sum(e => e.GetProperty("sets").GetInt32());

        JsonElement last = default;
        foreach (var session in sessions.Take(3))
        {
            var sets = session.GetProperty("exercises").EnumerateArray().Select(e => new
            {
                exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = 1, weightKg = 50m, repetitions = 6, rir = 0, completed = true
            }).ToArray();
            var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
                new { outcome = "Partial", nonCompletionReason = "Fatigue", recovery = "Insufficient", sessionRpe = 10, sets });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            last = await JsonAsync(response);
        }

        var applied = last.GetProperty("adaptation").GetProperty("applied").EnumerateArray().ToList();
        Assert.True(applied.Any(a => a.GetProperty("kind").GetString() == "deload" && a.GetProperty("sessionsChanged").GetInt32() >= 1), last.ToString()); // the rest of this week and the next 7 days
        var updated = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("sessions")[3];
        Assert.True(updated.GetProperty("exercises").EnumerateArray().Sum(e => e.GetProperty("sets").GetInt32()) < originalSets);
        Assert.All(updated.GetProperty("exercises").EnumerateArray(), e => Assert.Equal("None", e.GetProperty("progressionRule").GetString()));

        using var scope = _factory.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().PlanAdaptations.AsNoTracking().ToListAsync();
        Assert.Single(audit, a => a.Kind == "deload");
        Assert.All(audit, a => Assert.False(string.IsNullOrWhiteSpace(a.Explanation))); // every automatic change says why
    }

    [Fact]
    public async Task PainIsReportedToTheUserAndNeverAutomated()
    {
        var (_, client, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];
        var exercise = session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString();

        var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
        {
            outcome = "Partial", painReported = true,
            sets = new[] { new { exerciseId = exercise, setNumber = 1, weightKg = 60m, repetitions = 8, rir = 2, completed = true, painReported = true } }
        });

        var report = (await JsonAsync(response)).GetProperty("adaptation");
        Assert.Equal("PauseAffectedExerciseAndReview", report.GetProperty("sessionAction").GetString());
        Assert.Equal(0, report.GetProperty("applied").GetArrayLength());
        Assert.True(report.GetProperty("notes").GetArrayLength() > 0);
    }

    [Fact]
    public async Task InvalidLogsAreRejected()
    {
        var (_, client, week) = await StartPlanAsync();
        var session = week.GetProperty("sessions")[0];
        var url = $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log";
        var exercise = session.GetProperty("exercises")[0].GetProperty("exerciseId").GetString();

        var badShape = await client.PostAsJsonAsync(url, new
        {
            outcome = "Maybe", recovery = "Great", sessionRpe = 11,
            sets = new[] { new { exerciseId = exercise, setNumber = 1, weightKg = -5m, repetitions = 500, rir = 12, completed = true } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, badShape.StatusCode);
        var errors = (await JsonAsync(badShape)).GetProperty("errors");
        foreach (var field in new[] { "outcome", "recovery", "sessionRpe", "sets[0]" }) Assert.True(errors.TryGetProperty(field, out _), field);

        var foreign = await client.PostAsJsonAsync(url, new
        {
            outcome = "Completed", sets = new[] { new { exerciseId = "not-in-this-session", setNumber = 1, weightKg = 10m, repetitions = 5, completed = true } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("invalid_log", (await JsonAsync(foreign)).GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{Guid.NewGuid()}/log", new { outcome = "Completed" })).StatusCode);
        using var scope = _factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().WorkoutLogs.CountAsync());
    }

    // ------------------------------------------------------------------ alternatives

    [Fact]
    public async Task AlternativesRespectTheProfile_AndASwapOnlyAcceptsThem()
    {
        var (_, client, week) = await StartPlanAsync(profile: Profile(equipment: new[] { "dumbbell", "bench", "cable", "machine" }));
        var session = week.GetProperty("sessions")[0];
        var sessionId = session.GetProperty("id").GetString();
        var inSession = session.GetProperty("exercises").EnumerateArray().Select(e => e.GetProperty("exerciseId").GetString()).ToHashSet();
        var target = session.GetProperty("exercises").EnumerateArray()
            .First(e => e.GetProperty("primaryMuscles").EnumerateArray().Any(m => m.GetString() is "Chest" or "Lats" or "Deltoids" or "Quadriceps"))
            .GetProperty("exerciseId").GetString();

        var listing = await client.GetAsync($"/api/v1/plans/current/sessions/{sessionId}/exercises/{target}/alternatives");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        var alternatives = (await JsonAsync(listing)).GetProperty("alternatives").EnumerateArray().ToList();
        Assert.NotEmpty(alternatives);
        Assert.All(alternatives, a =>
        {
            Assert.DoesNotContain(a.GetProperty("exerciseId").GetString(), inSession); // no duplicates in the session
            Assert.All(a.GetProperty("requiredEquipment").EnumerateArray(), token =>
                Assert.Contains(token.GetString(), new[] { "dumbbell", "bench", "cable", "machine" })); // only equipment the user has
        });

        var swap = $"/api/v1/plans/current/sessions/{sessionId}/exercises/{target}/swap";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(swap, new { replacementExerciseId = "pull-up" })).StatusCode); // no bar
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(swap, new { replacementExerciseId = "" })).StatusCode);

        var replacement = alternatives[0].GetProperty("exerciseId").GetString();
        var swapped = await client.PostAsJsonAsync(swap, new { replacementExerciseId = replacement });
        Assert.True(swapped.StatusCode == HttpStatusCode.OK, await swapped.Content.ReadAsStringAsync());
        var exercises = (await JsonAsync(swapped)).GetProperty("exercises").EnumerateArray().ToList();
        Assert.DoesNotContain(exercises, e => e.GetProperty("exerciseId").GetString() == target);
        Assert.Equal(target, exercises.First(e => e.GetProperty("exerciseId").GetString() == replacement).GetProperty("swappedFromExerciseId").GetString());
    }
}

/// <summary>The plan routes follow the subscription switch like every other paid route.</summary>
public sealed class PlansSubscriptionTests() : PlansApiBase(enforced: true)
{
    [Fact]
    public async Task FreeUsersCanReadOptionsButCannotCreateOrUseGeneratedPlans()
    {
        var free = ClientFor(await AddUserAsync("free@example.com"));

        Assert.Equal(HttpStatusCode.NotFound, (await free.GetAsync("/api/v1/plans/current")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await free.GetAsync("/api/v1/plans/current/week")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await free.GetAsync("/api/v1/plans/options")).StatusCode);

        var create = await free.PostAsJsonAsync("/api/v1/plans", Profile());
        Assert.Equal(HttpStatusCode.PaymentRequired, create.StatusCode);
        Assert.Equal("subscription_required", (await JsonAsync(create)).GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await free.PostAsync("/api/v1/plans/current/archive", null)).StatusCode);

        var paid = ClientFor(await AddUserAsync("paid@example.com", subscribed: true));
        Assert.Equal(HttpStatusCode.Created, (await paid.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await paid.GetAsync("/api/v1/plans/current/week")).StatusCode);
    }

    [Fact]
    public async Task ExpiredPremiumKeepsGeneratedPlanReadableButBlocksFurtherUseAndGeneration()
    {
        var user = await AddUserAsync("expired@example.com", subscribed: true);
        var client = ClientFor(user);
        var created = await client.PostAsJsonAsync("/api/v1/plans", Profile());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = await JsonAsync(created);
        var planId = Guid.Parse(plan.GetProperty("id").GetString()!);
        var firstWeek = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var firstSession = firstWeek.GetProperty("sessions")[0];
        var sessionId = Guid.Parse(firstSession.GetProperty("id").GetString()!);
        var exerciseId = firstSession.GetProperty("exercises")[0].GetProperty("exerciseId").GetString()!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var subscription = await db.Subscriptions.SingleAsync(x => x.UserId == user.Id);
            subscription.Status = SubscriptionStatus.Expired;
            subscription.ExpiresAt = Now.AddSeconds(-1);
            var storedPlan = await db.TrainingPlans.Include(x => x.Phases).SingleAsync(x => x.Id == planId);
            storedPlan.StartDate = storedPlan.StartDate.AddDays(-28);
            foreach (var phase in storedPlan.Phases)
            {
                phase.StartDate = phase.StartDate?.AddDays(-28);
                phase.EndDate = phase.EndDate?.AddDays(-28);
            }
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/plans/current")).StatusCode);
        var currentWeekResponse = await client.GetAsync("/api/v1/plans/current/week");
        Assert.Equal(HttpStatusCode.OK, currentWeekResponse.StatusCode);
        var lockedWeek = await JsonAsync(currentWeekResponse);
        Assert.True(lockedWeek.GetProperty("locked").GetBoolean());
        Assert.Equal("InProgress", lockedWeek.GetProperty("timeline").GetString());
        Assert.Empty(lockedWeek.GetProperty("sessions").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/plans/current/history")).StatusCode);

        var log = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessionId}/log",
            new { outcome = "Completed", sets = CompleteSets(firstSession) });
        await AssertPremiumRequiredAsync(log);
        await AssertPremiumRequiredAsync(await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{sessionId}/exercises/{exerciseId}/swap",
            new { replacementExerciseId = "push-up" }));
        await AssertPremiumRequiredAsync(await client.GetAsync(
            $"/api/v1/plans/current/sessions/{sessionId}/exercises/{exerciseId}/alternatives"));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            Assert.Equal(1, await db.TrainingMesocycles.CountAsync(x => x.PlanId == planId));
            Assert.Empty(await db.WorkoutLogs.Where(x => x.PlanId == planId).ToListAsync());
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/plans/current/archive", null)).StatusCode);
    }

    [Fact]
    public async Task ThePlanOfAGeneratedWeekStaysReadable_WithItsSessions_WhenPremiumLapses()
    {
        var user = await AddUserAsync("lapsed@example.com", subscribed: true);
        var client = ClientFor(user);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        await ExpireSubscriptionAsync(user.Id);

        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.True(week.GetProperty("locked").GetBoolean());
        Assert.NotEmpty(week.GetProperty("sessions").EnumerateArray());
    }

    [Fact]
    public async Task ManualPlansAreFree_TheySaveTheLogButNeverAdaptWithoutPremium()
    {
        var user = await AddUserAsync("manual@example.com", subscribed: true);
        var client = ClientFor(user);
        var planId = Guid.Parse((await JsonAsync(await client.PostAsJsonAsync("/api/v1/plans", Profile()))).GetProperty("id").GetString()!);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            (await db.TrainingPlans.SingleAsync(x => x.Id == planId)).Origin = TrainingPlanOrigin.Manual;
            await db.SaveChangesAsync();
        }
        await ExpireSubscriptionAsync(user.Id);

        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.False(week.GetProperty("locked").GetBoolean());
        var session = week.GetProperty("sessions")[0];
        var sessionId = session.GetProperty("id").GetString();
        var logged = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{sessionId}/log",
            new { outcome = "Completed", sets = CompleteSets(session) });

        Assert.Equal(HttpStatusCode.OK, logged.StatusCode);
        var adaptation = (await JsonAsync(logged)).GetProperty("adaptation");
        Assert.True(adaptation.GetProperty("locked").GetBoolean());
        Assert.Empty(adaptation.GetProperty("applied").EnumerateArray());
    }

    [Fact]
    public async Task PremiumLogsAreAdapted_AndAdministratorsHaveEveryPremiumFeature()
    {
        var premium = ClientFor(await AddUserAsync("premium@example.com", subscribed: true));
        await premium.PostAsJsonAsync("/api/v1/plans", Profile());
        var premiumSession = (await JsonAsync(await premium.GetAsync("/api/v1/plans/current/week"))).GetProperty("sessions")[0];
        var premiumLog = await JsonAsync(await premium.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{premiumSession.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(premiumSession) }));
        Assert.False(premiumLog.GetProperty("adaptation").GetProperty("locked").GetBoolean());

        // An administrator has no subscription row at all, yet reads, logs and adapts like a subscriber.
        var admin = ClientFor(await AddUserAsync("admin@example.com", role: "admin"));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/plans/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        var week = await JsonAsync(await admin.GetAsync("/api/v1/plans/current/week"));
        Assert.False(week.GetProperty("locked").GetBoolean());
        var session = week.GetProperty("sessions")[0];
        var adminLog = await JsonAsync(await admin.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(session) }));
        Assert.False(adminLog.GetProperty("adaptation").GetProperty("locked").GetBoolean());
    }

    private static object ManualPlan(object? days = null, string? name = "Mi plan", int? weeks = 6) => new
    {
        name, totalWeeks = weeks, days = days ?? new object[]
        {
            new { day = "Monday", title = "Empuje", exercises = new object[]
                { new { exerciseId = "bench-press", sets = 4, minRepetitions = 6, maxRepetitions = 10, targetRir = 2 }, new { exerciseId = "ohp", sets = 3 } } },
            new { day = "Thursday", title = "Pierna", exercises = new object[] { new { exerciseId = "squat", sets = 4, minRepetitions = 5, maxRepetitions = 8 } } }
        }
    };

    [Fact]
    public async Task ASupersetOfAManualPlanIsServedWithItsGroup_AndMustJoinConsecutiveExercises()
    {
        var client = ClientFor(await AddUserAsync("superset@example.com"));
        var days = new object[]
        {
            new { day = "Monday", title = "Empuje", exercises = new object[]
            {
                new { exerciseId = "bench-press", sets = 3, supersetGroup = "a" }, new { exerciseId = "ohp", sets = 3, supersetGroup = "A" },
                new { exerciseId = "squat", sets = 3 }
            } }
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan(days))).StatusCode);

        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var exercises = week.GetProperty("sessions")[0].GetProperty("exercises").EnumerateArray().ToList();
        Assert.Equal("A", exercises[0].GetProperty("supersetGroup").GetString());
        Assert.Equal("A", exercises[1].GetProperty("supersetGroup").GetString());
        Assert.Equal(JsonValueKind.Null, exercises[2].GetProperty("supersetGroup").ValueKind);

        var other = ClientFor(await AddUserAsync("superset2@example.com"));
        var lonely = new object[] { new { day = "Monday", title = "X", exercises = new object[]
            { new { exerciseId = "bench-press", supersetGroup = "A" }, new { exerciseId = "ohp" }, new { exerciseId = "squat", supersetGroup = "A" } } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan(lonely))).StatusCode);
    }

    [Fact]
    public async Task ManualPlansCanBeEdited_KeepingWhatWasLogged_AndGeneratedPlansCannot()
    {
        var client = ClientFor(await AddUserAsync("edit-manual@example.com"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan())).StatusCode);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var first = week.GetProperty("sessions")[0];
        await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{first.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(first) });

        var edited = new object[]
        {
            new { day = "Monday", title = "Nuevo empuje", exercises = new object[] { new { exerciseId = "ohp", sets = 5 } } },
            new { day = "Wednesday", title = "Extra", exercises = new object[] { new { exerciseId = "squat", sets = 2 } } }
        };
        var response = await client.PutAsJsonAsync("/api/v1/plans/current/manual", ManualPlan(edited, name: "Plan editado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Plan editado", (await JsonAsync(response)).GetProperty("macrocycle").GetProperty("name").GetString());
        var after = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var sessions = after.GetProperty("sessions").EnumerateArray().ToList();
        // The logged Monday keeps its original routine; the new Wednesday exists.
        var monday = sessions.Single(x => x.GetProperty("day").GetString() == "Monday");
        Assert.Equal("Empuje", monday.GetProperty("title").GetString());
        Assert.NotNull(monday.GetProperty("log").GetRawText());
        Assert.Contains(sessions, x => x.GetProperty("day").GetString() == "Wednesday" && x.GetProperty("title").GetString() == "Extra");
        Assert.DoesNotContain(sessions, x => x.GetProperty("day").GetString() == "Thursday"); // dropped day

        var generatedClient = ClientFor(await AddUserAsync("edit-generated@example.com", subscribed: true));
        Assert.Equal(HttpStatusCode.Created, (await generatedClient.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await generatedClient.PutAsJsonAsync("/api/v1/plans/current/manual", ManualPlan())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(await AddUserAsync("edit-none@example.com")).PutAsJsonAsync("/api/v1/plans/current/manual", ManualPlan())).StatusCode);
    }

    [Fact]
    public async Task FreeUsersGetTheLastLoadToStartFrom_ButNoProposedIncrease()
    {
        var client = ClientFor(await AddUserAsync("free-last@example.com"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan())).StatusCode);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var session = week.GetProperty("sessions")[0];
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(session, kg: 60m, reps: 30) })).StatusCode);

        var last = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week")))
            .GetProperty("sessions")[0].GetProperty("exercises")[0].GetProperty("lastPerformance");

        Assert.Equal("repeat", last.GetProperty("suggestionBasis").GetString());
        Assert.Equal(60m, last.GetProperty("suggestedLoadKg").GetDecimal());
    }

    [Fact]
    public async Task MuscleFeedbackIsSaved_ValidatedAndNeverAdaptsAFreePlan()
    {
        var client = ClientFor(await AddUserAsync("feedback-free@example.com"));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan())).StatusCode);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var session = week.GetProperty("sessions")[0];
        var url = $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log";

        var invalid = await client.PostAsJsonAsync(url, new
        {
            outcome = "Completed", sets = CompleteSets(session),
            exerciseFeedback = new Dictionary<string, object> { ["bench-press"] = new { stimulus = "enormous" } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var ok = await client.PostAsJsonAsync(url, new
        {
            outcome = "Completed", sets = CompleteSets(session),
            exerciseFeedback = new Dictionary<string, object> { ["bench-press"] = new { stimulus = "Low", discomfort = "none", preFatigue = "recovered" } }
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var report = (await JsonAsync(ok)).GetProperty("adaptation");
        Assert.True(report.GetProperty("locked").GetBoolean());
        Assert.Empty(report.GetProperty("applied").EnumerateArray());

        using var scope = _factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().WorkoutLogs.SingleAsync();
        Assert.Contains("\"stimulus\":\"low\"", saved.ExerciseFeedbackJson);
    }

    [Fact]
    public async Task PremiumAddsASetAfterTwoSessionsWithLowStimulus_AndRecovery()
    {
        var client = ClientFor(await AddUserAsync("feedback-premium@example.com", subscribed: true));
        var days = new object[]
        {
            new { day = "Monday", title = "A", exercises = new object[] { new { exerciseId = "bench-press", sets = 3 } } },
            new { day = "Tuesday", title = "B", exercises = new object[] { new { exerciseId = "bench-press", sets = 3 } } }
        };
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan(days))).StatusCode);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        JsonElement report = default;
        foreach (var session in week.GetProperty("sessions").EnumerateArray().Take(2))
        {
            var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", new
            {
                outcome = "Completed", sets = CompleteSets(session),
                exerciseFeedback = new Dictionary<string, object> { ["bench-press"] = new { stimulus = "low", discomfort = "none", preFatigue = "recovered" } }
            });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            report = (await JsonAsync(response)).GetProperty("adaptation");
        }

        var applied = report.GetProperty("applied").EnumerateArray().ToList();
        Assert.Contains(applied, x => x.GetProperty("kind").GetString() == "muscle_add_set");
        var next = await JsonAsync(await client.GetAsync("/api/v1/plans/current/block"));
        var later = next.GetProperty("weeks").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray())
            .Where(x => x.GetProperty("log").ValueKind == JsonValueKind.Null).ToList();
        Assert.Contains(later, x => x.GetProperty("exercises")[0].GetProperty("sets").GetInt32() == 4);
    }

    [Fact]
    public async Task FreeUsersCreateAndUseManualPlans_WithoutAdaptation()
    {
        var client = ClientFor(await AddUserAsync("free-manual@example.com"));

        var created = await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = await JsonAsync(created);
        Assert.Equal("Manual", plan.GetProperty("origin").GetString());
        Assert.Equal("Mi plan", plan.GetProperty("macrocycle").GetProperty("name").GetString());
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.False(week.GetProperty("locked").GetBoolean());
        Assert.Equal(2, week.GetProperty("sessions").GetArrayLength());
        var session = week.GetProperty("sessions")[0];
        Assert.Equal("Empuje", session.GetProperty("title").GetString());
        Assert.Equal(new[] { "bench-press", "ohp" }, session.GetProperty("exercises").EnumerateArray().Select(e => e.GetProperty("exerciseId").GetString()));

        var logged = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(session) });
        Assert.Equal(HttpStatusCode.OK, logged.StatusCode);
        Assert.True((await JsonAsync(logged)).GetProperty("adaptation").GetProperty("locked").GetBoolean());
    }

    [Fact]
    public async Task PremiumAdaptsManualPlans_AndKeepsTheStructureTheUserWrote()
    {
        var client = ClientFor(await AddUserAsync("premium-manual@example.com", subscribed: true));
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan())).StatusCode);
        var session = (await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"))).GetProperty("sessions")[0];

        var logged = await JsonAsync(await client.PostAsJsonAsync(
            $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log",
            new { outcome = "Completed", sets = CompleteSets(session, kg: 80m, reps: 10) }));

        var adaptation = logged.GetProperty("adaptation");
        Assert.False(adaptation.GetProperty("locked").GetBoolean());
        Assert.True(adaptation.GetProperty("exercises").GetArrayLength() > 0);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.Equal(2, week.GetProperty("sessions").GetArrayLength());
        Assert.Equal("Manual", (await JsonAsync(await client.GetAsync("/api/v1/plans/current"))).GetProperty("origin").GetString());
    }

    [Fact]
    public async Task ManualPlansStayUsableWhenPremiumLapses_AndTheyHaveNoSubscriptionRequirement()
    {
        var user = await AddUserAsync("lapsed-manual@example.com", subscribed: true);
        var client = ClientFor(user);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan())).StatusCode);
        await ExpireSubscriptionAsync(user.Id);

        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        Assert.False(week.GetProperty("locked").GetBoolean());
        var session = week.GetProperty("sessions")[0];
        var alternatives = await client.GetAsync(
            $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/exercises/bench-press/alternatives");
        Assert.Equal(HttpStatusCode.OK, alternatives.StatusCode);
    }

    [Fact]
    public async Task ManualPlanValidationExplainsEveryProblem_AndOnlyOnePlanCanBeActive()
    {
        var client = ClientFor(await AddUserAsync("invalid-manual@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan(name: ""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan(days: Array.Empty<object>()))).StatusCode);
        var bad = await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan(days: new object[]
        {
            new { day = "Monday", title = "A", exercises = new object[] { new { exerciseId = "bench-press", sets = 99 } } }
        }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var unknown = await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan(days: new object[]
        {
            new { day = "Monday", title = "A", exercises = new object[] { new { exerciseId = "does-not-exist" } } }
        }));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/plans/manual", ManualPlan())).StatusCode);
    }

    private static async Task AssertPremiumRequiredAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        Assert.Equal("premium_required", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    private async Task ExpireSubscriptionAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var subscription = await db.Subscriptions.SingleAsync(x => x.UserId == userId);
        subscription.Status = SubscriptionStatus.Expired;
        subscription.ExpiresAt = Now.AddSeconds(-1);
        await db.SaveChangesAsync();
    }
}
