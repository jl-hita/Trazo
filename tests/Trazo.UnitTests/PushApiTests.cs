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
using Trazo.Domain.Identity;
using Trazo.Domain.Push;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Push;
using Trazo.Infrastructure.Settings;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>Stand-in for Firebase: records what would be sent.</summary>
public sealed class FakePushSender : IPushSender
{
    public List<(IReadOnlyCollection<Guid>? Users, PushMessage Message)> Sent { get; } = [];
    public PushException? Failure { get; set; }
    public int Devices { get; set; } = 2;

    public Task<PushSendResult> SendAsync(IReadOnlyCollection<Guid>? userIds, PushMessage message, CancellationToken ct)
    {
        if (Failure is not null) throw Failure;
        Sent.Add((userIds, message));
        return Task.FromResult(new PushSendResult(Devices, Devices, 0, 0));
    }

    public Task CheckConnectionAsync(CancellationToken ct) => Failure is not null ? throw Failure : Task.CompletedTask;
}

public sealed class FakeFcmTokens : IFcmAccessTokenProvider
{
    public Task<string> GetAccessTokenAsync(string serviceAccountJson, CancellationToken ct) => Task.FromResult("access-token");
}

public sealed class StubHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(Uri? Url, string? Auth, string Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests) Requests.Add((request.RequestUri, request.Headers.Authorization?.ToString(), body));
        return respond(request, body);
    }
}

public sealed class PushApiTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private const string Key = "{\"type\":\"service_account\",\"project_id\":\"trazo-app\",\"client_email\":\"fcm@trazo-app.iam.gserviceaccount.com\",\"private_key\":\"x\"}";
    private const string DeviceA = "token-aaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string DeviceB = "token-bbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly FakePushSender _push = new();
    private readonly WebApplicationFactory<Program> _factory;

    public PushApiTests()
    {
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
                    .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>());
                services.RemoveAll<IPushSender>();
                services.AddSingleton<IPushSender>(_push);
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

    private async Task<List<DeviceToken>> DevicesAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().DeviceTokens.AsNoTracking().ToListAsync();
    }

    private static object ValidSettings(string? key = Key) => new
    {
        projectId = "trazo-app", applicationId = "1:123456789012:android:0123456789abcdef", apiKey = "AIzaSyExampleExampleExampleExample12345",
        senderId = "123456789012", serviceAccountJson = key
    };

    [Fact]
    public async Task Devices_RequireSignIn()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).PutAsJsonAsync("/api/v1/devices", new { token = DeviceA })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/push/config")).StatusCode);
    }

    [Fact]
    public async Task Register_StoresTheToken_AndRegisteringAgainDoesNotDuplicateIt()
    {
        var user = await AddUserAsync("alice@example.com");

        Assert.Equal(HttpStatusCode.NoContent, (await ClientFor(user).PutAsJsonAsync("/api/v1/devices", new { token = DeviceA, platform = "android" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ClientFor(user).PutAsJsonAsync("/api/v1/devices", new { token = DeviceA, platform = "android" })).StatusCode);

        var row = Assert.Single(await DevicesAsync());
        Assert.Equal(user.Id, row.UserId);
        Assert.Equal(DeviceA, row.Token);
    }

    [Fact]
    public async Task Register_MovesThePhoneToTheUserWhoSignsInOnIt()
    {
        var alice = await AddUserAsync("alice@example.com");
        var bob = await AddUserAsync("bob@example.com");
        await ClientFor(alice).PutAsJsonAsync("/api/v1/devices", new { token = DeviceA });

        await ClientFor(bob).PutAsJsonAsync("/api/v1/devices", new { token = DeviceA });

        Assert.Equal(bob.Id, Assert.Single(await DevicesAsync()).UserId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has a space inside it......")]
    public async Task Register_RejectsAnythingThatIsNotAToken(string token)
    {
        var user = await AddUserAsync("alice@example.com");
        var response = await ClientFor(user).PutAsJsonAsync("/api/v1/devices", new { token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await DevicesAsync());
    }

    [Fact]
    public async Task Register_KeepsOnlyTheMostRecentDevicesOfAUser()
    {
        var user = await AddUserAsync("alice@example.com");
        for (var i = 0; i < PushEndpoints_MaxDevices + 2; i++)
            await ClientFor(user).PutAsJsonAsync("/api/v1/devices", new { token = $"token-{i:D2}-aaaaaaaaaaaaaaaaaaaaaaaa" });

        Assert.Equal(PushEndpoints_MaxDevices, (await DevicesAsync()).Count);
    }

    private const int PushEndpoints_MaxDevices = Trazo.Api.Endpoints.PushEndpoints.MaxDevicesPerUser;

    [Fact]
    public async Task Unregister_OnlyRemovesTheCallersOwnToken()
    {
        var alice = await AddUserAsync("alice@example.com");
        var bob = await AddUserAsync("bob@example.com");
        await ClientFor(alice).PutAsJsonAsync("/api/v1/devices", new { token = DeviceA });

        await ClientFor(bob).PostAsJsonAsync("/api/v1/devices/unregister", new { token = DeviceA });
        Assert.Single(await DevicesAsync());

        await ClientFor(alice).PostAsJsonAsync("/api/v1/devices/unregister", new { token = DeviceA });
        Assert.Empty(await DevicesAsync());
    }

    [Fact]
    public async Task ClientConfig_IsOffUntilEverythingIsSaved_ThenServesOnlyPublicValues()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("alice@example.com");

        var before = await ClientFor(user).GetFromJsonAsync<JsonElement>("/api/v1/push/config");
        Assert.False(before.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, before.GetProperty("apiKey").ValueKind);

        var saved = await ClientFor(admin).PutAsJsonAsync("/api/v1/admin/settings/push", ValidSettings()); Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());

        var after = await ClientFor(user).GetFromJsonAsync<JsonElement>("/api/v1/push/config");
        Assert.True(after.GetProperty("enabled").GetBoolean());
        Assert.Equal("trazo-app", after.GetProperty("projectId").GetString());
        Assert.Equal("1:123456789012:android:0123456789abcdef", after.GetProperty("applicationId").GetString());
        Assert.DoesNotContain("private_key", after.GetRawText());
    }

    [Fact]
    public async Task Settings_AreSuperadminOnly()
    {
        var admin = await AddUserAsync("admin@example.com", "admin");
        var user = await AddUserAsync("alice@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(admin).GetAsync("/api/v1/admin/settings/push")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(user).PutAsJsonAsync("/api/v1/admin/settings/push", ValidSettings())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(admin).PostAsJsonAsync("/api/v1/admin/settings/push/broadcast", new { title = "a", body = "b" })).StatusCode);
    }

    [Fact]
    public async Task Settings_NeverReturnTheKey_AndNullKeepsTheStoredOne()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var client = ClientFor(admin);
        await client.PutAsJsonAsync("/api/v1/admin/settings/push", ValidSettings());

        var view = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings/push");
        Assert.True(view.GetProperty("serviceAccountSet").GetBoolean());
        Assert.True(view.GetProperty("configured").GetBoolean());
        Assert.Equal("fcm@trazo-app.iam.gserviceaccount.com", view.GetProperty("serviceAccountEmail").GetString());
        Assert.DoesNotContain("private_key", view.GetRawText());

        // Saving the form again without pasting the key keeps it.
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/v1/admin/settings/push", ValidSettings(null))).StatusCode);
        var again = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings/push");
        Assert.True(again.GetProperty("configured").GetBoolean());

        // The key is stored encrypted.
        using var scope = _factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<TrazoDbContext>().AppSettings.AsNoTracking()
            .SingleAsync(x => x.Key == SettingKeys.PushServiceAccountJson);
        Assert.True(row.IsSecret);
        Assert.DoesNotContain("private_key", row.Value);
    }

    [Fact]
    public async Task Settings_RejectBadValues_AndAKeyOfAnotherProject()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var client = ClientFor(admin);

        var bad = await client.PutAsJsonAsync("/api/v1/admin/settings/push",
            new { projectId = "NO", applicationId = "x", apiKey = "y", senderId = "z", serviceAccountJson = "{}" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var other = Key.Replace("trazo-app\"", "another-project\"");
        var mismatch = await client.PutAsJsonAsync("/api/v1/admin/settings/push", ValidSettings(other));
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Contains("another-project", await mismatch.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Settings_AllEmptySwitchesPushOff()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var client = ClientFor(admin);
        await client.PutAsJsonAsync("/api/v1/admin/settings/push", ValidSettings());

        var off = await client.PutAsJsonAsync("/api/v1/admin/settings/push", new { projectId = "", applicationId = "", apiKey = "", senderId = "", serviceAccountJson = "" });

        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        var view = await client.GetFromJsonAsync<JsonElement>("/api/v1/admin/settings/push");
        Assert.False(view.GetProperty("configured").GetBoolean());
        Assert.False(view.GetProperty("serviceAccountSet").GetBoolean());
    }

    [Fact]
    public async Task Test_SendsOnlyToTheSuperadminsOwnDevices()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");

        var response = await ClientFor(admin).PostAsync("/api/v1/admin/settings/push/test", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(_push.Sent);
        Assert.Equal([admin.Id], sent.Users);
        Assert.Equal("test", sent.Message.Kind);
    }

    [Fact]
    public async Task Test_ReportsAConfigurationProblemWithoutFailing()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        _push.Failure = new PushException(PushFailure.NotConfigured, "Push notifications are not configured.");

        var body = await ClientFor(admin).PostAsync("/api/v1/admin/settings/push/test", null);

        Assert.Equal(HttpStatusCode.OK, body.StatusCode);
        var json = await body.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Contains("not configured", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Broadcast_SendsToEveryDevice_AndValidatesTheText()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var client = ClientFor(admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/settings/push/broadcast", new { title = "", body = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/settings/push/broadcast", new { title = "x", body = new string('a', 241) })).StatusCode);
        Assert.Empty(_push.Sent);

        var ok = await client.PostAsJsonAsync("/api/v1/admin/settings/push/broadcast", new { title = "Novedades", body = "Hay una versión nueva." });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var sent = Assert.Single(_push.Sent);
        Assert.Null(sent.Users);
        Assert.Equal("Novedades", sent.Message.Title);
        Assert.Equal("announcement", sent.Message.Kind);
    }

    [Fact]
    public async Task FcmSender_SendsDataMessages_AndDeletesTokensFirebaseNoLongerKnows()
    {
        var alice = await AddUserAsync("alice@example.com");
        var client = ClientFor(alice);
        await client.PutAsJsonAsync("/api/v1/devices", new { token = DeviceA });
        await client.PutAsJsonAsync("/api/v1/devices", new { token = DeviceB });
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ISettingsWriter>().UpdatePushAsync(
            new PushInput("trazo-app", "1:123456789012:android:0123456789abcdef", "AIzaSyExampleExampleExampleExample12345", null, Key), default);
        var handler = new StubHandler((_, body) => body.Contains(DeviceB)
            ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":{\"status\":\"NOT_FOUND\",\"details\":[{\"errorCode\":\"UNREGISTERED\"}]}}") }
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        var sender = new FcmPushSender(new HttpClient(handler), scope.ServiceProvider.GetRequiredService<IRuntimeSettings>(),
            scope.ServiceProvider.GetRequiredService<TrazoDbContext>(), new FakeFcmTokens());

        var result = await sender.SendAsync([alice.Id], new PushMessage("TRAZO", "Hola", "review-reply"), default);

        Assert.Equal(new PushSendResult(2, 1, 1, 1), result);
        Assert.Equal(DeviceA, Assert.Single(await DevicesAsync()).Token);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("https://fcm.googleapis.com/v1/projects/trazo-app/messages:send", r.Url!.ToString());
            Assert.Equal("Bearer access-token", r.Auth);
            // A data message: nothing but the text the app needs, and no "notification" block that Android would show by itself.
            Assert.Contains("\"data\"", r.Body);
            Assert.DoesNotContain("\"notification\"", r.Body);
            Assert.Contains("review-reply", r.Body);
        });
    }

    [Fact]
    public async Task FcmSender_RefusesToSendWhenNotConfigured()
    {
        using var scope = _factory.Services.CreateScope();
        var sender = new FcmPushSender(new HttpClient(new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK))),
            scope.ServiceProvider.GetRequiredService<IRuntimeSettings>(), scope.ServiceProvider.GetRequiredService<TrazoDbContext>(), new FakeFcmTokens());

        var ex = await Assert.ThrowsAsync<PushException>(() => sender.SendAsync(null, new PushMessage("a", "b"), default));
        Assert.Equal(PushFailure.NotConfigured, ex.Failure);
    }
}
