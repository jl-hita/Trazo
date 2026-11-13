using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trazo.Api.Endpoints;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Domain.Training;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Seeding;
using Xunit;

namespace Trazo.UnitTests.Planning;

/// <summary>The archive TRAZO builds for DietoExpress and the administration endpoint that sends it.</summary>
public sealed class ExerciseLibraryTransferTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private WebApplicationFactory<Program>? _factory;

    public ExerciseLibraryTransferTests()
    {
        using var db = _fixture.NewContext();
        ExerciseTestSchema.Create(db);
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_keys, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static byte[] Webp(byte fill)
    {
        var bytes = new byte[40];
        "RIFF"u8.CopyTo(bytes); "WEBP"u8.CopyTo(bytes.AsSpan(8));
        Array.Fill(bytes, fill, 12, 28);
        return bytes;
    }

    private static ExerciseCatalogItem Own(string id, string source = "trazo", int steps = 3, bool active = true)
    {
        var item = ExerciseCandidateMapperTests.Item(id, active: active);
        item.Source = source;
        item.InstructionsEs = Enumerable.Range(1, steps).Select(i => $"Paso {i}.").ToArray();
        return item;
    }

    private void Seed()
    {
        using var db = _fixture.NewContext();
        var imported = Own("imported-press", "repdb-free");
        db.ExerciseCatalog.AddRange(Own("own-press"), Own("Bad_Id", "trazo-curated"), Own("own-short", steps: 1),
            Own("own-hidden", active: false), imported);
        db.SaveChanges();
        Image(db, "own-press", 0, Webp(1), "TRAZO");
        Image(db, "own-press", 1, "not an image at all......"u8.ToArray(), "TRAZO");
        Image(db, "imported-press", 0, Webp(2), "DietoExpress");
    }

    private static void Image(TrazoDbContext db, string id, int slot, byte[] data, string origin) =>
        db.Database.ExecuteSql($"""
            INSERT INTO exercise_catalog_images (exercise_id, slot, content_type, data, sha256, source, origin_system, imported_at)
            VALUES ({id}, {slot}, {"image/webp"}, {data}, {"x"}, {"custom"}, {origin}, {DateTimeOffset.UtcNow})
            """);

    private static Dictionary<string, JsonElement> ReadEntries(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return new() { ["json"] = JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone() };
    }

    [Fact]
    public async Task Exporter_SendsOnlyOwnValidExercises_WithTheirTrazoImages()
    {
        Seed();
        await using var db = _fixture.NewContext();
        using var stream = new MemoryStream();

        var result = await new ExerciseLibraryExporter(db).BuildAsync(stream);

        Assert.Equal(1, result.ExerciseCount);
        Assert.Equal(1, result.ImageCount);
        Assert.Equal(["Bad_Id", "own-short"], result.Skipped.Select(s => s.ExerciseId).Order().ToArray());

        stream.Position = 0;
        using var archive = new ZipArchive(stream);
        Assert.Equal(["exercises.json", "images/own-press/0.webp", "manifest.json"], archive.Entries.Select(e => e.FullName).Order().ToArray());
        var exercises = ReadEntries(archive, "exercises.json")["json"];
        Assert.Equal("own-press", Assert.Single(exercises.EnumerateArray()).GetProperty("id").GetString());
        var manifest = ReadEntries(archive, "manifest.json")["json"];
        Assert.Equal("TRAZO", manifest.GetProperty("source").GetString());
        Assert.Equal(1, manifest.GetProperty("schemaVersion").GetInt32());
        using var exerciseBytes = new MemoryStream();
        await archive.GetEntry("exercises.json")!.Open().CopyToAsync(exerciseBytes);
        Assert.Equal(ExerciseImageRules.Sha256(exerciseBytes.ToArray()), manifest.GetProperty("exercisesSha256").GetString());
        var image = Assert.Single(manifest.GetProperty("images").EnumerateArray());
        Assert.Equal("custom", image.GetProperty("source").GetString());
        Assert.Equal(40, image.GetProperty("size").GetInt32());
    }

    private sealed class StubHandler(HttpStatusCode status, string body = "{\"exercisesInserted\":1}") : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public byte[] Body { get; private set; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private async Task<HttpClient> ClientAsync(string role, StubHandler? stub, bool configured = true)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Trazo", "Host=unused");
            builder.UseSetting("Authentication:Jwt:Issuer", "TRAZO");
            builder.UseSetting("Authentication:Jwt:Audience", "TRAZO-Clients");
            builder.UseSetting("Authentication:Jwt:SigningKey", new string((char)107, 48));
            builder.UseSetting("DataProtection:KeysPath", _keys);
            if (configured)
            {
                builder.UseSetting("DietoExpressTransfer:Endpoint", "https://dieto.test/");
                builder.UseSetting("DietoExpressImport:ApiKey", "shared-secret-key");
            }
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TrazoDbContext>>();
                services.RemoveAll(typeof(IDbContextOptionsConfiguration<TrazoDbContext>));
                services.AddDbContext<TrazoDbContext>(o => o.UseSqlite(_fixture.ConnectionString));
                if (stub is not null)
                    services.AddHttpClient(AdminLibraryTransferEndpoints.ClientName).ConfigurePrimaryHttpMessageHandler(() => stub);
            });
        });
        var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = $"{role}-{Guid.NewGuid():N}@example.com", NormalizedEmail = $"{role}-{Guid.NewGuid():N}@example.com".ToUpperInvariant(),
            DisplayName = role, Role = role, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private const string Route = "/api/v1/admin/exercise-library-transfer/dietoexpress";

    [Fact]
    public async Task Send_PostsTheArchiveToDietoExpressWithTheSharedKey()
    {
        Seed();
        var stub = new StubHandler(HttpStatusCode.OK);
        var response = await (await ClientAsync("superadmin", stub)).PostAsync(Route, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<JsonElement>())!;
        Assert.True(body.GetProperty("sent").GetBoolean());
        Assert.Equal(1, body.GetProperty("exercises").GetInt32());
        Assert.Equal(1, body.GetProperty("images").GetInt32());
        Assert.Equal(2, body.GetProperty("skipped").GetArrayLength());
        Assert.Equal(1, body.GetProperty("remote").GetProperty("exercisesInserted").GetInt32());

        Assert.Equal("https://dieto.test/api/internal/trazo-exercise-library/import", stub.Request!.RequestUri!.ToString());
        Assert.Equal("shared-secret-key", stub.Request.Headers.GetValues(AdminLibraryTransferEndpoints.KeyHeader).Single());
        Assert.Equal("application/zip", stub.Request.Content!.Headers.ContentType!.MediaType);
        using var archive = new ZipArchive(new MemoryStream(stub.Body));
        Assert.NotNull(archive.GetEntry("manifest.json"));
    }

    [Fact]
    public async Task Send_ReportsMissingConfiguration_RejectedTransfers_AndEmptyLibraries()
    {
        Seed();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await (await ClientAsync("superadmin", null, configured: false)).PostAsync(Route, null)).StatusCode);
        _factory!.Dispose();

        var rejected = await (await ClientAsync("superadmin", new StubHandler(HttpStatusCode.Unauthorized))).PostAsync(Route, null);
        Assert.Equal(HttpStatusCode.BadGateway, rejected.StatusCode);
        Assert.Equal(401, (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("targetStatus").GetInt32());
        _factory.Dispose();

        await using (var db = _fixture.NewContext())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM exercise_catalog WHERE \"Source\" <> 'repdb-free'");
        Assert.Equal(HttpStatusCode.Conflict, (await (await ClientAsync("superadmin", new StubHandler(HttpStatusCode.OK))).PostAsync(Route, null)).StatusCode);
    }

    [Fact]
    public async Task Send_RequiresTheSuperadminRole()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientAsync("admin", new StubHandler(HttpStatusCode.OK))).PostAsync(Route, null)).StatusCode);
    }
}
