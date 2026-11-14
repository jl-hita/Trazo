using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;
using static Trazo.Api.Endpoints.PlanReviewEndpoints;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The administrators' side of the plan reviews (administrators and superadmins). Anyone may read a request and take a pending
/// one; only the administrator who has it can write, give it back to the queue (so somebody else can continue) or close it.
/// A superadmin may also give back a request that someone else holds, for instance when that person is away.
/// </summary>
public static class AdminPlanReviewEndpoints
{
    public sealed record ReleaseRequest(string? Reason);
    public sealed record CloseRequest(string? Message);

    public sealed record AdminReviewItem(
        Guid Id, string Status, Guid UserId, string UserName, string UserEmail, string Goal, string Experience, string Split, int TotalWeeks,
        Guid? AssignedToUserId, string? AssignedToName, bool AssignedToMe, string? Preview, string? LastAuthor, int MessageCount,
        bool AwaitingReply, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ReviewedAt);

    public sealed record AdminMessageView(Guid Id, string Author, string? AuthorName, string Body, DateTimeOffset CreatedAt);

    public sealed record AdminReviewDetail(
        AdminReviewItem Review, DateOnly StartDate, bool CanTake, bool CanReply, bool CanRelease, bool CanClose,
        IReadOnlyList<AdminMessageView> Messages);

    public static IEndpointRouteBuilder MapAdminPlanReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/plan-reviews").WithTags("Admin plan reviews")
            .RequireAuthorization(SetupEndpoints.StaffPolicy);
        admin.MapGet("", ListAsync);
        admin.MapGet("/pending-count", PendingCountAsync);
        admin.MapGet("/{id:guid}", DetailAsync);
        admin.MapPost("/{id:guid}/claim", ClaimAsync);
        admin.MapPost("/{id:guid}/release", ReleaseAsync);
        admin.MapPost("/{id:guid}/messages", PostMessageAsync);
        admin.MapPost("/{id:guid}/review", CloseAsync);
        return endpoints;
    }

    private static bool IsSuperadmin(ClaimsPrincipal principal) => principal.IsInRole("superadmin");

    private static async Task<string> NameOfAsync(TrazoDbContext db, Guid id, CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id).Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "Un administrador";

    private static void AddMessage(TrazoDbContext db, Guid requestId, string author, Guid? authorId, string body, DateTimeOffset now) =>
        db.PlanReviewMessages.Add(new PlanReviewMessage { Id = Guid.NewGuid(), RequestId = requestId, Author = author, AuthorUserId = authorId, Body = body, CreatedAt = now });

    // ------------------------------------------------------------------ queue

    private static async Task<IReadOnlyList<AdminReviewItem>> BuildItemsAsync(
        TrazoDbContext db, IReadOnlyList<Guid> ids, Guid me, CancellationToken ct)
    {
        var rows = await (
            from r in db.PlanReviewRequests.AsNoTracking()
            where ids.Contains(r.Id)
            join u in db.Users on r.UserId equals u.Id
            join p in db.TrainingPlans on r.PlanId equals p.Id
            join a in db.Users on r.AssignedToUserId equals a.Id into assignees
            from a in assignees.DefaultIfEmpty()
            select new { r, u.DisplayName, u.Email, p.Goal, p.Experience, p.Split, p.TotalWeeks, AssigneeName = a != null ? a.DisplayName : null })
            .ToListAsync(ct);
        var messages = (await db.PlanReviewMessages.AsNoTracking()
            .Where(m => ids.Contains(m.RequestId) && m.Author != PlanReviewAuthors.System)
            .Select(m => new { m.RequestId, m.Author, m.CreatedAt, Body = m.Body.Length > 200 ? m.Body.Substring(0, 200) : m.Body })
            .ToListAsync(ct)).GroupBy(m => m.RequestId).ToDictionary(g => g.Key, g => g.OrderBy(m => m.CreatedAt).ToList());
        return rows.Select(x =>
        {
            var thread = messages.GetValueOrDefault(x.r.Id) ?? [];
            var last = thread.LastOrDefault();
            return new AdminReviewItem(x.r.Id, StatusName(x.r.Status), x.r.UserId, x.DisplayName, x.Email, x.Goal, x.Experience, x.Split, x.TotalWeeks,
                x.r.AssignedToUserId, x.AssigneeName, x.r.AssignedToUserId == me, thread.FirstOrDefault()?.Body, last?.Author, thread.Count,
                x.r.Status != PlanReviewStatus.Reviewed && last?.Author == PlanReviewAuthors.User, x.r.CreatedAt, x.r.UpdatedAt, x.r.ReviewedAt);
        }).ToList();
    }

    /// <param name="status">pending, in_progress, reviewed, open (pending and in progress) or mine (in progress and held by me).</param>
    private static async Task<IResult> ListAsync(
        ClaimsPrincipal principal, TrazoDbContext db, string? status, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (UserId(principal) is not { } me) return Results.Unauthorized();
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || pageSize is < 1 or > 100) errors["paging"] = ["page must be positive and pageSize between 1 and 100."];
        if (!string.IsNullOrEmpty(status) && status is not ("pending" or "in_progress" or "reviewed" or "open" or "mine"))
            errors["status"] = ["Use pending, in_progress, reviewed, open or mine."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var query = db.PlanReviewRequests.AsNoTracking().Where(r =>
            status == null || status == "" ||
            (status == "pending" && r.Status == PlanReviewStatus.Pending) ||
            (status == "in_progress" && r.Status == PlanReviewStatus.InProgress) ||
            (status == "reviewed" && r.Status == PlanReviewStatus.Reviewed) ||
            (status == "open" && r.Status != PlanReviewStatus.Reviewed) ||
            (status == "mine" && r.Status == PlanReviewStatus.InProgress && r.AssignedToUserId == me));
        var total = await query.CountAsync(ct);
        // Open requests first, the one waiting longest on top so nothing is forgotten; then the closed ones, newest first.
        var ids = await query
            .OrderBy(r => r.Status == PlanReviewStatus.Reviewed ? 1 : 0)
            .ThenBy(r => r.Status == PlanReviewStatus.Reviewed ? DateTimeOffset.MaxValue : r.UpdatedAt)
            .ThenByDescending(r => r.UpdatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(r => r.Id).ToListAsync(ct);
        var items = (await BuildItemsAsync(db, ids, me, ct)).OrderBy(i => ids.IndexOf(i.Id)).ToList();
        return Results.Ok(new { items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
    }

    private static async Task<IResult> PendingCountAsync(ClaimsPrincipal principal, TrazoDbContext db, CancellationToken ct)
    {
        if (UserId(principal) is not { } me) return Results.Unauthorized();
        var pending = await db.PlanReviewRequests.CountAsync(r => r.Status == PlanReviewStatus.Pending, ct);
        var mine = await db.PlanReviewRequests.Where(r => r.Status == PlanReviewStatus.InProgress && r.AssignedToUserId == me).Select(r => r.Id).ToListAsync(ct);
        var mineAwaiting = 0;
        foreach (var id in mine)
        {
            var last = await db.PlanReviewMessages.Where(m => m.RequestId == id && m.Author != PlanReviewAuthors.System)
                .OrderByDescending(m => m.CreatedAt).Select(m => m.Author).FirstOrDefaultAsync(ct);
            if (last == PlanReviewAuthors.User) mineAwaiting++;
        }
        return Results.Ok(new { pending, mine = mine.Count, mineAwaitingReply = mineAwaiting });
    }

    private static async Task<IResult> DetailAsync(Guid id, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (UserId(principal) is not { } me) return Results.Unauthorized();
        var entity = await db.PlanReviewRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return NotFound();
        var item = (await BuildItemsAsync(db, [id], me, ct)).Single();
        var startDate = await db.TrainingPlans.AsNoTracking().Where(p => p.Id == entity.PlanId).Select(p => p.StartDate).FirstOrDefaultAsync(ct);
        var rows = await (
            from m in db.PlanReviewMessages.AsNoTracking()
            where m.RequestId == id
            join u in db.Users on m.AuthorUserId equals u.Id into authors
            from u in authors.DefaultIfEmpty()
            orderby m.CreatedAt
            select new AdminMessageView(m.Id, m.Author, u != null ? u.DisplayName : null, m.Body, m.CreatedAt)).ToListAsync(ct);
        // The conversation can hold health information: reading it leaves a trace in the log (who, which request; never the text).
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation("Administrator {Actor} opened plan review {ReviewId}.", OperationsSettingsEndpoints.Actor(principal), id);
        var mineNow = entity.Status == PlanReviewStatus.InProgress && entity.AssignedToUserId == me;
        return Results.Ok(new AdminReviewDetail(item, startDate,
            CanTake: entity.Status == PlanReviewStatus.Pending, CanReply: mineNow,
            CanRelease: entity.Status == PlanReviewStatus.InProgress && (mineNow || IsSuperadmin(principal)), CanClose: mineNow, rows));
    }

    // ------------------------------------------------------------------ actions

    private static IResult NotFound() => Problem(StatusCodes.Status404NotFound, "Petición no encontrada.", "review_not_found");
    private static IResult NotAssignee() => Problem(StatusCodes.Status409Conflict, "Solo puede hacerlo el administrador que tiene la petición.", "not_assignee");

    private static async Task<IResult> ClaimAsync(
        Guid id, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (UserId(principal) is not { } me) return Results.Unauthorized();
        var now = clock.GetUtcNow();
        // One statement decides who gets it, so two administrators clicking at once cannot both take it.
        var taken = await db.PlanReviewRequests.Where(r => r.Id == id && r.Status == PlanReviewStatus.Pending).ExecuteUpdateAsync(s => s
            .SetProperty(r => r.Status, PlanReviewStatus.InProgress).SetProperty(r => r.AssignedToUserId, me)
            .SetProperty(r => r.AssignedAt, now).SetProperty(r => r.UpdatedAt, now), ct);
        if (taken == 0)
            return await db.PlanReviewRequests.AnyAsync(r => r.Id == id, ct)
                ? Problem(StatusCodes.Status409Conflict, "Esta petición ya la tiene otro administrador o está cerrada.", "review_not_available")
                : NotFound();
        AddMessage(db, id, PlanReviewAuthors.System, me, $"{await NameOfAsync(db, me, ct)} tomó la petición.", now);
        await db.SaveChangesAsync(ct);
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation("Administrator {Actor} took plan review {ReviewId}.", OperationsSettingsEndpoints.Actor(principal), id);
        return Results.NoContent();
    }

    private static async Task<IResult> ReleaseAsync(
        Guid id, ReleaseRequest? request, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (UserId(principal) is not { } me) return Results.Unauthorized();
        var reason = string.IsNullOrWhiteSpace(request?.Reason) ? null : request!.Reason.Trim();
        if (reason?.Length > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["El motivo no puede superar 500 caracteres."] });
        var entity = await db.PlanReviewRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.Status != PlanReviewStatus.InProgress)
            return Problem(StatusCodes.Status409Conflict, "Solo se puede devolver a la cola una petición en curso.", "review_not_in_progress");
        if (entity.AssignedToUserId != me && !IsSuperadmin(principal)) return NotAssignee();

        var previous = entity.AssignedToUserId;
        var now = clock.GetUtcNow();
        entity.Status = PlanReviewStatus.Pending;
        entity.AssignedToUserId = null;
        entity.AssignedAt = null;
        entity.UpdatedAt = now;
        var text = $"{await NameOfAsync(db, me, ct)} devolvió la petición a la cola";
        if (previous is { } p && p != me) text += $" (la tenía {await NameOfAsync(db, p, ct)})";
        AddMessage(db, id, PlanReviewAuthors.System, me, reason is null ? text + "." : $"{text}. Motivo: {reason}", now);
        await db.SaveChangesAsync(ct);
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation("Administrator {Actor} gave plan review {ReviewId} back to the queue.", OperationsSettingsEndpoints.Actor(principal), id);
        return Results.NoContent();
    }

    private static async Task<IResult> PostMessageAsync(
        Guid id, MessageRequest? request, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock,
        IServiceScopeFactory scopes, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (UserId(principal) is not { } me) return Results.Unauthorized();
        var body = request?.Body?.Trim();
        if (string.IsNullOrEmpty(body) || body.Length > MaxStaffMessageLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = [$"Escribe un mensaje de hasta {MaxStaffMessageLength} caracteres."] });
        var entity = await db.PlanReviewRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.Status != PlanReviewStatus.InProgress || entity.AssignedToUserId != me) return NotAssignee();
        if (await db.PlanReviewMessages.CountAsync(m => m.RequestId == id, ct) >= MaxMessagesPerRequest * 2)
            return Problem(StatusCodes.Status409Conflict, "Esta conversación ha alcanzado su límite de mensajes.", "review_message_limit");

        var now = clock.GetUtcNow();
        AddMessage(db, id, PlanReviewAuthors.Staff, me, body, now);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await PlanReviewNotifier.NotifyUserAsync(scopes, loggerFactory, entity.UserId, closed: false);
        return Results.NoContent();
    }

    private static async Task<IResult> CloseAsync(
        Guid id, CloseRequest? request, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock,
        IServiceScopeFactory scopes, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (UserId(principal) is not { } me) return Results.Unauthorized();
        var message = string.IsNullOrWhiteSpace(request?.Message) ? null : request!.Message.Trim();
        if (message?.Length > MaxStaffMessageLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["message"] = [$"El mensaje no puede superar {MaxStaffMessageLength} caracteres."] });
        var entity = await db.PlanReviewRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (entity is null) return NotFound();
        if (entity.Status == PlanReviewStatus.Reviewed)
            return Problem(StatusCodes.Status409Conflict, "Esta petición ya está cerrada.", "review_already_done");
        if (entity.Status != PlanReviewStatus.InProgress || entity.AssignedToUserId != me) return NotAssignee();

        var now = clock.GetUtcNow();
        if (message is not null) AddMessage(db, id, PlanReviewAuthors.Staff, me, message, now);
        AddMessage(db, id, PlanReviewAuthors.System, me, $"{await NameOfAsync(db, me, ct)} cerró la petición.", now.AddTicks(1));
        entity.Status = PlanReviewStatus.Reviewed;
        entity.ReviewedAt = now;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation("Administrator {Actor} closed plan review {ReviewId}.", OperationsSettingsEndpoints.Actor(principal), id);
        await PlanReviewNotifier.NotifyUserAsync(scopes, loggerFactory, entity.UserId, closed: true);
        return Results.NoContent();
    }
}
