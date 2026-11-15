namespace Trazo.Infrastructure.Push;

/// <summary>
/// What a notification carries. It is sent as a data message and the app builds the notification itself, so it also works
/// when the app is closed. <c>Kind</c> lets the app open the right screen and use its own translation; Title and Body are the
/// text to show otherwise. Never put health data or the text of a conversation in it: it goes through Google's servers.
/// </summary>
public sealed record PushMessage(string Title, string Body, string? Kind = null);

public sealed record PushSendResult(int Devices, int Sent, int Failed, int Removed)
{
    public static PushSendResult None { get; } = new(0, 0, 0, 0);
}

public enum PushFailure { NotConfigured, Unauthorized, Unavailable }

public sealed class PushException(PushFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public PushFailure Failure { get; } = failure;
}

public interface IPushSender
{
    /// <summary>
    /// Sends to every device of the given users (all devices when <paramref name="userIds"/> is null). Tokens that Firebase says
    /// no longer exist are deleted. Throws <see cref="PushException"/> when push is not configured or cannot be reached.
    /// </summary>
    Task<PushSendResult> SendAsync(IReadOnlyCollection<Guid>? userIds, PushMessage message, CancellationToken ct);

    /// <summary>Checks the service-account key against Firebase without sending anything to a phone.</summary>
    Task CheckConnectionAsync(CancellationToken ct);
}

/// <summary>Returns an OAuth access token for the Firebase Cloud Messaging API from a service-account key.</summary>
public interface IFcmAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(string serviceAccountJson, CancellationToken ct);
}
