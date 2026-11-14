using Microsoft.EntityFrameworkCore;
using Trazo.Api.Contracts;
using Trazo.Domain.Billing;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

/// <summary>Subscriptions and the Google Play Billing configuration for the administration console (superadmin only).</summary>
public static class AdminSubscriptionsEndpoints
{
    private static readonly string[] StatusNames =
        Enum.GetValues<SubscriptionStatus>().Select(SubscriptionService.ToApiName).ToArray();

    public static IEndpointRouteBuilder MapAdminSubscriptionsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/subscriptions").WithTags("Admin subscriptions")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("/{id:guid}/refresh", RefreshAsync);

        var settings = endpoints.MapGroup("/api/v1/admin/settings/play-billing").WithTags("Settings")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);
        settings.MapGet("", async (IRuntimeSettings s, CancellationToken ct) => Results.Ok(await s.GetPlayBillingViewAsync(ct)));
        settings.MapPut("", SaveSettingsAsync);
        var access = endpoints.MapGroup("/api/v1/admin/settings/access").WithTags("Settings")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);
        access.MapGet("", async (IRuntimeSettings s, CancellationToken ct) =>
            Results.Ok(new { subscriptionsEnforced = await s.GetSubscriptionsEnforcedAsync(ct) }));
        access.MapPut("", async (AccessSettingsRequest r, ISettingsWriter w, CancellationToken ct) =>
        {
            await w.SetSubscriptionsEnforcedAsync(r.SubscriptionsEnforced, ct);
            return Results.Ok(new { subscriptionsEnforced = r.SubscriptionsEnforced });
        });
        settings.MapPost("/test", TestConnectionAsync).RequireRateLimiting(SetupEndpoints.SetupTestPolicy);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        TrazoDbContext db, TimeProvider clock, string? q, string? status, string? source, string? user,
        DateTimeOffset? expiresFrom, DateTimeOffset? expiresTo, string? sort, int page = 1, int pageSize = 25,
        CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || pageSize is < 1 or > 100) errors["paging"] = ["page must be positive and pageSize between 1 and 100."];
        if (q?.Length > 100) errors["q"] = ["The search text is too long."];
        SubscriptionStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            var match = Enum.GetValues<SubscriptionStatus>().Cast<SubscriptionStatus?>()
                .FirstOrDefault(s => SubscriptionService.ToApiName(s!.Value) == status);
            if (match is null) errors["status"] = [$"Use one of: {string.Join(", ", StatusNames)}."];
            statusFilter = match;
        }
        if (!string.IsNullOrWhiteSpace(source) && source != SubscriptionSources.GooglePlay) errors["source"] = ["Unknown source."];
        Guid? userId = null;
        if (!string.IsNullOrWhiteSpace(user))
        {
            if (Guid.TryParse(user, out var parsed)) userId = parsed; else errors["user"] = ["Expected a user id."];
        }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var query = from s in db.Subscriptions.AsNoTracking()
                    join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                    select new { s, u.Email, u.DisplayName, u.NormalizedEmail };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var upper = q.Trim().ToUpperInvariant();
            var lower = q.Trim().ToLowerInvariant();
            query = query.Where(x => x.NormalizedEmail.Contains(upper) || x.DisplayName.ToLower().Contains(lower));
        }
        if (statusFilter is { } st) query = query.Where(x => x.s.Status == st);
        if (!string.IsNullOrWhiteSpace(source)) query = query.Where(x => x.s.Source == source);
        if (userId is { } uid) query = query.Where(x => x.s.UserId == uid);
        if (expiresFrom is { } from) query = query.Where(x => x.s.ExpiresAt >= from);
        if (expiresTo is { } to) query = query.Where(x => x.s.ExpiresAt <= to);

        var totalCount = await query.CountAsync(ct);
        query = sort switch
        {
            "expires" => query.OrderBy(x => x.s.ExpiresAt).ThenBy(x => x.s.Id),
            "started" => query.OrderByDescending(x => x.s.StartedAt).ThenBy(x => x.s.Id),
            _ => query.OrderByDescending(x => x.s.UpdatedAt).ThenBy(x => x.s.Id)
        };
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var now = clock.GetUtcNow();
        var items = rows.Select(r => Summary(r.s, r.Email, r.DisplayName, now)).ToList();
        return Results.Ok(new AdminSubscriptionPage(items, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    private static async Task<IResult> GetAsync(Guid id, TrazoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var detail = await BuildDetailAsync(db, id, clock.GetUtcNow(), ct);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> RefreshAsync(
        Guid id, ISubscriptionService subscriptions, TrazoDbContext db, TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        try
        {
            if (await subscriptions.RefreshAsync(id, ct) is null) return Results.NotFound();
        }
        catch (PlayBillingException ex)
        {
            return SubscriptionEndpoints.BillingProblem(ex, loggerFactory.CreateLogger("Trazo.Billing"));
        }
        db.ChangeTracker.Clear();
        return Results.Ok(await BuildDetailAsync(db, id, clock.GetUtcNow(), ct));
    }

    private static async Task<IResult> SaveSettingsAsync(PlayBillingSettingsRequest request, ISettingsWriter writer, CancellationToken ct)
    {
        var errors = await writer.UpdatePlayBillingAsync(new PlayBillingInput(
            request.PackageName?.Trim() ?? "", request.ServiceAccountJson, request.RtdnSecret,
            request.RtdnAudience, request.RtdnServiceAccountEmail), ct);
        return errors.Count > 0 ? Results.ValidationProblem(errors) : Results.Ok(new { saved = true });
    }

    private static async Task<IResult> TestConnectionAsync(IPlayBillingClient play, CancellationToken ct)
    {
        try
        {
            await play.CheckConnectionAsync(ct);
            return Results.Ok(new { ok = true, message = "Google Play accepted the service account and package name." });
        }
        catch (PlayBillingException ex)
        {
            // Messages are written by us and never include credentials.
            return Results.Ok(new { ok = false, message = ex.Message });
        }
    }

    private static AdminSubscriptionSummary Summary(Subscription s, string email, string displayName, DateTimeOffset now) =>
        new(s.Id, s.UserId, email, displayName, s.ProductId, SubscriptionService.ToApiName(s.Status),
            SubscriptionAccess.Grants(s.Status, s.ExpiresAt, now), s.StartedAt, s.ExpiresAt, s.AutoRenewing, s.Source, s.UpdatedAt);

    private static async Task<AdminSubscriptionDetail?> BuildDetailAsync(TrazoDbContext db, Guid id, DateTimeOffset now, CancellationToken ct)
    {
        var row = await (from s in db.Subscriptions.AsNoTracking()
                         join u in db.Users.AsNoTracking() on s.UserId equals u.Id
                         where s.Id == id
                         select new { s, u.Email, u.DisplayName }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        return new AdminSubscriptionDetail(
            Summary(row.s, row.Email, row.DisplayName, now), row.s.StoreState, row.s.LatestOrderId, row.s.AcknowledgedAt,
            row.s.IsTestPurchase, row.s.CreatedAt, row.s.PurchaseTokenHash[..12]);
    }
}

public sealed record AdminSubscriptionSummary(
    Guid Id, Guid UserId, string Email, string DisplayName, string ProductId, string Status, bool HasAccess,
    DateTimeOffset StartedAt, DateTimeOffset ExpiresAt, bool AutoRenewing, string Source, DateTimeOffset UpdatedAt);

public sealed record AdminSubscriptionPage(
    IReadOnlyList<AdminSubscriptionSummary> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record AdminSubscriptionDetail(
    AdminSubscriptionSummary Subscription, string? StoreState, string? LatestOrderId, DateTimeOffset? AcknowledgedAt,
    bool IsTestPurchase, DateTimeOffset CreatedAt, string PurchaseTokenFingerprint);
