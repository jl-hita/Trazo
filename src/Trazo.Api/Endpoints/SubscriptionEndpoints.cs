using System.Security.Claims;
using System.Text.Json;
using Trazo.Api.Billing;
using Trazo.Api.Contracts;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The signed-in user's subscription (Google Play Billing) and the Pub/Sub webhook that keeps it current.
/// Google Play is the source of truth: the app only hands over a purchase token, and every notification
/// makes the API ask Google again instead of trusting the notification body.
/// </summary>
public static class SubscriptionEndpoints
{
    public const string RateLimitPolicy = "subscription";
    private const int MaxTokenLength = 2000;
    private const int MaxNotificationBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapSubscriptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/subscription").WithTags("Subscription");
        group.MapGet("", GetAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/verify", VerifyAsync).RequireAuthorization().RequireRateLimiting(RateLimitPolicy);
        // Authenticated by the Pub/Sub OIDC token or the shared secret, not by a user session.
        group.MapPost("/notifications", NotificationAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(ClaimsPrincipal principal, ISubscriptionService subscriptions, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        return Results.Ok(await subscriptions.GetStatusAsync(userId, ct));
    }

    private static async Task<IResult> VerifyAsync(
        VerifySubscriptionRequest request, ClaimsPrincipal principal, ISubscriptionService subscriptions,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var errors = new Dictionary<string, string[]>();
        var productId = request.ProductId?.Trim();
        var token = request.PurchaseToken?.Trim();
        if (string.IsNullOrEmpty(productId) || productId.Length > 200) errors["productId"] = ["A product id is required."];
        if (string.IsNullOrEmpty(token) || token.Length > MaxTokenLength) errors["purchaseToken"] = ["A purchase token is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var logger = loggerFactory.CreateLogger("Trazo.Billing");
        try
        {
            var result = await subscriptions.VerifyAsync(userId, productId!, token!, ct);
            switch (result.Outcome)
            {
                case VerifyOutcome.Verified:
                    return Results.Ok(await subscriptions.GetStatusAsync(userId, ct));
                case VerifyOutcome.TokenInUse:
                    logger.LogWarning("User {UserId} tried to link a purchase token that belongs to another account.", userId);
                    return Results.Json(new { error = "purchase_already_linked", message = "This purchase is linked to another account." },
                        statusCode: StatusCodes.Status409Conflict);
                case VerifyOutcome.AccountMismatch:
                    logger.LogWarning("User {UserId} tried to link a purchase made by a different account.", userId);
                    return Results.Json(new { error = "purchase_account_mismatch", message = "This purchase was made by another account." },
                        statusCode: StatusCodes.Status409Conflict);
                default:
                    return Results.Json(new { error = "product_mismatch", message = "The purchase is for a different product." },
                        statusCode: StatusCodes.Status400BadRequest);
            }
        }
        catch (PlayBillingException ex)
        {
            return BillingProblem(ex, logger);
        }
    }

    private static async Task<IResult> NotificationAsync(
        HttpRequest request, IRuntimeSettings settings, IPubSubTokenValidator validator,
        ISubscriptionService subscriptions, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Trazo.Billing");
        var config = await settings.GetPlayBillingAsync(ct);
        if (!config.IsComplete || !config.RtdnAuthConfigured)
            return Results.Problem("Google Play notifications are not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (!await RtdnAuthenticator.IsAuthenticAsync(request, config, validator, ct))
            return Results.Unauthorized();
        if (request.ContentLength is > MaxNotificationBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        RtdnMessage? message;
        try
        {
            message = await ReadMessageAsync(request, ct);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
        {
            // Retrying a malformed message would never help, so acknowledge it to Pub/Sub.
            logger.LogWarning(ex, "Ignored a malformed Google Play notification.");
            return Results.Ok(new { ignored = true });
        }
        if (message is null) return Results.Ok(new { ignored = true });
        if (!string.IsNullOrEmpty(message.PackageName) &&
            !string.Equals(message.PackageName, config.PackageName, StringComparison.Ordinal))
        {
            logger.LogWarning("Ignored a Google Play notification for another package ({Package}).", message.PackageName);
            return Results.Ok(new { ignored = true });
        }
        if (message.PurchaseToken is null) return Results.Ok(new { ignored = true }); // test notification

        try
        {
            // The notification only says "something changed for this token": the state comes from Google.
            var row = message.Voided
                ? await subscriptions.RevokeAsync(message.PurchaseToken, ct)
                : await subscriptions.SyncAsync(message.PurchaseToken, ct);
            if (row is null)
                logger.LogInformation("Google Play notification for a purchase that is not linked to a user yet.");
            return Results.Ok(new { processed = row is not null });
        }
        catch (PlayBillingException ex)
        {
            // A 5xx makes Pub/Sub redeliver the message later.
            logger.LogError(ex, "Could not process a Google Play notification ({Failure}).", ex.Failure);
            return Results.Problem("The notification could not be processed.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private sealed record RtdnMessage(string? PackageName, string? PurchaseToken, bool Voided);

    private static async Task<RtdnMessage?> ReadMessageAsync(HttpRequest request, CancellationToken ct)
    {
        using var envelope = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        if (!envelope.RootElement.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("data", out var data) || data.GetString() is not { Length: > 0 } encoded)
            return null;
        using var body = JsonDocument.Parse(Convert.FromBase64String(encoded));
        var root = body.RootElement;
        var package = root.TryGetProperty("packageName", out var p) ? p.GetString() : null;
        if (root.TryGetProperty("subscriptionNotification", out var sub))
            return new RtdnMessage(package, sub.TryGetProperty("purchaseToken", out var t) ? t.GetString() : null, false);
        // productType 1 = subscription; one-time products are not sold by TRAZO.
        if (root.TryGetProperty("voidedPurchaseNotification", out var voided) &&
            voided.TryGetProperty("productType", out var type) && type.TryGetInt32(out var kind) && kind == 1)
            return new RtdnMessage(package, voided.TryGetProperty("purchaseToken", out var vt) ? vt.GetString() : null, true);
        return new RtdnMessage(package, null, false); // testNotification and unknown kinds
    }

    internal static IResult BillingProblem(PlayBillingException ex, ILogger logger)
    {
        switch (ex.Failure)
        {
            case PlayBillingFailure.InvalidPurchase:
                return Results.Json(new { error = "invalid_purchase", message = "Google Play does not recognise this purchase." },
                    statusCode: StatusCodes.Status400BadRequest);
            case PlayBillingFailure.NotConfigured:
                logger.LogError(ex, "Google Play Billing is not configured.");
                return Results.Json(new { error = "billing_not_configured", message = "Subscriptions are not available yet." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            case PlayBillingFailure.Unauthorized:
                logger.LogError(ex, "Google Play rejected the configured service account.");
                return Results.Json(new { error = "billing_unavailable", message = "The purchase could not be verified. Try again later." },
                    statusCode: StatusCodes.Status502BadGateway);
            default:
                logger.LogWarning(ex, "Google Play is unavailable.");
                return Results.Json(new { error = "billing_unavailable", message = "The purchase could not be verified. Try again later." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    internal static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
