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
using Trazo.Api.Security;
using Trazo.Domain.Billing;
using Trazo.Domain.Growth;
using Trazo.Domain.Identity;
using Trazo.Domain.Marketing;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Marketing;
using Trazo.Infrastructure.Persistence;
using Trazo.UnitTests.Planning;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>The marketing panel end to end: the commercial role, campaigns and tracking links, attribution and the strategy analytics.</summary>
public sealed class MarketingApiTests : IDisposable
{
    private const string Browser = "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/120 Mobile Safari/537.36";
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public MarketingApiTests()
    {
        using (var db = _fixture.NewContext()) ExerciseTestSchema.Create(db);
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
            builder.UseSetting("Billing:Play:PackageName", "com.trazo.android");
            builder.UseSetting("Email:ActionBaseUrl", "https://api.trazo.test");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString)
                    .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>());
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(new RecordingEmailSender());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
            });
        });
    }

    private void CreateIdentityTables()
    {
        var options = new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(_fixture.ConnectionString)
            .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>().Options;
        using var db = new TrazoDbContext(options);
        var script = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>(db).GenerateCreateScript();
        foreach (var statement in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (statement.StartsWith("CREATE", StringComparison.Ordinal) && new[] { "email_action_tokens", "auth_challenges", "auth_recovery_codes" }.Any(t => statement.StartsWith($"CREATE TABLE \"{t}\"", StringComparison.Ordinal) || statement.Contains($" ON \"{t}\" ", StringComparison.Ordinal)))
                db.Database.ExecuteSqlRaw(statement);
    }

    public void Dispose()
    {
        _factory.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private TrazoDbContext Db() => new(new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(_fixture.ConnectionString)
        .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>().Options);

    private async Task<TrazoUser> AddUserAsync(string role, DateTimeOffset? createdAt = null)
    {
        await using var db = Db();
        var email = $"{role}-{Guid.NewGuid():N}@trazo.test";
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = role, Role = role,
            EmailVerifiedAt = Now, CreatedAt = createdAt ?? Now, UpdatedAt = Now
        };
        user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, "CorrectHorse1Battery");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<HttpClient> ClientAsync(string role)
    {
        var user = await AddUserAsync(role);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private HttpClient LinkClient(string? userAgent = Browser)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userAgent is not null) client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static object Campaign(string slug, string name, string channel = "ads") => new { slug, name, channel, status = "active", notes = "Test" };

    private async Task<Guid> CreateCampaignAsync(HttpClient c, string slug, string name, string channel = "ads")
    {
        var response = await c.PostAsJsonAsync("/api/v1/admin/marketing/campaigns", Campaign(slug, name, channel));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Json(response)).GetProperty("campaign").GetProperty("id").GetGuid();
    }

    private static readonly string From = "2026-09-01T00:00:00Z";
    private static readonly string To = "2026-11-17T00:00:00Z";
    private static string Query() => $"?from={Uri.EscapeDataString(From)}&to={Uri.EscapeDataString(To)}";

    /// <summary>A signed-up account of the cohort, optionally attributed to a campaign, with its funnel events.</summary>
    private async Task<Guid> SeedPersonAsync(Guid? campaignId, int daysAgo = 20, bool verified = false, bool trial = false, bool paid = false,
        int renewals = 0, bool testPurchase = false, bool trained = false)
    {
        var user = await AddUserAsync("user", Now.AddDays(-daysAgo));
        await using var db = Db();
        await db.Database.OpenConnectionAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        var at = Now.AddDays(-daysAgo);
        void Event(string name, string? key = null, DateTimeOffset? when = null) =>
            db.GrowthEvents.Add(new GrowthEvent { Id = Guid.NewGuid(), Name = name, OccurredAt = when ?? at, UserId = user.Id, DedupeKey = key });
        Event(GrowthEventNames.Signup, $"signup:{user.Id:N}");
        if (campaignId is { } cid)
            db.CampaignAttributions.Add(new CampaignAttribution { UserId = user.Id, CampaignId = cid, Source = "install_referrer", AttributedAt = at });
        if (verified) Event(GrowthEventNames.EmailVerified, $"verified:{user.Id:N}");
        var sub = new Subscription
        {
            Id = Guid.NewGuid(), UserId = user.Id, ProductId = "trazo_premium", PurchaseTokenHash = Guid.NewGuid().ToString("N"), ProtectedPurchaseToken = "x",
            Status = SubscriptionStatus.Active, StartedAt = at, ExpiresAt = at.AddDays(30), IsTestPurchase = testPurchase, PaidAt = paid ? at : null,
            CreatedAt = at, UpdatedAt = at
        };
        if (trial || paid) { db.Subscriptions.Add(sub); }
        if (trial) Event(GrowthEventNames.TrialStarted, $"trial:{sub.Id}");
        if (paid) Event(GrowthEventNames.FirstPurchase, $"first_purchase:{sub.Id}", at.AddDays(1));
        for (var i = 0; i < renewals; i++) Event(GrowthEventNames.Renewed, $"renewed:{sub.Id}:order{i}", at.AddDays(31 + 30 * i));
        if (trained)
            db.WorkoutLogs.Add(new WorkoutLog
            {
                Id = Guid.NewGuid(), UserId = user.Id, PlanId = Guid.NewGuid(), PlannedSessionId = Guid.NewGuid(), LoggedAt = at.AddDays(8), PerformedAt = at.AddDays(8),
                PerformedOn = DateOnly.FromDateTime(at.AddDays(8).UtcDateTime), TimeZoneId = "UTC", Outcome = WorkoutOutcome.Completed
            });
        await db.SaveChangesAsync();
        return user.Id;
    }

    // ------------------------------------------------------------------ access

    [Theory]
    [InlineData("user")]
    [InlineData("admin")]
    [InlineData("accountant")]
    public async Task OtherRolesCannotOpenTheMarketingPanel(string role)
    {
        var client = await ClientAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/marketing/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/marketing/campaigns")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/marketing/campaigns", Campaign("nope-one", "No"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/admin/marketing/summary")).StatusCode);
    }

    [Theory]
    [InlineData("commercial")]
    [InlineData("superadmin")]
    public async Task TheCommercialAndTheSuperadminSeeTheWholePanel(string role)
    {
        var client = await ClientAsync(role);
        foreach (var path in new[] { "summary", "strategies", "funnel", "retention", "paywall", "waitlist", "referrals", "campaigns", "options" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/admin/marketing/{path}")).StatusCode);
    }

    [Fact]
    public async Task TheCommercialSeesNothingElseOfTheConsole()
    {
        var commercial = await ClientAsync("commercial");
        foreach (var path in new[] { "users", "subscriptions", "referrals", "fiscal/profile", "settings/promotions", "logs", "error-tickets", "waitlist", "overview" })
            Assert.Equal(HttpStatusCode.Forbidden, (await commercial.GetAsync($"/api/v1/admin/{path}")).StatusCode);

        var root = await ClientAsync("superadmin");
        var users = await Json(await root.GetAsync("/api/v1/admin/users?role=commercial"));
        Assert.True(users.TryGetProperty("items", out _));
        var target = await AddUserAsync("user");
        var assigned = await root.PatchAsJsonAsync($"/api/v1/admin/users/{target.Id}", new { role = "commercial" });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
    }

    // ------------------------------------------------------------------ campaigns and links

    [Fact]
    public async Task CampaignsAreValidatedAndTheLinkNameIsUniqueAndFixed()
    {
        var c = await ClientAsync("commercial");
        foreach (var bad in new[] { "Ab", "has space", "-start", "end-", "UP_PER", new string('a', 40) })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/admin/marketing/campaigns", Campaign(bad, "Bad"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/admin/marketing/campaigns", Campaign("good-one", "X", "unknown"))).StatusCode);

        var created = await c.PostAsJsonAsync("/api/v1/admin/marketing/campaigns", Campaign("Spring-Ads", "Spring ads"));
        var body = await Json(created);
        Assert.Equal("spring-ads", body.GetProperty("campaign").GetProperty("slug").GetString());
        Assert.Equal("https://api.trazo.test/c/spring-ads", body.GetProperty("link").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/admin/marketing/campaigns", Campaign("spring-ads", "Again"))).StatusCode);

        var id = body.GetProperty("campaign").GetProperty("id").GetGuid();
        var updated = await Json(await c.PutAsJsonAsync($"/api/v1/admin/marketing/campaigns/{id}",
            new { slug = "other-slug", name = "Spring ads v2", channel = "social", status = "paused", notes = "Changed" }));
        Assert.Equal("spring-ads", updated.GetProperty("campaign").GetProperty("slug").GetString());
        Assert.Equal("paused", updated.GetProperty("campaign").GetProperty("status").GetString());
        Assert.Equal("social", updated.GetProperty("campaign").GetProperty("channel").GetString());
    }

    [Fact]
    public async Task SpendIsRecordedPerDayAndValidated()
    {
        var c = await ClientAsync("commercial");
        var id = await CreateCampaignAsync(c, "ads-one", "Ads one");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/v1/admin/marketing/campaigns/{id}/spend", new { day = "2026-11-10", amount = -5 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync($"/api/v1/admin/marketing/campaigns/{id}/spend", new { day = "2030-01-01", amount = 5 })).StatusCode);
        var ok = await c.PostAsJsonAsync($"/api/v1/admin/marketing/campaigns/{id}/spend", new { day = "2026-11-10", amount = 120.456, note = "Week 1" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var spendId = (await Json(ok)).GetProperty("id").GetGuid();
        var detail = await Json(await c.GetAsync($"/api/v1/admin/marketing/campaigns/{id}{Query()}"));
        Assert.Equal(120.46m, detail.GetProperty("campaign").GetProperty("spend").GetDecimal());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/admin/marketing/campaigns/{id}/spend/{spendId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/v1/admin/marketing/campaigns/{id}/spend/{spendId}")).StatusCode);
    }

    [Fact]
    public async Task TheTrackingLinkCountsPeopleNotRobotsAndSendsThemToGooglePlayWithTheCampaign()
    {
        var c = await ClientAsync("commercial");
        var id = await CreateCampaignAsync(c, "podcast-spot", "Podcast spot", "influencer");

        var response = await LinkClient().GetAsync("/c/podcast-spot");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var target = response.Headers.Location!.ToString();
        Assert.StartsWith("https://play.google.com/store/apps/details?id=com.trazo.android&referrer=", target);
        var referrer = Uri.UnescapeDataString(target[(target.IndexOf("referrer=", StringComparison.Ordinal) + 9)..]);
        Assert.Contains("trazo_cmp=podcast-spot", referrer);
        Assert.Contains("utm_source=influencer", referrer);

        // Link-preview robots and clients without a user agent are redirected but not counted.
        await LinkClient("WhatsApp/2.23 A").GetAsync("/c/podcast-spot");
        await LinkClient("Slackbot-LinkExpanding 1.0").GetAsync("/c/podcast-spot");
        await LinkClient(null).GetAsync("/c/podcast-spot");
        var unknown = await LinkClient().GetAsync("/c/not-a-campaign");
        Assert.Equal("https://play.google.com/store/apps/details?id=com.trazo.android", unknown.Headers.Location!.ToString());

        var install = await _factory.CreateClient().PostAsJsonAsync("/api/v1/campaigns/install-referrer", new { campaign = "podcast-spot" });
        Assert.Equal(HttpStatusCode.NoContent, install.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _factory.CreateClient().PostAsJsonAsync("/api/v1/campaigns/install-referrer", new { campaign = "nothing" })).StatusCode);

        var list = await Json(await c.GetAsync("/api/v1/admin/marketing/campaigns"));
        var row = list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("campaign").GetProperty("id").GetGuid() == id).GetProperty("campaign");
        Assert.Equal(1, row.GetProperty("clicks").GetInt32());
        Assert.Equal(1, row.GetProperty("installs").GetInt32());
    }

    [Fact]
    public async Task RegistrationWithACampaignCodeAttributesTheAccountAndItsEvents()
    {
        var c = await ClientAsync("commercial");
        var id = await CreateCampaignAsync(c, "radio-ad", "Radio ad", "ads");

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "new.person@example.com", password = "CorrectHorse1Battery", displayName = "New", campaignCode = "RADIO-AD", referralSource = "install_referrer"
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        // An unknown code never blocks the registration.
        Assert.Equal(HttpStatusCode.Accepted, (await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "other.person@example.com", password = "CorrectHorse1Battery", displayName = "Other", campaignCode = "'; drop table users;--"
        })).StatusCode);

        await using var db = Db();
        var attribution = await db.CampaignAttributions.AsNoTracking().SingleAsync();
        Assert.Equal(id, attribution.CampaignId);
        var events = await db.GrowthEvents.AsNoTracking().Where(e => e.UserId == attribution.UserId).ToListAsync();
        Assert.Contains(events, e => e.Name == GrowthEventNames.CampaignAttributed && e.Channel == "campaign" && e.Reference == "radio-ad");
    }

    [Fact]
    public async Task AttributionOnlyHappensWhenTheAccountIsCreated()
    {
        var c = await ClientAsync("commercial");
        await CreateCampaignAsync(c, "late-one", "Late one");
        var old = await AddUserAsync("user", Now.AddDays(-30));
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICampaignService>();
        Assert.False(await service.AttributeAsync(old.Id, "late-one", "install_referrer", default));
        var fresh = await AddUserAsync("user");
        Assert.True(await service.AttributeAsync(fresh.Id, "late-one", "install_referrer", default));
        Assert.False(await service.AttributeAsync(fresh.Id, "late-one", "install_referrer", default)); // the first one stays
    }

    // ------------------------------------------------------------------ analytics

    [Fact]
    public async Task StrategiesCompareCampaignsWithCostsAndLeaveTestPurchasesOut()
    {
        var c = await ClientAsync("commercial");
        var ads = await CreateCampaignAsync(c, "spring-ads", "Spring ads", "ads");
        var pod = await CreateCampaignAsync(c, "podcast", "Podcast", "influencer");
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/v1/admin/marketing/campaigns/{ads}/spend", new { day = "2026-11-10", amount = 100, note = "Week" })).StatusCode);
        for (var i = 0; i < 4; i++) await LinkClient().GetAsync("/c/spring-ads");
        await _factory.CreateClient().PostAsJsonAsync("/api/v1/campaigns/install-referrer", new { campaign = "spring-ads" });

        await SeedPersonAsync(ads, verified: true, trial: true, paid: true, renewals: 1, trained: true);
        await SeedPersonAsync(ads, verified: true, trial: true);
        await SeedPersonAsync(ads, verified: true);
        await SeedPersonAsync(ads, trial: true, paid: true, testPurchase: true); // a test purchase: no trial, no payment
        await SeedPersonAsync(pod);
        await SeedPersonAsync(pod, paid: true);
        await SeedPersonAsync(null);
        await SeedPersonAsync(null, verified: true);
        await SeedPersonAsync(null);

        var report = await Json(await c.GetAsync($"/api/v1/admin/marketing/strategies{Query()}"));
        Assert.Equal(9, report.GetProperty("overall").GetProperty("signups").GetInt32());
        var campaigns = report.GetProperty("campaigns").EnumerateArray().ToDictionary(r => r.GetProperty("key").GetString()!);
        var a = campaigns["campaign:spring-ads"];
        Assert.Equal(4, a.GetProperty("signups").GetInt32());
        Assert.Equal(3, a.GetProperty("verified").GetInt32());
        Assert.Equal(2, a.GetProperty("trials").GetInt32());   // the test trial does not count
        Assert.Equal(1, a.GetProperty("paid").GetInt32());     // the test payment does not count
        Assert.Equal(1, a.GetProperty("renewed").GetInt32());
        Assert.Equal(1, a.GetProperty("activated").GetInt32());
        Assert.Equal(4, a.GetProperty("clicks").GetInt32());
        Assert.Equal(1, a.GetProperty("installs").GetInt32());
        Assert.Equal(100m, a.GetProperty("spend").GetDecimal());
        Assert.Equal(25m, a.GetProperty("costPerSignup").GetDecimal());
        Assert.Equal(100m, a.GetProperty("costPerPaid").GetDecimal());
        Assert.Equal(0.25, a.GetProperty("signupToPaid").GetDouble());
        Assert.False(a.GetProperty("reliable").GetBoolean());
        Assert.True(a.GetProperty("signupToPaidLow").GetDouble() < 0.25 && a.GetProperty("signupToPaidHigh").GetDouble() > 0.25);
        Assert.True(a.GetProperty("estimatedRevenue").GetDecimal() > 0);
        Assert.True(a.GetProperty("returnOnSpend").GetDouble() < 0); // two small charges against 100 € of spend

        var p = campaigns["campaign:podcast"];
        Assert.Equal(2, p.GetProperty("signups").GetInt32());
        Assert.Equal(1, p.GetProperty("paid").GetInt32());
        Assert.Equal(JsonValueKind.Null, p.GetProperty("costPerPaid").ValueKind); // no spend recorded

        var channels = report.GetProperty("channels").EnumerateArray().ToDictionary(r => r.GetProperty("key").GetString()!);
        Assert.Equal(3, channels["organic"].GetProperty("signups").GetInt32());
        Assert.Equal(4, channels["channel:ads"].GetProperty("signups").GetInt32());
        Assert.Equal(2, channels["channel:influencer"].GetProperty("signups").GetInt32());
        Assert.Contains(report.GetProperty("insights").EnumerateArray(), i => i.GetProperty("title").GetString()!.StartsWith("Not enough data"));
    }

    [Fact]
    public async Task AReferralCodeWinsOverACampaignSoNobodyCountsTwice()
    {
        var c = await ClientAsync("commercial");
        var ads = await CreateCampaignAsync(c, "both-ways", "Both ways");
        var both = await SeedPersonAsync(ads);
        var referrer = await AddUserAsync("user");
        await using (var db = Db())
        {
            db.ReferralRelationships.Add(new ReferralRelationship
            {
                Id = Guid.NewGuid(), ReferrerUserId = referrer.Id, ReferredUserId = both, Code = "ABCDEFGH", Source = "manual", Status = ReferralStatuses.Registered,
                AttributedAt = Now, UpdatedAt = Now
            });
            await db.SaveChangesAsync();
        }
        var report = await Json(await c.GetAsync($"/api/v1/admin/marketing/strategies{Query()}"));
        var channels = report.GetProperty("channels").EnumerateArray().ToDictionary(r => r.GetProperty("key").GetString()!);
        Assert.Equal(1, channels["referral"].GetProperty("signups").GetInt32());
        Assert.Equal(0, report.GetProperty("campaigns").EnumerateArray().Single().GetProperty("signups").GetInt32());
    }

    [Fact]
    public async Task TheFunnelAndRetentionCanBeCutBySegment()
    {
        var c = await ClientAsync("commercial");
        var ads = await CreateCampaignAsync(c, "funnel-ads", "Funnel ads");
        await SeedPersonAsync(ads, verified: true, trial: true, paid: true, trained: true);
        await SeedPersonAsync(ads, verified: true);
        await SeedPersonAsync(null, verified: true, trained: true);

        var funnel = await Json(await c.GetAsync($"/api/v1/admin/marketing/funnel{Query()}&segment=campaign:funnel-ads"));
        Assert.Equal(2, funnel.GetProperty("cohort").GetInt32());
        var stages = funnel.GetProperty("stages").EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!, s => s.GetProperty("users").GetInt32());
        Assert.Equal(2, stages["email_verified"]);
        Assert.Equal(1, stages["trial_started"]);
        Assert.Equal(1, stages["first_purchase"]);
        Assert.Equal(1, stages["first_workout"]);
        Assert.Equal("Funnel ads", funnel.GetProperty("segmentName").GetString());

        var all = await Json(await c.GetAsync($"/api/v1/admin/marketing/funnel{Query()}"));
        Assert.Equal(3, all.GetProperty("cohort").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/v1/admin/marketing/funnel{Query()}&segment=users:everyone")).StatusCode);

        var retention = await Json(await c.GetAsync($"/api/v1/admin/marketing/retention{Query()}&segment=organic"));
        Assert.Equal(1, retention.GetProperty("cohort").GetInt32());
        var week1 = retention.GetProperty("average")[1];
        Assert.Equal(1.0, week1.GetDouble()); // trained 8 days after signing up: week 1 (days 7 to 13)
        Assert.Equal(0.0, retention.GetProperty("average")[0].GetDouble());
        Assert.Equal(JsonValueKind.Null, retention.GetProperty("average")[2].ValueKind); // week 2 is not over yet for people who signed up 20 days ago
    }

    [Fact]
    public async Task TheSummaryComparesWithThePreviousPeriodAndCountsPaywallViews()
    {
        var c = await ClientAsync("commercial");
        await SeedPersonAsync(null, daysAgo: 5, trial: true, paid: true);
        await SeedPersonAsync(null, daysAgo: 6);
        await SeedPersonAsync(null, daysAgo: 60);
        await using (var db = Db())
        {
            db.PaywallViews.Add(new Trazo.Domain.Analytics.PaywallView { Day = DateOnly.FromDateTime(Now.AddDays(-3).UtcDateTime), Source = "plan", Views = 7 });
            db.PaywallViews.Add(new Trazo.Domain.Analytics.PaywallView { Day = DateOnly.FromDateTime(Now.AddDays(-2).UtcDateTime), Source = "session", Views = 3 });
            await db.SaveChangesAsync();
        }
        var from = Uri.EscapeDataString(Now.AddDays(-30).ToString("O"));
        var to = Uri.EscapeDataString(Now.AddDays(1).ToString("O"));
        var summary = await Json(await c.GetAsync($"/api/v1/admin/marketing/summary?from={from}&to={to}"));
        var kpis = summary.GetProperty("kpis").EnumerateArray().ToDictionary(k => k.GetProperty("key").GetString()!);
        Assert.Equal(2, kpis["signups"].GetProperty("current").GetInt32());
        Assert.Equal(1, kpis["signups"].GetProperty("previous").GetInt32());
        Assert.Equal(1, kpis["paid"].GetProperty("current").GetInt32());
        Assert.Equal(10, summary.GetProperty("paywallViews").GetInt32());

        var paywall = await Json(await c.GetAsync($"/api/v1/admin/marketing/paywall?from={from}&to={to}"));
        Assert.Equal(10, paywall.GetProperty("total").GetInt32());
        Assert.Equal("plan", paywall.GetProperty("sources")[0].GetProperty("source").GetString());
        Assert.Equal(10.0, paywall.GetProperty("viewsPerPurchase").GetDouble());
    }

    [Fact]
    public async Task TheWaitlistIsReportedAsCountsOnly()
    {
        var c = await ClientAsync("commercial");
        await using (var db = Db())
        {
            db.WaitlistEntries.Add(new Trazo.Domain.Waitlist.WaitlistEntry { Id = Guid.NewGuid(), Email = "a@x.test", Language = "es", ConsentAt = Now, CreatedAt = Now, ConfirmedAt = Now, InvitedAt = Now });
            db.WaitlistEntries.Add(new Trazo.Domain.Waitlist.WaitlistEntry { Id = Guid.NewGuid(), Email = "b@x.test", Language = "en", ConsentAt = Now, CreatedAt = Now });
            await db.SaveChangesAsync();
        }
        var text = await (await c.GetAsync("/api/v1/admin/marketing/waitlist")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("@x.test", text);
        var waitlist = JsonDocument.Parse(text).RootElement;
        Assert.Equal(2, waitlist.GetProperty("total").GetInt32());
        Assert.Equal(1, waitlist.GetProperty("confirmed").GetInt32());
        Assert.Equal(1, waitlist.GetProperty("invited").GetInt32());
    }

    [Fact]
    public async Task TheCsvExportDefusesSpreadsheetFormulas()
    {
        var c = await ClientAsync("commercial");
        await CreateCampaignAsync(c, "csv-test", "=HYPERLINK(\"http://evil.test\")");
        var response = await c.GetAsync($"/api/v1/admin/marketing/strategies.csv{Query()}");
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("kind,key,name", csv);
        Assert.DoesNotContain(",=HYPERLINK", csv);
        Assert.Contains("'=HYPERLINK", csv);
    }

    [Fact]
    public async Task ThePeriodIsValidated()
    {
        var c = await ClientAsync("commercial");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/v1/admin/marketing/summary?from=2026-11-10T00:00:00Z&to=2026-11-01T00:00:00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync("/api/v1/admin/marketing/strategies?from=2020-01-01T00:00:00Z&to=2026-11-01T00:00:00Z")).StatusCode);
    }

    [Fact]
    public void WilsonIntervalWidensWithFewerPeople()
    {
        var (smallLow, smallHigh) = MarketingAnalytics.Wilson(1, 4);
        var (bigLow, bigHigh) = MarketingAnalytics.Wilson(250, 1000);
        Assert.True(smallHigh - smallLow > bigHigh - bigLow);
        Assert.Equal((null, null), MarketingAnalytics.Wilson(0, 0));
        Assert.Equal("\"a,b\"", MarketingAnalytics.Csv("a,b"));
    }
}
