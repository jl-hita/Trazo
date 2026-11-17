using Trazo.Domain.Growth;
using Trazo.Infrastructure.Settings;

namespace Trazo.Infrastructure.Growth;

/// <summary>The account milestones that feed the funnel and give the welcome Premium period.</summary>
public interface ISignupTrialService
{
    /// <summary>The account was created (registration form or first Google sign-in).</summary>
    Task SignedUpAsync(Guid userId, string? appVersion, CancellationToken ct);

    /// <summary>
    /// The account became usable (e-mail verified, or Google sign-in). Gives the welcome Premium period when the superadmin has
    /// it switched on, for the number of days configured at that moment. Idempotent: it is granted once per account.
    /// </summary>
    Task<PremiumGrant?> ActivatedAsync(Guid userId, CancellationToken ct);
}

public sealed class SignupTrialService(IRuntimeSettings settings, IPremiumGrantService grants, IGrowthEvents events) : ISignupTrialService
{
    public Task SignedUpAsync(Guid userId, string? appVersion, CancellationToken ct) =>
        events.RecordAsync(GrowthEventNames.Signup, userId, new GrowthEventInfo(AppVersion: appVersion, DedupeKey: $"signup:{userId:N}"), ct);

    public async Task<PremiumGrant?> ActivatedAsync(Guid userId, CancellationToken ct)
    {
        await events.RecordAsync(GrowthEventNames.EmailVerified, userId, new GrowthEventInfo(DedupeKey: $"email_verified:{userId:N}"), ct);
        var promotions = await settings.GetPromotionsAsync(ct);
        if (!promotions.SignupTrialEnabled || promotions.SignupTrialDays <= 0) return null;
        var result = await grants.GrantAsync(new GrantRequest(userId, PremiumGrantKinds.SignupTrial, userId.ToString("N"), promotions.SignupTrialDays), ct);
        if (result.Created)
            await events.RecordAsync(GrowthEventNames.SignupTrialGranted, userId, new GrowthEventInfo(DedupeKey: $"signup_trial:{userId:N}"), ct);
        return result.Grant;
    }
}
