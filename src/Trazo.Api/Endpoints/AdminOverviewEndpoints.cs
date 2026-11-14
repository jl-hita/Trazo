using Trazo.Api.Overview;

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
        catch (OverviewUnavailableException)
        {
            return Results.Problem("La base de datos no está disponible.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }
}
