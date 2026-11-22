using Trazo.Api.Support;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Backup status of the administration console (superadmin only; see docs/backups.md). Read-only: it reports what the
/// server's backup scripts recorded and never reaches Drive or triggers anything.
/// </summary>
public static class AdminBackupsEndpoints
{
    public static IEndpointRouteBuilder MapAdminBackupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/v1/admin/backups")
            .WithTags("Admin backups")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy)
            .MapGet("", (IConfiguration configuration, TimeProvider time) =>
                Results.Ok(BackupStatusReader.Read(configuration["Backups:StatusPath"], time.GetUtcNow())));
        return endpoints;
    }
}
