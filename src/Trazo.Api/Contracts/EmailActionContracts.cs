namespace Trazo.Api.Contracts;
public sealed record EmailTokenRequest(string Token);
public sealed record ResendVerificationRequest(string Email);
public sealed record ForgotPasswordRequest(string Email);
public sealed record ResetPasswordRequest(string Token,string NewPassword);
