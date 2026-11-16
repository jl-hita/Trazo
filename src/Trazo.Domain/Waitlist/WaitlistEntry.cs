namespace Trazo.Domain.Waitlist;

/// <summary>
/// A person who asked, from the public landing page, to be told when the app opens. It is deliberately not linked to
/// <c>TrazoUser</c>: the person may never register, and deleting an account must not depend on it (see docs/waitlist.md).
/// </summary>
public sealed class WaitlistEntry
{
    public Guid Id { get; set; }
    /// <summary>Trimmed and lower-cased; unique. This is the only form that is stored.</summary>
    public required string Email { get; set; }
    /// <summary>"es" or "en": the language of the landing page the person used.</summary>
    public required string Language { get; set; }
    /// <summary>When the person ticked the consent box (the legal basis for contacting them).</summary>
    public DateTimeOffset ConsentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When the person confirmed the address from the link we mailed (double opt-in). Null until then: nothing but the confirmation email is sent to an unconfirmed address.</summary>
    public DateTimeOffset? ConfirmedAt { get; set; }
    /// <summary>SHA-256 of the pending confirmation token (the token itself is never stored). Cleared once used.</summary>
    public string? ConfirmTokenHash { get; set; }
    public DateTimeOffset? ConfirmTokenExpiresAt { get; set; }
    /// <summary>When the invitation to the closed test was sent, after the superadmin added the address to the Play Console tester list.</summary>
    public DateTimeOffset? InvitedAt { get; set; }
}
