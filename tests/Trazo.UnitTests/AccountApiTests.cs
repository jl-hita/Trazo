using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Trazo.Api.Endpoints;
using Trazo.Api.Security;
using Trazo.Domain.Billing;
using Trazo.Domain.Identity;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Accounts;
using Trazo.Infrastructure.Persistence;
using Trazo.UnitTests.Planning;
using Xunit;

namespace Trazo.UnitTests;

public sealed class RecordingEmailSender : IEmailSender
{
    public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();
    public Task SendAsync(string recipient, string subject, string body, CancellationToken cancellationToken)
    {
        Sent.Enqueue((recipient, subject, body));
        return Task.CompletedTask;
    }
}

public sealed class FakeGoogleValidator : IGoogleIdTokenValidator
{
    public GoogleIdentity? Result { get; set; }
    public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct) => Task.FromResult(Result);
}

/// <summary>Account API: deletion, re-authentication, web flow, export, time zone, idempotent logging and catalog options.</summary>
public sealed class AccountApiTests : IDisposable
{
    private const string Password = "CorrectHorse1Battery";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero); // a Monday
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly RecordingEmailSender _mail = new();
    private readonly CapturingLoggerProvider _logs = new();
    private readonly FakeGoogleValidator _google = new();
    private readonly WebApplicationFactory<Program> _factory;

    public AccountApiTests()
    {
        using (var db = _fixture.NewContext())
        {
            ExerciseTestSchema.Create(db);
            db.ExerciseCatalog.AddRange(PlanTestCatalog.Items());
            db.SaveChanges();
        }
        PlanTestSchema.Create(_fixture);
        CreateIdentityTables();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            builder.UseSetting("Account:PublicBaseUrl", "https://trazo.example");
            builder.UseSetting("Email:ActionBaseUrl", "https://trazo.example");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString).ReplaceService<IModelCustomizer, PlanSqliteCustomizer>());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                services.AddLogging(l => l.AddProvider(_logs).SetMinimumLevel(LogLevel.Trace));
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_mail);
                services.RemoveAll<IGoogleIdTokenValidator>();
                services.AddSingleton<IGoogleIdTokenValidator>(_google);
            });
        });
    }

    private void CreateIdentityTables()
    {
        var options = new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(_fixture.ConnectionString)
            .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>().Options;
        using var db = new TrazoDbContext(options);
        var tables = new[] { "email_action_tokens", "auth_challenges", "auth_recovery_codes" };
        var script = db.GetService<IRelationalDatabaseCreator>().GenerateCreateScript();
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (tables.Any(t => statement.StartsWith($"CREATE TABLE \"{t}\"", StringComparison.Ordinal) || statement.Contains($" ON \"{t}\" ", StringComparison.Ordinal))
                && statement.StartsWith("CREATE", StringComparison.Ordinal))
                db.Database.ExecuteSqlRaw(statement);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------ helpers

    private async Task<TrazoUser> AddUserAsync(string email, string role = "user", bool google = false, bool twoFactor = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email.Split('@')[0],
            Role = role, EmailVerifiedAt = Now, CreatedAt = Now, UpdatedAt = Now, TwoFactorEnabled = twoFactor
        };
        if (google) user.GoogleSubject = "google-" + user.Id;
        else user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, Password);
        if (twoFactor)
        {
            user.ProtectedAuthenticatorKey = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("TRAZO.Authenticator.v1").Protect(TotpService.GenerateSecret());
            db.AuthRecoveryCodes.Add(new AuthRecoveryCode { Id = Guid.NewGuid(), UserId = user.Id, CodeHash = AuthTokenService.HashToken(TotpService.NormalizeRecoveryCode("AAAA-BBBB")), CreatedAt = Now });
        }
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = Guid.NewGuid().ToString("N"), CreatedAt = Now, ExpiresAt = Now.AddDays(30) });
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(), UserId = user.Id, ProductId = "trazo_premium", PurchaseTokenHash = Guid.NewGuid().ToString("N"),
            ProtectedPurchaseToken = "secret-token", Status = SubscriptionStatus.Active, AutoRenewing = true,
            StartedAt = Now.AddDays(-1), ExpiresAt = Now.AddDays(30), CreatedAt = Now, UpdatedAt = Now
        });
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>())!;

    private static object Profile(object? timeZone = null) => new
    {
        goal = "Hypertrophy", experience = "Intermediate", totalWeeks = 12,
        trainingDays = new[] { "Monday", "Tuesday", "Thursday", "Friday" }, maxSessionMinutes = 75,
        availableEquipment = new[] { "barbell", "bench", "squat-rack", "dumbbell", "cable", "machine", "pull-up-bar" },
        excludedExerciseIds = Array.Empty<string>(), limitations = Array.Empty<string>(), timeZone
    };

    private async Task<JsonElement> StartPlanAndLogAsync(HttpClient client)
    {
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        var week = await JsonAsync(await client.GetAsync("/api/v1/plans/current/week"));
        var session = week.GetProperty("sessions")[0];
        var response = await client.PostAsJsonAsync($"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log", LogBody(session));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return session;
    }

    private static object LogBody(JsonElement session) => new
    {
        outcome = "Completed", recovery = "Good", sessionRpe = 7, painReported = true, notes = "me dolía la rodilla",
        sets = session.GetProperty("exercises").EnumerateArray().SelectMany(e => Enumerable.Range(1, e.GetProperty("sets").GetInt32()).Select(n => new
        { exerciseId = e.GetProperty("exerciseId").GetString(), setNumber = n, weightKg = 60m, repetitions = 8, rir = 2, completed = true })).ToArray()
    };

    [Fact]
    public async Task DeletingAUserRemovesTheirReviewConversationsAndDeletingAnAdministratorOnlyDropsTheReference()
    {
        var ana = await AddUserAsync("ana@example.com");
        var eve = await AddUserAsync("eve@example.com");
        var bob = await AddUserAsync("bob@example.com", role: "admin");
        var anaClient = ClientFor(ana);
        await StartPlanAndLogAsync(anaClient);
        await StartPlanAndLogAsync(ClientFor(eve));
        Guid anaRequest, eveRequest;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var anaPlan = await db.TrainingPlans.Where(p => p.UserId == ana.Id).Select(p => p.Id).FirstAsync();
            var evePlan = await db.TrainingPlans.Where(p => p.UserId == eve.Id).Select(p => p.Id).FirstAsync();
            anaRequest = Guid.NewGuid();
            eveRequest = Guid.NewGuid();
            db.PlanReviewRequests.AddRange(
                new PlanReviewRequest { Id = anaRequest, UserId = ana.Id, PlanId = anaPlan, Status = PlanReviewStatus.InProgress, AssignedToUserId = bob.Id, CreatedAt = Now, UpdatedAt = Now },
                new PlanReviewRequest { Id = eveRequest, UserId = eve.Id, PlanId = evePlan, Status = PlanReviewStatus.InProgress, AssignedToUserId = bob.Id, AssignedAt = Now, CreatedAt = Now, UpdatedAt = Now });
            db.PlanReviewMessages.AddRange(
                new PlanReviewMessage { Id = Guid.NewGuid(), RequestId = anaRequest, Author = PlanReviewAuthors.User, Body = "my knee hurts", CreatedAt = Now },
                new PlanReviewMessage { Id = Guid.NewGuid(), RequestId = anaRequest, Author = PlanReviewAuthors.Staff, AuthorUserId = bob.Id, Body = "reply", CreatedAt = Now },
                new PlanReviewMessage { Id = Guid.NewGuid(), RequestId = eveRequest, Author = PlanReviewAuthors.Staff, AuthorUserId = bob.Id, Body = "hello", CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(anaClient, new { password = Password })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            Assert.False(await db.PlanReviewRequests.AnyAsync(r => r.UserId == ana.Id));
            Assert.False(await db.PlanReviewMessages.AnyAsync(m => m.RequestId == anaRequest));
            Assert.True(await db.PlanReviewRequests.AnyAsync(r => r.Id == eveRequest));
        }

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(ClientFor(bob), new { password = Password })).StatusCode);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var request = await db.PlanReviewRequests.SingleAsync(r => r.Id == eveRequest);
            Assert.Equal(PlanReviewStatus.Pending, request.Status);
            Assert.Null(request.AssignedToUserId);
            Assert.Null((await db.PlanReviewMessages.SingleAsync(m => m.RequestId == eveRequest)).AuthorUserId);
        }
    }

    /// <summary>Reusable learning history of a user: what a new plan reads to start from (observations) and what was decided with it.</summary>
    private async Task SeedLearningAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var plan = await db.TrainingPlans.FirstAsync(p => p.UserId == userId);
        db.MuscleWeekObservations.Add(new MuscleWeekObservation
        {
            Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Period = 1, MesocycleSequence = 1, Muscle = "Chest", Quality = "valid",
            PrescribedSets = 12, PerformedSets = 12, ProcessedThrough = Now, UpdatedAt = Now
        });
        db.LongitudinalDecisions.Add(new LongitudinalDecision
        {
            Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, MesocycleSequence = 1, Muscle = "Chest", Action = "hold", BaselineSets = 12,
            PreviousSets = 12, ProposedSets = 13, SignalsJson = "{}", Reason = "test",
            PolicyVersion = "longitudinal-2.0", CreatedAt = Now
        });
        await db.SaveChangesAsync();
    }

    private async Task<Dictionary<string, int>> RowsOfAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        return new()
        {
            ["users"] = await db.Users.CountAsync(x => x.Id == userId),
            ["refresh"] = await db.RefreshTokens.CountAsync(x => x.UserId == userId),
            ["subs"] = await db.Subscriptions.CountAsync(x => x.UserId == userId),
            ["recovery"] = await db.AuthRecoveryCodes.CountAsync(x => x.UserId == userId),
            ["tokens"] = await db.EmailActionTokens.CountAsync(x => x.UserId == userId),
            ["plans"] = await db.TrainingPlans.CountAsync(x => x.UserId == userId),
            ["logs"] = await db.WorkoutLogs.CountAsync(x => x.UserId == userId),
            ["observations"] = await db.MuscleWeekObservations.CountAsync(x => x.UserId == userId),
            ["decisions"] = await db.LongitudinalDecisions.CountAsync(x => x.UserId == userId),
            ["phases"] = await db.TrainingPlanPhases.CountAsync(p => db.TrainingPlans.Any(t => t.Id == p.PlanId && t.UserId == userId)),
            ["sessions"] = await db.PlannedSessions.CountAsync(p => db.TrainingPlans.Any(t => t.Id == p.PlanId && t.UserId == userId))
        };
    }

    private async Task<int> TotalRowsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        return await db.Users.CountAsync() + await db.WorkoutSetLogs.CountAsync() + await db.WorkoutLogs.CountAsync() + await db.PlannedSessions.CountAsync()
            + await db.PlanAdaptations.CountAsync() + await db.TrainingPlans.CountAsync();
    }

    private static async Task<string?> WaitForMailAsync(RecordingEmailSender mail, int expected)
    {
        for (var i = 0; i < 100 && mail.Sent.Count < expected; i++) await Task.Delay(50);
        return mail.Sent.Count >= expected ? mail.Sent.ToArray()[expected - 1].Body : null;
    }

    private static string TokenFrom(string body)
    {
        var start = body.IndexOf("token=", StringComparison.Ordinal) + 6;
        var end = body.IndexOfAny(['\n', '\r', ' '], start);
        return Uri.UnescapeDataString(body[start..end]);
    }

    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), System.Text.Encoding.UTF8, "application/json");

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, object body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/v1/account") { Content = Json(body) });

    // ------------------------------------------------------------------ model coverage

    [Fact]
    public void EveryTableOfTheModelIsEitherDeletedWithTheAccountOrKnownNotToBePersonal()
    {
        using var scope = _factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Model;
        var untreated = model.GetEntityTypes().Select(e => e.ClrType)
            .Where(t => !AccountData.UserOwned.Contains(t) && !AccountData.NotPersonal.Contains(t)).Select(t => t.Name).ToList();
        Assert.True(untreated.Count == 0,
            "These tables are not handled by account deletion (add them to AccountService and AccountData): " + string.Join(", ", untreated));

        // Anything that points at a user must be on the deleted list, whatever it is called.
        var userTable = model.FindEntityType(typeof(TrazoUser))!;
        var pointingAtUsers = model.GetEntityTypes().Where(e => e.GetForeignKeys().Any(k => k.PrincipalEntityType == userTable)
            || e.FindProperty("UserId") is not null).Select(e => e.ClrType);
        Assert.All(pointingAtUsers, t => Assert.Contains(t, AccountData.UserOwned));
    }

    // ------------------------------------------------------------------ deletion

    [Fact]
    public async Task TheSessionUserSaysWhetherGoogleIsAlreadyLinked()
    {
        var withGoogle = await JsonAsync(await ClientFor(await AddUserAsync("g@example.com", google: true)).GetAsync("/api/v1/auth/me"));
        var withoutGoogle = await JsonAsync(await ClientFor(await AddUserAsync("p@example.com")).GetAsync("/api/v1/auth/me"));
        Assert.True(withGoogle.GetProperty("googleLinked").GetBoolean());
        Assert.False(withoutGoogle.GetProperty("googleLinked").GetBoolean());
    }

    [Fact]
    public async Task DeletingTheAccountRemovesEverythingOfTheUserAndNothingOfOthers()
    {
        var ana = await AddUserAsync("ana@example.com");
        var eve = await AddUserAsync("eve@example.com");
        var anaClient = ClientFor(ana);
        await StartPlanAndLogAsync(anaClient);
        await StartPlanAndLogAsync(ClientFor(eve));
        await SeedLearningAsync(ana.Id);
        await SeedLearningAsync(eve.Id);
        var before = await RowsOfAsync(ana.Id);
        Assert.All(new[] { "users", "refresh", "subs", "plans", "logs", "phases", "sessions", "observations", "decisions" }, k => Assert.True(before[k] > 0, k));
        var eveBefore = await RowsOfAsync(eve.Id);
        using (var seed = _factory.Services.CreateScope())
        {
            var seedDb = seed.ServiceProvider.GetRequiredService<TrazoDbContext>();
            seedDb.WaitlistEntries.AddRange(
                new Trazo.Domain.Waitlist.WaitlistEntry { Id = Guid.NewGuid(), Email = "ana@example.com", Language = "es", ConsentAt = Now, CreatedAt = Now },
                new Trazo.Domain.Waitlist.WaitlistEntry { Id = Guid.NewGuid(), Email = "other@example.com", Language = "es", ConsentAt = Now, CreatedAt = Now });
            await seedDb.SaveChangesAsync();
        }

        var response = await DeleteAsync(anaClient, new { password = Password });

        var body = await JsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("deleted").GetBoolean());
        Assert.True(body.GetProperty("playSubscriptionNotCancelled").GetBoolean());
        Assert.All((await RowsOfAsync(ana.Id)).Values, count => Assert.Equal(0, count));
        Assert.Equal(eveBefore, await RowsOfAsync(eve.Id));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        // The waitlist entry with the account's email goes too; someone else's stays.
        Assert.Equal(["other@example.com"], await db.WaitlistEntries.Select(w => w.Email).ToListAsync());
        Assert.Equal(await db.WorkoutLogs.CountAsync(), await db.WorkoutLogs.CountAsync(l => l.UserId == eve.Id));
        Assert.Equal(await db.WorkoutSetLogs.CountAsync(), await db.WorkoutSetLogs.CountAsync(s => db.WorkoutLogs.Any(l => l.Id == s.WorkoutLogId && l.UserId == eve.Id)));
        // The old token no longer works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anaClient.GetAsync("/api/v1/account")).StatusCode);
    }

    // ------------------------------------------------------------------ anonymous engine research

    private async Task<(Guid PlanId, string Goal)> SeedDirtyLearningAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var plan = await db.TrainingPlans.FirstAsync(p => p.UserId == userId);
        db.MuscleWeekObservations.AddRange(
            new MuscleWeekObservation
            {
                Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Period = 1, MesocycleSequence = 1, Muscle = "Chest", Quality = "partial",
                Reason = "pain", PrescribedSets = 12, PerformedSets = 9, Exposures = 2, PainSessions = 1, ProcessedThrough = Now, UpdatedAt = Now
            },
            new MuscleWeekObservation
            {
                Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, Period = 2, MesocycleSequence = 1, Muscle = "Chest", Quality = "excluded",
                Reason = "me duele la rodilla izquierda desde el accidente", PrescribedSets = 12, ProcessedThrough = Now, UpdatedAt = Now
            });
        db.LongitudinalDecisions.Add(new LongitudinalDecision
        {
            Id = Guid.NewGuid(), UserId = userId, PlanId = plan.Id, MesocycleSequence = 1, Muscle = "Chest", Action = "decrease", BaselineSets = 12,
            PreviousSets = 12, ProposedSets = 10, AnalyzedFromPeriod = 1, AnalyzedToPeriod = 2, SignalsJson = "{\"secret\":\"ana@example.com\"}",
            Reason = "Texto libre con datos personales de Ana", PolicyVersion = "longitudinal-2.0", CreatedAt = Now
        });
        await db.SaveChangesAsync();
        return (plan.Id, plan.Goal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletingTheAccountLeavesAnAnonymousRecordThatPointsAtNobody(bool consent)
    {
        var ana = await AddUserAsync("ana@example.com");
        await StartPlanAndLogAsync(ClientFor(ana));
        var (planId, _) = await SeedDirtyLearningAsync(ana.Id);
        using (var scope = _factory.Services.CreateScope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IAccountService>().SetResearchConsentAsync(ana.Id, consent, default));

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(ClientFor(ana), new { password = Password })).StatusCode);

        using var check = _factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var observations = await db.EngineResearchObservations.AsNoTracking().ToListAsync();
        var decisions = await db.EngineResearchDecisions.AsNoTracking().ToListAsync();
        Assert.Equal(2, observations.Count);
        Assert.Single(decisions);
        // Only the closed reason codes survive; free text is dropped.
        Assert.Equal(new[] { null, "pain" }, observations.Select(o => o.Reason).OrderBy(r => r, StringComparer.Ordinal).ToArray());
        Assert.Equal("decrease", decisions[0].Action);
        // Grouping per plan only with consent, and with a random id.
        if (consent)
        {
            Assert.All(observations, o => Assert.NotNull(o.SampleGroupId));
            Assert.Single(observations.Select(o => o.SampleGroupId).Concat(decisions.Select(d => d.SampleGroupId)).Distinct());
        }
        else Assert.All(observations.Select(o => o.SampleGroupId).Concat(decisions.Select(d => d.SampleGroupId)), g => Assert.Null(g));
        // Nothing in any column leads back to the person, the plan or the original rows.
        var dump = System.Text.Json.JsonSerializer.Serialize(new { observations, decisions });
        Assert.DoesNotContain(ana.Id.ToString(), dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(planId.ToString(), dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ana", dump.Replace("analyzed", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rodilla", dump, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", dump, StringComparison.OrdinalIgnoreCase);
        Assert.All(observations, o => { Assert.False(string.IsNullOrEmpty(o.PlanGoal)); Assert.Equal(0, o.PlanTotalPeriodsBand % 4); });
    }

    [Fact]
    public async Task AnAccountWithoutLearningDataLeavesNoResearchRows()
    {
        var bob = await AddUserAsync("bob@example.com");

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(ClientFor(bob), new { password = Password })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Empty(await db.EngineResearchObservations.ToListAsync());
        Assert.Empty(await db.EngineResearchDecisions.ToListAsync());
    }

    [Fact]
    public void TheResearchTablesCannotHoldAnythingThatIdentifiesAPersonOrAPlan()
    {
        using var scope = _factory.Services.CreateScope();
        var model = scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Model;
        var forbiddenNames = new[] { "UserId", "PlanId", "Email", "Name", "Note", "Text", "Comment", "Message", "Json", "Limitation", "Equipment", "Exercise", "Seed", "TimeZone", "Day", "Date", "Token", "Ip" };
        foreach (var type in new[] { typeof(EngineResearchObservation), typeof(EngineResearchDecision) })
        {
            var entity = model.FindEntityType(type)!;
            Assert.Empty(entity.GetForeignKeys());
            Assert.True(AccountData.NotPersonal.Contains(type), type.Name + " must be declared NotPersonal.");
            foreach (var property in entity.GetProperties())
            {
                var name = property.Name;
                var where = $"{type.Name}.{name}";
                Assert.False(forbiddenNames.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase) && name != "PlanSessionsPerPeriod" && name != "PlanExperience" && name != "PlanTotalPeriodsBand"),
                    where + " looks like an identifier or free text.");
                Assert.False(name.EndsWith("At", StringComparison.Ordinal), where + " looks like an exact timestamp.");
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                Assert.False(clr == typeof(DateTime) || clr == typeof(DateTimeOffset) || clr == typeof(DateOnly) || clr == typeof(TimeOnly), where + " is a date.");
                if (clr == typeof(Guid)) Assert.True(name is "Id" or "SampleGroupId", where + ": the only ids allowed are the random row id and the random group id.");
                if (clr == typeof(string)) Assert.True(property.GetMaxLength() is > 0 and <= 40, where + " is a string without a short length limit (free text).");
                Assert.True(clr == typeof(Guid) || clr == typeof(string) || clr == typeof(int) || clr == typeof(bool) || clr == typeof(decimal), where + " has an unexpected type " + clr.Name);
            }
        }
        // Nothing may point at the research tables either.
        Assert.All(model.GetEntityTypes(), e => Assert.DoesNotContain(e.GetForeignKeys(),
            k => k.PrincipalEntityType.ClrType == typeof(EngineResearchObservation) || k.PrincipalEntityType.ClrType == typeof(EngineResearchDecision)));
    }

    [Fact]
    public void TheSamplerGeneralisesThePlanAndDropsWhatIsNotOnTheAllowList()
    {
        var plan = new TrainingPlan
        {
            Id = Guid.NewGuid(), UserId = Guid.NewGuid(), Goal = "Hypertrophy", Experience = "Intermediate", Split = "UpperLower", TotalWeeks = 9,
            SessionsPerPeriod = 4, Readiness = "Returning", EngineVersion = "engine-4.2", Limitations = ["rodilla"], Seed = 1234,
            TrainingDays = ["Mon"], AvailableEquipment = ["bar"]
        };
        var odd = new TrainingPlan { Id = Guid.NewGuid(), UserId = plan.UserId, Goal = "quiero mejorar mi rodilla", Experience = "x", Split = "y", EngineVersion = "a b c", Readiness = "algo" };

        var context = EngineResearchSampler.ContextOf(plan);
        var dirty = EngineResearchSampler.ContextOf(odd);

        Assert.Equal(("Hypertrophy", "Intermediate", "UpperLower", "Returning", "engine-4.2", 4, 12), (context.Goal, context.Experience, context.Split, context.Readiness, context.EngineVersion, context.SessionsPerPeriod, context.TotalPeriodsBand));
        Assert.Null(dirty.Goal);
        Assert.Null(dirty.Readiness);
        Assert.Null(dirty.EngineVersion);
        // A row whose muscle or action is not a closed token is not exported at all.
        var result = EngineResearchSampler.Build([plan],
            [new MuscleWeekObservation { Id = Guid.NewGuid(), UserId = plan.UserId, PlanId = plan.Id, Muscle = "mi pierna rota", Quality = "valid", ProcessedThrough = Now, UpdatedAt = Now }],
            [new LongitudinalDecision { Id = Guid.NewGuid(), UserId = plan.UserId, PlanId = plan.Id, Muscle = "Chest", Action = "texto libre", SignalsJson = "{}", Reason = "r", PolicyVersion = "p-1", CreatedAt = Now }],
            grouped: false);
        Assert.Empty(result.Observations);
        Assert.Empty(result.Decisions);
    }

    [Fact]
    public async Task ADeletionThatFailsHalfWayLeavesNoOrphanResearchRows()
    {
        var ana = await AddUserAsync("ana@example.com");
        await StartPlanAndLogAsync(ClientFor(ana));
        await SeedDirtyLearningAsync(ana.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            // The last step of the deletion (the user row) fails, after the research rows were already written in the same transaction.
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_user_delete BEFORE DELETE ON users BEGIN SELECT RAISE(ABORT, 'boom'); END;");
        }

        using (var scope = _factory.Services.CreateScope())
            await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<IAccountService>().DeleteAsync(ana.Id, default));

        using var check = _factory.Services.CreateScope();
        var after = check.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Equal(0, await after.EngineResearchObservations.CountAsync());
        Assert.Equal(0, await after.EngineResearchDecisions.CountAsync());
        Assert.Equal(1, await after.Users.CountAsync(u => u.Id == ana.Id));
        Assert.True(await after.MuscleWeekObservations.AnyAsync(o => o.UserId == ana.Id));
    }

    [Fact]
    public async Task ResearchConsentIsOptionalOffByDefaultAndRevocable()
    {
        var ana = await AddUserAsync("ana@example.com");
        var client = ClientFor(ana);
        Assert.False((await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("researchConsent").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/account/research-consent", new { consent = true })).StatusCode);
        Assert.True((await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("researchConsent").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/account/research-consent", new { consent = false })).StatusCode);
        Assert.False((await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("researchConsent").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).PutAsJsonAsync("/api/v1/account/research-consent", new { consent = true })).StatusCode);
    }

    [Fact]
    public async Task HealthConsentIsOffByDefaultAndWithdrawingItErasesTheHealthDataOnlyOfThatUser()
    {
        var ana = await AddUserAsync("ana@example.com");
        var eve = await AddUserAsync("eve@example.com");
        var client = ClientFor(ana);
        Assert.False((await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("healthConsent").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/account/health-consent", new { consent = true })).StatusCode);
        Assert.True((await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("healthConsent").GetBoolean());
        await StartPlanAndLogAsync(client);
        await StartPlanAndLogAsync(ClientFor(eve));
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/plans/current/limitations", new { limitations = new[] { "Knee" } })).StatusCode);
        await SeedLearningAsync(ana.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var log = await db.WorkoutLogs.Include(l => l.Sets).FirstAsync(l => l.UserId == ana.Id);
            log.NonCompletionReason = "Illness";
            log.ExerciseFeedbackJson = """[{"exerciseId":"x","stimulus":"adequate","discomfort":"strong"}]""";
            log.Sets[0].SkippedReason = "pain";
            log.Sets[0].PainReported = true;
            var observation = await db.MuscleWeekObservations.FirstAsync(o => o.UserId == ana.Id);
            observation.PainSessions = 2;
            observation.DiscomfortAnswers = 3;
            observation.Reason = "pain";
            await db.SaveChangesAsync();
        }
        var logsBefore = (await RowsOfAsync(ana.Id))["logs"];

        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/account/health-consent", new { consent = false })).StatusCode);

        Assert.False((await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("healthConsent").GetBoolean());
        using var after = _factory.Services.CreateScope();
        var data = after.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.NotNull((await data.Users.SingleAsync(u => u.Id == ana.Id)).HealthConsentWithdrawnAt);
        var plans = await data.TrainingPlans.Where(p => p.UserId == ana.Id).ToListAsync();
        Assert.All(plans, p => Assert.Empty(p.Limitations));
        var planIds = plans.Select(p => p.Id).ToList();
        foreach (var trace in await data.TrainingMesocycles.Where(m => planIds.Contains(m.PlanId)).Select(m => m.GenerationSnapshotJson).ToListAsync())
            Assert.DoesNotContain("Knee", trace ?? "");
        var logs = await data.WorkoutLogs.Include(l => l.Sets).Where(l => l.UserId == ana.Id).ToListAsync();
        Assert.Equal(logsBefore, logs.Count);
        Assert.All(logs, l =>
        {
            Assert.False(l.PainReported);
            Assert.NotEqual("Illness", l.NonCompletionReason);
            Assert.DoesNotContain("discomfort", l.ExerciseFeedbackJson ?? "");
            Assert.All(l.Sets, s => { Assert.False(s.PainReported); Assert.NotEqual("pain", s.SkippedReason); Assert.True(s.Repetitions > 0); });
        });
        Assert.Contains(logs, l => l.ExerciseFeedbackJson?.Contains("adequate") == true);
        var explanations = await data.PlanAdaptations.Where(a => planIds.Contains(a.PlanId)).Select(a => a.Explanation).ToListAsync();
        Assert.All(explanations, x => { Assert.DoesNotContain("dolor", x); Assert.DoesNotContain("lesion", x); Assert.DoesNotContain("Knee", x); });
        Assert.All(await data.MuscleWeekObservations.Where(o => o.UserId == ana.Id).ToListAsync(),
            o => { Assert.Equal(0, o.PainSessions); Assert.Equal(0, o.DiscomfortAnswers); Assert.NotEqual("pain", o.Reason); });
        // Somebody else's data is untouched.
        Assert.True(await data.WorkoutLogs.AnyAsync(l => l.UserId == eve.Id && l.PainReported));
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).PutAsJsonAsync("/api/v1/account/health-consent", new { consent = true })).StatusCode);
    }

    [Fact]
    public async Task InfoWarnsThatAnActivePlaySubscriptionIsNotCancelled()
    {
        var info = await JsonAsync(await ClientFor(await AddUserAsync("ana@example.com")).GetAsync("/api/v1/account"));

        var deletion = info.GetProperty("deletion");
        Assert.True(deletion.GetProperty("requiresPassword").GetBoolean());
        Assert.True(deletion.GetProperty("playSubscriptionNotCancelled").GetBoolean());
        Assert.Contains("NO se cancela", deletion.GetProperty("warning").GetString());
        Assert.Contains("play.google.com", deletion.GetProperty("manageSubscriptionUrl").GetString());
    }

    [Fact]
    public async Task DeletionNeedsAuthenticationAndTheRightPassword()
    {
        var ana = await AddUserAsync("ana@example.com");
        Assert.Equal(HttpStatusCode.Unauthorized, (await DeleteAsync(ClientFor(null), new { password = Password })).StatusCode);

        var wrong = await DeleteAsync(ClientFor(ana), new { password = "wrong-Password-1" });
        var none = await DeleteAsync(ClientFor(ana), new { });

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, none.StatusCode);
        Assert.Equal(1, (await RowsOfAsync(ana.Id))["users"]);
    }

    [Fact]
    public async Task TheSameDeletionCanBeSentWithPostForClientsWithoutDeleteBodies()
    {
        var ana = await AddUserAsync("ana@example.com");

        var response = await ClientFor(ana).PostAsync("/api/v1/account/delete", Json(new { password = Password }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await RowsOfAsync(ana.Id))["users"]);
    }

    [Fact]
    public async Task AGoogleAccountNeedsARecentGoogleToken()
    {
        var ana = await AddUserAsync("ana@example.com", google: true);
        var client = ClientFor(ana);

        _google.Result = new GoogleIdentity("someone-else", Now);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync(client, new { idToken = "t" })).StatusCode);
        _google.Result = new GoogleIdentity(ana.GoogleSubject!, Now.AddMinutes(-10));
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync(client, new { idToken = "t" })).StatusCode);
        Assert.Equal(1, (await RowsOfAsync(ana.Id))["users"]);

        _google.Result = new GoogleIdentity(ana.GoogleSubject!, Now.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(client, new { idToken = "t" })).StatusCode);
        Assert.Equal(0, (await RowsOfAsync(ana.Id))["users"]);
    }

    [Fact]
    public async Task TwoFactorCodeIsRequiredInAddition()
    {
        var ana = await AddUserAsync("ana@example.com", twoFactor: true);
        var client = ClientFor(ana);

        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync(client, new { password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await DeleteAsync(client, new { password = Password, code = "000000" })).StatusCode);
        Assert.Equal(1, (await RowsOfAsync(ana.Id))["users"]);

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(client, new { password = Password, code = "AAAA-BBBB" })).StatusCode);
        Assert.Equal(0, (await RowsOfAsync(ana.Id))["users"]);
    }

    [Fact]
    public async Task TheLastSuperadminCannotBeDeleted_ButCanOnceAnotherExists()
    {
        var owner = await AddUserAsync("owner@example.com", "superadmin");

        var blocked = await DeleteAsync(ClientFor(owner), new { password = Password });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("last_superadmin", (await JsonAsync(blocked)).GetProperty("code").GetString());
        Assert.False((await JsonAsync(await ClientFor(owner).GetAsync("/api/v1/account"))).GetProperty("canDelete").GetBoolean());

        await AddUserAsync("second@example.com", "superadmin");
        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(ClientFor(owner), new { password = Password })).StatusCode);
    }

    // ------------------------------------------------------------------ admin

    [Fact]
    public async Task ASuperadminCanDeleteAUserOnlyByRepeatingTheirEmail()
    {
        var admin = await AddUserAsync("admin@example.com", "superadmin");
        var ana = await AddUserAsync("ana@example.com");
        var client = ClientFor(admin);
        Task<HttpResponseMessage> Delete(Guid id, object body) =>
            client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/users/{id}") { Content = Json(body) });

        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(ana.Id, new { confirmEmail = "other@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin.Id, new { confirmEmail = admin.Email })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(await AddUserAsync("bob@example.com")).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/users/{ana.Id}") { Content = Json(new { confirmEmail = ana.Email }) })).StatusCode);
        Assert.Equal(1, (await RowsOfAsync(ana.Id))["users"]);

        Assert.Equal(HttpStatusCode.OK, (await Delete(ana.Id, new { confirmEmail = "ANA@example.com" })).StatusCode);
        Assert.All((await RowsOfAsync(ana.Id)).Values, count => Assert.Equal(0, count));
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(ana.Id, new { confirmEmail = ana.Email })).StatusCode);
    }

    [Fact]
    public async Task AnAdminCannotDeleteTheOnlyOtherSuperadminByTheAdminRoute_WhenItIsTheLastOne()
    {
        // The acting superadmin is disabled-proof: deleting another superadmin is allowed only while one active superadmin remains.
        var admin = await AddUserAsync("admin@example.com", "superadmin");
        var other = await AddUserAsync("other@example.com", "superadmin");

        var response = await ClientFor(admin).SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/admin/users/{other.Id}")
        { Content = Json(new { confirmEmail = other.Email }) });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode); // admin remains active
    }

    // ------------------------------------------------------------------ web flow

    private static FormUrlEncodedContent Form(params (string, string)[] values) =>
        new(values.Select(v => new KeyValuePair<string, string>(v.Item1, v.Item2)));

    [Fact]
    public async Task ThePublicPageIsSelfContainedWithAStrictPolicy()
    {
        var response = await _factory.CreateClient().GetAsync("/account/delete");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("lang=\"es\"", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http://", html);
        Assert.DoesNotContain("https://", html);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.StartsWith("default-src 'none'", csp);
        Assert.Contains(AccountPages.CspStyleHash, csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    [Fact]
    public async Task RequestingADeletionLinkAnswersTheSameWhetherOrNotTheAccountExists()
    {
        await AddUserAsync("ana@example.com");
        var client = _factory.CreateClient();

        var known = await client.PostAsync("/api/v1/account/deletion-request", Form(("email", "ana@example.com")));
        var unknown = await client.PostAsync("/api/v1/account/deletion-request", Form(("email", "nobody@example.com")));
        var garbage = await client.PostAsync("/api/v1/account/deletion-request", Form(("email", "not an email")));
        var knownJson = await client.PostAsync("/api/v1/account/deletion-request", Json(new { email = "ana@example.com" }));
        var unknownJson = await client.PostAsync("/api/v1/account/deletion-request", Json(new { email = "nobody@example.com" }));

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.StatusCode, garbage.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(await known.Content.ReadAsStringAsync(), await garbage.Content.ReadAsStringAsync());
        Assert.Equal(knownJson.StatusCode, unknownJson.StatusCode);
        Assert.Equal(await knownJson.Content.ReadAsStringAsync(), await unknownJson.Content.ReadAsStringAsync());

        Assert.NotNull(await WaitForMailAsync(_mail, 2)); // one per request for the known address, none for the others
        await Task.Delay(300);
        Assert.Equal(2, _mail.Sent.Count);
        Assert.All(_mail.Sent, m => Assert.Equal("ana@example.com", m.To));
    }

    [Fact]
    public async Task TheLinkNeedsAnExplicitConfirmationAndWorksOnlyOnce()
    {
        var ana = await AddUserAsync("ana@example.com");
        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/account/deletion-request", Form(("email", "ana@example.com")));
        var body = (await WaitForMailAsync(_mail, 1))!;
        Assert.Contains("https://trazo.example/account/delete/confirm?token=", body);
        var token = TokenFrom(body);

        // Opening the link only shows the confirmation: nothing is deleted yet.
        var page = await client.GetAsync($"/account/delete/confirm?token={Uri.EscapeDataString(token)}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Eliminar definitivamente", html);
        Assert.DoesNotContain("ana@example.com", html); // masked
        Assert.Equal(1, (await RowsOfAsync(ana.Id))["users"]);

        var confirmed = await client.PostAsync("/api/v1/account/deletion-request/confirm", Form(("token", token)));
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Contains("Cuenta eliminada", await confirmed.Content.ReadAsStringAsync());
        Assert.All((await RowsOfAsync(ana.Id)).Values, count => Assert.Equal(0, count));

        var again = await client.PostAsync("/api/v1/account/deletion-request/confirm", Form(("token", token)));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task AnExpiredOrUnknownLinkDeletesNothing()
    {
        var ana = await AddUserAsync("ana@example.com");
        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/account/deletion-request", Form(("email", "ana@example.com")));
        var token = TokenFrom((await WaitForMailAsync(_mail, 1))!);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            foreach (var t in db.EmailActionTokens.Where(x => x.UserId == ana.Id)) t.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/account/delete/confirm?token={Uri.EscapeDataString(token)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/account/deletion-request/confirm", Form(("token", token)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/v1/account/deletion-request/confirm", Form(("token", "nope")))).StatusCode);
        Assert.Equal(1, (await RowsOfAsync(ana.Id))["users"]);
    }

    [Fact]
    public async Task TheLinkOfAnAccountWithTwoFactorAlsoNeedsTheCode()
    {
        var ana = await AddUserAsync("ana@example.com", twoFactor: true);
        var client = _factory.CreateClient();
        await client.PostAsync("/api/v1/account/deletion-request", Form(("email", "ana@example.com")));
        var token = TokenFrom((await WaitForMailAsync(_mail, 1))!);

        Assert.Contains("id=\"code\"", await client.GetStringAsync($"/account/delete/confirm?token={Uri.EscapeDataString(token)}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/v1/account/deletion-request/confirm", Form(("token", token), ("code", "123456")))).StatusCode);
        Assert.Equal(1, (await RowsOfAsync(ana.Id))["users"]);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/account/deletion-request/confirm", Form(("token", token), ("code", "AAAA-BBBB")))).StatusCode);
        Assert.Equal(0, (await RowsOfAsync(ana.Id))["users"]);
    }

    [Fact]
    public async Task DeletionLinksPerEmailAreLimited()
    {
        await AddUserAsync("ana@example.com");
        var client = _factory.CreateClient();

        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync("/api/v1/account/deletion-request", Form(("email", "ana@example.com")))).StatusCode);

        await WaitForMailAsync(_mail, DeletionRequestThrottle.MaximumPerWindow);
        await Task.Delay(300);
        Assert.Equal(DeletionRequestThrottle.MaximumPerWindow, _mail.Sent.Count);
    }

    // ------------------------------------------------------------------ export

    [Fact]
    public void EveryPersonalTableHasItsPlaceInTheExport()
    {
        Assert.Equal(
            AccountData.UserOwned.Select(t => t.Name).Order(), AccountData.Exported.Keys.Select(t => t.Name).Order());
        var parts = typeof(AccountExport).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.All(AccountData.Exported.Values.OfType<string>(), part => Assert.Contains(part, parts));
        // Only secrets are left out.
        Assert.Equal(["AuthChallenge", "AuthRecoveryCode", "EmailActionToken"],
            AccountData.Exported.Where(x => x.Value is null).Select(x => x.Key.Name).Order().ToArray());
    }

    [Fact]
    public async Task TheExportHoldsReviewsAnswersReferralsAndConsents_WithoutOtherPeopleOrTokens()
    {
        var ana = await AddUserAsync("ana@example.com");
        var bob = await AddUserAsync("bob@example.com", role: "admin");
        var friend = await AddUserAsync("friend@example.com");
        await StartPlanAndLogAsync(ClientFor(ana));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var plan = await db.TrainingPlans.Where(p => p.UserId == ana.Id).Select(p => p.Id).SingleAsync();
            var log = await db.WorkoutLogs.SingleAsync(l => l.UserId == ana.Id);
            log.ExerciseFeedbackJson = """[{"exerciseId":"squat","stimulus":"adequate"}]""";
            var request = Guid.NewGuid();
            db.PlanReviewRequests.Add(new PlanReviewRequest { Id = request, UserId = ana.Id, PlanId = plan, AssignedToUserId = bob.Id, CreatedAt = Now, UpdatedAt = Now });
            foreach (var (author, who, body) in new[] { (PlanReviewAuthors.User, (Guid?)null, "can you look?"), (PlanReviewAuthors.Staff, bob.Id, "looks fine"), (PlanReviewAuthors.System, bob.Id, "taken by bob") })
                db.PlanReviewMessages.Add(new PlanReviewMessage { Id = Guid.NewGuid(), RequestId = request, Author = author, AuthorUserId = who, Body = body, CreatedAt = Now });
            db.ReferralCodes.Add(new Trazo.Domain.Growth.ReferralCode { Id = Guid.NewGuid(), UserId = ana.Id, Code = "ANACODE2", CreatedAt = Now });
            db.ReferralRelationships.Add(new Trazo.Domain.Growth.ReferralRelationship
            {
                Id = Guid.NewGuid(), ReferrerUserId = ana.Id, ReferredUserId = friend.Id, Code = "ANACODE2", Source = "link", Status = "registered",
                AttributedAt = Now, UpdatedAt = Now
            });
            db.DeviceTokens.Add(new Trazo.Domain.Push.DeviceToken { Id = Guid.NewGuid(), UserId = ana.Id, Token = "secret-push-token", CreatedAt = Now, LastSeenAt = Now });
            var user = await db.Users.SingleAsync(u => u.Id == ana.Id);
            user.Language = "es";
            user.ResearchConsent = true;
            await db.SaveChangesAsync();
        }

        var text = await ClientFor(ana).GetStringAsync("/api/v1/account/export");
        var export = JsonDocument.Parse(text).RootElement;

        Assert.Equal("2", export.GetProperty("version").GetString());
        Assert.Equal("es", export.GetProperty("account").GetProperty("language").GetString());
        Assert.True(export.GetProperty("account").GetProperty("researchConsent").GetBoolean());
        var messages = Assert.Single(export.GetProperty("reviews").EnumerateArray()).GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("text").GetString()).ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains("looks fine", messages);
        Assert.Equal("squat", export.GetProperty("workoutLogs")[0].GetProperty("exerciseAnswers")[0].GetProperty("exerciseId").GetString());
        Assert.True(export.GetProperty("blocks").GetArrayLength() > 0);
        var referrals = export.GetProperty("referrals");
        Assert.Equal("ANACODE2", referrals.GetProperty("myCode").GetString());
        Assert.Equal(1, referrals.GetProperty("myInvitations").GetArrayLength());
        Assert.Equal(1, export.GetProperty("notificationDevices").GetArrayLength());
        foreach (var secret in new[] { "secret-push-token", "friend@example.com", friend.Id.ToString(), bob.Id.ToString(), "bob@example.com", "taken by bob" })
            Assert.DoesNotContain(secret, text);
    }

    [Fact]
    public async Task CommentsAreExportedWithTheirAuthor_AndDeletedWithTheAccount()
    {
        var ana = await AddUserAsync("ana@example.com");
        var eve = await AddUserAsync("eve@example.com");
        foreach (var (user, text) in new[] { (ana, "from ana"), (eve, "from eve") })
            (await ClientFor(user).PostAsJsonAsync("/api/v1/feedback", new { category = "idea", message = text, clientRequestId = Guid.NewGuid() }))
                .EnsureSuccessStatusCode();

        var export = JsonDocument.Parse(await ClientFor(ana).GetStringAsync("/api/v1/account/export")).RootElement;
        Assert.Equal("from ana", Assert.Single(export.GetProperty("feedback").EnumerateArray()).GetProperty("message").GetString());

        (await DeleteAsync(ClientFor(ana), new { password = Password })).EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Equal("from eve", (await db.FeedbackEntries.AsNoTracking().SingleAsync()).Message);
    }

    [Fact]
    public async Task TheExportHoldsTheUsersDataWithoutAnySecret()
    {
        var ana = await AddUserAsync("ana@example.com", twoFactor: true);
        await StartPlanAndLogAsync(ClientFor(ana));

        var response = await ClientFor(ana).GetAsync("/api/v1/account/export");
        var text = await response.Content.ReadAsStringAsync();
        var export = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ana@example.com", export.GetProperty("account").GetProperty("email").GetString());
        Assert.Equal(1, export.GetProperty("plans").GetArrayLength());
        Assert.True(export.GetProperty("plans")[0].GetProperty("sessions").GetArrayLength() > 0);
        Assert.Equal("me dolía la rodilla", export.GetProperty("workoutLogs")[0].GetProperty("notes").GetString());
        Assert.True(export.GetProperty("workoutLogs")[0].GetProperty("sets").GetArrayLength() > 0);
        Assert.Equal(1, export.GetProperty("subscriptions").GetArrayLength());
        Assert.DoesNotContain("secret-token", text);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ana.Id);
        Assert.DoesNotContain(stored.PasswordHash!, text);
        Assert.DoesNotContain(stored.ProtectedAuthenticatorKey!, text);
        Assert.DoesNotContain(db.Subscriptions.First(s => s.UserId == ana.Id).PurchaseTokenHash, text);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/account/export")).StatusCode);
    }

    // ------------------------------------------------------------------ time zone

    [Fact]
    public async Task TodayFollowsTheTimeZoneOfTheUser()
    {
        // 08:00 UTC on Monday 16 November is still Sunday the 15th in Pago Pago (UTC-11).
        var madrid = ClientFor(await AddUserAsync("madrid@example.com"));
        Assert.Equal(HttpStatusCode.Created, (await madrid.PostAsJsonAsync("/api/v1/plans", Profile())).StatusCode);
        Assert.Equal("2026-11-16", (await JsonAsync(await madrid.GetAsync("/api/v1/plans/current"))).GetProperty("startDate").GetString());

        var pago = ClientFor(await AddUserAsync("pago@example.com"));
        Assert.Equal(HttpStatusCode.Created, (await pago.PostAsJsonAsync("/api/v1/plans", Profile("Pacific/Pago_Pago"))).StatusCode);
        Assert.Equal("2026-11-15", (await JsonAsync(await pago.GetAsync("/api/v1/plans/current"))).GetProperty("startDate").GetString());
        // The zone sent when creating the plan is saved in the profile.
        Assert.Equal("Pacific/Pago_Pago", (await JsonAsync(await pago.GetAsync("/api/v1/account"))).GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task TheTimeZoneCanBeSavedAndOverriddenPerRequest_AndMustBeValid()
    {
        var client = ClientFor(await AddUserAsync("ana@example.com"));
        Assert.Equal("Europe/Madrid", (await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("timeZone").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = "Mars/Base" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = "Romance Standard Time" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/plans", Profile("Mars/Base"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/v1/account/time-zone", new { timeZone = "Atlantic/Canary" })).StatusCode);
        Assert.Equal("Atlantic/Canary", (await JsonAsync(await client.GetAsync("/api/v1/account"))).GetProperty("timeZone").GetString());

        // The header wins for that request; an invalid header is ignored, not an error.
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/plans") { Content = JsonContent.Create(Profile()) };
        request.Headers.Add("X-Time-Zone", "Pacific/Pago_Pago");
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
        Assert.Equal("2026-11-15", (await JsonAsync(await client.GetAsync("/api/v1/plans/current"))).GetProperty("startDate").GetString());
        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/v1/plans/current");
        bad.Headers.Add("X-Time-Zone", "garbage");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(bad)).StatusCode);
    }

    // ------------------------------------------------------------------ plans

    [Fact]
    public async Task OptionsListTheWeekFromMondayWithStableIds()
    {
        var body = await JsonAsync(await ClientFor(await AddUserAsync("ana@example.com")).GetAsync("/api/v1/plans/options"));

        Assert.Equal(["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"],
            body.GetProperty("days").EnumerateArray().Select(d => d.GetString()!).ToArray());
        Assert.Equal("Monday", body.GetProperty("weekStartsOn").GetString());
        Assert.Equal("Europe/Madrid", body.GetProperty("defaultTimeZone").GetString());
        var options = body.GetProperty("dayOptions").EnumerateArray().ToList();
        Assert.Equal("Lunes", options[0].GetProperty("label").GetString());
        Assert.Equal(7, options[6].GetProperty("isoNumber").GetInt32());
    }

    [Fact]
    public async Task RetryingAnAlreadySavedLogReturnsTheSavedResult_ButADifferentLogStillConflicts()
    {
        var ana = ClientFor(await AddUserAsync("ana@example.com"));
        var session = await StartPlanAndLogAsync(ana);
        var url = $"/api/v1/plans/current/sessions/{session.GetProperty("id").GetString()}/log";

        var retry = await ana.PostAsJsonAsync(url, LogBody(session));
        var body = await JsonAsync(retry);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True(body.GetProperty("replayed").GetBoolean());
        Assert.Equal("Completed", body.GetProperty("session").GetProperty("log").GetProperty("outcome").GetString());
        Assert.Equal(1, (await JsonAsync(await ana.GetAsync("/api/v1/plans/current/history"))).GetArrayLength());

        var different = await ana.PostAsJsonAsync(url, new { outcome = "NotCompleted" });
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("session_already_logged", (await JsonAsync(different)).GetProperty("title").GetString());
    }

    // ------------------------------------------------------------------ catalog options

    [Fact]
    public async Task ExerciseOptionsExposeTheFilterVocabularyWithSpanishLabelsAndAreCacheable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/exercises/options");
        var body = await JsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var patterns = body.GetProperty("movementPatterns").EnumerateArray().ToList();
        Assert.NotEmpty(patterns);
        Assert.All(patterns, p => { Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("label").GetString())); Assert.True(p.GetProperty("count").GetInt32() > 0); });
        Assert.Contains(patterns, p => p.GetProperty("key").GetString() == "Squat" && p.GetProperty("label").GetString() == "Sentadilla");
        Assert.NotEmpty(body.GetProperty("muscles").EnumerateArray());
        Assert.NotEmpty(body.GetProperty("equipment").EnumerateArray());
        Assert.Equal("movementPattern", body.GetProperty("parameters").GetProperty("movementPattern").GetString());
        Assert.Contains("max-age", response.Headers.CacheControl!.ToString());

        var again = new HttpRequestMessage(HttpMethod.Get, "/api/v1/exercises/options");
        again.Headers.TryAddWithoutValidation("If-None-Match", response.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(again)).StatusCode);
        // The literal route does not clash with /exercises/{id}.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/exercises/does-not-exist")).StatusCode);
    }

    // ------------------------------------------------------------------ password reset requests

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _lines = [];
        public string All { get { lock (_lines) return string.Join("\n", _lines); } }
        public ILogger CreateLogger(string categoryName) => new Capture(this);
        public void Dispose() { }
        private sealed class Capture(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (owner._lines) owner._lines.Add(formatter(state, exception) + exception);
            }
        }
    }

    private async Task<HttpResponseMessage> ForgotAsync(string email, string? userAgent)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.Clear();
        if (userAgent is not null) client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        return await client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email });
    }

    private HttpClient ClientIn(string language)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", language);
        return client;
    }

    [Fact]
    public async Task ThePasswordResetEmailIsInEnglishWhenTheRequestAsksForEnglish_AndInSpanishByDefault()
    {
        await AddUserAsync("english.mail@example.com");
        var english = ClientIn("en-GB,en;q=0.9");
        english.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "okhttp/4.12.0");
        Assert.Equal(HttpStatusCode.Accepted, (await english.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = "english.mail@example.com" })).StatusCode);
        var body = await WaitForMailAsync(_mail, 1);
        var sent = _mail.Sent.ToArray()[0];
        Assert.Equal("Reset your TRAZO password", sent.Subject);
        Assert.Contains("Use this link to reset your password:", body);
        Assert.Contains("This request was made from the TRAZO app on", body);
        Assert.Contains("The link expires in 30 minutes. If you did not request this action, ignore this message.", body);
        Assert.DoesNotContain("enlace", body);
    }

    [Fact]
    public async Task AnEmailToAUserGoesOutInTheLanguageTheyChose_WhateverTheRequestAsked()
    {
        var user = await AddUserAsync("chose.english@example.com");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            (await db.Users.SingleAsync(u => u.Id == user.Id)).Language = "en";
            await db.SaveChangesAsync();
        }
        var spanishRequest = ClientIn("es-ES,es;q=0.9");
        Assert.Equal(HttpStatusCode.Accepted, (await spanishRequest.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = "chose.english@example.com" })).StatusCode);
        var body = await WaitForMailAsync(_mail, 1);
        Assert.Equal("Reset your TRAZO password", _mail.Sent.ToArray()[0].Subject);
        Assert.Contains("Use this link to reset your password:", body);
    }

    [Fact]
    public async Task TheErrorsOfTheAuthenticationAreInTheLanguageOfTheRequest_WhateverLanguageTheCodeWasWrittenIn()
    {
        await AddUserAsync("setup.done@example.com", role: "superadmin"); // with no administrator the API is still in its initial setup
        var invalid = new { email = "no-email", displayName = "", password = "x" };
        var spanish = await JsonAsync(await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", invalid));
        Assert.Equal("Se necesita un correo y un nombre válidos.", spanish.GetProperty("errors").GetProperty("account")[0].GetString());
        var english = await JsonAsync(await ClientIn("en").PostAsJsonAsync("/api/v1/auth/register", invalid));
        Assert.Equal("A valid email and display name are required.", english.GetProperty("errors").GetProperty("account")[0].GetString());

        // A body of the { error } kind: written in Spanish in the code for one link and in English for the other.
        var link = new { token = "nope" };
        var es = await JsonAsync(await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/email/verify", link));
        var en = await JsonAsync(await ClientIn("en").PostAsJsonAsync("/api/v1/auth/email/verify", link));
        Assert.Equal("El enlace de verificación no es válido o ha caducado.", es.GetProperty("error").GetString());
        Assert.Equal("The verification link is invalid or expired.", en.GetProperty("error").GetString());
        var reset = new { token = "nope", newPassword = "CorrectHorse1Battery" };
        Assert.Equal("El enlace no es válido o ha caducado.", (await JsonAsync(await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/password/reset", reset))).GetProperty("error").GetString());
        Assert.Equal("The link is not valid or has expired.", (await JsonAsync(await ClientIn("en").PostAsJsonAsync("/api/v1/auth/password/reset", reset))).GetProperty("error").GetString());

        var registrationEs = await JsonAsync(await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register",
            new { email = "register.es@example.com", displayName = "Spanish User", password = Password }));
        Assert.Equal("Te hemos enviado un enlace para verificar tu correo.", registrationEs.GetProperty("message").GetString());

        var registrationEn = await JsonAsync(await ClientIn("en").PostAsJsonAsync("/api/v1/auth/register",
            new { email = "register.en@example.com", displayName = "English User", password = Password }));
        Assert.Equal("We have sent you a link to verify your email.", registrationEn.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TheAccountDeletionPageFollowsTheLanguageOfTheBrowser()
    {
        var spanish = await _factory.CreateClient().GetStringAsync("/account/delete");
        Assert.Contains("<html lang=\"es\">", spanish);
        Assert.Contains("Eliminar mi cuenta de TRAZO", spanish);
        var english = await ClientIn("en-US,en;q=0.8").GetStringAsync("/account/delete");
        Assert.Contains("<html lang=\"en\">", english);
        Assert.Contains("Delete my TRAZO account", english);
        Assert.Contains("Deleting the account does not cancel your Google Play subscription.", english);
        Assert.DoesNotContain("Eliminar", english);
    }

    [Theory]
    [InlineData("okhttp/4.12.0", "desde la app de TRAZO", "app")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36", "desde la web", "web")]
    [InlineData("curl/8.5.0", "desde un origen desconocido", "unknown")]
    [InlineData(null, "desde un origen desconocido", "unknown")]
    public async Task PasswordResetEmailSaysWhereAndWhenTheRequestCameFromAndTheLogKeepsTheOriginButNotTheAddress(string? userAgent, string phrase, string logged)
    {
        var user = await AddUserAsync("who.asked@example.com");

        Assert.Equal(HttpStatusCode.Accepted, (await ForgotAsync("Who.Asked@Example.com", userAgent)).StatusCode);

        var mail = Assert.Single(_mail.Sent);
        Assert.Equal("who.asked@example.com", mail.To, ignoreCase: true);
        Assert.Contains($"Esta solicitud se hizo {phrase} el 16/11/2026 a las 08:00 (hora UTC).", mail.Body);
        Assert.Contains("/account/reset-password?token=", mail.Body);
        Assert.Contains($"origin {logged}, outcome sent, user {user.Id}", _logs.All);
        Assert.DoesNotContain("who.asked", _logs.All, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordResetRequestsThatSendNothingAreLoggedAsWellWithTheSameAnswer()
    {
        await AddUserAsync("google.only@example.com", google: true);
        var withPassword = await AddUserAsync("has.password@example.com");

        var unknown = await ForgotAsync("nobody@example.com", "okhttp/4.12.0");
        var googleOnly = await ForgotAsync("google.only@example.com", "okhttp/4.12.0");
        for (var i = 0; i < 4; i++) await ForgotAsync("has.password@example.com", "okhttp/4.12.0"); // the 4th is over the per-address limit

        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await googleOnly.Content.ReadAsStringAsync()); // nothing tells them apart
        Assert.Equal(DeletionRequestThrottle.MaximumPerWindow, _mail.Sent.Count);
        Assert.Contains("outcome no_account, user ", _logs.All);
        Assert.Contains("outcome no_password, user ", _logs.All);
        Assert.Contains($"outcome throttled, user {withPassword.Id}", _logs.All);
        Assert.DoesNotContain("nobody@", _logs.All, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("google.only", _logs.All, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("okhttp/4.12.0", RequestOriginKind.App)]
    [InlineData("OkHttp/5.0.0-alpha.14", RequestOriginKind.App)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) Firefox/131.0", RequestOriginKind.Web)]
    [InlineData("python-requests/2.32", RequestOriginKind.Unknown)]
    [InlineData("", RequestOriginKind.Unknown)]
    [InlineData(null, RequestOriginKind.Unknown)]
    public void TheOriginIsGuessedFromTheUserAgent(string? userAgent, RequestOriginKind expected) =>
        Assert.Equal(expected, RequestOrigin.Classify(userAgent));
}
