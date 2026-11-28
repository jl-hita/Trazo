using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Support;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Comments from the app. Any signed-in account may send them (limited per account); only the superadmin reads them, because they
/// carry who wrote them and free text. See docs/feedback.md.
/// </summary>
public static class FeedbackEndpoints
{
    public const string RateLimitPolicy = "feedback";
    public const int PerHour = 10;
    private const int MaxPageSize = 100;

    public sealed record SendFeedbackRequest(string? Category, string? Message, string? Screen, string? Device, Guid? ClientRequestId);
    public sealed record FeedbackStatusRequest(string? Status);

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/feedback", SendAsync)
            .WithTags("Feedback").RequireAuthorization().RequireRateLimiting(RateLimitPolicy);

        var admin = endpoints.MapGroup("/api/v1/admin/feedback")
            .WithTags("Admin feedback")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        admin.MapGet("", ListAsync);
        admin.MapPut("/{id:guid}/status", SetStatusAsync);
        admin.MapDelete("/{id:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> SendAsync(
        SendFeedbackRequest? request, HttpContext http, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock,
        Trazo.Application.Localization.RequestLanguage language, CancellationToken ct)
    {
        if (SubscriptionEndpoints.UserId(principal) is not { } userId) return Results.Unauthorized();
        var category = request?.Category?.Trim().ToLowerInvariant();
        var message = request?.Message?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (category is null || !FeedbackCategory.All.Contains(category)) errors["category"] = ["Use bug, idea or other."];
        if (string.IsNullOrEmpty(message)) errors["message"] = ["A message is required."];
        else if (message.Length > FeedbackEntry.MaxMessageLength) errors["message"] = [$"The message is too long (maximum {FeedbackEntry.MaxMessageLength} characters)."];
        if (request?.ClientRequestId is not { } key || key == Guid.Empty) errors["clientRequestId"] = ["A request id is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var id = request!.ClientRequestId!.Value;
        Task<bool> StoredAsync() => db.FeedbackEntries.AsNoTracking().AnyAsync(f => f.UserId == userId && f.ClientRequestId == id, ct);
        if (await StoredAsync()) return Results.NoContent();
        db.FeedbackEntries.Add(new FeedbackEntry
        {
            Id = Guid.NewGuid(), UserId = userId, CreatedAt = clock.GetUtcNow(), Category = category!, Message = message!,
            Screen = Cut(request.Screen, FeedbackEntry.MaxScreenLength), Device = Cut(request.Device, FeedbackEntry.MaxDeviceLength),
            AppVersion = ReferralEndpoints.AppVersion(http), Language = language.Current.ToString().ToLowerInvariant(),
            Status = ErrorTicketStatus.New, ClientRequestId = id
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // The same comment arrived twice at once: one of the two stored it.
        }
        return Results.NoContent();
    }

    private static string? Cut(string? value, int length)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= length ? text : text[..length];
    }

    private static async Task<IResult> ListAsync(
        TrazoDbContext db, string? status, string? category, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["page must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        if (!string.IsNullOrEmpty(status) && !ErrorTicketStatus.All.Contains(status)) errors["status"] = ["Use new, seen or resolved."];
        if (!string.IsNullOrEmpty(category) && !FeedbackCategory.All.Contains(category)) errors["category"] = ["Use bug, idea or other."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var query = db.FeedbackEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(category)) query = query.Where(x => x.Category == category);
        var total = await query.CountAsync(ct);
        // SQLite (tests) cannot order by DateTimeOffset on the server, so the page is chosen from the ordered keys.
        var keys = await query.Select(x => new { x.Id, x.CreatedAt }).ToListAsync(ct);
        var ids = keys.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).Select(x => x.Id).ToList();
        var rows = await db.FeedbackEntries.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var userIds = rows.Select(x => x.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email, u.DisplayName }).ToDictionaryAsync(u => u.Id, ct);
        var items = rows.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new
        {
            x.Id, x.CreatedAt, x.Category, x.Message, x.Screen, x.AppVersion, x.Device, x.Language, x.Status,
            email = users.GetValueOrDefault(x.UserId)?.Email, displayName = users.GetValueOrDefault(x.UserId)?.DisplayName
        });

        var byStatus = await db.FeedbackEntries.AsNoTracking().GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(string key) => byStatus.FirstOrDefault(x => x.Key == key)?.Count ?? 0;
        return Results.Ok(new
        {
            items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize),
            overall = new { total = byStatus.Sum(x => x.Count), @new = Count(ErrorTicketStatus.New), seen = Count(ErrorTicketStatus.Seen), resolved = Count(ErrorTicketStatus.Resolved) }
        });
    }

    private static async Task<IResult> SetStatusAsync(Guid id, FeedbackStatusRequest? request, TrazoDbContext db, CancellationToken ct)
    {
        if (request?.Status is null || !ErrorTicketStatus.All.Contains(request.Status))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use new, seen or resolved."] });
        var changed = await db.FeedbackEntries.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, request.Status), ct);
        return changed == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, TrazoDbContext db, CancellationToken ct)
    {
        var removed = await db.FeedbackEntries.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return removed == 0 ? Results.NotFound() : Results.NoContent();
    }
}
