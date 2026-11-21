using Trazo.Api.Overview;
using Trazo.Api.Support;

namespace Trazo.Api.Endpoints;

/// <summary>Operational overview of the administration console (superadmin only).</summary>
public static class AdminOverviewEndpoints
{
    public static IEndpointRouteBuilder MapAdminOverviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/overview").WithTags("Admin overview")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        group.MapGet("", GetAsync);
        group.MapGet("/system", (AdminOverviewService service) => Results.Ok(service.GetSystem()));
        return endpoints;
    }

    /// <param name="refresh">Skip the short cache (still limited to one rebuild every few seconds).</param>
    private static async Task<IResult> GetAsync(AdminOverviewService service, bool refresh = false, CancellationToken ct = default)
    {
        try
        {
            return Results.Ok(await service.GetAsync(refresh, ct));
        }
        catch (OverviewUnavailableException ex)
        {
            // The service only throws it (it does not report it). A database that does not answer is a real fault, so it is reported once
            // (the fingerprint groups the repeats of a console that polls) and the console still gets its 503.
            return ErrorResults.Failure(ex, StatusCodes.Status503ServiceUnavailable, "database_unavailable",
                "La base de datos no está disponible.", keepAnswerWhenReported: true);
        }
    }
}
