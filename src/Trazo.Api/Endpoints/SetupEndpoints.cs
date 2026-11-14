using Microsoft.AspNetCore.RateLimiting;
using Trazo.Api.Contracts;
using Trazo.Api.Security;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

/// <summary>
/// First-run setup (public while the installation has no users and no completed setup) and
/// later configuration edits (superadmin only).
/// </summary>
public static class SetupEndpoints
{
    public const string SetupPolicy = "setup";
    public const string SetupTestPolicy = "setup-test";
    public const string SuperadminPolicy = "superadmin";

    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var setup = endpoints.MapGroup("/api/v1/setup").WithTags("Setup").RequireRateLimiting(SetupPolicy);
        // Status stays readable after setup so clients can tell that no setup is needed.
        setup.MapGet("/status", StatusAsync);
        setup.MapPost("/test/google", (TestGoogleRequest r, ISetupService s, IHttpClientFactory f, CancellationToken ct) =>
            GuardOpenAsync(s, ct, () => TestGoogleAsync(r.ClientId, f, ct))).RequireRateLimiting(SetupTestPolicy);
        setup.MapPost("/test/smtp", (TestSmtpRequest r, ISetupService s, ISmtpTransport t, CancellationToken ct) =>
            GuardOpenAsync(s, ct, () => TestSmtpAsync(r, null, t, ct))).RequireRateLimiting(SetupTestPolicy);
        setup.MapPost("/complete", CompleteAsync);

        var admin = endpoints.MapGroup("/api/v1/admin/settings").WithTags("Settings")
            .RequireAuthorization(SuperadminPolicy).RequireRateLimiting(SetupPolicy);
        admin.MapGet("", async (IRuntimeSettings s, CancellationToken ct) => Results.Ok(await s.GetViewAsync(ct)));
        admin.MapPut("", UpdateAsync);
        admin.MapPost("/test/google", (TestGoogleRequest r, IHttpClientFactory f, CancellationToken ct) =>
            TestGoogleAsync(r.ClientId, f, ct)).RequireRateLimiting(SetupTestPolicy);
        admin.MapPost("/test/smtp", (TestSmtpRequest r, IRuntimeSettings s, ISmtpTransport t, IEmailDeliveryRecorder rec, CancellationToken ct) =>
            TestSmtpAsync(r, s, t, ct, rec)).RequireRateLimiting(SetupTestPolicy);
        return endpoints;
    }

    private static async Task<IResult> StatusAsync(ISetupService setup, CancellationToken ct)
    {
        var s = await setup.GetStatusAsync(ct);
        return Results.Ok(new
        {
            setupRequired = s.SetupRequired,
            needsSuperadmin = s.NeedsSuperadmin,
            googleConfigured = s.GoogleConfigured,
            emailConfigured = s.EmailConfigured,
            configurationComplete = s.ConfigurationComplete
        });
    }

    // After setup the public endpoints do not exist as far as callers can tell.
    private static async Task<IResult> GuardOpenAsync(ISetupService setup, CancellationToken ct, Func<Task<IResult>> action) =>
        await setup.IsOpenAsync(ct) ? await action() : Results.NotFound();

    private static async Task<IResult> CompleteAsync(CompleteSetupRequest request, ISetupService setup, CancellationToken ct)
    {
        if (request.Google is null || request.Email is null || request.Admin is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["setup"] = ["google, email and admin are required."] });
        var result = await setup.CompleteAsync(new SetupInput(
            ToGoogle(request.Google), ToEmail(request.Email),
            new AdminAccountInput(request.Admin.Email ?? "", request.Admin.DisplayName ?? "", request.Admin.Password ?? "")), ct);
        return result.Outcome switch
        {
            SetupOutcome.Completed => Results.Ok(new { completed = true }),
            SetupOutcome.Invalid => Results.ValidationProblem(result.Errors!.ToDictionary(x => x.Key, x => x.Value)),
            _ => Results.NotFound()
        };
    }

    private static async Task<IResult> UpdateAsync(
        UpdateSettingsRequest request, ISettingsWriter writer, System.Security.Claims.ClaimsPrincipal principal,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (request.Google is null || request.Email is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["settings"] = ["google and email are required."] });
        var errors = await writer.UpdateAsync(ToGoogle(request.Google), ToEmail(request.Email), ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        // Audit trail: who changed which section. Values (the SMTP password in particular) are never written.
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} updated the Google sign-in and email settings.", OperationsSettingsEndpoints.Actor(principal));
        return Results.Ok(new { saved = true });
    }

    private static async Task<IResult> TestGoogleAsync(string? clientId, IHttpClientFactory factory, CancellationToken ct)
    {
        var errors = SettingsValidator.ValidateGoogle(new GoogleConfig(clientId ?? ""));
        if (errors.Count > 0)
            return Results.Ok(new { ok = false, message = errors.First().Value[0] });
        // A client ID cannot be proven valid without an ID token, so we validate its format and that
        // the API can reach Google's signing keys (needed to validate ID tokens at login).
        try
        {
            using var http = factory.CreateClient("google-test");
            using var response = await http.GetAsync("https://www.googleapis.com/oauth2/v3/certs", ct);
            return response.IsSuccessStatusCode
                ? Results.Ok(new { ok = true, message = "Client ID format is valid and the API can reach Google's signing keys." })
                : Results.Ok(new { ok = false, message = $"Google returned HTTP {(int)response.StatusCode}." });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Results.Ok(new { ok = false, message = "The API server cannot reach Google to validate ID tokens." });
        }
    }

    private static async Task<IResult> TestSmtpAsync(
        TestSmtpRequest request, IRuntimeSettings? stored, ISmtpTransport transport, CancellationToken ct,
        IEmailDeliveryRecorder? recorder = null)
    {
        if (request.Email is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Required."] });
        var config = ToEmail(request.Email);
        // A superadmin may test without retyping the saved password.
        if (config.Password is null && stored is not null)
            config = config with { Password = (await stored.GetEmailAsync(ct)).Password };
        var errors = SettingsValidator.ValidateEmail(config);
        if (SettingsValidator.NormalizeEmail(request.Recipient) is null)
            errors["recipient"] = ["A valid test recipient is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        try
        {
            await transport.SendAsync(config with { Host = config.Host.Trim(), From = config.From.Trim() },
                request.Recipient!.Trim(), "TRAZO: prueba de correo",
                "Este es un correo de prueba de TRAZO. Si lo has recibido, la configuración SMTP es correcta.", ct);
            if (recorder is not null) await recorder.RecordAsync(null, CancellationToken.None);
            return Results.Ok(new { ok = true, message = "Test email sent." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            if (recorder is not null) await recorder.RecordAsync(ex, CancellationToken.None);
            // Return only the exception type and a short message; never echo credentials.
            return Results.Ok(new { ok = false, message = $"{ex.GetType().Name}: {Truncate(ex.InnerException?.Message ?? ex.Message)}" });
        }
    }

    private static string Truncate(string s) => s.Length <= 200 ? s : s[..200];

    private static GoogleConfig ToGoogle(GoogleSettingsRequest g) => new(g.ClientId?.Trim() ?? "");

    private static EmailConfig ToEmail(EmailSettingsRequest e) =>
        new(e.Host?.Trim() ?? "", e.Port, e.EnableSsl, e.Username, e.Password, e.From?.Trim() ?? "", e.ActionBaseUrl?.Trim() ?? "");
}
