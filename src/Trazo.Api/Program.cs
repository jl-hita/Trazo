using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Trazo.Infrastructure.Seeding;
using Trazo.Application;
using Trazo.Infrastructure;
using Trazo.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Apply all pending EF Core migrations before accepting HTTP traffic.
// This makes first deployment and subsequent upgrades use the same safe path.
await app.Services.InitializeDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AdminFrontend");

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "trazo-api"
}))
.WithName("Health");

app.MapGet("/health/ready", async (TrazoDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ready", database = "available" })
        : Results.Problem("Database is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("Readiness");

app.MapGet("/api/v1", () => Results.Ok(new
{
    name = "TRAZO API",
    version = "v1"
}))
.WithName("ApiInfo");

app.MapPost("/api/internal/exercise-catalog/import", async (
    HttpContext context,
    IConfiguration configuration,
    ExerciseLibraryImportService importer,
    ILogger<Program> logger) =>
{
    const long maxArchiveBytes = 512L * 1024L * 1024L;
    var expectedKey = configuration["DietoExpressImport:ApiKey"];
    var suppliedKey = context.Request.Headers["X-Trazo-Import-Key"].ToString();
    if (string.IsNullOrWhiteSpace(expectedKey))
        return Results.Problem("The private exercise import endpoint is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey));
    if (string.IsNullOrWhiteSpace(suppliedKey) || !CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))
        return Results.Unauthorized();
    if (!string.Equals(context.Request.ContentType?.Split(';')[0], "application/zip", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Content-Type must be application/zip." });
    if (context.Request.ContentLength is > maxArchiveBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = maxArchiveBytes;
    var path = Path.Combine(Path.GetTempPath(), $"trazo-exercise-import-{Guid.NewGuid():N}.zip");
    try
    {
        long total = 0;
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[128 * 1024];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
            {
                total += read;
                if (total > maxArchiveBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
                await output.WriteAsync(buffer.AsMemory(0, read), context.RequestAborted);
            }
        }
        var result = await importer.ImportAsync(path, context.RequestAborted);
        return Results.Json(result);
    }
    catch (InvalidDataException exception)
    {
        logger.LogWarning(exception, "Rejected invalid DietoExpress exercise-library archive.");
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "DietoExpress exercise-library import failed.");
        return Results.Problem("The exercise library could not be imported.", statusCode: StatusCodes.Status500InternalServerError);
    }
    finally
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) { logger.LogWarning(exception, "Could not remove temporary exercise import archive."); }
    }
})
.WithName("ImportDietoExpressExerciseLibrary");

app.Run();

public partial class Program
{
}
