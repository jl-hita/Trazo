using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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

/// <summary>Comments from the app: who may send and read them, and that retries store them once (export and deletion are in AccountApiTests).</summary>
public sealed class FeedbackApiTests : IDisposable
{
    private const string Send = "/api/v1/feedback";
    private const string Admin = "/api/v1/admin/feedback";
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public FeedbackApiTests()
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

    private async Task<(HttpClient Client, Guid Id)> SignedInAsync(string email, string role = "user")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = email.Split('@')[0], Role = role,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", scope.ServiceProvider.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        client.DefaultRequestHeaders.Add("X-App-Version", "0.2.30");
        return (client, user.Id);
    }

    private static object Comment(string message = "The timer stops", string category = "bug", Guid? id = null, string? screen = "workout") =>
        new { category, message, screen, device = "Pixel 9 · Android 16", clientRequestId = id ?? Guid.NewGuid() };

    private static async Task<JsonElement> ListAsync(HttpClient client, string query = "") =>
        await client.GetFromJsonAsync<JsonElement>(Admin + query);

    [Fact]
    public async Task ACommentIsStoredWithItsContext_AndTheSuperadminReadsWhoSentIt()
    {
        var (user, _) = await SignedInAsync("ana@example.com");
        var (admin, _) = await SignedInAsync("owner@example.com", "superadmin");

        Assert.Equal(HttpStatusCode.NoContent, (await user.PostAsJsonAsync(Send, Comment())).StatusCode);

        var item = Assert.Single((await ListAsync(admin)).GetProperty("items").EnumerateArray());
        Assert.Equal("The timer stops", item.GetProperty("message").GetString());
        Assert.Equal("bug", item.GetProperty("category").GetString());
        Assert.Equal("workout", item.GetProperty("screen").GetString());
        Assert.Equal("0.2.30", item.GetProperty("appVersion").GetString());
        Assert.Equal("Pixel 9 · Android 16", item.GetProperty("device").GetString());
        Assert.Equal("new", item.GetProperty("status").GetString());
        Assert.Equal("ana@example.com", item.GetProperty("email").GetString());
    }

    [Fact]
    public async Task SendingTheSameCommentAgain_StoresItOnce()
    {
        var (user, _) = await SignedInAsync("ana@example.com");
        var (admin, _) = await SignedInAsync("owner@example.com", "superadmin");
        var id = Guid.NewGuid();

        (await user.PostAsJsonAsync(Send, Comment(id: id))).EnsureSuccessStatusCode();
        (await user.PostAsJsonAsync(Send, Comment(id: id))).EnsureSuccessStatusCode();

        Assert.Equal(1, (await ListAsync(admin)).GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("praise", "text")]
    [InlineData("bug", "   ")]
    [InlineData("bug", null)]
    public async Task IncompleteComments_AreRefused(string category, string? message)
    {
        var (user, _) = await SignedInAsync("ana@example.com");

        var answer = await user.PostAsJsonAsync(Send, new { category, message, clientRequestId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact]
    public async Task AMessageThatIsTooLong_IsRefused_AndLongContextIsCut()
    {
        var (user, _) = await SignedInAsync("ana@example.com");
        var (admin, _) = await SignedInAsync("owner@example.com", "superadmin");

        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync(Send, Comment(new string('a', 2001)))).StatusCode);
        (await user.PostAsJsonAsync(Send, Comment(new string('a', 2000), screen: new string('s', 200)))).EnsureSuccessStatusCode();

        var item = Assert.Single((await ListAsync(admin)).GetProperty("items").EnumerateArray());
        Assert.Equal(60, item.GetProperty("screen").GetString()!.Length);
    }

    [Fact]
    public async Task SendingNeedsASession_AndReadingNeedsTheSuperadmin()
    {
        var anonymous = _factory.CreateClient();
        var (administrator, _) = await SignedInAsync("admin@example.com", "admin");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Send, Comment())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await administrator.GetAsync(Admin)).StatusCode);
    }

    [Fact]
    public async Task AnAccountCannotFloodTheInbox()
    {
        var (user, _) = await SignedInAsync("ana@example.com");

        for (var i = 0; i < 10; i++) (await user.PostAsJsonAsync(Send, Comment())).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.TooManyRequests, (await user.PostAsJsonAsync(Send, Comment())).StatusCode);
    }

    [Fact]
    public async Task TheNewestComesFirst_AndItCanBeFiltered_Marked_AndDeleted()
    {
        var (user, _) = await SignedInAsync("ana@example.com");
        var (admin, _) = await SignedInAsync("owner@example.com", "superadmin");
        (await user.PostAsJsonAsync(Send, Comment("first"))).EnsureSuccessStatusCode();
        await Task.Delay(20);
        (await user.PostAsJsonAsync(Send, Comment("second", "idea"))).EnsureSuccessStatusCode();

        var all = (await ListAsync(admin)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["second", "first"], all.Select(i => i.GetProperty("message").GetString()!).ToArray());
        Assert.Equal(1, (await ListAsync(admin, "?category=idea")).GetProperty("totalCount").GetInt32());

        var id = all[0].GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"{Admin}/{id}/status", new { status = "resolved" })).EnsureSuccessStatusCode();
        Assert.Equal(1, (await ListAsync(admin, "?status=resolved")).GetProperty("totalCount").GetInt32());
        (await admin.DeleteAsync($"{Admin}/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(1, (await ListAsync(admin)).GetProperty("totalCount").GetInt32());
    }
}
