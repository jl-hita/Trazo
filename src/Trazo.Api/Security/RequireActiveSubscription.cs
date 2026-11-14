using System.Security.Claims;
using Trazo.Infrastructure.Billing;

namespace Trazo.Api.Security;

/// <summary>
/// Endpoint filter that lets a request through only when the signed-in user has paid access: a trial,
/// an active subscription, a grace period, or a cancelled one that has not reached its end date.
/// Administrators and superadmins skip the check. Answers 401 without a user and 402 without access.
/// Usage: <c>group.RequireActiveSubscription()</c> on any route group or endpoint.
/// </summary>
public sealed class SubscriptionAccessFilter : IEndpointFilter
{
    public const string ErrorCode = "subscription_required";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var principal = http.User;
        var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (principal.Identity?.IsAuthenticated != true || !Guid.TryParse(subject, out var userId))
            return Results.Unauthorized();
        if (principal.IsInRole("admin") || principal.IsInRole("superadmin"))
            return await next(context);

        var subscriptions = http.RequestServices.GetRequiredService<ISubscriptionService>();
        if (await subscriptions.HasAccessAsync(userId, http.RequestAborted))
            return await next(context);
        return Results.Json(new { error = ErrorCode, message = "An active subscription or free trial is required." },
            statusCode: StatusCodes.Status402PaymentRequired);
    }
}

public static class SubscriptionAccessExtensions
{
    /// <summary>Requires a signed-in user with an active subscription or trial (admins excepted).</summary>
    public static TBuilder RequireActiveSubscription<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter<TBuilder, SubscriptionAccessFilter>();
        return builder;
    }
}
