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
        endpoints.MapGroup("/api/v1/analytics").WithTags("Analytics").RequireAuthorization().RequireRateLimiting(RateLimitPolicy)
            .MapPost("/paywall-view", RecordPaywallViewAsync);
        return endpoints;
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
