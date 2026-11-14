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
}
