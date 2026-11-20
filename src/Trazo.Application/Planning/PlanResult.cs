namespace Trazo.Application.Planning;

public enum PlanFailure
{
    None,
    NoActivePlan,
    ActivePlanExists,
    SessionNotFound,
    /// <summary>Only the current week (and the one before, to log a late session) can be used.</summary>
    SessionOutOfWindow,
    AlreadyLogged,
    InvalidRequest,
    GenerationFailed,
    ExerciseNotInSession,
    NotAllowed,
    /// <summary>The plan was generated automatically and the account has no Premium access.</summary>
    PremiumRequired,
    /// <summary>A session was changed by another request after this one read it; nothing of this request was stored.</summary>
    ConcurrentModification
}

public sealed record PlanResult<T>(
    T? Value, PlanFailure Failure = PlanFailure.None, string? Message = null,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public bool Succeeded => Failure == PlanFailure.None;
    public static PlanResult<T> Ok(T value) => new(value);
    public static PlanResult<T> Fail(PlanFailure failure, string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(default, failure, message, errors);
}
