namespace Trazo.Api.Contracts;

public sealed record RegisterRequest(string Email, string Password, string DisplayName, string? DeviceName);
public sealed record LoginRequest(string Email, string Password, string? DeviceName);
public sealed record GoogleLoginRequest(string IdToken, string? DeviceName);
public sealed record GoogleLinkRequest(string IdToken);
public sealed record RefreshRequest(string RefreshToken, string? DeviceName);
public sealed record TwoFactorVerifyRequest(string ChallengeToken, string Code, string? DeviceName);
public sealed record TwoFactorCodeRequest(string Code);
public sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn, UserSummary User);
public sealed record UserSummary(Guid Id, string Email, string DisplayName, string Role, bool TwoFactorEnabled);
