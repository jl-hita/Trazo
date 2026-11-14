using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Trazo.Infrastructure.Seeding;
using Npgsql;
using Trazo.Application;
using Trazo.Infrastructure;
using Trazo.Infrastructure.Settings;
using Trazo.Infrastructure.Logging;
using Trazo.Infrastructure.Persistence;
using Trazo.Api.Endpoints;
using Trazo.Api.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();
builder.Services.AddApplication();
if (!builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeysPath"]))
{
    throw new InvalidOperationException(
        "DataProtection:KeysPath must point to a persistent, access-restricted directory in non-development environments.");
}
builder.Services.AddInfrastructure(builder.Configuration);
// Logs shown in the administration console. The test host has no PostgreSQL, so it does not store them.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddDatabaseLogging(builder.Configuration);
builder.Services.AddSingleton<AuthTokenService>();
builder.Services.AddSingleton<Trazo.Api.Billing.IPubSubTokenValidator, Trazo.Api.Billing.GooglePubSubTokenValidator>();
builder.Services.AddSingleton<ISmtpTransport, SystemNetSmtpTransport>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient("google-test", c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient(AdminLibraryTransferEndpoints.ClientName, c => c.Timeout = TimeSpan.FromMinutes(5));

var jwtIssuer = builder.Configuration["Authentication:Jwt:Issuer"];
var jwtAudience = builder.Configuration["Authentication:Jwt:Audience"];
var jwtSigningKey = builder.Configuration["Authentication:Jwt:SigningKey"];
if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience) ||
    string.IsNullOrWhiteSpace(jwtSigningKey) || Encoding.UTF8.GetByteCount(jwtSigningKey) < 32)
{
    throw new InvalidOperationException(
        "Authentication:Jwt:Issuer, Audience and a SigningKey of at least 32 UTF-8 bytes are required.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Name,
            RoleClaimType = ClaimTypes.Role
        };
        // Access tokens live for an hour, so check the account on every request: disabling a user or
        // changing a role by an administrator takes effect immediately instead of when the token expires.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirstValue("sub") ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(subject, out var userId)) { context.Fail("Invalid token subject."); return; }
                var db = context.HttpContext.RequestServices.GetRequiredService<TrazoDbContext>();
                var current = await db.Users.AsNoTracking().Where(x => x.Id == userId)
                    .Select(x => new { x.Role, x.DisabledAt })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                if (current is null || current.DisabledAt is not null) { context.Fail("The account is disabled or no longer exists."); return; }
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    var roles = identity.FindAll(ClaimTypes.Role).ToList();
                    if (roles.Count != 1 || roles[0].Value != current.Role)
                    {
                        foreach (var claim in roles) identity.RemoveClaim(claim);
                        identity.AddClaim(new Claim(ClaimTypes.Role, current.Role));
                    }
                }
            }
        };
    });
builder.Services.AddAuthorization(options =>
    options.AddPolicy(SetupEndpoints.SuperadminPolicy, p => p.RequireRole("superadmin")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(SetupEndpoints.SetupPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(SetupEndpoints.SetupTestPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(SubscriptionEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(PlansEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});

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
// Integration tests use the "Testing" environment with their own (non-PostgreSQL) database.
if (!app.Environment.IsEnvironment("Testing"))
{
    await app.Services.InitializeDatabaseAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
// Local Development is intentionally reachable over HTTP from the Android emulator.
// Non-development environments continue to enforce HTTPS redirection.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseCors("AdminFrontend");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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

app.MapExerciseCatalogEndpoints();
app.MapSetupEndpoints();
app.MapAuthenticationEndpoints();
app.MapAdminUsersEndpoints();
app.MapAdminExercisesEndpoints();
app.MapAdminPlanningEndpoints();
app.MapAdminLibraryTransferEndpoints();
app.MapAdminLogsEndpoints();
app.MapAccountEmailEndpoints();
app.MapSubscriptionEndpoints();
app.MapPlansEndpoints();
app.MapAdminSubscriptionsEndpoints();

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


app.MapGet("/api/v1/exercises/{id}/images", async (string id, TrazoDbContext db, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(id) || id.Length > 100)
        return Results.BadRequest();
    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;
    if (shouldClose) await connection.OpenAsync(cancellationToken);
    try
    {
        var slots = new List<int>();
        var hasRepDbImages = false;
        await using var command = new NpgsqlCommand(
            "SELECT slot, source FROM exercise_catalog_images WHERE exercise_id = @id ORDER BY slot", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            slots.Add(reader.GetInt16(0));
            hasRepDbImages |= reader.GetString(1) == "repdb-free";
        }
        return Results.Ok(new { slots, attribution = hasRepDbImages ? "Exercise data by RepDB (repdb.co)" : null });
    }
    finally
    {
        if (shouldClose) await connection.CloseAsync();
    }
})
.WithName("GetExerciseImageSlots");

app.MapGet("/api/v1/exercises/{id}/images/{slot:int}", async (
    string id, int slot, TrazoDbContext db, HttpContext context, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || slot is < 0 or > 3)
        return Results.BadRequest();
    var connection = (NpgsqlConnection)db.Database.GetDbConnection();
    var shouldClose = connection.State != System.Data.ConnectionState.Open;
    if (shouldClose) await connection.OpenAsync(cancellationToken);
    try
    {
        await using var command = new NpgsqlCommand(
            "SELECT content_type, data, source FROM exercise_catalog_images WHERE exercise_id = @id AND slot = @slot", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("slot", (short)slot);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return Results.NotFound();
        context.Response.Headers.CacheControl = "public, max-age=86400";
        if (reader.GetString(2) == "repdb-free")
            context.Response.Headers["X-Image-Attribution"] = "Exercise data by RepDB (repdb.co)";
        return Results.File((byte[])reader["data"], reader.GetString(0));
    }
    finally
    {
        if (shouldClose) await connection.CloseAsync();
    }
})
.WithName("GetExerciseImage");

app.Run();

public partial class Program
{
}
