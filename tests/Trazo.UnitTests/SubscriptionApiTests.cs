using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Billing;
using Trazo.Api.Security;
using Trazo.Domain.Billing;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>Stand-in for Google Play: returns configured purchases and records acknowledgements.</summary>
public sealed class FakePlayBillingClient : IPlayBillingClient
{
    public Dictionary<string, PlaySubscription> Purchases { get; } = new();
    public List<string> Acknowledged { get; } = [];
    public PlayBillingException? Failure { get; set; }
    public int Lookups { get; private set; }

    public Task<PlaySubscription> GetSubscriptionAsync(string purchaseToken, CancellationToken ct)
    {
        Lookups++;
        if (Failure is not null) throw Failure;
        return Purchases.TryGetValue(purchaseToken, out var p)
            ? Task.FromResult(p)
            : throw new PlayBillingException(PlayBillingFailure.InvalidPurchase, "unknown");
    }

    public Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken ct)
    {
        Acknowledged.Add(purchaseToken);
        return Task.CompletedTask;
    }

    public Task CheckConnectionAsync(CancellationToken ct) =>
        Failure is not null ? throw Failure : Task.CompletedTask;
}

public sealed class FakePubSubValidator : IPubSubTokenValidator
{
    public Task<string?> ValidateAsync(string jwt, string audience, CancellationToken ct) =>
        Task.FromResult<string?>(jwt == "good-token" && audience == "https://api.test/notifications" ? "pusher@project.iam.gserviceaccount.com" : null);
}

/// <summary>SQLite cannot order by DateTimeOffset; store it as a sortable number in these tests (PostgreSQL has no such limit).</summary>
public sealed class SqliteDateTimeOffsetCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
                     .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
            property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
    }
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

public sealed class SubscriptionApiTests : IDisposable
{
    private const string Product = "trazo_premium";
    private const string Secret = "0123456789abcdef-secret";
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly FakePlayBillingClient _play = new();
    private readonly WebApplicationFactory<Program> _factory;

    public SubscriptionApiTests()
    {
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
            builder.UseSetting("Billing:Play:RtdnAudience", "https://api.test/notifications");
            builder.UseSetting("Billing:Play:RtdnServiceAccountEmail", "pusher@project.iam.gserviceaccount.com");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString)
                    .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>());
                services.RemoveAll<IPlayBillingClient>();
                services.AddSingleton<IPlayBillingClient>(_play);
                services.RemoveAll<IPubSubTokenValidator>();
                services.AddSingleton<IPubSubTokenValidator, FakePubSubValidator>();
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

    private async Task<TrazoUser> AddUserAsync(string email, string role = "user")
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

    private PlaySubscription Purchase(string state = PlaySubscription.Active, bool trial = false, int expiresInDays = 14,
        bool acknowledged = false, string? account = null) =>
        new(state, Product, Now.AddDays(-1), Now.AddDays(expiresInDays), true, trial, acknowledged, "GPA.1", account, false);

    private Task<HttpResponseMessage> VerifyAsync(TrazoUser user, string token, string product = Product) =>
        ClientFor(user).PostAsJsonAsync("/api/v1/subscription/verify", new { productId = product, purchaseToken = token });

    private HttpRequestMessage Notification(string json, string? secret = Secret, string? bearer = null)
    {
        var body = JsonSerializer.Serialize(new { message = new { data = Convert.ToBase64String(Encoding.UTF8.GetBytes(json)), messageId = "1" } });
        var url = "/api/v1/subscription/notifications" + (secret is null ? "" : $"?secret={secret}");
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return request;
    }

    private static string SubscriptionNotification(string token, string package = "com.trazo.android") =>
        JsonSerializer.Serialize(new
        {
            version = "1.0", packageName = package, eventTimeMillis = "1",
            subscriptionNotification = new { version = "1.0", notificationType = 2, purchaseToken = token, subscriptionId = Product }
        });

    private async Task<List<Subscription>> RowsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().Subscriptions.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task Verify_FreeTrial_StoresTheSubscription_AcknowledgesIt_AndGrantsAccess()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase(trial: true);

        var response = await VerifyAsync(user, "tok-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.True(body.GetProperty("hasAccess").GetBoolean());
        Assert.Equal("trial", body.GetProperty("status").GetString());
        Assert.Equal(Product, body.GetProperty("productId").GetString());
        Assert.Equal(["tok-1"], _play.Acknowledged);
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(user.Id, row.UserId);
        Assert.NotNull(row.AcknowledgedAt);
        Assert.DoesNotContain("tok-1", row.ProtectedPurchaseToken); // stored encrypted
        Assert.Equal(SubscriptionService.HashToken("tok-1"), row.PurchaseTokenHash);
    }

    [Fact]
    public async Task Verify_AlreadyAcknowledgedPurchase_IsNotAcknowledgedAgain()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase(acknowledged: true);
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(user, "tok-1")).StatusCode);
        Assert.Empty(_play.Acknowledged);
    }

    [Fact]
    public async Task Verify_SameUserTwice_IsIdempotent()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase();
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(user, "tok-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(user, "tok-1")).StatusCode);
        Assert.Single(await RowsAsync());
        Assert.Single(_play.Acknowledged);
    }

    [Fact]
    public async Task Verify_TokenAlreadyLinkedToAnotherUser_IsRejected_AndNothingChanges()
    {
        var alice = await AddUserAsync("alice@example.com");
        var mallory = await AddUserAsync("mallory@example.com");
        _play.Purchases["tok-1"] = Purchase();
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(alice, "tok-1")).StatusCode);

        var stolen = await VerifyAsync(mallory, "tok-1");

        Assert.Equal(HttpStatusCode.Conflict, stolen.StatusCode);
        Assert.Equal("purchase_already_linked", (await JsonAsync(stolen)).GetProperty("error").GetString());
        Assert.Equal(alice.Id, Assert.Single(await RowsAsync()).UserId);
        var status = await JsonAsync(await ClientFor(mallory).GetAsync("/api/v1/subscription"));
        Assert.False(status.GetProperty("hasAccess").GetBoolean());
    }

    [Fact]
    public async Task Verify_PurchaseMadeByAnotherAccount_IsRejected()
    {
        var alice = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase(account: Guid.NewGuid().ToString());
        var response = await VerifyAsync(alice, "tok-1");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("purchase_account_mismatch", (await JsonAsync(response)).GetProperty("error").GetString());
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task Verify_PurchaseWithMatchingAccountId_IsAccepted()
    {
        var alice = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase(account: alice.Id.ToString());
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(alice, "tok-1")).StatusCode);
    }

    [Fact]
    public async Task Verify_RejectsUnknownTokens_WrongProducts_AndBadInput()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase();

        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(user, "does-not-exist")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(user, "tok-1", "other_product")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(user, "")).StatusCode);
        Assert.Empty(await RowsAsync());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ClientFor(null).PostAsJsonAsync("/api/v1/subscription/verify", new { productId = Product, purchaseToken = "x" })).StatusCode);
    }

    [Fact]
    public async Task Verify_WhenGoogleIsDownOrMisconfigured_ReturnsAServerError_AndStoresNothing()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Failure = new PlayBillingException(PlayBillingFailure.Unavailable, "down");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await VerifyAsync(user, "tok-1")).StatusCode);
        _play.Failure = new PlayBillingException(PlayBillingFailure.NotConfigured, "no config");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await VerifyAsync(user, "tok-1")).StatusCode);
        _play.Failure = new PlayBillingException(PlayBillingFailure.Unauthorized, "bad key");
        Assert.Equal(HttpStatusCode.BadGateway, (await VerifyAsync(user, "tok-1")).StatusCode);
        Assert.Empty(await RowsAsync());
    }

    [Theory]
    [InlineData(PlaySubscription.Active, false, 14, "active", true)]
    [InlineData(PlaySubscription.Active, true, 14, "trial", true)]
    [InlineData(PlaySubscription.InGracePeriod, false, 3, "grace_period", true)]
    [InlineData(PlaySubscription.OnHold, false, -1, "on_hold", false)]
    [InlineData(PlaySubscription.Paused, false, 10, "on_hold", false)]
    [InlineData(PlaySubscription.Pending, false, 10, "pending", false)]
    [InlineData(PlaySubscription.Canceled, false, 10, "canceled", true)]   // cancelled but paid until the end date
    [InlineData(PlaySubscription.Canceled, false, -2, "expired", false)]
    [InlineData(PlaySubscription.Expired, false, -30, "expired", false)]
    [InlineData(PlaySubscription.PendingPurchaseCanceled, false, 0, "expired", false)]
    public async Task GoogleStates_MapToTrazoStatusAndAccess(string state, bool trial, int days, string expected, bool access)
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase(state, trial, days);

        var response = await VerifyAsync(user, "tok-1");

        var body = await JsonAsync(response);
        Assert.Equal(expected, body.GetProperty("status").GetString());
        Assert.Equal(access, body.GetProperty("hasAccess").GetBoolean());
    }

    [Fact]
    public async Task Get_WithoutSubscription_ReportsNoAccess_AndRequiresSignIn()
    {
        var user = await AddUserAsync("alice@example.com");
        var body = await JsonAsync(await ClientFor(user).GetAsync("/api/v1/subscription"));
        Assert.False(body.GetProperty("hasAccess").GetBoolean());
        Assert.Equal("none", body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/subscription")).StatusCode);
    }

    [Fact]
    public async Task Get_ReportsExpiredWhenThePeriodEndedBeforeAnyNotificationArrived()
    {
        var user = await AddUserAsync("alice@example.com");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            db.Subscriptions.Add(new Subscription
            {
                Id = Guid.NewGuid(), UserId = user.Id, ProductId = Product, PurchaseTokenHash = "h", ProtectedPurchaseToken = "p",
                Status = SubscriptionStatus.Active, StartedAt = Now.AddDays(-40), ExpiresAt = Now.AddDays(-1),
                CreatedAt = Now, UpdatedAt = Now
            });
            await db.SaveChangesAsync();
        }
        var body = await JsonAsync(await ClientFor(user).GetAsync("/api/v1/subscription"));
        Assert.False(body.GetProperty("hasAccess").GetBoolean());
        Assert.Equal("expired", body.GetProperty("status").GetString());
    }

    // ---- Pub/Sub webhook ----

    [Fact]
    public async Task Webhook_RejectsRequestsWithoutValidCredentials()
    {
        var client = _factory.CreateClient();
        var body = SubscriptionNotification("tok-1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Notification(body, secret: null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Notification(body, secret: "wrong-secret-value"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(Notification(body, secret: null, bearer: "bad-token"))).StatusCode);
        Assert.Equal(0, _play.Lookups);
    }

    [Fact]
    public async Task Webhook_AcceptsAPubSubOidcToken_AndASharedSecret()
    {
        var client = _factory.CreateClient();
        var body = SubscriptionNotification("tok-1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Notification(body, secret: null, bearer: "good-token"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Notification(body))).StatusCode);
    }

    [Fact]
    public async Task Webhook_ReconsultsGoogle_InsteadOfTrustingThePayload()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase();
        await VerifyAsync(user, "tok-1");

        // The notification claims nothing about the state; Google now says the subscription is on hold.
        _play.Purchases["tok-1"] = Purchase(PlaySubscription.OnHold, expiresInDays: -1, acknowledged: true);
        var response = await _factory.CreateClient().SendAsync(Notification(SubscriptionNotification("tok-1")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("processed").GetBoolean());
        var row = Assert.Single(await RowsAsync());
        Assert.Equal(SubscriptionStatus.OnHold, row.Status);
        var status = await JsonAsync(await ClientFor(user).GetAsync("/api/v1/subscription"));
        Assert.False(status.GetProperty("hasAccess").GetBoolean());
    }

    [Fact]
    public async Task Webhook_RenewalExtendsTheExpiry()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase(expiresInDays: 5, acknowledged: true);
        await VerifyAsync(user, "tok-1");
        _play.Purchases["tok-1"] = Purchase(expiresInDays: 35, acknowledged: true);

        await _factory.CreateClient().SendAsync(Notification(SubscriptionNotification("tok-1")));

        Assert.Equal(Now.AddDays(35), Assert.Single(await RowsAsync()).ExpiresAt);
    }

    [Fact]
    public async Task Webhook_VoidedPurchase_RevokesAccessImmediately()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase();
        await VerifyAsync(user, "tok-1");
        var voided = JsonSerializer.Serialize(new
        {
            version = "1.0", packageName = "com.trazo.android", eventTimeMillis = "1",
            voidedPurchaseNotification = new { purchaseToken = "tok-1", orderId = "GPA.1", productType = 1, refundType = 1 }
        });

        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().SendAsync(Notification(voided))).StatusCode);

        Assert.Equal(SubscriptionStatus.Revoked, Assert.Single(await RowsAsync()).Status);
        var status = await JsonAsync(await ClientFor(user).GetAsync("/api/v1/subscription"));
        Assert.False(status.GetProperty("hasAccess").GetBoolean());
        Assert.Equal("revoked", status.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Webhook_IgnoresTestMessages_UnknownTokens_OtherPackages_AndMalformedBodies()
    {
        var client = _factory.CreateClient();
        var test = JsonSerializer.Serialize(new { version = "1.0", packageName = "com.trazo.android", testNotification = new { version = "1.0" } });
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Notification(test))).StatusCode);

        var unknown = await client.SendAsync(Notification(SubscriptionNotification("never-verified")));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.False((await JsonAsync(unknown)).GetProperty("processed").GetBoolean());

        var other = await client.SendAsync(Notification(SubscriptionNotification("tok-1", package: "com.someone.else")));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);

        var malformed = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/subscription/notifications?secret={Secret}")
            { Content = new StringContent("not json", Encoding.UTF8, "application/json") };
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(malformed)).StatusCode);
        Assert.Equal(0, _play.Lookups); // only the "unknown token" case never reaches Google, and the others neither
    }

    [Fact]
    public async Task Webhook_AsksPubSubToRetry_WhenGoogleIsUnavailable()
    {
        var user = await AddUserAsync("alice@example.com");
        _play.Purchases["tok-1"] = Purchase();
        await VerifyAsync(user, "tok-1");
        _play.Failure = new PlayBillingException(PlayBillingFailure.Unavailable, "down");

        var response = await _factory.CreateClient().SendAsync(Notification(SubscriptionNotification("tok-1")));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    // ---- Access policy ----

    private async Task<HttpResponseMessage> RunFilterAsync(TrazoUser? user)
    {
        using var scope = _factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        if (user is not null)
            http.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role)], "test", "name", ClaimTypes.Role));
        var reached = false;
        var result = await new SubscriptionAccessFilter().InvokeAsync(
            new DefaultEndpointFilterInvocationContext(http), _ => { reached = true; return ValueTask.FromResult<object?>("ok"); });
        if (reached) return new HttpResponseMessage(HttpStatusCode.OK);
        http.Response.Body = new MemoryStream();
        await ((IResult)result!).ExecuteAsync(http);
        return new HttpResponseMessage((HttpStatusCode)http.Response.StatusCode);
    }

    [Fact]
    public async Task AccessFilter_AllowsTrialActiveAndGrace_BlocksEverythingElse()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await RunFilterAsync(null)).StatusCode);

        var none = await AddUserAsync("none@example.com");
        Assert.Equal(HttpStatusCode.PaymentRequired, (await RunFilterAsync(none)).StatusCode);

        var i = 0;
        foreach (var (state, trial, days, allowed) in new[]
        {
            (PlaySubscription.Active, true, 7, true), (PlaySubscription.Active, false, 20, true),
            (PlaySubscription.InGracePeriod, false, 2, true), (PlaySubscription.Canceled, false, 9, true),
            (PlaySubscription.OnHold, false, -1, false), (PlaySubscription.Expired, false, -10, false)
        })
        {
            var user = await AddUserAsync($"user{i}@example.com");
            _play.Purchases[$"tok-{i}"] = Purchase(state, trial, days, acknowledged: true);
            await VerifyAsync(user, $"tok-{i}");
            Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.PaymentRequired, (await RunFilterAsync(user)).StatusCode);
            i++;
        }
    }

    [Fact]
    public async Task AccessFilter_LetsAdminsAndSuperadminsThrough_WithoutASubscription()
    {
        Assert.Equal(HttpStatusCode.OK, (await RunFilterAsync(await AddUserAsync("a@example.com", "admin"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RunFilterAsync(await AddUserAsync("s@example.com", "superadmin"))).StatusCode);
    }

    [Fact]
    public void Grants_RequiresBothAnAccessStateAndAFutureExpiry()
    {
        Assert.True(SubscriptionAccess.Grants(SubscriptionStatus.Trial, Now.AddSeconds(1), Now));
        Assert.False(SubscriptionAccess.Grants(SubscriptionStatus.Active, Now, Now));
        Assert.False(SubscriptionAccess.Grants(SubscriptionStatus.Revoked, Now.AddDays(30), Now));
        Assert.False(SubscriptionAccess.Grants(SubscriptionStatus.OnHold, Now.AddDays(30), Now));
        Assert.False(SubscriptionAccess.Grants(SubscriptionStatus.Pending, Now.AddDays(30), Now));
    }

    // ---- Administration console ----

    [Fact]
    public async Task AdminSubscriptions_AreSuperadminOnly_AndFilterByStatusUserAndExpiry()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var alice = await AddUserAsync("alice@example.com");
        var bob = await AddUserAsync("bob@example.com");
        _play.Purchases["a"] = Purchase(trial: true, expiresInDays: 5, acknowledged: true);
        _play.Purchases["b"] = Purchase(PlaySubscription.Expired, expiresInDays: -30, acknowledged: true);
        await VerifyAsync(alice, "a");
        await VerifyAsync(bob, "b");
        var admin = ClientFor(root);

        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(alice).GetAsync("/api/v1/admin/subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/admin/subscriptions")).StatusCode);

        var all = await JsonAsync(await admin.GetAsync("/api/v1/admin/subscriptions"));
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await admin.GetAsync("/api/v1/admin/subscriptions?status=trial"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await admin.GetAsync("/api/v1/admin/subscriptions?q=BOB"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await admin.GetAsync($"/api/v1/admin/subscriptions?user={alice.Id}"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await admin.GetAsync("/api/v1/admin/subscriptions?source=google_play&pageSize=1"))).GetProperty("items").GetArrayLength() );
        var from = Uri.EscapeDataString(Now.ToString("O"));
        var soon = await JsonAsync(await admin.GetAsync($"/api/v1/admin/subscriptions?expiresFrom={from}"));
        Assert.Equal(1, soon.GetProperty("totalCount").GetInt32());
        Assert.Equal("alice@example.com", soon.GetProperty("items")[0].GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/v1/admin/subscriptions?status=bogus")).StatusCode);

        var id = all.GetProperty("items")[0].GetProperty("id").GetString();
        var detail = await JsonAsync(await admin.GetAsync($"/api/v1/admin/subscriptions/{id}"));
        Assert.Equal(12, detail.GetProperty("purchaseTokenFingerprint").GetString()!.Length);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/subscriptions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task AdminRefresh_ReadsTheStoredTokenAndUpdatesTheRow()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var alice = await AddUserAsync("alice@example.com");
        _play.Purchases["a"] = Purchase(expiresInDays: 5, acknowledged: true);
        await VerifyAsync(alice, "a");
        _play.Purchases["a"] = Purchase(PlaySubscription.Expired, expiresInDays: -1, acknowledged: true);
        var id = (await RowsAsync()).Single().Id;

        var response = await ClientFor(root).PostAsync($"/api/v1/admin/subscriptions/{id}/refresh", null);

        Assert.Equal("expired", (await JsonAsync(response)).GetProperty("subscription").GetProperty("status").GetString());
    }

    [Fact]
    public async Task PlayBillingSettings_AreSuperadminOnly_NeverReturnSecrets_AndKeepSecretsWhenOmitted()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("alice@example.com");
        var key = JsonSerializer.Serialize(new
        {
            type = "service_account", client_email = "billing@project.iam.gserviceaccount.com",
            private_key = "-----BEGIN PRIVATE KEY-----\nSECRETKEYMATERIAL\n-----END PRIVATE KEY-----\n"
        });
        var client = ClientFor(root);

        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(user).GetAsync("/api/v1/admin/settings/play-billing")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/admin/settings/play-billing",
            new { packageName = "not a package", serviceAccountJson = "{}" })).StatusCode);

        var save = await client.PutAsJsonAsync("/api/v1/admin/settings/play-billing", new
        {
            packageName = "com.trazo.android", serviceAccountJson = key, rtdnSecret = "a-long-random-secret-value",
            rtdnAudience = "https://api.trazo.example/api/v1/subscription/notifications", rtdnServiceAccountEmail = ""
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var raw = await (await client.GetAsync("/api/v1/admin/settings/play-billing")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("SECRETKEYMATERIAL", raw);
        Assert.DoesNotContain("a-long-random-secret-value", raw);
        var view = JsonDocument.Parse(raw).RootElement;
        Assert.True(view.GetProperty("serviceAccountSet").GetBoolean());
        Assert.Equal("billing@project.iam.gserviceaccount.com", view.GetProperty("serviceAccountEmail").GetString());
        Assert.True(view.GetProperty("rtdnSecretSet").GetBoolean());

        // Omitting the secrets keeps them; the stored value is encrypted at rest.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/admin/settings/play-billing",
            new { packageName = "com.trazo.android", rtdnAudience = "https://x.example/n" })).StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var row = await db.AppSettings.AsNoTracking().SingleAsync(x => x.Key == "Billing:Play:ServiceAccountJson");
        Assert.True(row.IsSecret);
        Assert.DoesNotContain("SECRETKEYMATERIAL", row.Value);
        Assert.True((await JsonAsync(await client.GetAsync("/api/v1/admin/settings/play-billing"))).GetProperty("serviceAccountSet").GetBoolean());
    }

    [Fact]
    public async Task PlayBillingConnectionTest_ReportsGoogleFailuresWithoutThrowing()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var client = ClientFor(root);
        Assert.True((await JsonAsync(await client.PostAsync("/api/v1/admin/settings/play-billing/test", null))).GetProperty("ok").GetBoolean());
        _play.Failure = new PlayBillingException(PlayBillingFailure.Unauthorized, "Google Play rejected the service account.");
        var failed = await JsonAsync(await client.PostAsync("/api/v1/admin/settings/play-billing/test", null));
        Assert.False(failed.GetProperty("ok").GetBoolean());
    }

    // ---- Enforcement switch ----

    [Fact]
    public async Task NotEnforced_EveryAuthenticatedUserHasAccess_AndTheStatusSaysSo()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("free@example.com");
        Assert.Equal(HttpStatusCode.PaymentRequired, (await RunFilterAsync(user)).StatusCode);
        Assert.True((await JsonAsync(await ClientFor(user).GetAsync("/api/v1/subscription"))).GetProperty("enforced").GetBoolean());

        var off = await ClientFor(root).PutAsJsonAsync("/api/v1/admin/settings/access", new { subscriptionsEnforced = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var body = await JsonAsync(await ClientFor(user).GetAsync("/api/v1/subscription"));
        Assert.True(body.GetProperty("hasAccess").GetBoolean());
        Assert.False(body.GetProperty("enforced").GetBoolean());
        Assert.Equal("none", body.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await RunFilterAsync(user)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RunFilterAsync(null)).StatusCode);

        await ClientFor(root).PutAsJsonAsync("/api/v1/admin/settings/access", new { subscriptionsEnforced = true });
        Assert.Equal(HttpStatusCode.PaymentRequired, (await RunFilterAsync(user)).StatusCode);
    }

    [Fact]
    public async Task AccessSetting_IsSuperadminOnly_AndStoredInAppSettings()
    {
        var root = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("alice@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(user).GetAsync("/api/v1/admin/settings/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClientFor(user).PutAsJsonAsync("/api/v1/admin/settings/access", new { subscriptionsEnforced = false })).StatusCode);
        await ClientFor(root).PutAsJsonAsync("/api/v1/admin/settings/access", new { subscriptionsEnforced = false });
        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().AppSettings.AsNoTracking()
            .SingleAsync(x => x.Key == "Billing:Enforced");
        Assert.Equal("false", row.Value);
        Assert.False((await JsonAsync(await ClientFor(root).GetAsync("/api/v1/admin/settings/access"))).GetProperty("subscriptionsEnforced").GetBoolean());
    }
}
