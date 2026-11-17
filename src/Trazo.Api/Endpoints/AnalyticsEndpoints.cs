using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Analytics;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Anonymous product counters. The app says "the Premium offer was opened from here"; the API only adds one to the
/// day's counter for that place. Nothing about who, which device or which session is stored.
/// </summary>
public static class AnalyticsEndpoints
{
    public const string RateLimitPolicy = "analytics";

    public sealed record PaywallViewRequest(string? Source);

    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/analytics").WithTags("Analytics").RequireAuthorization().RequireRateLimiting(RateLimitPolicy);
        group.MapPost("/paywall-view", RecordPaywallViewAsync);
        group.MapPost("/events", RecordEventAsync);
        return endpoints;
    }

    /// <summary>
    /// Usage milestones the app may report for the signed-in user (a closed list: see <see cref="Trazo.Domain.Growth.GrowthEventNames.ClientReportable"/>).
    /// Money events (trial, payment, renewal, refund) are never accepted here: the backend writes them from Google Play.
    /// Each milestone is recorded once per user.
    /// </summary>
    private static async Task<IResult> RecordEventAsync(
        ClientEventRequest? request, HttpContext http, System.Security.Claims.ClaimsPrincipal principal,
        Trazo.Infrastructure.Growth.IGrowthEvents events, CancellationToken ct)
    {
        var name = request?.Name?.Trim();
        if (name is null || name != Trazo.Domain.Growth.GrowthEventNames.OnboardingCompleted)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Unknown event."] });
        if (SubscriptionEndpoints.UserId(principal) is not { } userId) return Results.Unauthorized();
        await events.RecordAsync(name, userId, new Trazo.Infrastructure.Growth.GrowthEventInfo(
            AppVersion: ReferralEndpoints.AppVersion(http), DedupeKey: $"{name}:{userId:N}"), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RecordPaywallViewAsync(
        PaywallViewRequest? request, TrazoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var source = PaywallSources.Normalize(request?.Source);
        await IncrementAsync(db, day, source, ct);
        return Results.NoContent();
    }

    private static async Task IncrementAsync(TrazoDbContext db, DateOnly day, string source, CancellationToken ct)
    {
        Task<int> Bump() => db.PaywallViews.Where(x => x.Day == day && x.Source == source)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Views, x => x.Views + 1), ct);
        if (await Bump() > 0) return;
        try
        {
            db.PaywallViews.Add(new PaywallView { Day = day, Source = source, Views = 1 });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another request created today's row first: add to it.
            db.ChangeTracker.Clear();
            await Bump();
        }
    }
}
