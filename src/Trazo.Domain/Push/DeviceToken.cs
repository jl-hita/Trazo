namespace Trazo.Domain.Push;

/// <summary>
/// A phone that can receive push notifications for a user. The token is the FCM registration token of the app install; it
/// belongs to one user at a time (signing in with another account on the same phone moves it).
/// </summary>
public sealed class DeviceToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string Token { get; set; }
    public string Platform { get; set; } = "android";
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Last time the app registered this token; tokens that have not been seen for a long time can be pruned.</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
