namespace Trazo.Domain.Planning;

public enum PlanReviewStatus
{
    /// <summary>Waiting in the queue: nobody is looking at it.</summary>
    Pending,
    /// <summary>An administrator took it and is the only one who can answer.</summary>
    InProgress,
    /// <summary>Closed. The thread stays readable; a new request is needed to continue.</summary>
    Reviewed
}

/// <summary>
/// A user's request to have an administrator look over their plan by hand (the engine only produces drafts, not clinical
/// prescriptions), and the conversation around it. One open request (pending or in progress) per plan. Any administrator may
/// take a pending request; the one who has it can give it back to the queue so somebody else can continue.
/// </summary>
public sealed class PlanReviewRequest
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    public PlanReviewStatus Status { get; set; } = PlanReviewStatus.Pending;
    /// <summary>The administrator who has the request; null while it is pending or after it was closed by a deleted account.</summary>
    public Guid? AssignedToUserId { get; set; }
    public DateTimeOffset? AssignedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Time of the last message or state change; the queue is ordered by it.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    /// <summary>When the user last opened the conversation; team messages after it count as unread.</summary>
    public DateTimeOffset? UserLastReadAt { get; set; }
}

public static class PlanReviewAuthors
{
    public const string User = "user";
    /// <summary>An administrator. The user only ever sees "the team", never who it was.</summary>
    public const string Staff = "staff";
    /// <summary>Audit lines (taken, given back, closed). Visible to administrators only.</summary>
    public const string System = "system";
}

/// <summary>
/// One message of a review conversation. The text may contain health information: it is never written to the logs and is
/// deleted with the account.
/// </summary>
public sealed class PlanReviewMessage
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public required string Author { get; set; }
    /// <summary>The administrator behind a staff or system message (kept for the audit trail, never shown to the user).</summary>
    public Guid? AuthorUserId { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Identity the app gives a message it writes, so sending it again stores nothing new.</summary>
    public Guid? ClientRequestId { get; set; }
}
