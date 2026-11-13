using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Trazo.Infrastructure.Seeding;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Sends the exercises created in TRAZO to DietoExpress. It reuses the shared secret of the DietoExpress to TRAZO
/// transfer (<c>DietoExpressImport:ApiKey</c> here, <c>TrazoTransfer:ApiKey</c> there); the destination is
/// <c>DietoExpressTransfer:Endpoint</c>. The archive is built in a temporary file and never returned to the browser.
/// </summary>
public static class AdminLibraryTransferEndpoints
{
    public const string ClientName = "dietoexpress-transfer";
    public const string KeyHeader = "X-Trazo-Transfer-Key";
    public const string ImportPath = "api/internal/trazo-exercise-library/import";

    public static IEndpointRouteBuilder MapAdminLibraryTransferEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/v1/admin/exercise-library-transfer")
            .WithTags("Admin library transfer")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy)
            .MapPost("/dietoexpress", SendAsync);
        return endpoints;
    }

    private static async Task<IResult> SendAsync(
        ExerciseLibraryExporter exporter, IHttpClientFactory httpClientFactory, IConfiguration configuration,
        ClaimsPrincipal principal, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Trazo.Admin");
        var endpoint = configuration["DietoExpressTransfer:Endpoint"];
        var key = configuration["DietoExpressImport:ApiKey"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var target) ||
            !(target.Scheme == Uri.UriSchemeHttps || (target.Scheme == Uri.UriSchemeHttp && target.IsLoopback)) ||
            string.IsNullOrWhiteSpace(key))
            return Results.Json(new
            {
                error = "The transfer to DietoExpress is not configured. Set DietoExpressTransfer:Endpoint and DietoExpressImport:ApiKey on the server."
            }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var path = Path.Combine(Path.GetTempPath(), $"trazo-dietoexpress-{Guid.NewGuid():N}.zip");
        try
        {
            ExportResult exported;
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                exported = await exporter.BuildAsync(file, cancellationToken);
            if (exported.ExerciseCount == 0)
                return Results.Conflict(new { error = "There are no exercises created in TRAZO to send.", skipped = exported.Skipped });

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(target, ImportPath));
            request.Headers.Add(KeyHeader, key);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StreamContent(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");

            using var response = await httpClientFactory.CreateClient(ClientName).SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("DietoExpress rejected the exercise transfer with {StatusCode}: {Body}", (int)response.StatusCode,
                    body.Length > 500 ? body[..500] : body);
                return Results.Json(new
                {
                    error = "DietoExpress did not accept the transfer. Nothing was confirmed.",
                    targetStatus = (int)response.StatusCode, skipped = exported.Skipped
                }, statusCode: StatusCodes.Status502BadGateway);
            }

            logger.LogInformation("Superadmin {Actor} sent {Exercises} exercises and {Images} images to DietoExpress.",
                principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), exported.ExerciseCount, exported.ImageCount);
            JsonElement? remote = null;
            try { remote = JsonDocument.Parse(body).RootElement.Clone(); } catch (JsonException) { /* the receiver answered without JSON */ }
            return Results.Ok(new { sent = true, exercises = exported.ExerciseCount, images = exported.ImageCount, skipped = exported.Skipped, remote });
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Could not reach DietoExpress for the exercise transfer.");
            return Results.Json(new { error = "DietoExpress could not be reached.", skipped = Array.Empty<object>() }, statusCode: StatusCodes.Status502BadGateway);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception exception) { logger.LogWarning(exception, "Could not remove the temporary transfer archive."); }
        }
    }
}
