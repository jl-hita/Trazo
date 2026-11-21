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
using Trazo.Api.Support;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
// Settings edited in the administration console that framework features watch through IConfiguration (the log level).
// Added last so a database value wins over the environment; empty until the first read of the database.
var runtimeOverrides = new Trazo.Infrastructure.Settings.RuntimeOverridesConfigurationSource();
((Microsoft.Extensions.Configuration.IConfigurationBuilder)builder.Configuration).Add(runtimeOverrides);
builder.Services.AddSingleton(runtimeOverrides);
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
// Retries pending referral rewards and converts invitations the event path missed. Not run by the test host.
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<Trazo.Infrastructure.Growth.ReferralReconcilerWorker>();
builder.Services.AddSingleton<AuthTokenService>();
builder.Services.AddSingleton<Trazo.Api.Overview.OverviewCache>();
builder.Services.AddScoped<Trazo.Api.Overview.OverviewInsightsBuilder>();
builder.Services.AddScoped<Trazo.Api.Overview.AdminOverviewService>();
builder.Services.AddSingleton<Trazo.Api.Billing.IPubSubTokenValidator, Trazo.Api.Billing.GooglePubSubTokenValidator>();
builder.Services.AddSingleton<ISmtpTransport, SystemNetSmtpTransport>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddErrorTickets();
builder.Services.AddScoped<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
builder.Services.AddScoped<Reauthenticator>();
builder.Services.AddSingleton<DeletionRequestThrottle>();
builder.Services.AddSingleton<LoginAttemptThrottle>();
builder.Services.AddSingleton<GoogleNonceStore>();
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(180));
builder.Services.AddSingleton<Trazo.Infrastructure.Push.IFcmAccessTokenProvider, Trazo.Infrastructure.Push.GoogleFcmAccessTokenProvider>();
builder.Services.AddHttpClient<Trazo.Infrastructure.Push.IPushSender, Trazo.Infrastructure.Push.FcmPushSender>(c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHttpClient("google-test",c => c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient(OperationsSettingsEndpoints.TestClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
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
{
    options.AddPolicy(SetupEndpoints.SuperadminPolicy, p => p.RequireRole("superadmin"));
    // Work every administrator can do (plan reviews). Global configuration, users, logs and support tickets stay superadmin-only.
    options.AddPolicy(SetupEndpoints.StaffPolicy, p => p.RequireRole("admin", "superadmin"));
    options.AddPolicy(SetupEndpoints.FiscalPolicy, p => p.RequireRole("superadmin", "accountant"));
    options.AddPolicy(SetupEndpoints.MarketingPolicy, p => p.RequireRole("superadmin", "commercial"));
    // The commercial role can administer referrals; unrelated admin settings remain superadmin-only.
    options.AddPolicy(ReferralEndpoints.ManagementPolicy, p => p.RequireRole("superadmin", "commercial"));
});
// Limits come from the runtime settings (database > environment > default) and are read on every request, so a
// change applies without a restart. The limit is part of the partition key: a new limit starts a fresh window.
// The login limiter ("auth") is deliberately fixed: it protects the sign-in itself and is not editable.
static string LimiterKey(string baseKey, int permitLimit) => $"{baseKey}|{permitLimit}";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(SetupEndpoints.SetupPolicy, httpContext =>
    {
        var limit = httpContext.RequestServices.GetRequiredService<IRuntimeOptions>().Current.SetupPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            LimiterKey(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", limit),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    options.AddPolicy(SetupEndpoints.SetupTestPolicy, httpContext =>
    {
        var limit = httpContext.RequestServices.GetRequiredService<IRuntimeOptions>().Current.SetupTestPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            LimiterKey(httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", limit),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    options.AddPolicy(SubscriptionEndpoints.RateLimitPolicy, httpContext =>
    {
        var limit = httpContext.RequestServices.GetRequiredService<IRuntimeOptions>().Current.SubscriptionPerMinute;
        return RateLimitPartition.GetFixedWindowLimiter(
            LimiterKey(httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown", limit),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });
    });
    options.AddPolicy(PlansEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(AccountEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Public deletion-request form: per IP. (The per-email limit lives in DeletionRequestThrottle.)
    options.AddPolicy(AccountEndpoints.DeletionRequestPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            "del|" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    options.AddPolicy(AnalyticsEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            "ana|" + (httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(PushEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Public landing form: a few attempts per minute per IP. Fixed on purpose, like the sign-in limiter.
    options.AddPolicy(WaitlistEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Crash reports of the app: anonymous, so per IP and fixed.
    options.AddPolicy(ErrorTicketEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            "diag|" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy(ReferralEndpoints.RateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            "ref|" + (httpContext.User.FindFirstValue("sub") ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Public invitation page and install-referrer counter: anonymous, so per IP and fixed.
    options.AddPolicy(ReferralEndpoints.PublicRateLimitPolicy, httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            "refpub|" + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"),
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
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

// Origins from the environment (Cors:AllowedOrigins) are always allowed; the console can only ADD origins on top of
// them, so a wrong entry there can never lock the administration web out.
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
    .Configure<IRuntimeOptions>((options, runtime) =>
        options.AddPolicy("AdminFrontend", policy => policy
            .SetIsOriginAllowed(runtime.IsCorsOriginAllowed)
            .AllowAnyHeader()
            .AllowAnyMethod()));
builder.Services.AddCors();
// Error answers (ProblemDetails) are put in the language of the request on the way out (see ProblemTranslator).
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    // Every problem answer gets the trace id and a stable code (see ErrorResults), then its text in the language of the request.
    Trazo.Api.Support.ErrorResults.Describe(context.ProblemDetails, context.HttpContext);
    if (context.HttpContext.RequestServices.GetService<Trazo.Application.Localization.RequestLanguage>() is { } language)
        Trazo.Api.Support.ProblemTranslator.Translate(context.ProblemDetails, language.Current);
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

// First, so it sees every exception of the pipeline: reports it as an anonymous error ticket and answers a plain 500.
app.UseExceptionHandler();
app.UseForwardedHeaders();
// The language of the request, for everything that answers in text: from Accept-Language, Spanish by default.
app.Use(async (context, next) =>
{
    context.RequestServices.GetRequiredService<Trazo.Application.Localization.RequestLanguage>().Current =
        Trazo.Application.Localization.LanguageNegotiation.FromAcceptLanguage(context.Request.Headers.AcceptLanguage.ToString());
    await next();
});
// Baseline for every answer of the API. Pages that need more (the account-deletion pages) set their own on top.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "no-referrer";
    await next();
});
// Local Development is intentionally reachable over HTTP from the Android emulator.
// Non-development environments continue to enforce HTTPS redirection.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseCors("AdminFrontend");
app.UseAuthentication();
// A user who chose a language gets it whatever the header says (so texts, e-mails and notifications agree with each other).
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true
        && Guid.TryParse(context.User.FindFirst("sub")?.Value ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var userId))
    {
        var db = context.RequestServices.GetRequiredService<Trazo.Infrastructure.Persistence.TrazoDbContext>();
        var choice = await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Language).FirstOrDefaultAsync(context.RequestAborted);
        if (Trazo.Application.Localization.LanguageNegotiation.FromChoice(choice) is { } chosen)
            context.RequestServices.GetRequiredService<Trazo.Application.Localization.RequestLanguage>().Current = chosen;
    }
    await next();
});
// After authentication: the per-user limiters partition by the "sub" claim, which does not exist before it.
app.UseRateLimiter();
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
app.MapAdminPremiumGrantsEndpoints();
app.MapAdminExercisesEndpoints();
app.MapAdminPlanningEndpoints();
app.MapAdminLibraryTransferEndpoints();
app.MapAdminLogsEndpoints();
app.MapAdminOverviewEndpoints();
app.MapAdminIntelligenceEndpoints();
app.MapAccountEmailEndpoints();
app.MapPasswordSetupEndpoints();
app.MapAccountEndpoints();
app.MapSubscriptionEndpoints();
app.MapPlansEndpoints();
app.MapPlanReviewEndpoints();
app.MapAdminSubscriptionsEndpoints();
app.MapOperationsSettingsEndpoints();
app.MapWaitlistEndpoints();
app.MapPushEndpoints();
app.MapAnalyticsEndpoints();
app.MapReferralEndpoints();
app.MapAdminFiscalEndpoints();
app.MapMarketingEndpoints();
app.MapErrorTicketEndpoints();

app.MapPost("/api/internal/exercise-catalog/import", async (
    HttpContext context,
    IRuntimeSettings runtimeSettings,
    ExerciseLibraryImportService importer,
    ILogger<Program> logger) =>
{
    const long maxArchiveBytes = 512L * 1024L * 1024L;
    // Database value (set in the console) wins over the DietoExpressImport__ApiKey environment variable.
    var expectedKey = (await runtimeSettings.GetDietoExpressAsync(context.RequestAborted)).ApiKey;
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
        // Reported as an error ticket (not an expected condition); the sender still gets the specific text.
        return ErrorResults.Failure(exception, StatusCodes.Status500InternalServerError, "library_import_failed",
            "The exercise library could not be imported.", keepAnswerWhenReported: true);
    }
    finally
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { logger.LogWarning(exception, "Could not remove temporary exercise import archive."); }
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
        // Images share the origin of the administration console: opened directly, an SVG must not be able to run anything.
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
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
