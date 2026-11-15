using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Infrastructure.Push;

public sealed class GoogleFcmAccessTokenProvider : IFcmAccessTokenProvider
{
    public const string Scope = "https://www.googleapis.com/auth/firebase.messaging";

    public async Task<string> GetAccessTokenAsync(string serviceAccountJson, CancellationToken ct)
    {
        try
        {
            var credential = CredentialFactory.FromJson<ServiceAccountCredential>(serviceAccountJson)
                .ToGoogleCredential().CreateScoped(Scope);
            return await ((ITokenAccess)credential).GetAccessTokenForRequestAsync(cancellationToken: ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or JsonException)
        {
            throw new PushException(PushFailure.NotConfigured, "The saved service-account key is not valid.", ex);
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException ex)
        {
            throw new PushException(PushFailure.Unauthorized, "Google rejected the service-account key. Check that it is active and belongs to the Firebase project.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PushException(PushFailure.Unavailable, "Google could not be reached.", ex);
        }
    }
}

/// <summary>Firebase Cloud Messaging HTTP v1 sender. The configuration is read on every call, so a change applies at once.</summary>
public sealed class FcmPushSender(
    HttpClient http, IRuntimeSettings settings, TrazoDbContext db, IFcmAccessTokenProvider tokens, ILogger<FcmPushSender>? logger = null)
    : IPushSender
{
    public const string Endpoint = "https://fcm.googleapis.com/";
    private const int MaxParallel = 8;

    public async Task<PushSendResult> SendAsync(IReadOnlyCollection<Guid>? userIds, PushMessage message, CancellationToken ct)
    {
        var (config, accessToken) = await PrepareAsync(ct);
        var query = db.DeviceTokens.AsNoTracking();
        if (userIds is not null) query = query.Where(d => userIds.Contains(d.UserId));
        var devices = await query.Select(d => new { d.Id, d.Token }).ToListAsync(ct);
        if (devices.Count == 0) return PushSendResult.None;

        var gone = new ConcurrentBag<Guid>();
        var sent = 0;
        var failed = 0;
        await Parallel.ForEachAsync(devices, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = ct },
            async (device, token) =>
            {
                switch (await SendOneAsync(config.ProjectId, accessToken, device.Token, message, token))
                {
                    case Outcome.Sent: Interlocked.Increment(ref sent); break;
                    case Outcome.TokenGone: gone.Add(device.Id); Interlocked.Increment(ref failed); break;
                    default: Interlocked.Increment(ref failed); break;
                }
            });

        var removed = 0;
        if (!gone.IsEmpty)
        {
            var ids = gone.ToList();
            removed = await db.DeviceTokens.Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync(ct);
        }
        return new PushSendResult(devices.Count, sent, failed, removed);
    }

    public async Task CheckConnectionAsync(CancellationToken ct)
    {
        var (config, accessToken) = await PrepareAsync(ct);
        // validate_only: Firebase authenticates and validates the request without delivering anything.
        using var request = BuildRequest(config.ProjectId, accessToken, new
        {
            validate_only = true,
            message = new { token = "trazo-connection-check", data = new Dictionary<string, string> { ["kind"] = "check" } }
        });
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct); }
        catch (HttpRequestException ex) { throw new PushException(PushFailure.Unavailable, "Firebase could not be reached.", ex); }
        using (response)
        {
            // A bad (fake) token answers 400 INVALID_ARGUMENT only after the key and project were accepted.
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest) return;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
                throw new PushException(PushFailure.Unauthorized,
                    "Firebase rejected the service account or project. Check that the Firebase Cloud Messaging API is enabled and the project ID is right.");
            throw new PushException(PushFailure.Unavailable, "Firebase returned an error. Try again later.");
        }
    }

    private async Task<(PushConfig Config, string AccessToken)> PrepareAsync(CancellationToken ct)
    {
        var config = await settings.GetPushAsync(ct);
        if (!config.IsComplete)
            throw new PushException(PushFailure.NotConfigured,
                "Push notifications are not configured. A superadmin must save the Firebase project and service account.");
        var accessToken = await tokens.GetAccessTokenAsync(config.ServiceAccountJson!, ct);
        return (config, accessToken);
    }

    private enum Outcome { Sent, TokenGone, Failed }

    private async Task<Outcome> SendOneAsync(string projectId, string accessToken, string deviceToken, PushMessage message, CancellationToken ct)
    {
        var data = new Dictionary<string, string> { ["title"] = message.Title, ["body"] = message.Body };
        if (!string.IsNullOrEmpty(message.Kind)) data["kind"] = message.Kind;
        using var request = BuildRequest(projectId, accessToken, new
        {
            message = new { token = deviceToken, data, android = new { priority = "HIGH", ttl = "86400s" } }
        });
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return Outcome.Sent;
            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.NotFound || body.Contains("UNREGISTERED", StringComparison.Ordinal)) return Outcome.TokenGone;
            logger?.LogWarning("Firebase answered {Status} to a push message.", (int)response.StatusCode);
            return Outcome.Failed;
        }
        catch (HttpRequestException ex)
        {
            logger?.LogWarning(ex, "A push message could not be delivered to Firebase.");
            return Outcome.Failed;
        }
    }

    private static HttpRequestMessage BuildRequest(string projectId, string accessToken, object payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(Endpoint), $"v1/projects/{Uri.EscapeDataString(projectId)}/messages:send"))
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }
}
