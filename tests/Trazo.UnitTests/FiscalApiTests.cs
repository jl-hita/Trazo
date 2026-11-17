using System.IO.Compression;
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
using Trazo.Api.Security;
using Trazo.Domain.Billing;
using Trazo.Domain.Growth;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.UnitTests.Planning;
using Xunit;

namespace Trazo.UnitTests;

/// <summary>The fiscal API end to end: superadmin-only access, ledgers, the Google Play estimate, closing and reopening a month, and the package.</summary>
public sealed class FiscalApiTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);
    private readonly SetupFixture _fixture = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), $"trazo-keys-{Guid.NewGuid():N}");
    private readonly WebApplicationFactory<Program> _factory;

    public FiscalApiTests()
    {
        using (var db = _fixture.NewContext()) ExerciseTestSchema.Create(db);
        PlanTestSchema.Create(_fixture);
        using (var db = Db())
            db.Database.ExecuteSqlRaw("""
                CREATE TABLE fiscal_entries ("Id" TEXT PRIMARY KEY, "Kind" TEXT NOT NULL, "Date" TEXT NOT NULL, "Period" TEXT NOT NULL, "Concept" TEXT NOT NULL,
                  "Counterparty" TEXT NULL, "CounterpartyTaxId" TEXT NULL, "CounterpartyCountry" TEXT NULL, "InvoiceNumber" TEXT NULL, "Category" TEXT NOT NULL,
                  "Base" TEXT NOT NULL, "VatTreatment" TEXT NOT NULL, "VatRatePct" TEXT NOT NULL, "VatAmount" TEXT NOT NULL, "WithholdingPct" TEXT NOT NULL,
                  "WithholdingAmount" TEXT NOT NULL, "DeductiblePct" TEXT NOT NULL, "Total" TEXT NOT NULL, "Source" TEXT NOT NULL, "Notes" TEXT NULL,
                  "CreatedBy" TEXT NULL, "CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL);
                CREATE TABLE fiscal_closures ("Period" TEXT PRIMARY KEY, "ClosedAt" TEXT NOT NULL, "ClosedBy" TEXT NOT NULL, "EntryCount" INTEGER NOT NULL, "SnapshotJson" TEXT NOT NULL);
                CREATE TABLE fiscal_audit ("Id" TEXT PRIMARY KEY, "Period" TEXT NOT NULL, "Action" TEXT NOT NULL, "Actor" TEXT NOT NULL, "Reason" TEXT NULL, "At" TEXT NOT NULL);
                """);
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
                    .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>());
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

    private TrazoDbContext Db() => new(new DbContextOptionsBuilder<TrazoDbContext>().UseSqlite(_fixture.ConnectionString)
        .ReplaceService<IModelCustomizer, PlanSqliteCustomizer>().Options);

    private async Task<HttpClient> ClientAsync(string role)
    {
        await using var db = Db();
        var email = $"{role}-{Guid.NewGuid():N}@trazo.test";
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(), Email = email, NormalizedEmail = email.ToUpperInvariant(), DisplayName = role, Role = role,
            EmailVerifiedAt = Now, CreatedAt = Now, UpdatedAt = Now
        };
        user.PasswordHash = new PasswordHasher<TrazoUser>().HashPassword(user, "CorrectHorse1Battery");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _factory.Services.GetRequiredService<AuthTokenService>().CreateAccessToken(user));
        return client;
    }

    private static object Entry(string kind, string date, decimal @base, string treatment, string category, string concept = "Concepto",
        string? invoice = "F-1", string? taxId = null, decimal? rate = null) => new
        {
            kind, date, concept, category, @base, vatTreatment = treatment, vatRatePct = rate, invoiceNumber = invoice,
            counterparty = "Proveedor SL", counterpartyTaxId = taxId, counterpartyCountry = taxId is null ? "ES" : "IE"
        };

    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task FillProfileAsync(HttpClient c)
    {
        var r = await c.PutAsJsonAsync("/api/v1/admin/fiscal/profile", new
        {
            legalName = "Juan Pérez", taxId = "12345678z", address = "Calle 1", gestorEmail = "", defaultVatPct = 21, irpfAdvancePct = 20,
            filesModelo130 = true, playMonthlyPrice = 6.99, playAnnualPrice = 49.99, consumerVatPct = 21, googleFeePct = 15
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // ------------------------------------------------------------------ access

    [Theory]
    [InlineData("user")]
    [InlineData("admin")]
    public async Task OnlySuperadminReachesTheFiscalApi(string role)
    {
        var client = await ClientAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/fiscal/years/2026")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-01", 10, "no_vat", "other"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/api/v1/admin/fiscal/profile")).StatusCode);
    }


    [Fact]
    public async Task TheAccountantSeesTheFiscalApiAndNothingElseOfTheConsole()
    {
        var accountant = await ClientAsync("accountant");
        Assert.Equal(HttpStatusCode.OK, (await accountant.GetAsync("/api/v1/admin/fiscal/years/2026")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await accountant.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-01", 10, "no_vat", "other"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/v1/admin/subscriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.GetAsync("/api/v1/admin/referrals")).StatusCode);

        // The superadmin can give the role.
        var root = await ClientAsync("superadmin");
        var users = await Json(await root.GetAsync("/api/v1/admin/users?role=accountant"));
        Assert.Equal(1, users.GetProperty("totalCount").GetInt32());
    }
    // ------------------------------------------------------------------ profile

    [Fact]
    public async Task Profile_IsValidatedNormalisedAndPersisted()
    {
        var c = await ClientAsync("superadmin");
        var bad = await c.PutAsJsonAsync("/api/v1/admin/fiscal/profile", new { legalName = "x", taxId = "y", address = "", defaultVatPct = 99, irpfAdvancePct = 20, filesModelo130 = true, playMonthlyPrice = 6.99, playAnnualPrice = 49.99, consumerVatPct = 21, googleFeePct = 15 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        await FillProfileAsync(c);
        var profile = await Json(await c.GetAsync("/api/v1/admin/fiscal/profile"));
        Assert.Equal("12345678Z", profile.GetProperty("taxId").GetString());
    }

    // ------------------------------------------------------------------ entries and totals

    [Fact]
    public async Task Entries_AreValidatedAndComputed()
    {
        var c = await ClientAsync("superadmin");
        var invalid = await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-01", 0, "bogus", "nope"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var created = await Json(await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-03", 100, "domestic", "hosting")));
        Assert.Equal(21m, created.GetProperty("vatAmount").GetDecimal());
        Assert.Equal(121m, created.GetProperty("total").GetDecimal());
        Assert.Equal("2026-10", created.GetProperty("period").GetString());

        var month = await Json(await c.GetAsync("/api/v1/admin/fiscal/months/2026-10"));
        Assert.Equal(21m, month.GetProperty("totals").GetProperty("vatInput").GetDecimal());
        Assert.Equal(1, month.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task Entries_CanBeEditedAndDeletedWhileTheMonthIsOpen()
    {
        var c = await ClientAsync("superadmin");
        var created = await Json(await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-03", 100, "domestic", "hosting")));
        var id = created.GetProperty("id").GetGuid();
        var updated = await Json(await c.PutAsJsonAsync($"/api/v1/admin/fiscal/entries/{id}", Entry("expense", "2026-10-04", 200, "domestic", "software", rate: 10)));
        Assert.Equal(20m, updated.GetProperty("vatAmount").GetDecimal());
        Assert.Equal(HttpStatusCode.NoContent, (await c.DeleteAsync($"/api/v1/admin/fiscal/entries/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.DeleteAsync($"/api/v1/admin/fiscal/entries/{id}")).StatusCode);
    }

    // ------------------------------------------------------------------ Google Play estimate

    private async Task SeedGoogleSalesAsync()
    {
        await using var db = Db();
        Subscription Sub(Guid id, int days) => new()
        {
            Id = id, UserId = Guid.NewGuid(), ProductId = "trazo_premium", PurchaseTokenHash = id.ToString("N"), ProtectedPurchaseToken = "x",
            Status = SubscriptionStatus.Active, StartedAt = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero),
            ExpiresAt = new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero).AddDays(days), CreatedAt = Now, UpdatedAt = Now
        };
        var monthly = Guid.NewGuid(); var annual = Guid.NewGuid();
        db.Subscriptions.AddRange(Sub(monthly, 30), Sub(annual, 365));
        GrowthEvent Ev(string name, string key, int month, int day) => new()
        { Id = Guid.NewGuid(), Name = name, OccurredAt = new DateTimeOffset(2026, month, day, 9, 0, 0, TimeSpan.Zero), DedupeKey = key };
        db.GrowthEvents.AddRange(
            Ev(GrowthEventNames.FirstPurchase, $"first_purchase:{monthly}", 10, 5),
            Ev(GrowthEventNames.Renewed, $"renewed:{monthly}:GPA.1", 11, 5),
            Ev(GrowthEventNames.FirstPurchase, $"first_purchase:{annual}", 10, 10));
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GoogleEstimate_IsShownAndCanBeBookedOnce()
    {
        await SeedGoogleSalesAsync();
        var c = await ClientAsync("superadmin");
        var month = await Json(await c.GetAsync("/api/v1/admin/fiscal/months/2026-10"));
        Assert.Equal(40.03m, month.GetProperty("estimate").GetProperty("income").GetDecimal());
        Assert.Contains(month.GetProperty("warnings").EnumerateArray(), w => w.GetProperty("code").GetString() == "missing_google_income");

        var booked = await Json(await c.PostAsync("/api/v1/admin/fiscal/months/2026-10/book-google-estimate", null));
        Assert.Equal("eu_reverse_charge", booked.GetProperty("vatTreatment").GetString());
        Assert.Equal(0m, booked.GetProperty("vatAmount").GetDecimal());
        Assert.Equal(40.03m, booked.GetProperty("base").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsync("/api/v1/admin/fiscal/months/2026-10/book-google-estimate", null)).StatusCode);
    }

    // ------------------------------------------------------------------ closing

    [Fact]
    public async Task ClosingNeedsAProfileAndAFinishedMonth()
    {
        var c = await ClientAsync("superadmin");
        Assert.Equal((HttpStatusCode)422, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/close", new { acknowledgeWarnings = true })).StatusCode);
        await FillProfileAsync(c);
        // 2026-11 is the current month in the test clock.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-11/close", new { acknowledgeWarnings = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-1/close", new { })).StatusCode);
    }

    [Fact]
    public async Task Closing_WithWarningsNeedsAcknowledgement_ThenFreezesTheMonth()
    {
        var c = await ClientAsync("superadmin");
        await FillProfileAsync(c);
        await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-03", 100, "domestic", "hosting", invoice: null));
        var created = await Json(await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("income", "2026-10-31", 40, "eu_reverse_charge", "google_play", taxId: "IE6388047V")));

        var refused = await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/close", new { acknowledgeWarnings = false });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("warnings", (await Json(refused)).GetProperty("error").GetString());

        var closed = await Json(await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/close", new { acknowledgeWarnings = true }));
        Assert.True(closed.GetProperty("closed").GetBoolean());

        // Frozen: no create, edit or delete.
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-20", 5, "no_vat", "other"))).StatusCode);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await c.PutAsJsonAsync($"/api/v1/admin/fiscal/entries/{id}", Entry("income", "2026-10-31", 41, "eu_reverse_charge", "google_play"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.DeleteAsync($"/api/v1/admin/fiscal/entries/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/close", new { acknowledgeWarnings = true })).StatusCode);
    }

    [Fact]
    public async Task Reopening_NeedsAReasonAndIsAudited()
    {
        var c = await ClientAsync("superadmin");
        await FillProfileAsync(c);
        await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("income", "2026-10-31", 40, "eu_reverse_charge", "google_play", taxId: "IE6388047V"));
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/reopen", new { reason = "ya estaba abierto el mes" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/close", new { acknowledgeWarnings = true })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/reopen", new { reason = "corto" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/reopen", new { reason = "Falta una factura del hosting" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-20", 5, "no_vat", "other"))).StatusCode);

        var audit = await Json(await c.GetAsync("/api/v1/admin/fiscal/audit"));
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Contains("closed", actions);
        Assert.Contains("reopened", actions);
    }

    // ------------------------------------------------------------------ package

    [Fact]
    public async Task Package_NeedsAClosedMonth_AndContainsTheBooks()
    {
        var c = await ClientAsync("superadmin");
        await FillProfileAsync(c);
        await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("income", "2026-10-31", 40.03m, "eu_reverse_charge", "google_play", concept: "=HYPERLINK(\"x\")", taxId: "IE6388047V"));
        await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", "2026-10-03", 100, "domestic", "hosting"));

        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync("/api/v1/admin/fiscal/months/2026-10/package")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync("/api/v1/admin/fiscal/months/2026-10/close", new { acknowledgeWarnings = true })).StatusCode);

        var response = await c.GetAsync("/api/v1/admin/fiscal/months/2026-10/package");
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        using var zip = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("LEEME.txt", names);
        Assert.Contains("libro_ingresos_2026-10.csv", names);
        Assert.Contains("libro_gastos_2026-10.csv", names);
        Assert.Contains("resumen_trimestre_2026T4.csv", names);
        Assert.Contains("modelo349_2026T4.csv", names);

        string Read(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8); return r.ReadToEnd(); }
        var income = Read("libro_ingresos_2026-10.csv");
        Assert.Contains("40,03", income);
        Assert.Contains("'=HYPERLINK", income); // formulas are neutralised
        Assert.Contains("21,00", Read("libro_gastos_2026-10.csv"));
        Assert.Contains("12345678Z", Read("LEEME.txt"));
    }

    [Fact]
    public async Task QuarterPackage_NeedsTheThreeMonthsClosed()
    {
        var c = await ClientAsync("superadmin");
        await FillProfileAsync(c);
        Assert.Equal(HttpStatusCode.Conflict, (await c.GetAsync("/api/v1/admin/fiscal/quarters/2026/3/package")).StatusCode);
        foreach (var month in new[] { "2026-07", "2026-08", "2026-09" })
        {
            await c.PostAsJsonAsync("/api/v1/admin/fiscal/entries", Entry("expense", $"{month}-10", 10, "no_vat", "other"));
            Assert.Equal(HttpStatusCode.OK, (await c.PostAsJsonAsync($"/api/v1/admin/fiscal/months/{month}/close", new { acknowledgeWarnings = true })).StatusCode);
        }
        var ok = await c.GetAsync("/api/v1/admin/fiscal/quarters/2026/3/package");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        using var zip = new ZipArchive(new MemoryStream(await ok.Content.ReadAsByteArrayAsync()));
        Assert.Contains(zip.Entries, e => e.FullName == "libro_gastos_2026T3.csv");

        var year = await Json(await c.GetAsync("/api/v1/admin/fiscal/years/2026"));
        Assert.True(year.GetProperty("quarters")[2].GetProperty("allClosed").GetBoolean());
        Assert.Equal(12, year.GetProperty("months").GetArrayLength());
    }
}
