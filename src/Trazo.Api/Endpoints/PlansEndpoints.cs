using System.Security.Claims;
using Trazo.Api.Security;
using Trazo.Application.Planning;
using Trazo.Infrastructure.Planning;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The signed-in user's training plan. Every route needs an active subscription (or free trial) and only ever touches the
/// caller's own plan; ids of other users' sessions answer 404. The API serves the macrocycle as a summary of phases and the
/// detail of the CURRENT week only: there is deliberately no route that returns future sessions or the whole plan.
/// </summary>
public static class PlansEndpoints
{
    public const string RateLimitPolicy = "plans";

    public static IEndpointRouteBuilder MapPlansEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/plans").WithTags("Plans")
            .RequireActiveSubscription()
            .RequireRateLimiting(RateLimitPolicy)
            .AddEndpointFilter(async (context, next) =>
            {
                // The app sends its IANA zone with each request (X-Time-Zone); an invalid value is ignored.
                context.HttpContext.RequestServices.GetRequiredService<IUserTimeZone>().RequestOverride =
                    context.HttpContext.Request.Headers["X-Time-Zone"].ToString();
                return await next(context);
            });

        group.MapGet("/options", OptionsAsync).WithName("GetPlanOptions")
            .WithSummary("Allowed values for the plan profile: goals, levels, splits, limitations and equipment of the catalog.")
            .Produces<PlanOptions>();
        group.MapPost("", CreateAsync).WithName("CreatePlan")
            .WithSummary("Generates a personalized plan (macrocycle summary and the first block) from the profile.")
            .Produces<PlanView>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/current", GetCurrentAsync).WithName("GetCurrentPlan")
            .WithSummary("The active plan: profile and macrocycle phases, without any session detail.")
            .Produces<PlanView>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/current/week", GetWeekAsync).WithName("GetCurrentPlanWeek")
            .WithSummary("The sessions of the current week with sets, repetitions, RIR, rest and load targets.")
            .Produces<WeekView>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/current/history", GetHistoryAsync).WithName("GetPlanHistory")
            .WithSummary("The sessions the user has logged, newest first.")
            .Produces<IReadOnlyList<HistoryItem>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/current/archive", ArchiveAsync).WithName("ArchivePlan")
            .WithSummary("Closes the active plan so a new one can be created. The history is kept.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/current/sessions/{sessionId:guid}/log", LogAsync).WithName("LogPlanSession")
            .WithSummary("Records a session (sets, loads, repetitions, RIR, recovery, pain) and adapts the upcoming sessions.")
            .Produces<LogResult>().ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/current/sessions/{sessionId:guid}/exercises/{exerciseId}/alternatives", AlternativesAsync)
            .WithName("GetExerciseAlternatives")
            .WithSummary("Reviewed exercises that can replace one of the session, respecting level, goal, equipment and limitations.")
            .Produces<AlternativesResult>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/current/sessions/{sessionId:guid}/exercises/{exerciseId}/swap", SwapAsync).WithName("SwapPlanExercise")
            .WithSummary("Replaces an exercise of a pending session (optionally in the rest of the block) with one of its alternatives.")
            .Produces<SessionView>().ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
        return endpoints;
    }

    private static Guid? UserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static async Task<IResult> OptionsAsync(ITrainingPlanService plans, CancellationToken ct) =>
        Results.Ok(await plans.GetOptionsAsync(ct));

    private static async Task<IResult> CreateAsync(
        PlanProfileInput request, ClaimsPrincipal principal, ITrainingPlanService plans, IUserTimeZone zones, Trazo.Infrastructure.Accounts.IAccountService accounts, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var sentZone = !string.IsNullOrWhiteSpace(request.TimeZone);
        if (sentZone && !UserTimeZone.TryParse(request.TimeZone, out _))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZone"] = ["Usa un identificador IANA válido, por ejemplo Europe/Madrid."] });
        if (sentZone) zones.RequestOverride = request.TimeZone;
        var parsed = PlanProfileParser.Parse(request, await plans.TodayAsync(userId, ct));
        if (!parsed.Succeeded) return Results.ValidationProblem(parsed.Errors.ToDictionary(p => p.Key, p => p.Value));
        var result = await plans.CreateAsync(userId, parsed.Profile!, null, ct);
        if (result.Succeeded && sentZone) await accounts.SetTimeZoneAsync(userId, request.TimeZone!, ct);
        return result.Succeeded ? Results.Created("/api/v1/plans/current", result.Value) : Failure(result);
    }

    private static async Task<IResult> GetCurrentAsync(ClaimsPrincipal principal, ITrainingPlanService plans, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        return await plans.GetCurrentAsync(userId, ct) is { } plan ? Results.Ok(plan) : NoPlan();
    }

    private static async Task<IResult> GetWeekAsync(ClaimsPrincipal principal, ITrainingPlanService plans, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        return await plans.GetCurrentWeekAsync(userId, ct) is { } week ? Results.Ok(week) : NoPlan();
    }

    private static async Task<IResult> GetHistoryAsync(
        ClaimsPrincipal principal, ITrainingPlanService plans, CancellationToken ct, int take = 30)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        if (take is < 1 or > 100) return Results.ValidationProblem(new Dictionary<string, string[]> { ["take"] = ["Debe estar entre 1 y 100."] });
        return await plans.GetHistoryAsync(userId, take, ct) is { } items ? Results.Ok(items) : NoPlan();
    }

    private static async Task<IResult> ArchiveAsync(ClaimsPrincipal principal, ITrainingPlanService plans, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        return await plans.ArchiveAsync(userId, ct) ? Results.NoContent() : NoPlan();
    }

    private static async Task<IResult> LogAsync(
        Guid sessionId, LogSessionRequest request, ClaimsPrincipal principal, ITrainingPlanService plans, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var parsed = LogSessionValidator.Parse(request);
        if (!parsed.Succeeded) return Results.ValidationProblem(parsed.Errors.ToDictionary(p => p.Key, p => p.Value));
        var result = await plans.LogSessionAsync(userId, sessionId, parsed.Log!, ct);
        return result.Succeeded ? Results.Ok(result.Value) : Failure(result);
    }

    private static async Task<IResult> AlternativesAsync(
        Guid sessionId, string exerciseId, ClaimsPrincipal principal, ITrainingPlanService plans, CancellationToken ct, int limit = 5)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        if (limit is < 1 or > 10) return Results.ValidationProblem(new Dictionary<string, string[]> { ["limit"] = ["Debe estar entre 1 y 10."] });
        var result = await plans.GetAlternativesAsync(userId, sessionId, exerciseId.Trim().ToLowerInvariant(), limit, ct);
        return result.Succeeded ? Results.Ok(result.Value) : Failure(result);
    }

    private static async Task<IResult> SwapAsync(
        Guid sessionId, string exerciseId, SwapRequest request, ClaimsPrincipal principal, ITrainingPlanService plans, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var replacement = request.ReplacementExerciseId?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(replacement) || replacement.Length > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["replacementExerciseId"] = ["El ejercicio de sustitución es obligatorio."] });
        var result = await plans.SwapExerciseAsync(userId, sessionId, exerciseId.Trim().ToLowerInvariant(), replacement,
            request.ApplyToRemainingBlock ?? false, ct);
        return result.Succeeded ? Results.Ok(result.Value) : Failure(result);
    }

    private static IResult NoPlan() => Problem(StatusCodes.Status404NotFound, "No tienes un plan activo.", "no_active_plan");

    private static IResult Problem(int status, string detail, string code, IDictionary<string, string[]>? errors = null) =>
        Results.Problem(statusCode: status, title: code, detail: detail,
            extensions: errors is null ? null : new Dictionary<string, object?> { ["errors"] = errors });

    private static IResult Failure<T>(PlanResult<T> result)
    {
        var errors = result.Errors?.ToDictionary(p => p.Key, p => p.Value);
        return result.Failure switch
        {
            PlanFailure.NoActivePlan => Problem(StatusCodes.Status404NotFound, result.Message!, "no_active_plan"),
            PlanFailure.SessionNotFound => Problem(StatusCodes.Status404NotFound, result.Message!, "session_not_found"),
            PlanFailure.ActivePlanExists => Problem(StatusCodes.Status409Conflict, result.Message!, "active_plan_exists"),
            PlanFailure.AlreadyLogged => Problem(StatusCodes.Status409Conflict, result.Message!, "session_already_logged"),
            PlanFailure.SessionOutOfWindow => Problem(StatusCodes.Status409Conflict, result.Message!, "session_out_of_window"),
            PlanFailure.GenerationFailed => Problem(StatusCodes.Status422UnprocessableEntity, result.Message!, "plan_generation_failed", errors),
            PlanFailure.ExerciseNotInSession when errors is not null => Problem(StatusCodes.Status400BadRequest, result.Message!, "invalid_log", errors),
            PlanFailure.ExerciseNotInSession => Problem(StatusCodes.Status404NotFound, result.Message!, "exercise_not_in_session"),
            PlanFailure.InvalidRequest => Problem(StatusCodes.Status400BadRequest, result.Message!, "invalid_request", errors),
            _ => Problem(StatusCodes.Status400BadRequest, result.Message ?? "No se pudo completar la operación.", "not_allowed", errors)
        };
    }
}
