using System.Security.Claims;
using Trazo.Api.Contracts;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Operational settings (extra CORS origins, rate limits, log level and retention, DietoExpress transfer) for superadmins.
/// Nothing here is needed to start the API or to sign in, which is why it can live in the database. See docs/configuration.md.
/// </summary>
public static class OperationsSettingsEndpoints
{
    public const string TestClientName = "dietoexpress-test";

    public static IEndpointRouteBuilder MapOperationsSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/settings/operations").WithTags("Settings")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);
        group.MapGet("", async (IRuntimeSettings s, CancellationToken ct) => Results.Ok(await s.GetOperationsViewAsync(ct)));
        group.MapPut("", SaveAsync);
        group.MapPost("/test/dietoexpress", TestDietoExpressAsync).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        return endpoints;
    }

    private static async Task<IResult> SaveAsync(
        OperationsSettingsRequest request, ISettingsWriter writer, IRuntimeSettings settings, IRuntimeOptions runtime,
        ClaimsPrincipal principal, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var result = await writer.UpdateOperationsAsync(new OperationsInput(
            request.AdditionalCorsOrigins, request.SetupPerMinute, request.SetupTestPerMinute, request.SubscriptionPerMinute,
            request.LogLevel, request.LogRetentionDays, request.DietoExpressEndpoint, request.DietoExpressApiKey), ct);
        if (!result.Succeeded) return Results.ValidationProblem(result.Errors.ToDictionary(x => x.Key, x => x.Value));

        // Applied right away in this process (CORS, limits, log level); other values are read on demand.
        await runtime.RefreshAsync(ct);
        // Audit trail (stored with the application logs): who changed what. Secret values are never written.
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} updated the operational settings. Changes: {Changes}",
            Actor(principal), result.Changes.Count == 0 ? "none" : string.Join("; ", result.Changes));
        return Results.Ok(await settings.GetOperationsViewAsync(ct));
    }

    /// <summary>
    /// Checks that the destination answers. It cannot prove the key is accepted (the receiver only checks it when a
    /// transfer is sent), and it never sends the key anywhere.
    /// </summary>
    private static async Task<IResult> TestDietoExpressAsync(
        TestDietoExpressRequest request, IRuntimeSettings settings, IHttpClientFactory factory, CancellationToken ct)
    {
        var endpoint = request.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint)) endpoint = (await settings.GetDietoExpressAsync(ct)).Endpoint;
        if (!OperationalValidator.TryParseEndpoint(endpoint ?? "", out var target))
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["dietoExpressEndpoint"] = ["Indica una URL https:// válida (http solo para localhost)."]
            });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var http = factory.CreateClient(TestClientName);
            using var response = await http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return Results.Ok(new
            {
                ok = true,
                message = $"DietoExpress respondió (HTTP {(int)response.StatusCode}). Se ha comprobado que el servidor es alcanzable; la clave solo se valida al enviar la biblioteca."
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Results.Ok(new { ok = false, message = "No se pudo conectar con DietoExpress en esa dirección." });
        }
    }

    internal static string Actor(ClaimsPrincipal principal) =>
        principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
}
