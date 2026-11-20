using System.IO.Compression;
using System.Net;
using Xunit;

namespace Trazo.UnitTests.Planning;

public sealed class AdminIntelligenceApiTests() : PlansApiBase(enforced: true)
{
    private async Task<HttpClient> SuperadminAsync() =>
        ClientFor(await AddUserAsync($"intelligence-{Guid.NewGuid():N}@trazo.test", role: "superadmin"));

    [Fact]
    public async Task IntelligenceEndpointsRemainSuperadminOnly()
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ClientFor(null).GetAsync("/api/v1/admin/intelligence/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ClientFor(await AddUserAsync($"staff-{Guid.NewGuid():N}@trazo.test", role: "admin"))
                .GetAsync("/api/v1/admin/intelligence/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await (await SuperadminAsync()).GetAsync("/api/v1/admin/intelligence/summary")).StatusCode);
    }

    [Fact]
    public async Task SummaryKeepsTheExistingResponseContract()
    {
        var response = await (await SuperadminAsync()).GetAsync(
            "/api/v1/admin/intelligence/summary?from=2026-11-01T00:00:00Z&to=2026-11-20T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), body.GetProperty("from").GetDateTimeOffset());
        Assert.Equal(0, body.GetProperty("plans").GetProperty("total").GetInt32());
        Assert.Equal(0, body.GetProperty("prescriptions").GetProperty("sessions").GetInt32());
        Assert.Equal(0, body.GetProperty("workouts").GetProperty("total").GetInt32());
        Assert.True(body.GetProperty("adaptations").ValueKind == System.Text.Json.JsonValueKind.Array);
        Assert.True(body.GetProperty("observations").ValueKind == System.Text.Json.JsonValueKind.Array);
        Assert.True(body.GetProperty("decisions").ValueKind == System.Text.Json.JsonValueKind.Array);
    }

    [Fact]
    public async Task SummaryAndExportRejectInvalidPeriods()
    {
        var client = await SuperadminAsync();
        const string invalid = "?from=2026-11-20T00:00:00Z&to=2026-11-01T00:00:00Z";

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/admin/intelligence/summary" + invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.GetAsync("/api/v1/admin/intelligence/export" + invalid)).StatusCode);
    }

    [Fact]
    public async Task ExportStillReturnsTheResearchZipWithItsManifestAndTables()
    {
        var response = await (await SuperadminAsync()).GetAsync(
            "/api/v1/admin/intelligence/export?from=2026-11-01T00:00:00Z&to=2026-11-20T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4);
        Assert.Equal(new byte[] { 0x50, 0x4b }, bytes[..2]);

        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, x => x.FullName == "manifest.json");
        Assert.Contains(archive.Entries, x => x.FullName == "research.json");
        Assert.Contains(archive.Entries, x => x.FullName == "plans.csv");
        Assert.Contains(archive.Entries, x => x.FullName == "workouts.csv");
        Assert.Contains(archive.Entries, x => x.FullName == "decisions.csv");
    }
}
