using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Billing;

/// <summary>Verifies the OIDC token that Pub/Sub attaches to push requests. Returns the token's email, or null when invalid.</summary>
public interface IPubSubTokenValidator
{
    Task<string?> ValidateAsync(string jwt, string audience, CancellationToken ct);
}

public sealed class GooglePubSubTokenValidator : IPubSubTokenValidator
{
    public async Task<string?> ValidateAsync(string jwt, string audience, CancellationToken ct)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(jwt,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [audience] });
            return payload.EmailVerified ? payload.Email : null;
        }
        catch (InvalidJwtException)
        {
            return null;
        }
    }
}

/// <summary>Decides whether a request to the RTDN webhook comes from our Pub/Sub subscription.</summary>
public static class RtdnAuthenticator
{
    public const string SecretQueryName = "secret";

    public static async Task<bool> IsAuthenticAsync(
        HttpRequest request, PlayBillingConfig config, IPubSubTokenValidator validator, CancellationToken ct)
    {
        // Preferred: Pub/Sub signs its push with an OIDC token for a service account of ours.
        if (!string.IsNullOrWhiteSpace(config.RtdnAudience))
        {
            var header = request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var email = await validator.ValidateAsync(header["Bearer ".Length..].Trim(), config.RtdnAudience!, ct);
                if (email is not null &&
                    (string.IsNullOrWhiteSpace(config.RtdnServiceAccountEmail) ||
                     string.Equals(email, config.RtdnServiceAccountEmail, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        // Alternative: a long random secret in the push endpoint URL (?secret=...).
        if (!string.IsNullOrWhiteSpace(config.RtdnSecret))
        {
            var supplied = request.Query[SecretQueryName].ToString();
            if (supplied.Length > 0 && FixedTimeEquals(supplied, config.RtdnSecret!)) return true;
        }
        return false;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
