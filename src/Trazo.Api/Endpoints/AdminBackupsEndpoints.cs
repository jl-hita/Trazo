using System.Security.Claims;
using Trazo.Api.Support;
using Trazo.Infrastructure.Push;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Backup status of the administration console (superadmin only; see docs/backups.md). The status is read-only: it reports what the
/// server's backup scripts recorded and never reaches Drive or triggers anything. The only thing stored here is who receives the
/// push notification when the backups go wrong.
/// </summary>
public static class AdminBackupsEndpoints
{
    public sealed record BackupAlertRequest(Guid? UserId);

    public static IEndpointRouteBuilder MapAdminBackupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/backups")
            .WithTags("Admin backups")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        group.MapGet("", (IConfiguration configuration, TimeProvider time) =>
            Results.Ok(BackupStatusReader.Read(configuration["Backups:StatusPath"], time.GetUtcNow())));
        group.MapGet("/alerts", async (BackupAlertSettings settings, CancellationToken ct) => Results.Ok(await settings.GetViewAsync(ct)));
        group.MapPut("/alerts", SaveAlertsAsync);
        group.MapPost("/alerts/test", TestAlertAsync).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        return endpoints;
    }

    private static async Task<IResult> SaveAlertsAsync(
        BackupAlertRequest? request, BackupAlertSettings settings, ClaimsPrincipal principal, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (request is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["userId"] = ["Required."] });
        if (!await settings.SetRecipientAsync(request.UserId, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["userId"] = ["Elige una cuenta de administración activa."] });
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} {Change} the backup notifications.", OperationsSettingsEndpoints.Actor(principal),
            request.UserId is null ? "turned off" : "chose the recipient of");
        return Results.Ok(await settings.GetViewAsync(ct));
    }

    private static async Task<IResult> TestAlertAsync(
        BackupAlertSettings settings, IPushSender push, HttpContext http, IErrorReporter reporter, CancellationToken ct)
    {
        if (await settings.GetRecipientIdAsync(ct) is not { } recipient)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["userId"] = ["Elige primero quién recibe los avisos."] });
        return await PushEndpoints.SendAsync(push, http, reporter, [recipient],
            new PushMessage(BackupAlerter.Title, "Aviso de prueba: los avisos de backup llegan a este móvil.", "backup-test"), ct,
            noDevices: "Esa cuenta no tiene ningún móvil registrado. Debe abrir la app con su cuenta y conceder el permiso de notificaciones.");
    }
}
