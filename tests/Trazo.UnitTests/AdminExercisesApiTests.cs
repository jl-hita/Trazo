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

/// <summary>HTTP-level behaviour of the exercise library management endpoints.</summary>
public sealed class AdminExercisesApiTests : IDisposable
{
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public AdminExercisesApiTests()
    {
        using (var db = _fixture.NewContext())
        {
            // SQLite stand-ins for the PostgreSQL tables (arrays are stored as JSON text).
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE exercise_catalog (
                  exercise_id TEXT PRIMARY KEY, "Source" TEXT NOT NULL, "NameEn" TEXT NOT NULL, "NameEs" TEXT NOT NULL,
                  "DescriptionEs" TEXT NOT NULL, "InstructionsEs" TEXT NOT NULL, "TipsEs" TEXT NOT NULL, "ForceType" TEXT NOT NULL,
                  "Mechanic" TEXT NOT NULL, "Difficulty" TEXT NOT NULL, "Equipment" TEXT NULL, "BodyPart" TEXT NOT NULL,
                  "Met" REAL NOT NULL, "IsUnilateral" INTEGER NOT NULL, "IsBodyweight" INTEGER NOT NULL, "MovementPattern" TEXT NOT NULL,
                  "PrimaryMuscles" TEXT NOT NULL, "SecondaryMuscles" TEXT NOT NULL, "MinimumExperience" TEXT NOT NULL,
                  "SuitableGoals" TEXT NOT NULL, "RequiredEquipment" TEXT NOT NULL, "SelectionPriority" INTEGER NOT NULL,
                  "SeedVersion" INTEGER NOT NULL, "IsActive" INTEGER NOT NULL DEFAULT 1, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
                CREATE TABLE exercise_catalog_import_metadata (
                  exercise_id TEXT PRIMARY KEY, source_system TEXT NOT NULL, is_locally_curated INTEGER NOT NULL DEFAULT 0);
                CREATE TABLE exercise_catalog_images (exercise_id TEXT NOT NULL, slot INTEGER NOT NULL, PRIMARY KEY (exercise_id, slot));
                """);
        }

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

    private async Task<HttpClient> ClientAsync(string? role)
    {
        var client = _factory.CreateClient();
        if (role is null) return client;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = $"{role}@example.com", NormalizedEmail = $"{role}@example.com".ToUpperInvariant(),
            DisplayName = role, Role = role, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private static object Input(string nameEn = "Press de Banca", string? id = null) => new
    {
        id, nameEs = "Press de banca", nameEn, descriptionEs = "Empuje horizontal con barra.",
        instructionsEs = new[] { "Tumbado, baja la barra al pecho.", "Empuja hasta extender los codos." },
        tipsEs = new[] { "Escápulas retraídas." }, difficulty = "intermediate", equipment = "barbell", bodyPart = "chest",
        movementPattern = "HorizontalPush", primaryMuscles = new[] { "Chest" }, secondaryMuscles = new[] { "Triceps" },
        minimumExperience = "Beginner", suitableGoals = new[] { "Strength" }, requiredEquipment = new[] { "barbell", "bench" },
        selectionPriority = 80
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>())!;

    [Fact]
    public async Task Endpoints_RequireTheSuperadminRole()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await (await ClientAsync(null)).GetAsync("/api/v1/admin/exercises")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await ClientAsync("user")).GetAsync("/api/v1/admin/exercises")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await ClientAsync("superadmin")).GetAsync("/api/v1/admin/exercises")).StatusCode);
    }

    [Fact]
    public async Task Create_ValidatesDerivesTheIdAndRejectsDuplicates()
    {
        var client = await ClientAsync("superadmin");

        var invalid = await client.PostAsJsonAsync("/api/v1/admin/exercises", new { nameEs = "", primaryMuscles = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await JsonAsync(invalid)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("nameEs", out _));
        Assert.True(errors.TryGetProperty("primaryMuscles", out _));
        Assert.True(errors.TryGetProperty("instructionsEs", out _));

        var created = await client.PostAsJsonAsync("/api/v1/admin/exercises", Input("Press de Banca Pausado"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var detail = await JsonAsync(created);
        Assert.Equal("press-de-banca-pausado", detail.GetProperty("id").GetString());
        Assert.Equal("trazo", detail.GetProperty("source").GetString());
        Assert.True(detail.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, detail.GetProperty("instructionsEs").GetArrayLength());

        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/admin/exercises", Input("Press de Banca Pausado"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/admin/exercises", Input(id: "Bad Id!"))).StatusCode);

        var publicDetail = await (await ClientAsync(null)).GetAsync("/api/v1/exercises/press-de-banca-pausado");
        Assert.Equal(HttpStatusCode.OK, publicDetail.StatusCode);
    }

    [Fact]
    public async Task List_FiltersAndOptions()
    {
        var client = await ClientAsync("superadmin");
        await client.PostAsJsonAsync("/api/v1/admin/exercises", Input("Bench Press"));
        await client.PostAsJsonAsync("/api/v1/admin/exercises", new
        {
            nameEs = "Sentadilla", nameEn = "Squat", descriptionEs = "Flexión de rodillas.", instructionsEs = new[] { "Baja con control." },
            difficulty = "beginner", movementPattern = "Squat", primaryMuscles = new[] { "Quadriceps" }, minimumExperience = "Beginner",
            suitableGoals = new[] { "Hypertrophy" }
        });

        Assert.Equal(2, (await JsonAsync(await client.GetAsync("/api/v1/admin/exercises"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await client.GetAsync("/api/v1/admin/exercises?q=SENTA"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await client.GetAsync("/api/v1/admin/exercises?movementPattern=HorizontalPush"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await client.GetAsync("/api/v1/admin/exercises?muscle=Chest"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/admin/exercises?status=hidden")).StatusCode);

        var options = await JsonAsync(await client.GetAsync("/api/v1/admin/exercises/options"));
        Assert.Contains("HorizontalPush", options.GetProperty("movementPatterns").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("Quadriceps", options.GetProperty("muscles").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task EditingAnImportedExercise_MarksItAsCurated_AndCurationCanBeReleased()
    {
        var client = await ClientAsync("superadmin");
        await client.PostAsJsonAsync("/api/v1/admin/exercises", Input("Imported Press", "imported-press"));
        using (var db = _fixture.NewContext())
            db.Database.ExecuteSqlRaw("INSERT INTO exercise_catalog_import_metadata (exercise_id, source_system, is_locally_curated) VALUES ('imported-press', 'DietoExpress', 0)");

        var before = await JsonAsync(await client.GetAsync("/api/v1/admin/exercises/imported-press"));
        Assert.False(before.GetProperty("locallyCurated").GetBoolean());
        Assert.Equal("DietoExpress", before.GetProperty("importedFrom").GetString());

        var edited = await client.PutAsJsonAsync("/api/v1/admin/exercises/imported-press", Input("Imported Press Edited", "ignored"));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var detail = await JsonAsync(edited);
        Assert.True(detail.GetProperty("locallyCurated").GetBoolean());
        Assert.Equal("Imported Press Edited", detail.GetProperty("nameEn").GetString());
        Assert.Equal("imported-press", detail.GetProperty("id").GetString());

        var released = await JsonAsync(await client.PostAsync("/api/v1/admin/exercises/imported-press/release-curation", null));
        Assert.False(released.GetProperty("locallyCurated").GetBoolean());

        await client.PostAsJsonAsync("/api/v1/admin/exercises", Input("Local Only", "local-only"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/v1/admin/exercises/local-only/release-curation", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/v1/admin/exercises/missing", Input())).StatusCode);
    }

    [Fact]
    public async Task DeactivatingAnExercise_HidesItFromTheCatalog_UntilItIsActivated()
    {
        var client = await ClientAsync("superadmin");
        var anonymous = await ClientAsync(null);
        await client.PostAsJsonAsync("/api/v1/admin/exercises", Input("Hidden Press", "hidden-press"));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/exercises/hidden-press")).StatusCode);

        var off = await JsonAsync(await client.PostAsync("/api/v1/admin/exercises/hidden-press/deactivate", null));
        Assert.False(off.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/v1/exercises/hidden-press")).StatusCode);
        Assert.Equal(0, (await JsonAsync(await anonymous.GetAsync("/api/v1/exercises"))).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await JsonAsync(await client.GetAsync("/api/v1/admin/exercises?status=inactive"))).GetProperty("totalCount").GetInt32());

        await client.PostAsync("/api/v1/admin/exercises/hidden-press/activate", null);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/api/v1/exercises/hidden-press")).StatusCode);
    }
}
