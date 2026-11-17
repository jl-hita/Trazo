using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Billing;
using Trazo.Api.Security;
using Trazo.Domain.Billing;
using Trazo.Domain.Growth;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Growth;
using Trazo.Infrastructure.Persistence;
using Trazo.UnitTests.Planning;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>The referral programme end to end: codes, attribution, conversion on the first real payment, rewards, Google deferral and audit.</summary>
public sealed class ReferralApiTests : IDisposable
{
    private const string Product = "trazo_premium";
    private const string Secret = "0123456789abcdef-secret";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly FakePlayBillingClient _play = new();
    private readonly RecordingEmailSender _mail = new();
    private readonly WebApplicationFactory<Program> _factory;

    public ReferralApiTests()
    {
        using (var seed = _fixture.NewContext()) ExerciseTestSchema.Create(seed);
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
            builder.UseSetting("Billing:Enforced", "true");
            builder.UseSetting("Billing:Play:PackageName", "com.trazo.android");
            builder.UseSetting("Billing:Play:ServiceAccountJson", "{\"configured\":true}");
            builder.UseSetting("Billing:Play:RtdnSecret", Secret);
            builder.UseSetting("Email:ActionBaseUrl", "https://api.trazo.test");
            builder.UseSetting("AppLinks:Sha256Fingerprints", "AA:BB:CC");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString)
                    .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>());
                services.RemoveAll<IPlayBillingClient>();
                services.AddSingleton<IPlayBillingClient>(_play);
                services.RemoveAll<IEmailSender>();
                services.AddSingleton<IEmailSender>(_mail);
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
            });
        });
    }

    private void CreateIdentityTables()
    {
        var options = new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(_fixture.ConnectionString)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>().Options;
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

    // ------------------------------------------------------------------ helpers

    private async Task<TrazoUser> AddUserAsync(string email, string role = "user", DateTimeOffset? createdAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email.Split('@')[0],
            Role = role, EmailVerifiedAt = Now, CreatedAt = createdAt ?? Now, UpdatedAt = Now
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>())!;

    private async Task<string> CodeOfAsync(TrazoUser user) =>
        (await JsonAsync(await ClientFor(user).GetAsync("/api/v1/referrals/me"))).GetProperty("code").GetString()!;

    /// <summary>A referred account: created now and attributed to the referrer's code, exactly as registration does.</summary>
    private async Task<TrazoUser> ReferredAsync(TrazoUser referrer, string email)
    {
        var code = await CodeOfAsync(referrer);
        var user = await AddUserAsync(email);
        using var scope = _factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<IReferralService>().AttributeAsync(user.Id, user.Email, code, "link", "1.0", default));
        return user;
    }

    private PlaySubscription Purchase(bool trial = false, int expiresInDays = 20, string order = "GPA.1", bool autoRenew = true,
        string state = PlaySubscription.Active, int startedDaysAgo = 1) =>
        new(state, Product, Now.AddDays(-startedDaysAgo), Now.AddDays(expiresInDays), autoRenew, trial, true, order, null, false, "etag-1");

    private Task<HttpResponseMessage> VerifyAsync(TrazoUser user, string token) =>
        ClientFor(user).PostAsJsonAsync("/api/v1/subscription/verify", new { productId = Product, purchaseToken = token });

    private async Task NotifyAsync(string token)
    {
        var json = JsonSerializer.Serialize(new
        {
            version = "1.0", packageName = "com.trazo.android", eventTimeMillis = "1",
            subscriptionNotification = new { version = "1.0", notificationType = 2, purchaseToken = token, subscriptionId = Product }
        });
        var body = JsonSerializer.Serialize(new { message = new { data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)), messageId = "1" } });
        var response = await _factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscription/notifications?secret={Secret}")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task RefundAsync(string token)
    {
        var json = JsonSerializer.Serialize(new
        {
            version = "1.0", packageName = "com.trazo.android", eventTimeMillis = "1",
            voidedPurchaseNotification = new { purchaseToken = token, orderId = "GPA.1", productType = 1, refundType = 1 }
        });
        var body = JsonSerializer.Serialize(new { message = new { data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)), messageId = "2" } });
        await _factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscription/notifications?secret={Secret}")
        { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    private async Task<T> DbAsync<T>(Func<TrazoDbContext, Task<T>> query)
    {
        using var scope = _factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<TrazoDbContext>());
    }

    private Task<List<PremiumGrant>> GrantsOfAsync(Guid userId, string kind = PremiumGrantKinds.ReferralReward) =>
        DbAsync(async db => (await db.PremiumGrants.AsNoTracking().Where(g => g.UserId == userId && g.Kind == kind).ToListAsync()));

    private Task<ReferralRelationship> RelationshipOfAsync(Guid referredId) =>
        DbAsync(db => db.ReferralRelationships.AsNoTracking().SingleAsync(r => r.ReferredUserId == referredId));

    private async Task<bool> HasAccessAsync(TrazoUser user) =>
        (await JsonAsync(await ClientFor(user).GetAsync("/api/v1/subscription"))).GetProperty("hasAccess").GetBoolean();

    // ------------------------------------------------------------------ codes

    [Fact]
    public async Task Code_IsCreatedOnce_IsReadable_AndUniquePerUser()
    {
        var a = await AddUserAsync("a@example.com");
        var b = await AddUserAsync("b@example.com");

        var first = await JsonAsync(await ClientFor(a).GetAsync("/api/v1/referrals/me"));
        var again = await JsonAsync(await ClientFor(a).GetAsync("/api/v1/referrals/me"));
        var other = await JsonAsync(await ClientFor(b).GetAsync("/api/v1/referrals/me"));

        var code = first.GetProperty("code").GetString()!;
        Assert.Equal(ReferralService.CodeLength, code.Length);
        Assert.All(code, c => Assert.Contains(c, ReferralService.Alphabet));
        Assert.Equal(code, again.GetProperty("code").GetString());
        Assert.NotEqual(code, other.GetProperty("code").GetString());
        Assert.Equal($"https://api.trazo.test/invite/{code}", first.GetProperty("link").GetString());
        Assert.Equal(30, first.GetProperty("rewardDays").GetInt32());
        Assert.Equal(0, first.GetProperty("stats").GetProperty("invited").GetInt32());
    }

    [Fact]
    public async Task Code_RequiresSignIn()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/referrals/me")).StatusCode);
    }

    [Fact]
    public void Codes_AreAlwaysFromTheReadableAlphabet_AndNormaliseUserInput()
    {
        for (var i = 0; i < 500; i++) Assert.NotNull(ReferralService.NormalizeCode(ReferralService.NewCode()));
        Assert.Null(ReferralService.NewCode().Intersect("01ILO").FirstOrDefault() is char c && c != default ? "bad" : null);
        Assert.Equal("ABCD2345", ReferralService.NormalizeCode(" abcd-2345 "));
        Assert.Null(ReferralService.NormalizeCode("short"));
        Assert.Null(ReferralService.NormalizeCode("ABCD0OIL")); // look-alikes are not in the alphabet
        Assert.Null(ReferralService.NormalizeCode("'; DROP TABLE users;--"));
    }

    [Fact]
    public async Task ManyUsers_GetDifferentCodes()
    {
        var codes = new HashSet<string>();
        for (var i = 0; i < 25; i++) codes.Add(await CodeOfAsync(await AddUserAsync($"u{i}@example.com")));
        Assert.Equal(25, codes.Count);
    }

    // ------------------------------------------------------------------ attribution

    [Fact]
    public async Task Registration_WithACode_AttributesTheNewAccount_ToTheReferrer()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var code = await CodeOfAsync(referrer);

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = "friend@example.com", password = "CorrectHorse1Battery", displayName = "Friend", referralCode = code.ToLowerInvariant(), referralSource = "install_referrer"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var rel = await DbAsync(db => db.ReferralRelationships.AsNoTracking().SingleAsync());
        Assert.Equal(referrer.Id, rel.ReferrerUserId);
        Assert.Equal(code, rel.Code);
        Assert.Equal("install_referrer", rel.Source);
        Assert.Equal(ReferralStatuses.Registered, rel.Status);
        var events = await DbAsync(db => db.GrowthEvents.AsNoTracking().Where(e => e.Name == "referral_attributed" || e.Name == "signup").ToListAsync());
        Assert.Contains(events, e => e.Name == "referral_attributed" && e.Reference == code && e.Channel == "referral");
        Assert.Contains(events, e => e.Name == "signup");
    }

    [Fact]
    public async Task Registration_WithAnUnknownOrMalformedCode_StillSucceeds_WithoutAttribution()
    {
        await AddUserAsync("referrer@example.com");
        foreach (var bad in new[] { "ZZZZZZZZ", "x", "'; DROP TABLE users;--", new string('A', 500) })
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/register", new
            {
                email = $"f{Guid.NewGuid():N}@example.com", password = "CorrectHorse1Battery", displayName = "Friend", referralCode = bad
            });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.Equal(0, await DbAsync(db => db.ReferralRelationships.CountAsync()));
    }

    [Fact]
    public async Task OwnAlias_OfTheReferrersMailbox_IsNotAttributed()
    {
        var referrer = await AddUserAsync("john.smith@gmail.com");
        var code = await CodeOfAsync(referrer);
        var alias = await AddUserAsync("johnsmith+promo@gmail.com");

        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IReferralService>().AttributeAsync(alias.Id, alias.Email, code, "manual", null, default));
        Assert.False(await scope.ServiceProvider.GetRequiredService<IReferralService>().AttributeAsync(referrer.Id, referrer.Email, code, "manual", null, default));
        Assert.Equal(0, await DbAsync(db => db.ReferralRelationships.CountAsync()));
    }

    [Fact]
    public async Task AnExistingAccount_CanNeverBecomeAReferral()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var code = await CodeOfAsync(referrer);
        var old = await AddUserAsync("old@example.com", createdAt: Now.AddDays(-30));

        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IReferralService>().AttributeAsync(old.Id, old.Email, code, "manual", null, default));
        Assert.Equal(0, await DbAsync(db => db.ReferralRelationships.CountAsync()));
    }

    [Fact]
    public async Task TheReferrer_CannotBeChanged_ByAnotherCode()
    {
        var first = await AddUserAsync("first@example.com");
        var second = await AddUserAsync("second@example.com");
        var friend = await ReferredAsync(first, "friend@example.com");

        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IReferralService>()
            .AttributeAsync(friend.Id, friend.Email, await CodeOfAsync(second), "manual", null, default));
        Assert.Equal(first.Id, (await RelationshipOfAsync(friend.Id)).ReferrerUserId);
    }

    [Fact]
    public async Task ADisabledReferrer_CannotInvite()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var code = await CodeOfAsync(referrer);
        await DbAsync(async db => await db.Users.Where(u => u.Id == referrer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, Now)));
        var friend = await AddUserAsync("friend@example.com");

        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IReferralService>().AttributeAsync(friend.Id, friend.Email, code, "link", null, default));
    }

    [Fact]
    public async Task WhenTheProgrammeIsOff_NothingIsAttributed_AndNoCodeIsOffered()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var referrer = await AddUserAsync("referrer@example.com");
        var code = await CodeOfAsync(referrer);
        await ClientFor(root).PutAsJsonAsync("/api/v1/admin/settings/promotions",
            new { signupTrialEnabled = true, signupTrialDays = 30, referralsEnabled = false, referralRewardDays = 30 });

        var me = await JsonAsync(await ClientFor(referrer).GetAsync("/api/v1/referrals/me"));
        Assert.False(me.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("code").ValueKind);
        var friend = await AddUserAsync("friend@example.com");
        using var scope = _factory.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IReferralService>().AttributeAsync(friend.Id, friend.Email, code, "link", null, default));
    }

    // ------------------------------------------------------------------ conversion

    [Fact]
    public async Task FirstMonthlyPayment_Rewards30Days_ToAReferrerWithoutASubscription()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        Assert.False(await HasAccessAsync(referrer));
        _play.Purchases["tok-f"] = Purchase();

        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(friend, "tok-f")).StatusCode);

        var rel = await RelationshipOfAsync(friend.Id);
        Assert.Equal(ReferralStatuses.Rewarded, rel.Status);
        Assert.Equal("monthly", rel.ConversionPlan);
        Assert.NotNull(rel.ConvertedAt);
        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(30, grant.Days);
        Assert.Equal(PremiumGrantStatuses.Active, grant.Status);
        Assert.Equal(friend.Id, grant.OriginUserId);
        Assert.Equal(rel.ConversionSubscriptionId, grant.OriginSubscriptionId);
        Assert.Equal(rel.RewardGrantId, grant.Id);
        Assert.True(await HasAccessAsync(referrer));
        Assert.Empty(_play.DeferCalls); // no subscription to defer: promotional time, no fake purchase
        Assert.Equal(0, await DbAsync(db => db.Subscriptions.CountAsync(s => s.UserId == referrer.Id)));
    }

    [Fact]
    public async Task FirstAnnualPayment_AlsoRewards()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok-y"] = Purchase(expiresInDays: 364);

        await VerifyAsync(friend, "tok-y");

        var rel = await RelationshipOfAsync(friend.Id);
        Assert.Equal("annual", rel.ConversionPlan);
        Assert.Single(await GrantsOfAsync(referrer.Id));
    }

    [Fact]
    public async Task AFreeTrial_IsNotAConversion_AndThePaymentLaterIs_ExactlyOnce()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok-t"] = Purchase(trial: true, expiresInDays: 14);

        await VerifyAsync(friend, "tok-t");
        Assert.Equal(ReferralStatuses.Registered, (await RelationshipOfAsync(friend.Id)).Status);
        Assert.Empty(await GrantsOfAsync(referrer.Id));

        // The trial ends and the first charge is taken.
        _play.Purchases["tok-t"] = Purchase(trial: false, expiresInDays: 30, order: "GPA.2");
        await NotifyAsync("tok-t");
        await NotifyAsync("tok-t"); // duplicate notification
        // A renewal is not another conversion.
        _play.Purchases["tok-t"] = Purchase(trial: false, expiresInDays: 60, order: "GPA.3");
        await NotifyAsync("tok-t");

        Assert.Equal(ReferralStatuses.Rewarded, (await RelationshipOfAsync(friend.Id)).Status);
        Assert.Single(await GrantsOfAsync(referrer.Id));
    }

    [Fact]
    public async Task APendingPurchase_DoesNotConvert()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok-p"] = Purchase(state: PlaySubscription.Pending);

        await VerifyAsync(friend, "tok-p");

        Assert.Equal(ReferralStatuses.Registered, (await RelationshipOfAsync(friend.Id)).Status);
        Assert.Empty(await GrantsOfAsync(referrer.Id));
    }

    [Fact]
    public async Task ARegularUser_WithoutAReferrer_ProducesNoReward()
    {
        var plain = await AddUserAsync("plain@example.com");
        _play.Purchases["tok-n"] = Purchase();
        await VerifyAsync(plain, "tok-n");
        Assert.Equal(0, await DbAsync(db => db.PremiumGrants.CountAsync()));
    }

    [Fact]
    public async Task Rewards_Accumulate_AndAreChained()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        for (var i = 0; i < 3; i++)
        {
            var friend = await ReferredAsync(referrer, $"friend{i}@example.com");
            _play.Purchases[$"tok-{i}"] = Purchase();
            await VerifyAsync(friend, $"tok-{i}");
        }

        var grants = (await GrantsOfAsync(referrer.Id)).OrderBy(g => g.StartsAt).ToList();
        Assert.Equal(3, grants.Count);
        Assert.Equal(grants[0].EndsAt, grants[1].StartsAt);
        Assert.Equal(grants[1].EndsAt, grants[2].StartsAt);
        var me = await JsonAsync(await ClientFor(referrer).GetAsync("/api/v1/referrals/me"));
        Assert.Equal(90, me.GetProperty("stats").GetProperty("daysEarned").GetInt32());
        Assert.Equal(3, me.GetProperty("stats").GetProperty("converted").GetInt32());
    }

    [Fact]
    public async Task TheRewardDays_FollowTheSuperadminSetting()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        await ClientFor(root).PutAsJsonAsync("/api/v1/admin/settings/promotions",
            new { signupTrialEnabled = true, signupTrialDays = 30, referralsEnabled = true, referralRewardDays = 45 });
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok"] = Purchase();

        await VerifyAsync(friend, "tok");

        Assert.Equal(45, Assert.Single(await GrantsOfAsync(referrer.Id)).Days);
    }

    [Fact]
    public async Task ConcurrentVerifications_OfTheSameFirstPurchase_CreateOneReward()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok-c"] = Purchase();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => VerifyAsync(friend, "tok-c")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Single(await GrantsOfAsync(referrer.Id));
    }

    [Fact]
    public async Task APurchaseThatIsRestoredOnAnotherAccount_IsRefused_AndNeverRewardsTwice()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        var thief = await ReferredAsync(referrer, "thief@example.com");
        _play.Purchases["tok-1"] = Purchase();
        await VerifyAsync(friend, "tok-1");

        Assert.Equal(HttpStatusCode.Conflict, (await VerifyAsync(thief, "tok-1")).StatusCode);

        Assert.Equal(ReferralStatuses.Registered, (await RelationshipOfAsync(thief.Id)).Status);
        Assert.Single(await GrantsOfAsync(referrer.Id));
    }

    [Fact]
    public async Task Reconciliation_ConvertsAnInvitationTheEventPathMissed_AndIsIdempotent()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        // The friend's paid purchase is stored (verified earlier) but the conversion step never ran.
        await DbAsync(async db =>
        {
            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(), UserId = friend.Id, ProductId = Product, PurchaseTokenHash = "h", ProtectedPurchaseToken = "p",
                Status = SubscriptionStatus.Active, StartedAt = Now.AddDays(-5), ExpiresAt = Now.AddDays(25), AutoRenewing = true,
                PaidAt = Now.AddDays(-5), CreatedAt = Now, UpdatedAt = Now
            });
            await db.SaveChangesAsync();
            return 0;
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var reconciler = scope.ServiceProvider.GetRequiredService<ReferralReconciler>();
            Assert.Equal(1, (await reconciler.RunOnceAsync(default)).Conversions);
            Assert.Equal(0, (await reconciler.RunOnceAsync(default)).Conversions);
        }
        Assert.Single(await GrantsOfAsync(referrer.Id));
    }

    // ------------------------------------------------------------------ Google Play deferral

    private async Task<TrazoUser> SubscribedReferrerAsync(string token = "tok-ref", int expiresInDays = 20)
    {
        var referrer = await AddUserAsync("referrer@example.com");
        _play.Purchases[token] = Purchase(expiresInDays: expiresInDays, order: "REF.1");
        await VerifyAsync(referrer, token);
        return referrer;
    }

    private async Task ConvertAsync(TrazoUser referrer, string email, string token)
    {
        var friend = await ReferredAsync(referrer, email);
        _play.Purchases[token] = Purchase(order: "F." + token);
        await VerifyAsync(friend, token);
    }

    [Fact]
    public async Task ReferrerWithAnActiveSubscription_GetsTheRenewalDeferred_OnlyWhenGoogleConfirms()
    {
        var referrer = await SubscribedReferrerAsync();
        var expiryBefore = _play.Purchases["tok-ref"].ExpiryTime;

        await ConvertAsync(referrer, "friend@example.com", "tok-f");

        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Deferred, grant.Status);
        Assert.Equal(expiryBefore, grant.GoogleExpiryBefore);
        Assert.Equal(expiryBefore.AddDays(30), grant.GoogleExpiryAfter);
        Assert.Equal(expiryBefore.AddDays(30), _play.Purchases["tok-ref"].ExpiryTime);
        // A dry run first, then the real call, both for 30 days and only for the referrer's purchase.
        Assert.Equal([("tok-ref", 30d, true), ("tok-ref", 30d, false)], _play.DeferCalls);
        // The deferral gives no promotional window: the extra days come from the subscription itself.
        Assert.Equal(1, await DbAsync(db => db.Subscriptions.CountAsync(s => s.UserId == referrer.Id)));
        Assert.Equal(expiryBefore.AddDays(30), (await DbAsync(db => db.Subscriptions.AsNoTracking().SingleAsync(s => s.UserId == referrer.Id))).ExpiresAt);
    }

    [Fact]
    public async Task SeveralRewards_AccumulateSeveralDeferrals()
    {
        var referrer = await SubscribedReferrerAsync();
        var before = _play.Purchases["tok-ref"].ExpiryTime;
        await ConvertAsync(referrer, "a@example.com", "tok-a");
        await ConvertAsync(referrer, "b@example.com", "tok-b");

        Assert.All(await GrantsOfAsync(referrer.Id), g => Assert.Equal(PremiumGrantStatuses.Deferred, g.Status));
        Assert.Equal(before.AddDays(60), _play.Purchases["tok-ref"].ExpiryTime);
    }

    [Fact]
    public async Task WhenGoogleRejectsTheDeferral_TheRewardStaysPending_AndNothingIsInvented()
    {
        var referrer = await SubscribedReferrerAsync();
        _play.Defer = FakePlayBillingClient.DeferBehaviour.Rejected;

        await ConvertAsync(referrer, "friend@example.com", "tok-f");

        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Pending, grant.Status);
        Assert.Contains("defer_failed", grant.LastDeferError);
        Assert.Null(grant.GoogleExpiryAfter);
        Assert.Null(grant.StartsAt); // no promotional window while the subscription still renews
        var me = await JsonAsync(await ClientFor(referrer).GetAsync("/api/v1/referrals/me"));
        Assert.Equal(30, me.GetProperty("stats").GetProperty("daysPending").GetInt32());
        Assert.Equal(0, me.GetProperty("stats").GetProperty("daysEarned").GetInt32());
    }

    [Fact]
    public async Task APendingReward_BecomesAPromotionalWindow_WhenTheSubscriptionStopsRenewing_AfterThePaidPeriod()
    {
        var referrer = await SubscribedReferrerAsync(expiresInDays: 20);
        _play.Defer = FakePlayBillingClient.DeferBehaviour.Rejected;
        await ConvertAsync(referrer, "friend@example.com", "tok-f");

        // The referrer cancels: the paid period runs to day 20 and does not renew.
        _play.Purchases["tok-ref"] = Purchase(expiresInDays: 20, order: "REF.1", autoRenew: false, state: PlaySubscription.Canceled);
        await NotifyAsync("tok-ref");

        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Active, grant.Status);
        Assert.Equal(Now.AddDays(20), grant.StartsAt); // starts when the paid period ends: nothing overlaps
        Assert.Equal(Now.AddDays(50), grant.EndsAt);
    }

    [Fact]
    public async Task IfGoogleAppliedTheDeferral_ButTheAnswerWasLost_ItIsConfirmedLater_WithoutDeferringTwice()
    {
        var referrer = await SubscribedReferrerAsync();
        var before = _play.Purchases["tok-ref"].ExpiryTime;
        _play.Defer = FakePlayBillingClient.DeferBehaviour.AppliesButAnswerIsLost;
        await ConvertAsync(referrer, "friend@example.com", "tok-f");
        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Pending, grant.Status); // not claimed: Google's answer never arrived
        var realCalls = _play.DeferCalls.Count(c => !c.ValidateOnly);

        using (var scope = _factory.Services.CreateScope())
            Assert.Equal(DeferOutcome.Deferred, await scope.ServiceProvider.GetRequiredService<IReferralRewardService>().ResolveAsync(grant.Id, default));

        Assert.Equal(realCalls, _play.DeferCalls.Count(c => !c.ValidateOnly)); // no second real deferral
        Assert.Equal(before.AddDays(30), _play.Purchases["tok-ref"].ExpiryTime);
        Assert.Equal(PremiumGrantStatuses.Deferred, Assert.Single(await GrantsOfAsync(referrer.Id)).Status);
    }

    [Fact]
    public async Task IfGoogleAnswersWithoutMovingTheDate_TheRewardIsNotClaimed()
    {
        var referrer = await SubscribedReferrerAsync();
        _play.Defer = FakePlayBillingClient.DeferBehaviour.AnswersWithoutMoving;

        await ConvertAsync(referrer, "friend@example.com", "tok-f");

        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Pending, grant.Status);
        Assert.Equal("defer_unconfirmed", grant.LastDeferError);
    }

    [Fact]
    public async Task ARenewalBetweenAttempts_IsNotMistakenForADeferral()
    {
        var referrer = await SubscribedReferrerAsync();
        _play.Defer = FakePlayBillingClient.DeferBehaviour.AnswersWithoutMoving;
        await ConvertAsync(referrer, "friend@example.com", "tok-f");
        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));

        // A normal monthly renewal moves the expiry by about the same 30 days, under a new order id.
        _play.Purchases["tok-ref"] = _play.Purchases["tok-ref"] with { ExpiryTime = _play.Purchases["tok-ref"].ExpiryTime.AddDays(30), LatestOrderId = "REF.2" };
        _play.Defer = FakePlayBillingClient.DeferBehaviour.Works;
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IReferralRewardService>().ResolveAsync(grant.Id, default);

        // The renewal did not count as the reward: a real deferral was still made.
        Assert.Equal(PremiumGrantStatuses.Deferred, Assert.Single(await GrantsOfAsync(referrer.Id)).Status);
        Assert.Equal(2, _play.DeferCalls.Count(c => !c.ValidateOnly));
    }

    [Fact]
    public async Task ReferrerInATrial_WaitsForTheFirstPayment_InsteadOfWastingTheDays()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        _play.Purchases["tok-ref"] = Purchase(trial: true, expiresInDays: 10, order: "REF.1");
        await VerifyAsync(referrer, "tok-ref");

        await ConvertAsync(referrer, "friend@example.com", "tok-f");

        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Pending, grant.Status);
        Assert.Empty(_play.DeferCalls);

        // The first charge is taken: the next sync defers.
        _play.Purchases["tok-ref"] = Purchase(trial: false, expiresInDays: 30, order: "REF.2");
        await NotifyAsync("tok-ref");
        Assert.Equal(PremiumGrantStatuses.Deferred, Assert.Single(await GrantsOfAsync(referrer.Id)).Status);
    }

    [Fact]
    public async Task ReferrerWhoseSubscriptionAlreadyEnded_GetsAPromotionalWindowAtOnce()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        _play.Purchases["tok-ref"] = Purchase(expiresInDays: -3, order: "REF.1", autoRenew: false, state: PlaySubscription.Expired, startedDaysAgo: 40);
        await VerifyAsync(referrer, "tok-ref");

        await ConvertAsync(referrer, "friend@example.com", "tok-f");

        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Active, grant.Status);
        Assert.Equal(Now, grant.StartsAt);
        Assert.Empty(_play.DeferCalls);
        Assert.True(await HasAccessAsync(referrer));
    }

    // ------------------------------------------------------------------ refunds

    [Fact]
    public async Task RefundingTheFirstPayment_CancelsThePromotionalReward()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok-f"] = Purchase();
        await VerifyAsync(friend, "tok-f");
        Assert.True(await HasAccessAsync(referrer));

        await RefundAsync("tok-f");
        await RefundAsync("tok-f"); // duplicate

        Assert.Equal(PremiumGrantStatuses.Voided, Assert.Single(await GrantsOfAsync(referrer.Id)).Status);
        Assert.Equal(ReferralStatuses.Voided, (await RelationshipOfAsync(friend.Id)).Status);
        Assert.False(await HasAccessAsync(referrer));
        Assert.Contains(await DbAsync(db => db.GrowthEvents.AsNoTracking().ToListAsync()), e => e.Name == "referral_reward_voided");
    }

    [Fact]
    public async Task RefundingAfterAGoogleDeferral_KeepsTheDays_AndFlagsAnIncident()
    {
        var referrer = await SubscribedReferrerAsync();
        await ConvertAsync(referrer, "friend@example.com", "tok-f");
        Assert.Equal(PremiumGrantStatuses.Deferred, Assert.Single(await GrantsOfAsync(referrer.Id)).Status);

        await RefundAsync("tok-f");

        Assert.Equal(PremiumGrantStatuses.Deferred, Assert.Single(await GrantsOfAsync(referrer.Id)).Status);
        var rel = await DbAsync(db => db.ReferralRelationships.AsNoTracking().SingleAsync());
        Assert.Equal("reward_not_reversible", rel.Incident);
    }

    [Fact]
    public async Task ALateRefund_DoesNotCancelTheReward()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok-f"] = Purchase();
        await VerifyAsync(friend, "tok-f");
        await DbAsync(async db => await db.ReferralRelationships.ExecuteUpdateAsync(s => s.SetProperty(r => r.ConvertedAt, Now.AddDays(-90))));

        await RefundAsync("tok-f");

        Assert.Equal(PremiumGrantStatuses.Active, Assert.Single(await GrantsOfAsync(referrer.Id)).Status);
        Assert.Equal("late_refund_kept", (await RelationshipOfAsync(friend.Id)).Incident);
    }

    // ------------------------------------------------------------------ what the inviter sees

    [Fact]
    public async Task Invitations_ShowStatusesWithoutExposingThePeople()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var paid = await ReferredAsync(referrer, "paid.person@example.com");
        await ReferredAsync(referrer, "waiting.person@example.com");
        _play.Purchases["tok"] = Purchase(expiresInDays: 364);
        await VerifyAsync(paid, "tok");

        var response = await ClientFor(referrer).GetAsync("/api/v1/referrals/invitations");
        var text = await response.Content.ReadAsStringAsync();

        var items = JsonDocument.Parse(text).RootElement.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(["registered", "rewarded"], items.EnumerateArray().Select(i => i.GetProperty("status").GetString()!).OrderBy(x => x).ToArray());
        Assert.DoesNotContain("person", text);
        Assert.DoesNotContain("@", text);
        Assert.DoesNotContain(paid.Id.ToString(), text);
        var onlyPending = await JsonAsync(await ClientFor(referrer).GetAsync("/api/v1/referrals/invitations?status=registered"));
        Assert.Equal(1, onlyPending.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await ClientFor(referrer).GetAsync("/api/v1/referrals/invitations?status=hack")).StatusCode);
    }

    [Fact]
    public async Task AUser_NeverSeesTheInvitationsOrRewardsOfSomeoneElse()
    {
        var a = await AddUserAsync("a@example.com");
        var b = await AddUserAsync("b@example.com");
        var friend = await ReferredAsync(a, "friend@example.com");
        _play.Purchases["tok"] = Purchase();
        await VerifyAsync(friend, "tok");

        Assert.Equal(0, (await JsonAsync(await ClientFor(b).GetAsync("/api/v1/referrals/invitations"))).GetProperty("items").GetArrayLength());
        Assert.Equal(0, (await JsonAsync(await ClientFor(b).GetAsync("/api/v1/referrals/rewards"))).GetProperty("items").GetArrayLength());
        Assert.Equal(1, (await JsonAsync(await ClientFor(a).GetAsync("/api/v1/referrals/rewards"))).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Sharing_IsCounted_AsAFunnelEvent()
    {
        var a = await AddUserAsync("a@example.com");
        Assert.Equal(HttpStatusCode.NoContent, (await ClientFor(a).PostAsJsonAsync("/api/v1/referrals/shared", new { channel = "whatsapp" })).StatusCode);
        var shared = Assert.Single(await DbAsync(db => db.GrowthEvents.AsNoTracking().Where(e => e.Name == "referral_shared").ToListAsync()));
        Assert.Equal("whatsapp", shared.Channel);
        Assert.Equal(a.Id, shared.UserId);
    }

    // ------------------------------------------------------------------ public pages

    [Fact]
    public async Task InvitePage_CountsTheOpening_AndPointsToGooglePlayWithTheInstallReferrer()
    {
        var a = await AddUserAsync("a@example.com");
        var code = await CodeOfAsync(a);

        var response = await _factory.CreateClient().GetAsync($"/invite/{code.ToLowerInvariant()}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("play.google.com/store/apps/details?id=com.trazo.android", html);
        Assert.Contains("referrer=trazo_ref%3D" + code, html);
        Assert.Contains("style-src 'sha256-", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Single(await DbAsync(db => db.GrowthEvents.AsNoTracking().Where(e => e.Name == "referral_link_opened" && e.Reference == code).ToListAsync()));
    }

    [Fact]
    public async Task InvitePage_WithAnInvalidCode_StillOffersTheApp_WithoutCountingOrReflecting()
    {
        var response = await _factory.CreateClient().GetAsync("/invite/%3Cscript%3Ealert(1)%3C%2Fscript%3E");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("referrer=", html);
        Assert.Equal(0, await DbAsync(db => db.GrowthEvents.CountAsync(e => e.Name == "referral_link_opened")));
    }

    [Fact]
    public async Task AssetLinks_AreServedOnlyWhenFingerprintsAreConfigured()
    {
        var response = await _factory.CreateClient().GetAsync("/.well-known/assetlinks.json");
        var doc = await JsonAsync(response);
        Assert.Equal("com.trazo.android", doc[0].GetProperty("target").GetProperty("package_name").GetString());
        Assert.Equal("AA:BB:CC", doc[0].GetProperty("target").GetProperty("sha256_cert_fingerprints")[0].GetString());
    }

    [Fact]
    public async Task InstallReferrer_IsCountedOnlyForRealCodes()
    {
        var a = await AddUserAsync("a@example.com");
        var code = await CodeOfAsync(a);
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/referrals/install-referrer", new { code });
        await client.PostAsJsonAsync("/api/v1/referrals/install-referrer", new { code = "ZZZZZZZZ" });
        Assert.Equal(1, await DbAsync(db => db.GrowthEvents.CountAsync(e => e.Name == "referral_install_attributed")));
    }

    [Fact]
    public async Task ClientEvents_AcceptOnlyUsageMilestones_NeverMoneyEvents()
    {
        var a = await AddUserAsync("a@example.com");
        var client = ClientFor(a);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/analytics/events", new { name = "onboarding_completed" })).StatusCode);
        await client.PostAsJsonAsync("/api/v1/analytics/events", new { name = "onboarding_completed" });
        foreach (var forged in new[] { "first_purchase", "renewed", "referral_reward_granted", "refunded", "x" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/analytics/events", new { name = forged })).StatusCode);
        Assert.Equal(1, await DbAsync(db => db.GrowthEvents.CountAsync()));
    }

    // ------------------------------------------------------------------ administration

    [Fact]
    public async Task AdminEndpoints_AreSuperadminOnly()
    {
        var user = await AddUserAsync("user@example.com");
        var admin = await AddUserAsync("admin@example.com", "admin");
        foreach (var path in new[] { "/api/v1/admin/referrals/summary", "/api/v1/admin/referrals", "/api/v1/admin/analytics/funnel" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(user).GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(admin).GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync(path)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(admin).PostAsJsonAsync($"/api/v1/admin/referrals/grants/{Guid.NewGuid()}/void", new { reason = "because" })).StatusCode);
    }

    [Fact]
    public async Task Admin_SeesTheFunnelAndTheLedger_AndCanVoidWithAReasonThatIsRecorded()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        await ClientFor(referrer).PostAsJsonAsync("/api/v1/referrals/shared", new { });
        await _factory.CreateClient().GetAsync($"/invite/{await CodeOfAsync(referrer)}");
        _play.Purchases["tok"] = Purchase();
        await VerifyAsync(friend, "tok");

        var summary = await JsonAsync(await ClientFor(root).GetAsync("/api/v1/admin/referrals/summary"));
        Assert.Equal(1, summary.GetProperty("attributed").GetInt32());
        Assert.Equal(1, summary.GetProperty("rewarded").GetInt32());
        Assert.Equal(30, summary.GetProperty("rewardDaysGranted").GetInt32());
        Assert.Equal(1, summary.GetProperty("linkOpens").GetInt32());
        Assert.Equal(1, summary.GetProperty("shares").GetInt32());
        Assert.Equal(1.0, summary.GetProperty("registeredToPaid").GetDouble());
        Assert.Equal(1, summary.GetProperty("paidMonthly").GetInt32());

        var list = await JsonAsync(await ClientFor(root).GetAsync("/api/v1/admin/referrals?status=rewarded"));
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var grantId = Assert.Single(await GrantsOfAsync(referrer.Id)).Id;
        var ledger = await JsonAsync(await ClientFor(root).GetAsync($"/api/v1/admin/referrals/users/{referrer.Id}/ledger"));
        Assert.Equal("referral_reward", ledger.GetProperty("items")[0].GetProperty("kind").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await ClientFor(root).PostAsJsonAsync($"/api/v1/admin/referrals/grants/{grantId}/void", new { reason = "no" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(root).PostAsJsonAsync($"/api/v1/admin/referrals/grants/{grantId}/void", new { reason = "Confirmed abuse ring" })).StatusCode);
        var voided = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Equal(PremiumGrantStatuses.Voided, voided.Status);
        Assert.Equal("Confirmed abuse ring", voided.VoidReason);
        Assert.Equal(root.Id.ToString(), voided.VoidedBy);
        Assert.Equal(ReferralStatuses.Voided, (await RelationshipOfAsync(friend.Id)).Status);
    }

    [Fact]
    public async Task Funnel_MeasuresTheWholeJourney_ForEveryAccount_NotJustReferrals()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var referrer = await AddUserAsync("referrer@example.com");
        var referred = await ReferredAsync(referrer, "friend@example.com");
        var organic = await AddUserAsync("organic@example.com");
        using (var scope = _factory.Services.CreateScope())
        {
            var growth = scope.ServiceProvider.GetRequiredService<ISignupTrialService>();
            foreach (var u in new[] { referred, organic }) { await growth.SignedUpAsync(u.Id, "1.2.3", default); await growth.ActivatedAsync(u.Id, default); }
        }
        _play.Purchases["tok"] = Purchase(trial: true, expiresInDays: 14);
        await VerifyAsync(referred, "tok");
        _play.Purchases["tok"] = Purchase(order: "GPA.2");
        await NotifyAsync("tok");

        var allResponse = await ClientFor(root).GetAsync("/api/v1/admin/analytics/funnel");
        Assert.True(allResponse.IsSuccessStatusCode, await allResponse.Content.ReadAsStringAsync());
        var all = await JsonAsync(allResponse);
        Assert.Equal(2, all.GetProperty("cohort").GetInt32());
        int Stage(JsonElement f, string n) => f.GetProperty("stages").EnumerateArray().Single(s => s.GetProperty("name").GetString() == n).GetProperty("users").GetInt32();
        Assert.Equal(2, Stage(all, "email_verified"));
        Assert.Equal(1, Stage(all, "trial_started"));
        Assert.Equal(1, Stage(all, "first_purchase"));
        Assert.Equal(1.0, all.GetProperty("trialToPaid").GetDouble());
        Assert.Equal(0.5, all.GetProperty("signupToPaid").GetDouble());

        var referralOnly = await JsonAsync(await ClientFor(root).GetAsync("/api/v1/admin/analytics/funnel?channel=referral"));
        Assert.Equal(1, referralOnly.GetProperty("cohort").GetInt32());
        var organicOnly = await JsonAsync(await ClientFor(root).GetAsync("/api/v1/admin/analytics/funnel?channel=organic"));
        Assert.Equal(1, organicOnly.GetProperty("cohort").GetInt32());
        Assert.Equal(0, Stage(organicOnly, "first_purchase"));
        Assert.Equal(HttpStatusCode.BadRequest, (await ClientFor(root).GetAsync("/api/v1/admin/analytics/funnel?channel=bogus")).StatusCode);
    }

    [Fact]
    public async Task EventsOfAReferredUser_CarryTheReferralChannelAndCode()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok"] = Purchase();
        await VerifyAsync(friend, "tok");

        var purchase = Assert.Single(await DbAsync(db => db.GrowthEvents.AsNoTracking().Where(e => e.Name == "first_purchase").ToListAsync()));
        Assert.Equal("referral", purchase.Channel);
        Assert.Equal(await CodeOfAsync(referrer), purchase.Reference);
        Assert.Equal(Product, purchase.Plan);
    }

    [Fact]
    public async Task DeletingTheInvitedAccount_KeepsTheReferrersReward_WithoutTheLink()
    {
        var referrer = await AddUserAsync("referrer@example.com");
        var friend = await ReferredAsync(referrer, "friend@example.com");
        _play.Purchases["tok"] = Purchase();
        await VerifyAsync(friend, "tok");

        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Trazo.Infrastructure.Accounts.IAccountService>().DeleteAsync(friend.Id, default);

        var grant = Assert.Single(await GrantsOfAsync(referrer.Id));
        Assert.Null(grant.OriginUserId);
        Assert.True(await HasAccessAsync(referrer));
        Assert.Equal(0, await DbAsync(db => db.ReferralRelationships.CountAsync()));
    }
}
