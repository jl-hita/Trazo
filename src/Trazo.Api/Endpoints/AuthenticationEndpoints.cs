using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Contracts;
using Trazo.Api.Security;
using Trazo.Domain.Identity;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Authentication")
            .RequireRateLimiting("auth");

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/google", GoogleLoginAsync);
        group.MapPost("/2fa/verify", VerifyTwoFactorAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapPost("/logout-all", LogoutAllAsync).RequireAuthorization();
        group.MapGet("/me", GetMe).RequireAuthorization();
        group.MapPost("/google/link", LinkGoogleAsync).RequireAuthorization();
        group.MapPost("/2fa/setup", SetupTwoFactorAsync).RequireAuthorization();
        group.MapPost("/2fa/enable", EnableTwoFactorAsync).RequireAuthorization();
        group.MapPost("/2fa/disable", DisableTwoFactorAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        TrazoDbContext db,
        PasswordHasher<TrazoUser> hasher,
        IEmailSender emailSender,
        IRuntimeSettings configuration,
        ISetupService setup,
        CancellationToken cancellationToken)
    {
        if (await setup.IsOpenAsync(cancellationToken)) return SetupRequired();
        var email = NormalizeEmail(request.Email);
        if (email is null || request.DisplayName is null || request.DisplayName.Trim().Length is < 2 or > 100)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = ["A valid email and display name are required."] });
        if (!IsValidPassword(request.Password))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Use at least 12 characters including uppercase, lowercase and a number."] });
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == email, cancellationToken))
            return Results.Conflict(new { error = "An account with this email already exists." });

        var now = DateTimeOffset.UtcNow;
        var user = new TrazoUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email.Trim(),
            NormalizedEmail = email,
            DisplayName = request.DisplayName.Trim(),
            Role = "user",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        try
        {
            await AccountEmailEndpoints.IssueAsync(user, "verify-email", "Verifica tu correo de TRAZO",
                "Para activar tu cuenta, abre este enlace:", TimeSpan.FromHours(24), db, emailSender, configuration, cancellationToken);
            return Results.Accepted(value: new { emailVerificationRequired = true, message = "Te hemos enviado un enlace para verificar tu correo." });
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new { error = "An account with this email already exists." });
        }
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        TrazoDbContext db,
        PasswordHasher<TrazoUser> hasher,
        AuthTokenService tokens,
        CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (email is null || string.IsNullOrEmpty(request.Password)) return Results.Unauthorized();
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == email, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (user is null || user.LockoutEnd > now || user.PasswordHash is null)
            return Results.Unauthorized();

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.FailedLoginAttempts++;
            if (user.FailedLoginAttempts >= 5)
            {
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = now.AddMinutes(15);
            }
            user.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
            return Results.Unauthorized();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        if (user.EmailVerifiedAt is null)
            return Results.Conflict(new { code = "email_not_verified", error = "Verifica tu correo antes de iniciar sesión." });
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = now;

        if (user.TwoFactorEnabled)
        {
            var challenge = await CreateChallengeAsync(user.Id, db, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { twoFactorRequired = true, challengeToken = challenge, expiresIn = 300 });
        }

        user.LastLoginAt = now;
        var pair = await tokens.CreateTokenPairAsync(user, db, request.DeviceName, cancellationToken);
        return Results.Ok(new TokenResponse(pair.AccessToken, pair.RefreshToken, "Bearer",
            AuthTokenService.AccessTokenMinutes * 60, ToSummary(user)));
    }

    private static async Task<IResult> GoogleLoginAsync(
        GoogleLoginRequest request,
        IRuntimeSettings configuration,
        ISetupService setup,
        TrazoDbContext db,
        AuthTokenService tokens,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await setup.IsOpenAsync(cancellationToken)) return SetupRequired();
        var payload = await ValidateGoogleTokenAsync(request.IdToken, configuration, loggerFactory.CreateLogger("Trazo.Auth"));
        if (payload is null || !payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
            return Results.Unauthorized();

        var subject = payload.Subject;
        var email = NormalizeEmail(payload.Email);
        if (string.IsNullOrWhiteSpace(subject) || email is null) return Results.Unauthorized();

        var user = await db.Users.SingleOrDefaultAsync(x => x.GoogleSubject == subject, cancellationToken);
        if (user is null)
        {
            if (await db.Users.AnyAsync(x => x.NormalizedEmail == email, cancellationToken))
                return Results.Conflict(new { error = "An account already uses this email. Sign in to that account and link Google from its security settings." });

            var now = DateTimeOffset.UtcNow;
            user = new TrazoUser
            {
                Id = Guid.NewGuid(),
                Email = payload.Email,
                NormalizedEmail = email,
                DisplayName = string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name,
                GoogleSubject = subject,
                Role = "user",
                EmailVerifiedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                LastLoginAt = now
            };
            db.Users.Add(user);
        }
        else
        {
            user.EmailVerifiedAt ??= DateTimeOffset.UtcNow;
            user.LastLoginAt = DateTimeOffset.UtcNow;
            user.UpdatedAt = user.LastLoginAt.Value;
        }

        if (user.TwoFactorEnabled)
        {
            var challenge = await CreateChallengeAsync(user.Id, db, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { twoFactorRequired = true, challengeToken = challenge, expiresIn = 300 });
        }

        var pair = await tokens.CreateTokenPairAsync(user, db, request.DeviceName, cancellationToken);
        return Results.Ok(new TokenResponse(pair.AccessToken, pair.RefreshToken, "Bearer",
            AuthTokenService.AccessTokenMinutes * 60, ToSummary(user)));
    }

    private static async Task<IResult> LinkGoogleAsync(
        GoogleLinkRequest request,
        ClaimsPrincipal principal,
        IRuntimeSettings configuration,
        TrazoDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var payload = await ValidateGoogleTokenAsync(request.IdToken, configuration, loggerFactory.CreateLogger("Trazo.Auth"));
        if (payload is null || !payload.EmailVerified || string.IsNullOrWhiteSpace(payload.Email))
            return Results.Unauthorized();
        var userId = GetUserId(principal);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (!string.Equals(user.Email, payload.Email, StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(new { error = "The verified Google email must match the email on this TRAZO account." });
        if (await db.Users.AnyAsync(x => x.GoogleSubject == payload.Subject && x.Id != userId, cancellationToken))
            return Results.Conflict(new { error = "This Google account is already linked to another TRAZO account." });
        user.GoogleSubject = payload.Subject;
        user.EmailVerifiedAt ??= DateTimeOffset.UtcNow;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> VerifyTwoFactorAsync(
        TwoFactorVerifyRequest request,
        TrazoDbContext db,
        IDataProtectionProvider protection,
        AuthTokenService tokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChallengeToken) || string.IsNullOrWhiteSpace(request.Code))
            return Results.Unauthorized();

        var hash = AuthTokenService.HashToken(request.ChallengeToken);
        var challenge = await db.AuthChallenges.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        if (challenge is null || challenge.ConsumedAt is not null || challenge.ExpiresAt <= now || challenge.FailedAttempts >= 5)
            return Results.Unauthorized();

        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == challenge.UserId, cancellationToken);
        if (user is null || !user.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.ProtectedAuthenticatorKey))
            return Results.Unauthorized();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var protector = protection.CreateProtector("TRAZO.Authenticator.v1");
        var secret = protector.Unprotect(user.ProtectedAuthenticatorKey);
        var valid = TotpService.TryGetMatchingStep(secret, request.Code, now, out var matchingStep) &&
            (user.LastTotpStep is null || matchingStep > user.LastTotpStep.Value);
        if (valid) user.LastTotpStep = matchingStep;
        if (!valid)
        {
            var recoveryHash = AuthTokenService.HashToken(TotpService.NormalizeRecoveryCode(request.Code));
            var recovery = await db.AuthRecoveryCodes.SingleOrDefaultAsync(
                x => x.UserId == user.Id && x.CodeHash == recoveryHash && x.UsedAt == null, cancellationToken);
            if (recovery is not null)
            {
                var consumed = await db.AuthRecoveryCodes
                    .Where(x => x.Id == recovery.Id && x.UsedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UsedAt, now), cancellationToken);
                valid = consumed == 1;
            }
        }

        if (!valid)
        {
            await db.AuthChallenges.Where(x => x.Id == challenge.Id && x.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Results.Unauthorized();
        }

        var marked = await db.AuthChallenges.Where(x => x.Id == challenge.Id && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ConsumedAt, now), cancellationToken);
        if (marked != 1) return Results.Unauthorized();

        user.LastLoginAt = now;
        user.UpdatedAt = now;
        var pair = await tokens.CreateTokenPairAsync(user, db, request.DeviceName, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new TokenResponse(pair.AccessToken, pair.RefreshToken, "Bearer",
            AuthTokenService.AccessTokenMinutes * 60, ToSummary(user)));
    }

    private static async Task<IResult> SetupTwoFactorAsync(
        ClaimsPrincipal principal,
        TrazoDbContext db,
        IDataProtectionProvider protection,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == GetUserId(principal), cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (user.TwoFactorEnabled)
            return Results.Conflict(new { error = "Disable two-factor authentication before replacing its authenticator." });
        var secret = TotpService.GenerateSecret();
        user.ProtectedAuthenticatorKey = protection.CreateProtector("TRAZO.Authenticator.v1").Protect(secret);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new
        {
            secret,
            provisioningUri = TotpService.BuildProvisioningUri(secret, user.Email, "TRAZO"),
            message = "Add this account to an authenticator app, then confirm with a current six-digit code. Two-factor authentication is not enabled until confirmation."
        });
    }

    private static async Task<IResult> EnableTwoFactorAsync(
        TwoFactorCodeRequest request,
        ClaimsPrincipal principal,
        TrazoDbContext db,
        IDataProtectionProvider protection,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == GetUserId(principal), cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(user.ProtectedAuthenticatorKey))
            return Results.BadRequest(new { error = "Start two-factor setup first." });
        var secret = protection.CreateProtector("TRAZO.Authenticator.v1").Unprotect(user.ProtectedAuthenticatorKey);
        if (!TotpService.TryGetMatchingStep(secret, request.Code, DateTimeOffset.UtcNow, out var matchingStep) ||
            (user.LastTotpStep is not null && matchingStep <= user.LastTotpStep.Value))
            return Results.BadRequest(new { error = "The authenticator code is invalid or has already been used." });

        var recoveryCodes = TotpService.GenerateRecoveryCodes();
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AuthRecoveryCodes.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        foreach (var code in recoveryCodes)
        {
            db.AuthRecoveryCodes.Add(new AuthRecoveryCode
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                CodeHash = AuthTokenService.HashToken(TotpService.NormalizeRecoveryCode(code)),
                CreatedAt = now
            });
        }
        user.TwoFactorEnabled = true;
        user.LastTotpStep = matchingStep;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new
        {
            enabled = true,
            recoveryCodes,
            warning = "These recovery codes are shown only once. Store them somewhere safe."
        });
    }

    private static async Task<IResult> DisableTwoFactorAsync(
        TwoFactorCodeRequest request,
        ClaimsPrincipal principal,
        TrazoDbContext db,
        IDataProtectionProvider protection,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == GetUserId(principal), cancellationToken);
        if (user is null) return Results.Unauthorized();
        if (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(user.ProtectedAuthenticatorKey))
            return Results.BadRequest(new { error = "Two-factor authentication is not enabled." });
        var secret = protection.CreateProtector("TRAZO.Authenticator.v1").Unprotect(user.ProtectedAuthenticatorKey);
        if (!TotpService.TryGetMatchingStep(secret, request.Code, DateTimeOffset.UtcNow, out var matchingStep) ||
            (user.LastTotpStep is not null && matchingStep <= user.LastTotpStep.Value))
            return Results.Unauthorized();
        user.LastTotpStep = matchingStep;
        user.TwoFactorEnabled = false;
        user.ProtectedAuthenticatorKey = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.AuthRecoveryCodes.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        TrazoDbContext db,
        AuthTokenService tokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return Results.Unauthorized();
        var hash = AuthTokenService.HashToken(request.RefreshToken);
        var now = DateTimeOffset.UtcNow;
        var existing = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (existing is null || existing.ExpiresAt <= now)
            return Results.Unauthorized();
        if (existing.RevokedAt is not null)
        {
            // A previously rotated token being presented again indicates possible theft.
            // Revoke the remaining active sessions in that user's token family.
            if (existing.ReplacedByTokenHash is not null)
            {
                await db.RefreshTokens
                    .Where(x => x.UserId == existing.UserId && x.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);
            }
            return Results.Unauthorized();
        }
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == existing.UserId, cancellationToken);
        if (user is null) return Results.Unauthorized();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var newRawToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var newHash = AuthTokenService.HashToken(newRawToken);
        var updated = await db.RefreshTokens
            .Where(x => x.Id == existing.Id && x.RevokedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.RevokedAt, now)
                .SetProperty(x => x.ReplacedByTokenHash, newHash), cancellationToken);
        if (updated != 1) return Results.Unauthorized();

        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = newHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(AuthTokenService.RefreshTokenDays),
            DeviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? existing.DeviceName : request.DeviceName.Trim()[..Math.Min(request.DeviceName.Trim().Length, 120)]
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new TokenResponse(tokens.CreateAccessToken(user), newRawToken, "Bearer",
            AuthTokenService.AccessTokenMinutes * 60, ToSummary(user)));
    }

    private static async Task<IResult> LogoutAsync(
        RefreshRequest request,
        ClaimsPrincipal principal,
        TrazoDbContext db,
        CancellationToken cancellationToken)
    {
        var hash = AuthTokenService.HashToken(request.RefreshToken ?? "");
        await db.RefreshTokens.Where(x => x.UserId == GetUserId(principal) && x.TokenHash == hash && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAllAsync(
        ClaimsPrincipal principal,
        TrazoDbContext db,
        CancellationToken cancellationToken)
    {
        await db.RefreshTokens.Where(x => x.UserId == GetUserId(principal) && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetMe(ClaimsPrincipal principal, TrazoDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == GetUserId(principal), cancellationToken);
        return user is null ? Results.Unauthorized() : Results.Ok(ToSummary(user));
    }

    private static async Task<string> CreateChallengeAsync(Guid userId, TrazoDbContext db, CancellationToken cancellationToken)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        db.AuthChallenges.Add(new AuthChallenge
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = AuthTokenService.HashToken(raw),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(5)
        });
        await Task.CompletedTask;
        return raw;
    }

    private static async Task<GoogleJsonWebSignature.Payload?> ValidateGoogleTokenAsync(
        string idToken, IRuntimeSettings configuration, ILogger logger)
    {
        var clientId = (await configuration.GetGoogleAsync()).ClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            logger.LogWarning("Google sign-in rejected: no Google client ID is configured.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        try
        {
            return await GoogleJsonWebSignature.ValidateAsync(idToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = [clientId] });
        }
        catch (InvalidJwtException ex)
        {
            // Never log the token itself; the reason (for example an untrusted audience) is enough
            // to tell a client ID mismatch from an expired or malformed token.
            logger.LogWarning("Google ID token rejected: {Reason}. Expected audience: {ClientId}", ex.Message, clientId);
            return null;
        }
    }

    private static IResult SetupRequired() =>
        Results.Json(new { code = "setup_required", error = "The initial setup has not been completed." }, statusCode: StatusCodes.Status409Conflict);

    private static string? NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 320 ||
            !new EmailAddressAttribute().IsValid(email.Trim())) return null;
        return email.Trim().ToUpperInvariant();
    }

    private static bool IsValidPassword(string? password) =>
        password is { Length: >= 12 and <= 128 } &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit);

    private static Guid GetUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ??
            principal.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var id)
            ? id : throw new UnauthorizedAccessException();

    private static UserSummary ToSummary(TrazoUser user) =>
        new(user.Id, user.Email, user.DisplayName, user.Role, user.TwoFactorEnabled);
}
