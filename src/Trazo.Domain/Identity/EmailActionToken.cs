namespace Trazo.Domain.Identity;
public sealed class EmailActionToken
{
 public Guid Id { get; set; }
 public Guid UserId { get; set; }
 public required string TokenHash { get; set; }
 public required string Purpose { get; set; }
 public DateTimeOffset CreatedAt { get; set; }
 public DateTimeOffset ExpiresAt { get; set; }
 public DateTimeOffset? ConsumedAt { get; set; }
}
