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
using Trazo.Infrastructure.Persistence;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>HTTP-level behaviour of the user management endpoints used by the administration console.</summary>
public sealed class AdminUsersApiTests : IDisposable
{
    private const string Password = "CorrectHorse1Battery";
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public AdminUsersApiTests()
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

    private async Task<TrazoUser> AddUserAsync(string email, string role = "user", bool verified = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = email.Split('@')[0], Role = role,
            EmailVerifiedAt = verified ? now : null, CreatedAt = now, UpdatedAt = now
        };
        user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, Password);
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

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Fact]
    public async Task Endpoints_RequireTheSuperadminRole()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var regular = await AddUserAsync("user@example.com");
        var other = await AddUserAsync("other@example.com", "admin");

        Assert.Equal(HttpStatusCode.Unauthorized, (await ClientFor(null).GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(regular).GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ClientFor(other).GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(admin).GetAsync("/api/v1/admin/users")).StatusCode);
    }

    [Fact]
    public async Task List_SearchesFiltersAndPages()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        await AddUserAsync("alice@example.com");
        await AddUserAsync("bob@example.com", "admin");
        await AddUserAsync("carol@example.com", verified: false);
        var client = ClientFor(admin);

        var all = await JsonAsync(await client.GetAsync("/api/v1/admin/users"));
        Assert.Equal(4, all.GetProperty("totalCount").GetInt32());
        Assert.Equal("alice@example.com", all.GetProperty("items")[0].GetProperty("email").GetString());

        Assert.Equal(1, (await JsonAsync(await client.GetAsync("/api/v1/admin/users?q=ALI"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await client.GetAsync("/api/v1/admin/users?role=admin"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await client.GetAsync("/api/v1/admin/users?status=unverified"))).GetProperty("totalCount").GetInt32());

        var paged = await JsonAsync(await client.GetAsync("/api/v1/admin/users?pageSize=2&page=2"));
        Assert.Equal(2, paged.GetProperty("items").GetArrayLength());
        Assert.Equal(2, paged.GetProperty("totalPages").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/admin/users?role=root")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/admin/users?pageSize=500")).StatusCode);
    }

    [Fact]
    public async Task ChangingARole_TakesEffectOnExistingTokens_AndOwnRoleIsProtected()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("user@example.com");
        var adminClient = ClientFor(admin);
        var userClient = ClientFor(user); // token issued while the account was a plain user

        Assert.Equal(HttpStatusCode.Forbidden, (await userClient.GetAsync("/api/v1/admin/users")).StatusCode);

        var promote = await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{user.Id}", new { role = "superadmin" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        Assert.Equal("superadmin", (await JsonAsync(promote)).GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/v1/admin/users")).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{admin.Id}", new { role = "user" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{user.Id}", new { role = "owner" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await adminClient.PatchAsJsonAsync($"/api/v1/admin/users/{Guid.NewGuid()}", new { role = "user" })).StatusCode);
    }

    [Fact]
    public async Task DisablingAnAccount_BlocksTokensRefreshAndLogin_UntilItIsEnabled()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("user@example.com");
        var adminClient = ClientFor(admin);
        var anonymous = _factory.CreateClient();

        var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = await JsonAsync(login);
        var access = tokens.GetProperty("accessToken").GetString()!;
        var refresh = tokens.GetProperty("refreshToken").GetString()!;
        var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync("/api/v1/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await adminClient.PostAsync($"/api/v1/admin/users/{admin.Id}/disable", null)).StatusCode);

        var disabled = await adminClient.PostAsync($"/api/v1/admin/users/{user.Id}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.True((await JsonAsync(disabled)).GetProperty("disabled").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await userClient.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = Password })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsync($"/api/v1/admin/users/{user.Id}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = Password })).StatusCode);
    }

    [Fact]
    public async Task RevokingSessions_ClearsRefreshTokens()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("user@example.com");
        var adminClient = ClientFor(admin);
        var anonymous = _factory.CreateClient();

        string refresh = "";
        for (var i = 0; i < 2; i++)
        {
            var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = Password, deviceName = $"phone-{i}" });
            refresh = (await JsonAsync(login)).GetProperty("refreshToken").GetString()!;
        }

        var detail = await JsonAsync(await adminClient.GetAsync($"/api/v1/admin/users/{user.Id}"));
        Assert.Equal(2, detail.GetProperty("activeSessionCount").GetInt32());

        var revoked = await JsonAsync(await adminClient.PostAsync($"/api/v1/admin/users/{user.Id}/revoke-sessions", null));
        Assert.Equal(0, revoked.GetProperty("activeSessionCount").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refresh })).StatusCode);
    }

    [Fact]
    public async Task UnlockAndVerifyEmail_UpdateTheAccount()
    {
        var admin = await AddUserAsync("root@example.com", "superadmin");
        var user = await AddUserAsync("user@example.com", verified: false);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var row = await db.Users.SingleAsync(x => x.Id == user.Id);
            row.FailedLoginAttempts = 3;
            row.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(10);
            await db.SaveChangesAsync();
        }
        var client = ClientFor(admin);

        var locked = await JsonAsync(await client.GetAsync($"/api/v1/admin/users/{user.Id}"));
        Assert.True(locked.GetProperty("lockedOut").GetBoolean());
        Assert.False(locked.GetProperty("emailVerified").GetBoolean());

        var unlocked = await JsonAsync(await client.PostAsync($"/api/v1/admin/users/{user.Id}/unlock", null));
        Assert.False(unlocked.GetProperty("lockedOut").GetBoolean());
        Assert.Equal(0, unlocked.GetProperty("failedLoginAttempts").GetInt32());

        var verified = await JsonAsync(await client.PostAsync($"/api/v1/admin/users/{user.Id}/verify-email", null));
        Assert.True(verified.GetProperty("emailVerified").GetBoolean());
    }
}
