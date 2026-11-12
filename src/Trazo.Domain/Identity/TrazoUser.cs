namespace Trazo.Domain.Identity;

public sealed class TrazoUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string DisplayName { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public string Role { get; set; } = "user";
    public bool TwoFactorEnabled { get; set; }
    public string? ProtectedAuthenticatorKey { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
