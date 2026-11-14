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
    private static readonly string[] Tables = ["training_plans", "training_plan_phases", "training_mesocycles", "planned_sessions", "workout_logs", "workout_set_logs", "plan_adaptations"];

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
            if (mine && text.StartsWith("CREATE", StringComparison.Ordinal)) db.Database.ExecuteSqlRaw(text + ";");
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
        Assert.Equal(12, plan.GetProperty("totalWeeks").GetInt32());
        Assert.Equal(1, plan.GetProperty("currentWeek").GetInt32());
        var phases = plan.GetProperty("macrocycle").GetProperty("phases").EnumerateArray().ToList();
        Assert.True(phases.Count >= 3); // training phases separated by deloads
        Assert.Contains(phases, p => p.GetProperty("type").GetString() == "deload");
        Assert.DoesNotContain("exerciseId", text);
        Assert.DoesNotContain("sessions", text);
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
    public async Task WithSubscriptionsEnforced_PlanRoutesAnswer402WithoutAccess_AndWorkWithIt()
    {
        var free = ClientFor(await AddUserAsync("free@example.com"));
        foreach (var response in new[] { await free.GetAsync("/api/v1/plans/current"), await free.GetAsync("/api/v1/plans/current/week"),
                     await free.GetAsync("/api/v1/plans/options"), await free.PostAsJsonAsync("/api/v1/plans", Profile()),
                     await free.PostAsync("/api/v1/plans/current/archive", null) })
        {
            Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
            Assert.Equal("subscription_required", (await JsonAsync(response)).GetProperty("error").GetString());
        }

        var paid = ClientFor(await AddUserAsync("paid@example.com", subscribed: true));
        Assert.Equal(HttpStatusCode.Created, (await paid.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await paid.GetAsync("/api/v1/plans/current/week")).StatusCode);
    }

    [Fact]
    public async Task AdministratorsSkipTheSubscriptionCheck()
    {
        var admin = ClientFor(await AddUserAsync("admin@example.com", role: "admin"));

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/plans/options")).StatusCode);
    }
}
