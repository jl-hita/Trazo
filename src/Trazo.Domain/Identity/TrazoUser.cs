namespace Trazo.Domain.Identity;

public sealed class TrazoUser
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public string? PasswordHash { get; set; }
    public string? GoogleSubject { get; set; }
    public string Role { get; set; } = "user";
    public bool TwoFactorEnabled { get; set; }
    public string? ProtectedAuthenticatorKey { get; set; }
    public long? LastTotpStep { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    /// <summary>Set by an administrator; a disabled account cannot sign in or use existing tokens.</summary>
    public DateTimeOffset? DisabledAt { get; set; }
    /// <summary>IANA time zone of the user (for example Europe/Madrid). Null means the default, Europe/Madrid.</summary>
    public string? TimeZoneId { get; set; }
    /// <summary>Language the user chose for the texts of the API, e-mails and notifications: "es" or "en". Null means automatic (the language of the request).</summary>
    public string? Language { get; set; }
    /// <summary>
    /// Optional. When true, the anonymous research rows written if the account is deleted may be grouped per plan (random group id) so the
    /// engine can study sequences. False (the default) keeps only independent anonymous rows. Never a condition of using the app.
    /// </summary>
    public bool ResearchConsent { get; set; }
    /// <summary>
    /// When the user gave the explicit consent to process health data (injuries, pain, discomfort, illness). Null: not given, or withdrawn.
    /// The app asks for it the first time one of those is about to be entered, and it is never a condition of using TRAZO.
    /// </summary>
    public DateTimeOffset? HealthConsentAt { get; set; }
    /// <summary>When that consent was last withdrawn (the health data was erased then).</summary>
    public DateTimeOffset? HealthConsentWithdrawnAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
