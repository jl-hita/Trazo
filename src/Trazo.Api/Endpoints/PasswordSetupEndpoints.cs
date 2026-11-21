using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Api.Support;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Adds a password credential to an existing Google account without creating a second account.
/// Both flows use the existing single-use, hashed reset-password token and password-reset page.
/// </summary>
public static class PasswordSetupEndpoints
{
    public static IEndpointRouteBuilder MapPasswordSetupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/account/password/setup", SetupOwnPasswordAsync)
            .WithTags("Account security")
            .RequireAuthorization()
            .RequireRateLimiting("auth")
            .WithSummary("Emails a single-use password setup link to the signed-in account when it has no password.");

        endpoints.MapPost("/api/v1/admin/users/{id:guid}/password-invitation", InvitePasswordAsync)
            .WithTags("Admin users")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy)
            .RequireRateLimiting("auth")
            .WithSummary("Sends an existing account a single-use invitation to configure its password.");

        return endpoints;
    }

    private static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static async Task<IResult> SetupOwnPasswordAsync(
        ClaimsPrincipal principal,
        TrazoDbContext db,
        IEmailSender email,
        IRuntimeSettings settings,
        DeletionRequestThrottle throttle,
        Trazo.Application.Localization.RequestLanguage language,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        if (UserId(principal) is not { } id) return Results.Unauthorized();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null || user.DisabledAt is not null) return Results.Unauthorized();

        // The signed-in session proves identity, but the mailbox must also have been verified before it receives a credential link.
        if (user.EmailVerifiedAt is null)
            return Results.Conflict(new { code = "email_not_verified", error = "Verifica primero tu correo electrónico." });

        if (user.PasswordHash is not null)
            return Results.Conflict(new { code = "password_already_configured", error = "Esta cuenta ya tiene una contraseña. Utiliza la opción de recuperación si no la recuerdas." });

        if (!throttle.TryAcquire("password-setup|" + user.Id))
            return Results.Accepted(value: new { message = "Si la cuenta puede configurar una contraseña, recibirás un enlace por correo." });

        try
        {
            await AccountEmailEndpoints.IssueAsync(
                user, "reset-password", "Configura tu contraseña de TRAZO",
                "Has solicitado añadir una contraseña a tu cuenta de TRAZO. Abre este enlace para elegirla:",
                TimeSpan.FromMinutes(30), db, email, settings, ct, language: language.Current);
            loggers.CreateLogger("Trazo.Audit").LogInformation(
                "Password setup link issued for user {UserId} through authenticated account security.", user.Id);
            return Results.Accepted(value: new { message = "Te hemos enviado un enlace para configurar tu contraseña." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A mail server that does not answer is expected (503, no ticket); anything else (the token could not be stored...) is a defect (500 and a ticket).
            return ErrorResults.Failure(ex, StatusCodes.Status503ServiceUnavailable, "email_unavailable", "No se ha podido enviar el correo. Inténtalo más tarde.");
        }
    }

    private static async Task<IResult> InvitePasswordAsync(
        Guid id,
        ClaimsPrincipal principal,
        TrazoDbContext db,
        IEmailSender email,
        IRuntimeSettings settings,
        DeletionRequestThrottle throttle,
        Trazo.Application.Localization.RequestLanguage language,
        ILoggerFactory loggers,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (user is null) return Results.NotFound();
        if (user.DisabledAt is not null)
            return Results.Conflict(new { code = "account_disabled", error = "No se puede invitar a una cuenta desactivada." });
        if (user.PasswordHash is not null)
            return Results.Conflict(new { code = "password_already_configured", error = "La cuenta ya tiene una contraseña." });
        if (!throttle.TryAcquire("admin-password-invite|" + user.Id))
            return Results.Conflict(new { code = "invitation_throttled", error = "Ya se ha enviado recientemente una invitación a esta cuenta." });

        try
        {
            await AccountEmailEndpoints.IssueAsync(
                user, "reset-password", "Configura tu contraseña de TRAZO",
                "Un administrador de TRAZO te ha enviado una invitación para configurar una contraseña para tu cuenta. Abre este enlace para elegirla:",
                TimeSpan.FromHours(24), db, email, settings, ct, language: language.Current);
            var actorId = UserId(principal);
            loggers.CreateLogger("Trazo.Admin").LogInformation(
                "Superadmin {Actor} sent a password setup invitation to user {Target}.", actorId, user.Id);
            return Results.Accepted(value: new { message = "Invitación enviada." });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ErrorResults.Failure(ex, StatusCodes.Status503ServiceUnavailable, "email_unavailable", "No se ha podido enviar la invitación. Inténtalo más tarde.");
        }
    }
}
