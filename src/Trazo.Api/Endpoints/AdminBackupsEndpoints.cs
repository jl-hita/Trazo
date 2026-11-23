using System.Security.Claims;
using Trazo.Api.Support;
using Trazo.Infrastructure.Push;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Backups in the administration console (superadmin only; see docs/backups.md). The status reports what the server's backup scripts
/// recorded; the API never reaches Drive. Taking a copy or restoring one only drops a request file that a root script picks up
/// (<see cref="BackupActions"/>). The only thing stored in the database is who receives the push notification when backups go wrong.
/// </summary>
public static class AdminBackupsEndpoints
{
    public sealed record BackupAlertRequest(Guid? UserId);
    public sealed record BackupRestoreRequest(string? Copy, string? Mode, string? Key, string? Confirmation);

    public static IEndpointRouteBuilder MapAdminBackupsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/backups")
            .WithTags("Admin backups")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        group.MapGet("", (IConfiguration configuration, TimeProvider time) =>
            Results.Ok(BackupStatusReader.Read(configuration["Backups:StatusPath"], time.GetUtcNow())));
        group.MapPost("/run", RunBackup).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        group.MapPost("/restore", Restore).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        group.MapGet("/alerts", async (BackupAlertSettings settings, CancellationToken ct) => Results.Ok(await settings.GetViewAsync(ct)));
        group.MapPut("/alerts", SaveAlertsAsync);
        group.MapPost("/alerts/test", TestAlertAsync).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        return endpoints;
    }

    private static IResult RunBackup(BackupActions actions, ClaimsPrincipal principal, ILoggerFactory loggerFactory)
    {
        var result = actions.RequestBackup();
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} asked for a backup now. Accepted: {Accepted}.", OperationsSettingsEndpoints.Actor(principal), result.Ok);
        return Reply(result);
    }

    // The private key travels in the body: nothing here may log the request or echo it back.
    private static IResult Restore(BackupRestoreRequest? request, BackupActions actions, ClaimsPrincipal principal, ILoggerFactory loggerFactory)
    {
        var result = actions.RequestRestore(request?.Copy, request?.Mode, request?.Key, request?.Confirmation);
        loggerFactory.CreateLogger("Trazo.Admin").LogWarning(
            "Superadmin {Actor} asked to restore {Copy} in mode {Mode}. Accepted: {Accepted}.",
            OperationsSettingsEndpoints.Actor(principal), request?.Copy, request?.Mode, result.Ok);
        return Reply(result);
    }

    private static IResult Reply(BackupActionResult result) =>
        Results.Json(new { ok = result.Ok, message = result.Message }, statusCode: result.StatusCode);

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
