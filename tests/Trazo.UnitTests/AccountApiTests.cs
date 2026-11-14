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
using Trazo.Api.Endpoints;
using Trazo.Api.Security;
using Trazo.Domain.Billing;
using Trazo.Domain.Identity;
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
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString).ReplaceService<IModelCustomizer, PlanSqliteCustomizer>());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
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
                db.Database.ExecuteSqlRaw(statement + ";");
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
    public async Task DeletingTheAccountRemovesEverythingOfTheUserAndNothingOfOthers()
    {
        var ana = await AddUserAsync("ana@example.com");
        var eve = await AddUserAsync("eve@example.com");
        var anaClient = ClientFor(ana);
        await StartPlanAndLogAsync(anaClient);
        await StartPlanAndLogAsync(ClientFor(eve));
        var before = await RowsOfAsync(ana.Id);
        Assert.All(new[] { "users", "refresh", "subs", "plans", "logs", "phases", "sessions" }, k => Assert.True(before[k] > 0, k));
        var eveBefore = await RowsOfAsync(eve.Id);

        var response = await DeleteAsync(anaClient, new { password = Password });

        var body = await JsonAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("deleted").GetBoolean());
        Assert.True(body.GetProperty("playSubscriptionNotCancelled").GetBoolean());
        Assert.All((await RowsOfAsync(ana.Id)).Values, count => Assert.Equal(0, count));
        Assert.Equal(eveBefore, await RowsOfAsync(eve.Id));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        Assert.Equal(await db.WorkoutLogs.CountAsync(), await db.WorkoutLogs.CountAsync(l => l.UserId == eve.Id));
        Assert.Equal(await db.WorkoutSetLogs.CountAsync(), await db.WorkoutSetLogs.CountAsync(s => db.WorkoutLogs.Any(l => l.Id == s.WorkoutLogId && l.UserId == eve.Id)));
        // The old token no longer works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await anaClient.GetAsync("/api/v1/account")).StatusCode);
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
            body.GetProperty("days").EnumerateArray().Select(d => d.GetString()).ToArray());
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
}
