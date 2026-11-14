using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Infrastructure.Accounts;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Planning;

namespace Trazo.Api.Endpoints;

public sealed record TimeZoneRequest(string? TimeZone);
public sealed record DeletionRequestBody(string? Email);
public sealed record DeletionConfirmBody(string? Token, string? Code);

/// <summary>
/// The signed-in user's own account: information, data export, time zone and deletion (see docs/account-deletion.md). It also
/// serves the public web flow to delete an account without the app: a page with a form, an email with a single-use link, a
/// confirmation page and only then the deletion.
/// </summary>
public static class AccountEndpoints
{
    public const string RateLimitPolicy = "account";
    public const string DeletionRequestPolicy = "account-deletion-request";
    public const string DeletePurpose = "delete-account";
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(30);
    private const string DefaultPublicBaseUrl = "https://trazostrength.duckdns.org";

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/account").WithTags("Account");

        group.MapGet("", InfoAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicy).WithName("GetAccount")
            .WithSummary("Account facts and what deleting it requires (password, Google token, 2FA code) and warns about (Google Play subscription).")
            .Produces<AccountInfo>();
        group.MapGet("/export", ExportAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicy).WithName("ExportAccount")
            .WithSummary("Downloads all the data of the user as JSON (access and portability). Contains no secrets.")
            .Produces<AccountExport>();
        group.MapPut("/time-zone", SetTimeZoneAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicy).WithName("SetTimeZone")
            .WithSummary("Saves the IANA time zone (for example Europe/Madrid) used for today and the current plan week.")
            .Produces(StatusCodes.Status204NoContent).ProducesValidationProblem();
        group.MapDelete("", DeleteAsync).RequireAuthorization().RequireRateLimiting("auth").WithName("DeleteAccount")
            .WithSummary("Permanently deletes the account and all its data. Needs recent re-authentication in the body.")
            .Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict);
        // Same operation for HTTP clients that cannot send a body with DELETE.
        group.MapPost("/delete", DeleteAsync).RequireAuthorization().RequireRateLimiting("auth").WithName("DeleteAccountPost")
            .WithSummary("Same as DELETE /api/v1/account, for clients that cannot send a DELETE body.");

        group.MapPost("/deletion-request", RequestDeletionAsync).AllowAnonymous().RequireRateLimiting(DeletionRequestPolicy)
            .WithName("RequestAccountDeletion")
            .WithSummary("Web flow: sends a confirmation link to the email if an account exists. Always answers the same.");
        group.MapPost("/deletion-request/confirm", ConfirmDeletionAsync).AllowAnonymous().RequireRateLimiting("auth")
            .WithName("ConfirmAccountDeletion")
            .WithSummary("Web flow: consumes the link token and deletes the account.");

        endpoints.MapGet("/account/delete", (HttpContext http) => Html(http, AccountPages.RequestForm())).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapGet("/account/delete/confirm", ConfirmPageAsync).AllowAnonymous().RequireRateLimiting("auth").ExcludeFromDescription();
        return endpoints;
    }

    private static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static async Task<IResult> InfoAsync(ClaimsPrincipal principal, IAccountService accounts, CancellationToken ct)
    {
        if (UserId(principal) is not { } id) return Results.Unauthorized();
        return await accounts.GetInfoAsync(id, ct) is { } info ? Results.Ok(info) : Results.Unauthorized();
    }

    private static async Task<IResult> ExportAsync(ClaimsPrincipal principal, IAccountService accounts, HttpContext http, CancellationToken ct)
    {
        if (UserId(principal) is not { } id) return Results.Unauthorized();
        if (await accounts.ExportAsync(id, ct) is not { } export) return Results.Unauthorized();
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.ContentDisposition = "attachment; filename=\"trazo-datos.json\"";
        return Results.Ok(export);
    }

    private static async Task<IResult> SetTimeZoneAsync(TimeZoneRequest request, ClaimsPrincipal principal, IAccountService accounts, CancellationToken ct)
    {
        if (UserId(principal) is not { } id) return Results.Unauthorized();
        if (!UserTimeZone.TryParse(request.TimeZone, out _))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZone"] = ["Usa un identificador IANA válido, por ejemplo Europe/Madrid."] });
        return await accounts.SetTimeZoneAsync(id, request.TimeZone!, ct) ? Results.NoContent() : Results.Unauthorized();
    }

    private static async Task<IResult> DeleteAsync(
        [Microsoft.AspNetCore.Mvc.FromBody] ReauthRequest request, ClaimsPrincipal principal, TrazoDbContext db, Reauthenticator reauth, IAccountService accounts,
        ILoggerFactory loggers, CancellationToken ct)
    {
        if (UserId(principal) is not { } id) return Results.Unauthorized();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return Results.Unauthorized();
        if (!await reauth.VerifyAsync(user, request, ct))
            return Results.Json(new { code = "reauthentication_failed", error = "La contraseña, el token de Google o el código no son válidos." },
                statusCode: StatusCodes.Status403Forbidden);
        var result = await accounts.DeleteAsync(id, ct);
        if (!result.Deleted)
            return Results.Conflict(new { code = "last_superadmin", error = "Eres el último superadministrador: nombra a otro antes de eliminar la cuenta." });
        Audit(loggers, "self", result);
        return Results.Ok(new { deleted = true, playSubscriptionNotCancelled = result.PlaySubscriptionNotCancelled,
            manageSubscriptionUrl = result.PlaySubscriptionNotCancelled ? AccountService.ManageSubscriptionsUrl : null });
    }

    /// <summary>The audit trail of a deletion: who started it and how much was removed, never an email or a name.</summary>
    public static void Audit(ILoggerFactory loggers, string initiator, AccountDeletionResult result) =>
        loggers.CreateLogger("Trazo.Audit").LogInformation(
            "Account deleted: subject {Subject}, initiated by {Initiator}, {Rows} rows removed.", result.SubjectHash, initiator, result.RowsDeleted);

    // ---------------------------------------------------------------- web flow

    private static async Task<IResult> RequestDeletionAsync(
        HttpRequest request, DeletionRequestThrottle throttle, IServiceScopeFactory scopes, IConfiguration configuration, CancellationToken ct)
    {
        string? email;
        var html = request.HasFormContentType;
        if (html) email = (await ReadFormAsync(request, ct))?["email"].ToString();
        else email = (await ReadJsonAsync<DeletionRequestBody>(request, ct))?.Email;

        // Whatever happens next, the visitor gets the same answer: nothing reveals whether the address has an account.
        var normalized = Normalize(email);
        if (normalized is not null && throttle.TryAcquire(normalized))
        {
            var baseUrl = (configuration["Account:PublicBaseUrl"] ?? DefaultPublicBaseUrl).TrimEnd('/');
            // Sent in the background so the response time does not depend on the address existing.
            _ = Task.Run(() => SendLinkAsync(scopes, normalized, baseUrl));
        }
        return html ? HtmlResult(request.HttpContext, AccountPages.RequestReceived(), StatusCodes.Status202Accepted)
            : Results.Accepted(value: new { message = "Si existe una cuenta con ese correo, enviaremos un enlace de confirmación." });
    }

    private static async Task SendLinkAsync(IServiceScopeFactory scopes, string normalizedEmail, string baseUrl)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrazoDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail);
            if (user is null || user.DisabledAt is not null) return;
            var raw = await AccountEmailEndpoints.CreateTokenAsync(user, DeletePurpose, LinkLifetime, db, CancellationToken.None);
            var url = $"{baseUrl}/account/delete/confirm?token={Uri.EscapeDataString(raw)}";
            await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(user.Email, "Confirma la eliminación de tu cuenta de TRAZO",
                $"Has pedido eliminar tu cuenta de TRAZO y todos sus datos.\n\nPara confirmarlo, abre este enlace:\n\n{url}\n\n" +
                $"El enlace caduca en {(int)LinkLifetime.TotalMinutes} minutos y solo se puede usar una vez. Si no has sido tú, ignora este mensaje: no se borrará nada.\n\n" +
                "Eliminar la cuenta no cancela tu suscripción de Google Play; cancélala en Google Play si tenías una.",
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            // Never log the exception text: mail errors can quote the recipient address.
            scopes.CreateScope().ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Trazo.Audit")
                .LogWarning("The account deletion email could not be sent ({Error}).", ex.GetType().Name);
        }
    }

    private static async Task<IResult> ConfirmPageAsync(HttpContext http, TrazoDbContext db, string? token, CancellationToken ct)
    {
        var link = await FindLinkAsync(token, db, ct);
        if (link is null) return HtmlResult(http, AccountPages.Invalid(), StatusCodes.Status400BadRequest);
        var (_, user) = link.Value;
        return Html(http, AccountPages.Confirm(token!, Mask(user.Email), user.TwoFactorEnabled));
    }

    private static async Task<IResult> ConfirmDeletionAsync(
        HttpRequest request, TrazoDbContext db, Reauthenticator reauth, IAccountService accounts, ILoggerFactory loggers, CancellationToken ct)
    {
        var html = request.HasFormContentType;
        string? token, code;
        if (html) { var form = await ReadFormAsync(request, ct); token = form?["token"].ToString(); code = form?["code"].ToString(); }
        else { var body = await ReadJsonAsync<DeletionConfirmBody>(request, ct); token = body?.Token; code = body?.Code; }
        var http = request.HttpContext;

        IResult Invalid() => html ? HtmlResult(http, AccountPages.Invalid(), StatusCodes.Status400BadRequest)
            : Results.BadRequest(new { code = "invalid_link", error = "El enlace no es válido o ha caducado." });

        var link = await FindLinkAsync(token, db, ct);
        if (link is null) return Invalid();
        var (row, user) = link.Value;

        if (user.TwoFactorEnabled && !await reauth.VerifySecondFactorAsync(user, code, ct, countFailure: true))
            return html ? HtmlResult(http, AccountPages.Confirm(token!, Mask(user.Email), true, "El código no es válido."), StatusCodes.Status403Forbidden)
                : Results.Json(new { code = "reauthentication_failed", error = "El código no es válido." }, statusCode: StatusCodes.Status403Forbidden);

        var info = await accounts.GetInfoAsync(user.Id, ct);
        if (info is { CanDelete: false })
            return html ? HtmlResult(http, AccountPages.Blocked(info.CannotDeleteReason!), StatusCodes.Status409Conflict)
                : Results.Conflict(new { code = "last_superadmin", error = info.CannotDeleteReason });

        // Single use: only one request can flip ConsumedAt.
        var now = DateTimeOffset.UtcNow;
        var consumed = await db.EmailActionTokens.Where(x => x.Id == row.Id && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), ct);
        if (consumed != 1) return Invalid();

        var result = await accounts.DeleteAsync(user.Id, ct);
        if (!result.Deleted) return Invalid();
        Audit(loggers, "web-link", result);
        return html ? HtmlResult(http, AccountPages.Done(), StatusCodes.Status200OK)
            : Results.Ok(new { deleted = true, playSubscriptionNotCancelled = result.PlaySubscriptionNotCancelled });
    }

    private static async Task<(Trazo.Domain.Identity.EmailActionToken Token, Trazo.Domain.Identity.TrazoUser User)?> FindLinkAsync(
        string? token, TrazoDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var row = await AccountEmailEndpoints.FindToken(token, DeletePurpose, db, ct);
        if (row is null) return null;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
        return user is null ? null : (row, user);
    }

    // ---------------------------------------------------------------- helpers

    private static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 || !new EmailAddressAttribute().IsValid(email.Trim())) return null;
        return email.Trim().ToUpperInvariant();
    }

    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? "***" + email[Math.Max(at, 0)..] : email[0] + new string('*', Math.Min(at - 1, 6)) + email[at..];
    }

    private static async Task<IFormCollection?> ReadFormAsync(HttpRequest request, CancellationToken ct)
    {
        Limit(request);
        try { return await request.ReadFormAsync(ct); } catch (InvalidDataException) { return null; } catch (BadHttpRequestException) { return null; }
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        Limit(request);
        try { return await request.ReadFromJsonAsync<T>(ct); } catch (System.Text.Json.JsonException) { return null; } catch (BadHttpRequestException) { return null; }
    }

    private static void Limit(HttpRequest request)
    {
        var feature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = 8 * 1024;
    }

    private static IResult Html(HttpContext http, string page) => HtmlResult(http, page, StatusCodes.Status200OK);

    private static IResult HtmlResult(HttpContext http, string page, int status)
    {
        var headers = http.Response.Headers;
        headers.ContentSecurityPolicy = AccountPages.ContentSecurityPolicy;
        headers.CacheControl = "no-store";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        return Results.Content(page, "text/html; charset=utf-8", System.Text.Encoding.UTF8, status);
    }
}
