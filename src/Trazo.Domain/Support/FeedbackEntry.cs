namespace Trazo.Domain.Support;

public static class FeedbackCategory
{
    public const string Bug = "bug";
    public const string Idea = "idea";
    public const string Other = "other";
    public static readonly string[] All = [Bug, Idea, Other];
}

/// <summary>
/// A comment a signed-in person sends from the app. It is linked to the account so it can be answered, exported with the person's data
/// and deleted with the account. See docs/feedback.md.
/// </summary>
public sealed class FeedbackEntry
{
    public const int MaxMessageLength = 2000;
    public const int MaxScreenLength = 60;
    public const int MaxDeviceLength = 120;

    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>One of <see cref="FeedbackCategory.All"/>.</summary>
    public required string Category { get; set; }
    public required string Message { get; set; }
    /// <summary>The screen of the app the person came from, as the app names it.</summary>
    public string? Screen { get; set; }
    public string? AppVersion { get; set; }
    /// <summary>Phone model and Android version.</summary>
    public string? Device { get; set; }
    public string? Language { get; set; }
    /// <summary>One of <see cref="ErrorTicketStatus.All"/>.</summary>
    public required string Status { get; set; }
    /// <summary>Chosen by the app, so that sending the same comment again stores it once.</summary>
    public Guid ClientRequestId { get; set; }
}
