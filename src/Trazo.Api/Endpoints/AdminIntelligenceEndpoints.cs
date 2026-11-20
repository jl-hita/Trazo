using Trazo.Infrastructure.Analytics;

namespace Trazo.Api.Endpoints;

/// <summary>Research-grade training analytics. Exports contain pseudonymous keys, never account identifiers or free-text notes.</summary>
public static class AdminIntelligenceEndpoints
{
    public static IEndpointRouteBuilder MapAdminIntelligenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/intelligence").WithTags("Admin intelligence")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        group.MapGet("/summary", SummaryAsync);
        group.MapGet("/export", ExportAsync);
        return endpoints;
    }

    private static async Task<IResult> SummaryAsync(
        IIntelligenceSummaryService intelligence, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        try
        {
            return Results.Ok(await intelligence.GetSummaryAsync(from, to, ct));
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    }

    private static async Task<IResult> ExportAsync(
        IIntelligenceExportService exporter, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        try
        {
            var export = await exporter.CreateAsync(from, to, ct);
            return Results.File(export.Content, "application/zip", export.FileName);
        }
        catch (ArgumentException exception)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
    }

}