using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Overview;
using Trazo.Api.Security;
using Trazo.Domain.Billing;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Domain.Settings;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;
using Trazo.UnitTests.Planning;
using Xunit;

namespace Trazo.UnitTests;

public sealed class MutableClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>HTTP-level behaviour of the administration overview: access, the numbers, the alerts and the short cache.</summary>
public sealed class AdminOverviewApiTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly MutableClock _clock = new(Start);
    private readonly FailingTransport _transport = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AdminOverviewApiTests()
    {
        using (var db = _fixture.NewContext())
        {
            ExerciseTestSchema.Create(db);
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE app_logs (
                  id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp_utc TEXT NOT NULL, level INTEGER NOT NULL,
                  category TEXT NOT NULL, message TEXT NOT NULL, exception TEXT NULL, trace_id TEXT NULL);
                CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT PRIMARY KEY, "ProductVersion" TEXT NOT NULL);
                """);
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString)
                    .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_clock);
                services.RemoveAll<ISmtpTransport>();
                services.AddSingleton<ISmtpTransport>(_transport);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------ helpers

    private TrazoDbContext Db() => new(new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(_fixture.ConnectionString)
        .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>().Options);

    private async Task<TrazoUser> AddUserAsync(string email, string role = "user", DateTimeOffset? createdAt = null)
    {
        await using var db = Db();
        var created = createdAt ?? Start;
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email.Split('@')[0],
            Role = role, EmailVerifiedAt = created, CreatedAt = created, UpdatedAt = created
        };
        user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, "CorrectHorse1Battery");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private HttpClient ClientFor(TrazoUser? user)
    {
        var client = _factory.CreateClient();
        if (user is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private async Task<HttpClient> SuperadminAsync() => ClientFor(await AddUserAsync($"root-{Guid.NewGuid():N}@trazo.test", "superadmin"));

    private async Task SetAsync(params (string Key, string Value)[] settings)
    {
        await using var db = Db();
        foreach (var (key, value) in settings)
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, IsSecret = false, UpdatedAt = Start });
        await db.SaveChangesAsync();
    }

    private Task ConfigureSmtpAsync() => SetAsync(
        (SettingKeys.SmtpHost, "smtp.example.com"), (SettingKeys.EmailFrom, "TRAZO <no-reply@example.com>"),
        (SettingKeys.EmailActionBaseUrl, "https://trazo.example.com"));

    private async Task MarkAllMigrationsAppliedAsync()
    {
        await using var db = Db();
        foreach (var id in db.Database.GetMigrations())
            await db.Database.ExecuteSqlRawAsync("INSERT INTO \"__EFMigrationsHistory\" VALUES ({0}, '10.0.0')", id);
    }

    private async Task LogAsync(DateTimeOffset at, int level, string category, string message = "m", string? exception = null)
    {
        await using var db = Db();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO app_logs (timestamp_utc, level, category, message, exception) VALUES ({0}, {1}, {2}, {3}, {4})",
            at, level, category, message, exception!);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>())!;
    }

    private static string[] AlertIds(JsonElement body) =>
        body.GetProperty("alerts").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToArray();

    // ------------------------------------------------------------------ access

    [Fact]
    public async Task OnlySuperadminsCanReadTheOverview()
    {
        foreach (var url in new[] { "/api/v1/admin/overview", "/api/v1/admin/overview/system" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(await AddUserAsync($"a{Guid.NewGuid():N}@trazo.test", "admin")).GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(await AddUserAsync($"u{Guid.NewGuid():N}@trazo.test")).GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await (await SuperadminAsync()).GetAsync(url)).StatusCode);
        }
    }

    [Fact]
    public async Task SystemReportsTheVersionAndTheEnvironmentWithoutTouchingTheDatabase()
    {
        var body = await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview/system"));

        Assert.Equal("Testing", body.GetProperty("environment").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
    }

    // ------------------------------------------------------------------ health

    [Fact]
    public async Task HealthShowsTheDatabaseAndTheAppliedMigrations()
    {
        await MarkAllMigrationsAppliedAsync();

        var health = (await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"))).GetProperty("health");

        Assert.True(health.GetProperty("databaseOk").GetBoolean());
        Assert.True(health.GetProperty("databaseLatencyMs").GetInt64() >= 0);
        var total = health.GetProperty("migrationsTotal").GetInt32();
        Assert.True(total > 0);
        Assert.Equal(total, health.GetProperty("migrationsApplied").GetInt32());
        Assert.Empty(health.GetProperty("pendingMigrations").EnumerateArray());
    }

    [Fact]
    public async Task PendingMigrationsAreListedAndRaiseACriticalAlert()
    {
        // The history holds no rows: every migration is pending.
        var body = await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"));

        Assert.Equal(0, body.GetProperty("health").GetProperty("migrationsApplied").GetInt32());
        Assert.NotEmpty(body.GetProperty("health").GetProperty("pendingMigrations").EnumerateArray());
        var alert = body.GetProperty("alerts").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "migrations-pending");
        Assert.Equal("critical", alert.GetProperty("severity").GetString());
    }

    // ------------------------------------------------------------------ summary

    [Fact]
    public async Task SummaryCountsUsersPlansWorkoutsSubscriptionsAndTheCatalog()
    {
        var recent = await AddUserAsync("recent@trazo.test", createdAt: Start.AddDays(-3));
        await AddUserAsync("old@trazo.test", createdAt: Start.AddDays(-30));
        await using (var db = Db())
        {
            var plan = new TrainingPlan { Id = Guid.NewGuid(), UserId = recent.Id, Goal = "Hypertrophy", Experience = "Intermediate", Split = "Automatic", CreatedAt = Start.AddDays(-2), UpdatedAt = Start };
            var oldPlan = new TrainingPlan { Id = Guid.NewGuid(), UserId = recent.Id, Status = TrainingPlanStatus.Archived, Goal = "Strength", Experience = "Beginner", Split = "Automatic", CreatedAt = Start.AddDays(-20), UpdatedAt = Start };
            var meso = new TrainingMesocycle { Id = Guid.NewGuid(), PlanId = plan.Id, PhaseType = "Build", CreatedAt = Start };
            var recentSession = new PlannedSession { Id = Guid.NewGuid(), PlanId = plan.Id, MesocycleId = meso.Id, Title = "A", PrescriptionJson = "{}", UpdatedAt = Start };
            var oldSession = new PlannedSession { Id = Guid.NewGuid(), PlanId = plan.Id, MesocycleId = meso.Id, Title = "B", PrescriptionJson = "{}", UpdatedAt = Start };
            db.TrainingPlans.AddRange(plan, oldPlan);
            db.TrainingMesocycles.Add(meso);
            db.PlannedSessions.AddRange(recentSession, oldSession);
            db.WorkoutLogs.AddRange(
                new WorkoutLog { Id = Guid.NewGuid(), UserId = recent.Id, PlanId = plan.Id, PlannedSessionId = recentSession.Id, LoggedAt = Start.AddDays(-1) },
                new WorkoutLog { Id = Guid.NewGuid(), UserId = recent.Id, PlanId = plan.Id, PlannedSessionId = oldSession.Id, LoggedAt = Start.AddDays(-9) });
            foreach (var status in new[] { SubscriptionStatus.Active, SubscriptionStatus.Active, SubscriptionStatus.OnHold })
                db.Subscriptions.Add(new Subscription
                {
                    Id = Guid.NewGuid(), UserId = recent.Id, ProductId = "trazo_premium", PurchaseTokenHash = Guid.NewGuid().ToString("N"),
                    ProtectedPurchaseToken = "x", Status = status, StartedAt = Start, ExpiresAt = Start.AddDays(30), CreatedAt = Start, UpdatedAt = Start
                });
            db.ExerciseCatalog.AddRange(
                Trazo.UnitTests.Planning.ExerciseCandidateMapperTests.Item("with-image"),
                Trazo.UnitTests.Planning.ExerciseCandidateMapperTests.Item("no-image"),
                Trazo.UnitTests.Planning.ExerciseCandidateMapperTests.Item("broken", pattern: "Teleport"),
                Trazo.UnitTests.Planning.ExerciseCandidateMapperTests.Item("hidden", active: false));
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO exercise_catalog_images VALUES ('with-image', 0, 'image/png', x'00', 'h', 'trazo', 'trazo', '2026-11-01')");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO exercise_catalog_images VALUES ('hidden', 0, 'image/png', x'00', 'h', 'trazo', 'trazo', '2026-11-01')");
        }

        var summary = (await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"))).GetProperty("summary");

        Assert.Equal(3, summary.GetProperty("usersTotal").GetInt32()); // recent, old and the superadmin created now
        Assert.Equal(2, summary.GetProperty("usersNew7d").GetInt32()); // the old one is outside the window
        Assert.Equal(1, summary.GetProperty("plansCreated7d").GetInt32());
        Assert.Equal(1, summary.GetProperty("workoutsLogged7d").GetInt32());
        Assert.Equal(3, summary.GetProperty("subscriptionsTotal").GetInt32());
        var byStatus = summary.GetProperty("subscriptionsByStatus").EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString()!, x => x.GetProperty("count").GetInt32());
        Assert.Equal(2, byStatus["active"]);
        Assert.Equal(1, byStatus["on_hold"]);
        Assert.Equal(0, byStatus["trial"]);
        Assert.False(summary.GetProperty("subscriptionsEnforced").GetBoolean());
        Assert.Equal(3, summary.GetProperty("exercisesActive").GetInt32());   // the hidden one is not counted
        Assert.Equal(2, summary.GetProperty("exercisesUsable").GetInt32());
        Assert.Equal(1, summary.GetProperty("exercisesUnusable").GetInt32()); // "broken"; hidden ones are not "unusable"
        Assert.Equal(2, summary.GetProperty("exercisesWithoutImages").GetInt32()); // no-image and broken; hidden is ignored
    }

    [Fact]
    public async Task InsightsGiveTheFunnelTheDailyActivityAndTheSubscriptionsToWatch()
    {
        var a = await AddUserAsync("a@trazo.test", createdAt: Start.AddDays(-2));
        var b = await AddUserAsync("b@trazo.test", createdAt: Start.AddDays(-5));
        await AddUserAsync("c@trazo.test", createdAt: Start.AddDays(-10));
        await using (var db = Db())
        {
            var plan = new TrainingPlan { Id = Guid.NewGuid(), UserId = a.Id, Goal = "Strength", Experience = "Beginner", Split = "Automatic", CreatedAt = Start.AddDays(-1), UpdatedAt = Start };
            var planB = new TrainingPlan { Id = Guid.NewGuid(), UserId = b.Id, Goal = "Strength", Experience = "Beginner", Split = "Automatic", CreatedAt = Start.AddDays(-1), UpdatedAt = Start };
            var meso = new TrainingMesocycle { Id = Guid.NewGuid(), PlanId = plan.Id, PhaseType = "Build", CreatedAt = Start };
            var session = new PlannedSession { Id = Guid.NewGuid(), PlanId = plan.Id, MesocycleId = meso.Id, Title = "A", PrescriptionJson = "{}", UpdatedAt = Start };
            db.TrainingPlans.AddRange(plan, planB);
            db.TrainingMesocycles.Add(meso);
            PlannedSession Extra() => new() { Id = Guid.NewGuid(), PlanId = plan.Id, MesocycleId = meso.Id, Title = "X", PrescriptionJson = "{}", UpdatedAt = Start };
            db.PlannedSessions.Add(session);
            for (var i = 0; i < 3; i++)
            {
                var one = i == 0 ? session : Extra();
                if (i > 0) db.PlannedSessions.Add(one);
                db.WorkoutLogs.Add(new WorkoutLog { Id = Guid.NewGuid(), UserId = a.Id, PlanId = plan.Id, PlannedSessionId = one.Id, LoggedAt = Start.AddHours(-1 - i) });
            }
            var sessionB = Extra();
            db.PlannedSessions.Add(sessionB);
            db.WorkoutLogs.Add(new WorkoutLog { Id = Guid.NewGuid(), UserId = b.Id, PlanId = planB.Id, PlannedSessionId = sessionB.Id, LoggedAt = Start.AddDays(-3) });
            Subscription Sub(SubscriptionStatus status, int expiresInDays, bool test = false) => new()
            {
                Id = Guid.NewGuid(), UserId = a.Id, ProductId = "trazo_premium", PurchaseTokenHash = Guid.NewGuid().ToString("N"),
                ProtectedPurchaseToken = "x", Status = status, StartedAt = Start, ExpiresAt = Start.AddDays(expiresInDays),
                IsTestPurchase = test, CreatedAt = Start, UpdatedAt = Start
            };
            db.Subscriptions.AddRange(Sub(SubscriptionStatus.Trial, 3), Sub(SubscriptionStatus.Trial, 20), Sub(SubscriptionStatus.OnHold, -1),
                Sub(SubscriptionStatus.Canceled, 5), Sub(SubscriptionStatus.Trial, 2, test: true));
            await db.SaveChangesAsync();
        }

        var body = await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"));
        var insights = body.GetProperty("insights");

        var funnel = insights.GetProperty("funnel");
        Assert.Equal(4, funnel.GetProperty("registered").GetInt32()); // a, b, c and the superadmin
        Assert.Equal(4, funnel.GetProperty("emailVerified").GetInt32());
        Assert.Equal(2, funnel.GetProperty("withPlan").GetInt32());
        Assert.Equal(2, funnel.GetProperty("withWorkout").GetInt32());
        Assert.Equal(1, funnel.GetProperty("recurring").GetInt32()); // only a has 3 sessions

        var days = insights.GetProperty("activity").GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(14, days.Count);
        Assert.Equal(4, days.Sum(d => d.GetProperty("workouts").GetInt32()));
        Assert.Equal(2, days.Sum(d => d.GetProperty("plans").GetInt32()));

        var watch = insights.GetProperty("subscriptions");
        Assert.Equal(1, watch.GetProperty("trialsEndingSoon").GetInt32()); // the 20-day trial and the test one are excluded
        Assert.Equal(1, watch.GetProperty("cancelingSoon").GetInt32());
        Assert.Equal(1, watch.GetProperty("paymentIssues").GetInt32());
        Assert.Equal(1, watch.GetProperty("testPurchases").GetInt32());
        var ids = AlertIds(body);
        Assert.Contains("subscriptions-payment-issues", ids);
        Assert.Contains("trials-ending", ids);
    }

    [Fact]
    public async Task HealthRelatedFiguresAreHiddenWhileTheGroupIsTooSmallToBeAnonymous()
    {
        var user = await AddUserAsync("h@trazo.test");
        Guid planId, mesoId;
        await using (var db = Db())
        {
            var plan = new TrainingPlan { Id = Guid.NewGuid(), UserId = user.Id, Goal = "Strength", Experience = "Beginner", Split = "Automatic", CreatedAt = Start, UpdatedAt = Start };
            var meso = new TrainingMesocycle { Id = Guid.NewGuid(), PlanId = plan.Id, PhaseType = "Build", CreatedAt = Start };
            db.TrainingPlans.Add(plan);
            db.TrainingMesocycles.Add(meso);
            await db.SaveChangesAsync();
            (planId, mesoId) = (plan.Id, meso.Id);
        }

        async Task AddLogsAsync(int count, WorkoutOutcome outcome, string reason, bool pain)
        {
            await using var db = Db();
            for (var i = 0; i < count; i++)
            {
                var session = new PlannedSession { Id = Guid.NewGuid(), PlanId = planId, MesocycleId = mesoId, Title = "S", PrescriptionJson = "{}", UpdatedAt = Start };
                db.PlannedSessions.Add(session);
                db.WorkoutLogs.Add(new WorkoutLog
                {
                    Id = Guid.NewGuid(), UserId = user.Id, PlanId = planId, PlannedSessionId = session.Id, LoggedAt = Start.AddHours(-i - 1),
                    Outcome = outcome, NonCompletionReason = reason, PainReported = pain
                });
            }
            await db.SaveChangesAsync();
        }

        await AddLogsAsync(2, WorkoutOutcome.Partial, "Illness", pain: true);
        var admin = await SuperadminAsync();
        var few = (await ReadAsync(await admin.GetAsync("/api/v1/admin/overview?refresh=true"))).GetProperty("insights").GetProperty("engine");
        Assert.Equal(2, few.GetProperty("workouts30d").GetInt32());
        Assert.Equal(JsonValueKind.Null, few.GetProperty("painReportedPercent").ValueKind); // fewer than five sessions
        Assert.Equal("Other", Assert.Single(few.GetProperty("notCompletedReasons").EnumerateArray()).GetProperty("name").GetString());

        _clock.Now = Start.AddMinutes(5); // let the cache expire
        await AddLogsAsync(6, WorkoutOutcome.NotCompleted, "Time", pain: false);
        var many = (await ReadAsync(await admin.GetAsync("/api/v1/admin/overview?refresh=true"))).GetProperty("insights").GetProperty("engine");
        Assert.Equal(25, many.GetProperty("painReportedPercent").GetInt32()); // 2 of 8
        var reasons = many.GetProperty("notCompletedReasons").EnumerateArray()
            .ToDictionary(x => x.GetProperty("name").GetString()!, x => x.GetProperty("count").GetInt32());
        Assert.Equal(6, reasons["Time"]);
        Assert.Equal(2, reasons["Other"]); // "Illness" has fewer than five, so it is merged
        Assert.DoesNotContain("Illness", reasons.Keys);
    }

    // ------------------------------------------------------------------ logs

    [Fact]
    public async Task LogsCountTheLast24HoursAndNeverExposeMessagesOrTraces()
    {
        await LogAsync(Start.AddHours(-1), 4, "Trazo.Billing", "Failed for secret@person.test", "System.Exception: boom\n   at Foo()");
        await LogAsync(Start.AddHours(-2), 5, "Trazo.Billing", "Critical failure");
        await LogAsync(Start.AddHours(-3), 4, "Trazo.Images");
        await LogAsync(Start.AddHours(-4), 3, "Trazo.Images");
        await LogAsync(Start.AddHours(-5), 2, "Trazo.Images");           // information: not counted
        await LogAsync(Start.AddHours(-30), 4, "Trazo.Old");             // previous window
        await LogAsync(Start.AddHours(-60), 4, "Trazo.Ancient");         // outside both windows

        var response = await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview");
        var raw = await response.Content.ReadAsStringAsync();
        var logs = JsonDocument.Parse(raw).RootElement.GetProperty("logs");

        Assert.Equal(3, logs.GetProperty("errors24h").GetInt32());
        Assert.Equal(1, logs.GetProperty("warnings24h").GetInt32());
        Assert.Equal(1, logs.GetProperty("errorsPrevious24h").GetInt32());
        var top = logs.GetProperty("topErrorSources").EnumerateArray().ToList();
        Assert.Equal(["Trazo.Billing", "Trazo.Images"], top.Select(x => x.GetProperty("category").GetString()));
        Assert.Equal(2, top[0].GetProperty("count").GetInt32());
        Assert.Equal("/logs?levels=error&range=24h&q=Trazo.Billing", top[0].GetProperty("link").GetString());
        Assert.DoesNotContain("secret@person.test", raw);
        Assert.DoesNotContain("boom", raw);
        Assert.DoesNotContain("Foo()", raw);
    }

    // ------------------------------------------------------------------ configuration

    [Fact]
    public async Task UnconfiguredInstallationListsWhatIsMissing_AndWarnsAboutSmtpAndGoogle()
    {
        await MarkAllMigrationsAppliedAsync();

        var body = await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"));

        var config = body.GetProperty("configuration");
        Assert.False(config.GetProperty("smtpConfigured").GetBoolean());
        Assert.False(config.GetProperty("googleConfigured").GetBoolean());
        Assert.False(config.GetProperty("playBillingConfigured").GetBoolean());
        Assert.False(config.GetProperty("subscriptionsEnforced").GetBoolean());
        Assert.Equal(JsonValueKind.Null, config.GetProperty("lastSmtpSend").ValueKind);
        var missing = config.GetProperty("missing").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Equal(["google-client-id", "smtp-host", "email-from", "email-action-base-url"], missing); // Play is optional while subscriptions are off
        var ids = AlertIds(body);
        Assert.Contains("smtp-not-configured", ids);
        Assert.Contains("google-not-configured", ids);
        Assert.DoesNotContain("migrations-pending", ids);
    }

    [Fact]
    public async Task ASuccessfulAndAFailedSmtpSendAreRecordedWithoutTheServerMessage()
    {
        await MarkAllMigrationsAppliedAsync();
        await ConfigureSmtpAsync();
        var client = await SuperadminAsync();

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync("someone@example.com", "s", "b", default);
        var ok = (await ReadAsync(await client.GetAsync("/api/v1/admin/overview"))).GetProperty("configuration");
        Assert.True(ok.GetProperty("smtpConfigured").GetBoolean());
        Assert.True(ok.GetProperty("lastSmtpSend").GetProperty("succeeded").GetBoolean());

        _clock.Now = Start.AddMinutes(5);
        _transport.Failure = new InvalidOperationException("535 Authentication failed for someone@example.com");
        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync("someone@example.com", "s", "b", default));
        var response = await client.GetAsync("/api/v1/admin/overview");
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;

        var last = body.GetProperty("configuration").GetProperty("lastSmtpSend");
        Assert.False(last.GetProperty("succeeded").GetBoolean());
        Assert.Equal("InvalidOperationException", last.GetProperty("error").GetString());
        Assert.Equal(Start.AddMinutes(5), last.GetProperty("at").GetDateTimeOffset());
        Assert.DoesNotContain("someone@example.com", raw);
        Assert.DoesNotContain("535", raw);
        var alert = body.GetProperty("alerts").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "smtp-failing");
        Assert.Equal("critical", alert.GetProperty("severity").GetString());
        Assert.Equal("/settings", alert.GetProperty("link").GetString());
    }

    [Fact]
    public async Task PlayBillingWithoutRtdnWarns_AndRequiredSubscriptionsWithoutPlayAreCritical()
    {
        await MarkAllMigrationsAppliedAsync();
        await SetAsync((SettingKeys.PlayPackageName, "com.example.trazo"), (SettingKeys.PlayServiceAccountJson, "{}"));

        var body = await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"));

        Assert.True(body.GetProperty("configuration").GetProperty("playBillingConfigured").GetBoolean());
        Assert.False(body.GetProperty("configuration").GetProperty("playRtdnConfigured").GetBoolean());
        Assert.Contains("play-rtdn-missing", AlertIds(body));
        Assert.DoesNotContain("play-not-configured", AlertIds(body));
    }

    [Fact]
    public async Task RequiredSubscriptionsWithoutPlayBillingAreCritical()
    {
        await MarkAllMigrationsAppliedAsync();
        await SetAsync((SettingKeys.SubscriptionsEnforced, "true"));

        var body = await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"));

        Assert.True(body.GetProperty("configuration").GetProperty("subscriptionsEnforced").GetBoolean());
        var alert = body.GetProperty("alerts").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "play-not-configured");
        Assert.Equal("critical", alert.GetProperty("severity").GetString());
        Assert.Contains("play-billing", body.GetProperty("configuration").GetProperty("missing").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task AlertsAreOrderedBySeverity_AndEveryLinkIsAnAdminRoute()
    {
        await LogAsync(Start.AddHours(-1), 4, "Trazo.X");

        var alerts = (await ReadAsync(await (await SuperadminAsync()).GetAsync("/api/v1/admin/overview"))).GetProperty("alerts").EnumerateArray().ToList();

        Assert.NotEmpty(alerts);
        var rank = alerts.Select(a => a.GetProperty("severity").GetString() switch { "critical" => 0, "warning" => 1, _ => 2 }).ToList();
        Assert.Equal(rank.OrderBy(x => x), rank);
        Assert.All(alerts, a =>
        {
            Assert.StartsWith("/", a.GetProperty("link").GetString());
            Assert.False(string.IsNullOrWhiteSpace(a.GetProperty("title").GetString()));
        });
    }

    // ------------------------------------------------------------------ cache

    [Fact]
    public async Task TheResultIsCachedBrieflyAndRefreshIsRateLimited()
    {
        var client = await SuperadminAsync();
        var first = await ReadAsync(await client.GetAsync("/api/v1/admin/overview"));
        var firstUsers = first.GetProperty("summary").GetProperty("usersTotal").GetInt32();
        await AddUserAsync("late@trazo.test");

        _clock.Now = Start.AddSeconds(5);
        var cached = await ReadAsync(await client.GetAsync("/api/v1/admin/overview"));
        Assert.Equal(first.GetProperty("generatedAt").GetDateTimeOffset(), cached.GetProperty("generatedAt").GetDateTimeOffset());
        Assert.Equal(firstUsers, cached.GetProperty("summary").GetProperty("usersTotal").GetInt32());

        // "Refresh" right after a build still serves the cache (a stuck button cannot hammer the database)...
        var tooSoon = await ReadAsync(await client.GetAsync("/api/v1/admin/overview?refresh=true"));
        Assert.Equal(firstUsers, tooSoon.GetProperty("summary").GetProperty("usersTotal").GetInt32());

        // ...but after the minimum age it rebuilds.
        _clock.Now = Start.AddSeconds(20);
        var refreshed = await ReadAsync(await client.GetAsync("/api/v1/admin/overview?refresh=true"));
        Assert.Equal(firstUsers + 1, refreshed.GetProperty("summary").GetProperty("usersTotal").GetInt32());
        Assert.Equal(Start.AddSeconds(20), refreshed.GetProperty("generatedAt").GetDateTimeOffset());
        Assert.Equal((int)AdminOverviewService.CacheTtl.TotalSeconds, refreshed.GetProperty("cacheSeconds").GetInt32());

        // Without refresh the cache expires by itself.
        await AddUserAsync("later@trazo.test");
        _clock.Now = Start.AddSeconds(20).Add(AdminOverviewService.CacheTtl).AddSeconds(1);
        var expired = await ReadAsync(await client.GetAsync("/api/v1/admin/overview"));
        Assert.Equal(firstUsers + 2, expired.GetProperty("summary").GetProperty("usersTotal").GetInt32());
    }

    // ------------------------------------------------------------------ alert rules

    private static OverviewHealth Healthy(long latency = 5, params string[] pending) =>
        new(true, latency, "1.0.0", null, "Production", 3 - pending.Length, 3, pending);

    private static OverviewLogs LogsWith(int errors, int previous) =>
        new(errors, 0, previous, [], "/logs?levels=error&range=24h", "/logs?levels=warning&range=24h");

    private static OverviewConfiguration Config(bool enforced = false, bool play = false, bool rtdn = false, OverviewSmtpSend? send = null) =>
        new(true, send, true, play, rtdn, enforced, []);

    private static OverviewSummary Catalog(int usable = 10, int unusable = 0, int noImages = 0) =>
        new(1, 0, 0, 0, 0, [], false, usable + unusable, noImages, unusable, usable);

    [Theory]
    [InlineData(4, 0, false)]   // below the threshold
    [InlineData(5, 0, true)]    // from nothing to a burst
    [InlineData(10, 6, false)]  // growing, but not doubled
    [InlineData(12, 6, true)]   // doubled
    public void ErrorsRisingNeedsABurstAtLeastTwiceThePreviousDay(int errors, int previous, bool rising)
    {
        var ids = AdminOverviewService.BuildAlerts(Healthy(), LogsWith(errors, previous), Config(), Catalog())
            .Select(a => a.Id).ToList();

        Assert.Equal(rising, ids.Contains("errors-rising"));
        Assert.Equal(!rising && errors > 0, ids.Contains("errors-present"));
    }

    [Fact]
    public void AHealthySystemHasNoAlerts()
    {
        Assert.Empty(AdminOverviewService.BuildAlerts(Healthy(), LogsWith(0, 0), Config(), Catalog()));
    }

    [Fact]
    public void SlowDatabaseAndEmptyCatalogAreReported()
    {
        var alerts = AdminOverviewService.BuildAlerts(Healthy(latency: AdminOverviewService.SlowDatabaseMs), LogsWith(0, 0), Config(), Catalog(usable: 0, unusable: 2));

        Assert.Equal(["catalog-empty", "database-slow"], alerts.Select(a => a.Id));
        Assert.Equal(["critical", "warning"], alerts.Select(a => a.Severity));
    }

    [Fact]
    public void InformationalAlertsComeLast()
    {
        var alerts = AdminOverviewService.BuildAlerts(Healthy(), LogsWith(1, 0), Config(play: true), Catalog(unusable: 1, noImages: 2));

        Assert.Equal(["warning", "info", "info", "info"], alerts.Select(a => a.Severity));
        Assert.Equal("play-rtdn-missing", alerts[0].Id);
    }

    private sealed class FailingTransport : ISmtpTransport
    {
        public Exception? Failure { get; set; }
        public Task SendAsync(EmailConfig config, string recipient, string subject, string body, CancellationToken ct) =>
            Failure is null ? Task.CompletedTask : Task.FromException(Failure);
    }
}
