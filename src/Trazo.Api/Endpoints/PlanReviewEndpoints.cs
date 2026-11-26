using Trazo.Api.Support;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The user's side of the manual review of their plan: ask for it, and talk with the team while it is open. The engine only
/// produces drafts, so this is the human check on top of it. The user never sees which administrator answers ("the team").
/// </summary>
public static class PlanReviewEndpoints
{
    public const int MaxUserMessageLength = 1000;
    public const int MaxStaffMessageLength = 2000;
    /// <summary>A user may send this many messages in a row without an answer before having to wait for the team.</summary>
    public const int MaxUnansweredUserMessages = 5;
    public const int MaxMessagesPerRequest = 300;

    public static IEndpointRouteBuilder MapPlanReviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var user = endpoints.MapGroup("/api/v1/plans/current/review-request").WithTags("Plans")
            .RequireActiveSubscription().RequireRateLimiting(PlansEndpoints.RateLimitPolicy);
        user.MapGet("", GetMineAsync).WithName("GetPlanReviewRequest")
            .WithSummary("The latest manual-review request of the active plan. With messages=true it includes the conversation and marks it as read.")
            .Produces<ReviewRequestView>().ProducesProblem(StatusCodes.Status404NotFound);
        user.MapPost("", CreateAsync).WithName("RequestPlanReview")
            .WithSummary("Asks the team to review the active plan by hand, with an optional first message. One open request per plan.")
            .Produces<ReviewRequestView>(StatusCodes.Status201Created).ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        user.MapPost("/messages", SendMessageAsync).WithName("SendPlanReviewMessage")
            .WithSummary("Writes to the team in the open request.")
            .Produces<ReviewRequestView>(StatusCodes.Status201Created).ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        return endpoints.MapAdminPlanReviewEndpoints();
    }

    public sealed record CreateReviewRequest(string? Note);
    public sealed record MessageRequest(string? Body, Guid? ClientRequestId = null);

    /// <summary>Author is "user" or "team"; audit lines are never sent to the user.</summary>
    public sealed record UserMessageView(Guid Id, string Author, string Body, DateTimeOffset CreatedAt);

    /// <summary>What the app sees. Status is pending, in_progress or reviewed.</summary>
    public sealed record ReviewRequestView(
        Guid Id, string Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ReviewedAt, int Unread,
        IReadOnlyList<UserMessageView>? Messages);

    internal static string StatusName(PlanReviewStatus status) => status switch
    {
        PlanReviewStatus.Pending => "pending",
        PlanReviewStatus.InProgress => "in_progress",
        _ => "reviewed"
    };

    internal static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    internal static IResult Problem(int status, string detail, string code) =>
        Results.Problem(statusCode: status, title: code, detail: detail);

    private static async Task<ReviewRequestView> BuildViewAsync(
        TrazoDbContext db, PlanReviewRequest r, bool withMessages, DateTimeOffset now, CancellationToken ct, bool markRead = false)
    {
        var visible = db.PlanReviewMessages.AsNoTracking().Where(m => m.RequestId == r.Id && m.Author != PlanReviewAuthors.System);
        var read = r.UserLastReadAt;
        var unread = await visible.CountAsync(m => m.Author == PlanReviewAuthors.Staff && (read == null || m.CreatedAt > read), ct);
        List<UserMessageView>? messages = null;
        if (withMessages)
        {
            messages = (await visible.OrderBy(m => m.CreatedAt).ToListAsync(ct))
                .Select(m => new UserMessageView(m.Id, m.Author == PlanReviewAuthors.User ? "user" : "team", m.Body, m.CreatedAt)).ToList();
            if (markRead) await db.PlanReviewRequests.Where(x => x.Id == r.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.UserLastReadAt, now), ct);
        }
        return new ReviewRequestView(r.Id, StatusName(r.Status), r.CreatedAt, r.UpdatedAt, r.ReviewedAt, withMessages ? 0 : unread, messages);
    }

    private static async Task<IResult> GetMineAsync(
        ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock, CancellationToken ct, bool messages = false)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var planId = await ActivePlanIdAsync(db, userId, ct);
        if (planId is null) return Problem(StatusCodes.Status404NotFound, "No tienes un plan activo.", "no_active_plan");
        var latest = await db.PlanReviewRequests.AsNoTracking().Where(r => r.PlanId == planId)
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        return latest is null
            ? Problem(StatusCodes.Status404NotFound, "No has pedido ninguna revisión.", "no_review_request")
            : Results.Ok(await BuildViewAsync(db, latest, messages, clock.GetUtcNow(), ct, markRead: messages));
    }

    private static async Task<IResult> CreateAsync(
        CreateReviewRequest? request, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock,
        IBackgroundWork background, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var note = string.IsNullOrWhiteSpace(request?.Note) ? null : request!.Note.Trim();
        if (note?.Length > MaxUserMessageLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = [$"El mensaje no puede superar {MaxUserMessageLength} caracteres."] });
        var planId = await ActivePlanIdAsync(db, userId, ct);
        if (planId is null) return Problem(StatusCodes.Status404NotFound, "No tienes un plan activo.", "no_active_plan");
        if (await db.PlanReviewRequests.AnyAsync(r => r.PlanId == planId && r.Status != PlanReviewStatus.Reviewed, ct))
            return Problem(StatusCodes.Status409Conflict, "Ya tienes una revisión abierta de este plan.", "review_already_open");

        var now = clock.GetUtcNow();
        var entity = new PlanReviewRequest { Id = Guid.NewGuid(), UserId = userId, PlanId = planId.Value, CreatedAt = now, UpdatedAt = now };
        db.PlanReviewRequests.Add(entity);
        if (note is not null)
            db.PlanReviewMessages.Add(new PlanReviewMessage { Id = Guid.NewGuid(), RequestId = entity.Id, Author = PlanReviewAuthors.User, Body = note, CreatedAt = now });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex)) // two simultaneous requests: the unique index on open requests rejects the second
        {
            return Problem(StatusCodes.Status409Conflict, "Ya tienes una revisión abierta de este plan.", "review_already_open");
        }

        PlanReviewNotifier.NotifyStaff(background, entity.Id, userId, isNew: true);
        return Results.Created("/api/v1/plans/current/review-request", await BuildViewAsync(db, entity, true, now, ct));
    }

    private static async Task<IResult> SendMessageAsync(
        MessageRequest? request, ClaimsPrincipal principal, TrazoDbContext db, TimeProvider clock,
        IBackgroundWork background, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var body = request?.Body?.Trim();
        if (string.IsNullOrEmpty(body) || body.Length > MaxUserMessageLength)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = [$"Escribe un mensaje de hasta {MaxUserMessageLength} caracteres."] });
        var planId = await ActivePlanIdAsync(db, userId, ct);
        if (planId is null) return Problem(StatusCodes.Status404NotFound, "No tienes un plan activo.", "no_active_plan");
        var entity = await db.PlanReviewRequests.Where(r => r.PlanId == planId).OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        if (entity is null) return Problem(StatusCodes.Status404NotFound, "No has pedido ninguna revisión.", "no_review_request");
        // A message that is already stored (the answer got lost, or it was sent twice) is answered as sent, even if the
        // conversation has since been closed or has reached a limit.
        var key = request!.ClientRequestId;
        Task<bool> StoredAsync() => db.PlanReviewMessages.AsNoTracking().AnyAsync(m => m.RequestId == entity.Id && m.ClientRequestId == key, ct);
        async Task<IResult> SentAsync() =>
            Results.Created("/api/v1/plans/current/review-request", await BuildViewAsync(db, entity, true, clock.GetUtcNow(), ct));
        if (key is not null && await StoredAsync()) return await SentAsync();
        if (entity.Status == PlanReviewStatus.Reviewed)
            return Problem(StatusCodes.Status409Conflict, "Esta revisión está cerrada. Pide una nueva para seguir hablando con el equipo.", "review_closed");

        var messages = db.PlanReviewMessages.Where(m => m.RequestId == entity.Id && m.Author != PlanReviewAuthors.System);
        if (await messages.CountAsync(ct) >= MaxMessagesPerRequest)
            return Problem(StatusCodes.Status409Conflict, "Esta conversación ha alcanzado su límite de mensajes.", "review_message_limit");
        var lastTeam = await messages.Where(m => m.Author == PlanReviewAuthors.Staff).MaxAsync(m => (DateTimeOffset?)m.CreatedAt, ct);
        var unanswered = await messages.CountAsync(m => m.Author == PlanReviewAuthors.User && (lastTeam == null || m.CreatedAt > lastTeam), ct);
        if (unanswered >= MaxUnansweredUserMessages)
            return Problem(StatusCodes.Status409Conflict, "Espera a que el equipo te responda antes de enviar más mensajes.", "wait_for_reply");

        var now = clock.GetUtcNow();
        var message = new PlanReviewMessage { Id = Guid.NewGuid(), RequestId = entity.Id, Author = PlanReviewAuthors.User, Body = body, CreatedAt = now, ClientRequestId = key };
        db.PlanReviewMessages.Add(message);
        entity.UpdatedAt = now;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (key is not null)
        {
            // The same message arrived twice at once and the other request stored it first.
            db.ChangeTracker.Clear();
            if (!await StoredAsync()) throw;
            entity = await db.PlanReviewRequests.SingleAsync(r => r.Id == entity.Id, ct);
            return await SentAsync();
        }

        PlanReviewNotifier.NotifyStaff(background, entity.Id, userId, isNew: false);
        return await SentAsync();
    }

    private static Task<Guid?> ActivePlanIdAsync(TrazoDbContext db, Guid userId, CancellationToken ct) =>
        db.TrainingPlans.AsNoTracking().Where(p => p.UserId == userId && p.Status == TrainingPlanStatus.Active)
            .Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
}
