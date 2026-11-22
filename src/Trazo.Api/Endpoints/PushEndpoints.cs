using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Support;
using Trazo.Domain.Push;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Push;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Push notifications. The app asks for the (non-secret) Firebase client options, registers its token and removes it when the
/// user signs out; superadmins edit the configuration, test it and send announcements. The configuration lives in the
/// database (Settings page), never in the environment. Notifications carry no personal or health data.
/// </summary>
public static class PushEndpoints
{
    public const string RateLimitPolicy = "devices";
    public const int MaxDevicesPerUser = 10;
    public const int MaxTitleLength = 65;
    public const int MaxBodyLength = 240;

    public sealed record RegisterDeviceRequest(string? Token, string? Platform);
    public sealed record UnregisterDeviceRequest(string? Token);
    public sealed record BroadcastRequest(string? Title, string? Body);
    public sealed record PushSettingsRequest(
        string? ProjectId, string? ApplicationId, string? ApiKey, string? SenderId, string? ServiceAccountJson);

    public static IEndpointRouteBuilder MapPushEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var user = endpoints.MapGroup("/api/v1").WithTags("Push").RequireAuthorization().RequireRateLimiting(RateLimitPolicy);
        user.MapGet("/push/config", async (IRuntimeSettings s, CancellationToken ct) =>
        {
            var c = await s.GetPushAsync(ct);
            // Without the server key nothing could be sent, so the app has no reason to register.
            return Results.Ok(c.IsComplete
                ? new PushClientConfig(true, c.ProjectId, c.ApplicationId, c.ApiKey, c.SenderId)
                : new PushClientConfig(false, null, null, null, null));
        });
        user.MapPut("/devices", RegisterAsync);
        user.MapPost("/devices/unregister", UnregisterAsync);

        var settings = endpoints.MapGroup("/api/v1/admin/settings/push").WithTags("Settings")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);
        settings.MapGet("", async (IRuntimeSettings s, CancellationToken ct) => Results.Ok(await s.GetPushViewAsync(ct)));
        settings.MapPut("", SaveSettingsAsync);
        settings.MapPost("/test", TestAsync).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        settings.MapPost("/check", CheckAsync).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        settings.MapPost("/broadcast", BroadcastAsync).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        return endpoints;
    }

    private static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static bool IsValidToken(string? token) =>
        token is { Length: >= 20 and <= 1024 } && !token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));

    private static async Task<IResult> RegisterAsync(
        RegisterDeviceRequest? request, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        if (!IsValidToken(request?.Token)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["token"] = ["Expected a registration token."] });
        var token = request!.Token!;
        var now = clock.GetUtcNow();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var existing = await db.DeviceTokens.FirstOrDefaultAsync(d => d.Token == token, ct);
            if (existing is not null)
            {
                // The phone now belongs to whoever is signed in on it.
                existing.UserId = userId;
                existing.LastSeenAt = now;
            }
            else
            {
                db.DeviceTokens.Add(new DeviceToken { Id = Guid.NewGuid(), UserId = userId, Token = token, Platform = "android", CreatedAt = now, LastSeenAt = now });
            }
            try
            {
                await db.SaveChangesAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (attempt == 0 && DbErrors.IsUniqueViolation(ex))
            {
                // Two requests registered the same token at once: the second one updates the row the first created.
                db.ChangeTracker.Clear();
            }
        }

        // A user keeps at most a few devices: the least recently seen ones go first.
        var stale = await db.DeviceTokens.Where(d => d.UserId == userId).OrderByDescending(d => d.LastSeenAt)
            .Skip(MaxDevicesPerUser).Select(d => d.Id).ToListAsync(ct);
        if (stale.Count > 0) await db.DeviceTokens.Where(d => stale.Contains(d.Id)).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> UnregisterAsync(
        UnregisterDeviceRequest? request, ClaimsPrincipal principal, TrazoDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        if (!IsValidToken(request?.Token)) return Results.NoContent();
        var token = request!.Token!;
        // Only the owner can remove it; anything else is silently ignored so tokens cannot be probed.
        await db.DeviceTokens.Where(d => d.Token == token && d.UserId == userId).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveSettingsAsync(
        PushSettingsRequest request, ISettingsWriter writer, ClaimsPrincipal principal, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var errors = await writer.UpdatePushAsync(new PushInput(
            request.ProjectId ?? "", request.ApplicationId ?? "", request.ApiKey ?? "", request.SenderId, request.ServiceAccountJson), ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} updated the push notification settings.", OperationsSettingsEndpoints.Actor(principal));
        return Results.Ok(new { saved = true });
    }

    /// <summary>
    /// The check and the sends answer 200 with <c>ok: false</c> and a message when Firebase cannot be used: that is the result the console
    /// shows, not a failure of this API. Which of those results are expected (no ticket) and which are a provider fault (a ticket):
    /// not configured, credentials refused and Firebase not reachable are conditions the superadmin fixes or waits out; Firebase
    /// answering with an error status is a fault on the provider's side or ours and is reported, with its trace id in the answer.
    /// </summary>
    private static async Task<string?> PushFailureAsync(PushException ex, HttpContext http, IErrorReporter reporter)
    {
        // A PushException with an inner exception wraps a network failure; without one it wraps an unexpected answer of Firebase.
        if (ex.Failure != PushFailure.Unavailable || ex.InnerException is not null) ex.AsExpected();
        var traceId = ErrorMarks.IsExpected(ex) ? null : ErrorResults.TraceIdOf(http);
        if (traceId is not null) await reporter.ReportAsync(ex, traceId, CancellationToken.None);
        return traceId;
    }

    private static async Task<IResult> CheckAsync(IPushSender push, HttpContext http, IErrorReporter reporter, CancellationToken ct)
    {
        try
        {
            await push.CheckConnectionAsync(ct);
            return Results.Ok(new { ok = true, message = "Firebase accepted the service account and project." });
        }
        catch (PushException ex)
        {
            // Messages are written by us and never include credentials.
            return Results.Ok(new { ok = false, message = ex.Message, traceId = await PushFailureAsync(ex, http, reporter) });
        }
    }

    private static async Task<IResult> TestAsync(IPushSender push, ClaimsPrincipal principal, HttpContext http, IErrorReporter reporter, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        return await SendAsync(push, http, reporter, [userId], new PushMessage("TRAZO", "Notificación de prueba: las notificaciones funcionan.", "test"), ct,
            noDevices: "No hay ningún dispositivo registrado con tu cuenta. Abre la app con tu cuenta de administrador y concede el permiso de notificaciones.");
    }

    private static async Task<IResult> BroadcastAsync(
        BroadcastRequest? request, IPushSender push, ClaimsPrincipal principal, HttpContext http, IErrorReporter reporter, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var title = request?.Title?.Trim() ?? "";
        var body = request?.Body?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (title.Length is 0 or > MaxTitleLength) errors["title"] = [$"The title is required and at most {MaxTitleLength} characters."];
        if (body.Length is 0 or > MaxBodyLength) errors["body"] = [$"The text is required and at most {MaxBodyLength} characters."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var result = await SendAsync(push, http, reporter, null, new PushMessage(title, body, "announcement"), ct, noDevices: "Todavía no hay dispositivos registrados.");
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} sent a push announcement to all devices.", OperationsSettingsEndpoints.Actor(principal));
        return result;
    }

    internal static async Task<IResult> SendAsync(
        IPushSender push, HttpContext http, IErrorReporter reporter, IReadOnlyCollection<Guid>? users, PushMessage message, CancellationToken ct, string noDevices)
    {
        try
        {
            var r = await push.SendAsync(users, message, ct);
            if (r.Devices == 0) return Results.Ok(new { ok = false, message = noDevices, r.Devices, r.Sent, r.Failed, r.Removed });
            return Results.Ok(new
            {
                ok = r.Sent > 0,
                message = $"Enviada a {r.Sent} de {r.Devices} dispositivos" + (r.Removed > 0 ? $" ({r.Removed} caducados eliminados)." : "."),
                r.Devices, r.Sent, r.Failed, r.Removed
            });
        }
        catch (PushException ex)
        {
            return Results.Ok(new { ok = false, message = ex.Message, traceId = await PushFailureAsync(ex, http, reporter), Devices = 0, Sent = 0, Failed = 0, Removed = 0 });
        }
    }
}
