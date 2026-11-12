namespace Trazo.Domain.Identity;

public sealed class AuthRecoveryCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required string CodeHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
