namespace Trazo.Domain.Planning;

/// <summary>
/// A proposal to change the training days of a plan because the user persistently trains a different number of days. It is a record of
/// why (the weeks analysed, the policy version) and of what the user decided; it is never deleted by a decision, so a reorganisation
/// can be reverted and the history kept. At most one proposal per plan is pending.
/// </summary>
public sealed class FrequencyProposal
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Dismissed = "dismissed";
    public const string Superseded = "superseded";

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>reduce, restore or revert.</summary>
    public required string Kind { get; set; }
    public required string Status { get; set; }
    /// <summary>Training days before, comma separated (Monday,Thursday...).</summary>
    public required string FromDays { get; set; }
    public required string ToDays { get; set; }
    /// <summary>Split the plan uses before and the one the proposal sets, when it has to change; null keeps it.</summary>
    public string? FromSplit { get; set; }
    public string? ToSplit { get; set; }
    /// <summary>Longest session the plan allowed before and the one the proposal sets, when it changes (a reduction keeps sessions as long as they are, never longer).</summary>
    public int? FromMaxSessionMinutes { get; set; }
    public int? ToMaxSessionMinutes { get; set; }
    /// <summary>Weeks of evidence and how many of them were below the plan.</summary>
    public int WeeksAnalysed { get; set; }
    public int LowWeeks { get; set; }
    public required string Reason { get; set; }
    /// <summary>What the next block would look like with the new days, as JSON.</summary>
    public required string PreviewJson { get; set; }
    public required string PolicyVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary>First block generated with the new days (set when accepted).</summary>
    public int? AppliedFromSequence { get; set; }
}
