using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Security;

/// <summary>A verified Google identity, reduced to what re-authentication needs.</summary>
public sealed record GoogleIdentity(string Subject, DateTimeOffset IssuedAt);

/// <summary>Validates a Google ID token against the configured Web client ID. Replaceable in tests.</summary>
public interface IGoogleIdTokenValidator
{
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct);
}

public sealed class GoogleIdTokenValidator(IRuntimeSettings configuration, ILogger<GoogleIdTokenValidator> logger) : IGoogleIdTokenValidator
{
    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct)
    {
        var clientId = (await configuration.GetGoogleAsync(ct)).ClientId;
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(idToken)) return null;
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
            if (string.IsNullOrWhiteSpace(payload.Subject) || payload.IssuedAtTimeSeconds is not { } issued) return null;
            return new GoogleIdentity(payload.Subject, DateTimeOffset.FromUnixTimeSeconds(issued));
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning("Google ID token rejected during re-authentication: {Reason}", ex.Message);
            return null;
        }
    }
}

/// <summary>What the user sends to prove it is really them before something irreversible happens.</summary>
public sealed record ReauthRequest(string? Password, string? IdToken, string? Code);

/// <summary>
/// Recent re-authentication for sensitive actions (deleting an account). A password OR a fresh Google ID token proves who the
/// user is; a TOTP or recovery code is required in addition when two-factor authentication is on. Failures count towards the
/// same lockout as sign-in, so a stolen access token cannot be used to guess the password.
/// </summary>
public sealed class Reauthenticator(
    TrazoDbContext db, PasswordHasher<TrazoUser> hasher, IDataProtectionProvider protection, IGoogleIdTokenValidator google, TimeProvider clock)
{
    /// <summary>A Google ID token older than this is not "recent" and is refused.</summary>
    public static readonly TimeSpan GoogleTokenMaxAge = TimeSpan.FromMinutes(5);

    public async Task<bool> VerifyAsync(TrazoUser user, ReauthRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (user.LockoutEnd > now) return false;

        var first = false;
        if (user.PasswordHash is not null && !string.IsNullOrEmpty(request.Password))
            first = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) != PasswordVerificationResult.Failed;
        else if (user.GoogleSubject is not null && !string.IsNullOrEmpty(request.IdToken))
        {
            var identity = await google.ValidateAsync(request.IdToken, ct);
            first = identity is not null && identity.Subject == user.GoogleSubject
                && now - identity.IssuedAt <= GoogleTokenMaxAge && identity.IssuedAt - now <= TimeSpan.FromMinutes(1);
        }
        if (first && user.TwoFactorEnabled) first = await VerifySecondFactorAsync(user, request.Code, ct);
        if (first) return true;
        await RegisterFailureAsync(user, ct);
        return false;
    }

    /// <summary>Checks a TOTP code (each time step usable once) or an unused recovery code. Counts failures like a sign-in.</summary>
    public async Task<bool> VerifySecondFactorAsync(TrazoUser user, string? code, CancellationToken ct, bool countFailure = false)
    {
        var now = clock.GetUtcNow();
        var valid = false;
        if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(user.ProtectedAuthenticatorKey))
        {
            var secret = protection.CreateProtector("TRAZO.Authenticator.v1").Unprotect(user.ProtectedAuthenticatorKey);
            if (TotpService.TryGetMatchingStep(secret, code, now, out var step) && (user.LastTotpStep is null || step > user.LastTotpStep.Value))
            {
                user.LastTotpStep = step;
                await db.SaveChangesAsync(ct);
                valid = true;
            }
            else
            {
                var hash = AuthTokenService.HashToken(TotpService.NormalizeRecoveryCode(code));
                var recovery = await db.AuthRecoveryCodes.FirstOrDefaultAsync(x => x.UserId == user.Id && x.CodeHash == hash && x.UsedAt == null, ct);
                if (recovery is not null)
                    valid = await db.AuthRecoveryCodes.Where(x => x.Id == recovery.Id && x.UsedAt == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), ct) == 1;
            }
        }
        if (!valid && countFailure) await RegisterFailureAsync(user, ct);
        return valid;
    }

    private async Task RegisterFailureAsync(TrazoUser user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        user.FailedLoginAttempts++;
        if (user.FailedLoginAttempts >= 5)
        {
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = now.AddMinutes(15);
        }
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Limits how many deletion links can be requested for one email address, without revealing whether the address exists: it
/// counts requests for any address, known or not, so the answer to the visitor is always the same.
/// </summary>
public sealed class DeletionRequestThrottle(TimeProvider clock)
{
    public const int MaximumPerWindow = 3;
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Start, int Count)> _counters = new();

    /// <summary>True when this request may send an email.</summary>
    public bool TryAcquire(string normalizedEmail)
    {
        var now = clock.GetUtcNow();
        if (_counters.Count > 10_000)
            foreach (var stale in _counters.Where(c => now - c.Value.Start > Window).Select(c => c.Key).ToList()) _counters.TryRemove(stale, out _);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)));
        var allowed = false;
        _counters.AddOrUpdate(key,
            _ => { allowed = true; return (now, 1); },
            (_, current) =>
            {
                if (now - current.Start > Window) { allowed = true; return (now, 1); }
                allowed = current.Count < MaximumPerWindow;
                return allowed ? (current.Start, current.Count + 1) : current;
            });
        return allowed;
    }
}
