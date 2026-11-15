namespace Trazo.Domain.Analytics;

/// <summary>
/// How many times the Premium offer was opened on a given UTC day from a given place of the app. A pure counter: it holds
/// no user, device or session, so it is not personal data and needs no consent.
/// </summary>
public sealed class PaywallView
{
    public DateOnly Day { get; set; }
    /// <summary>One of <see cref="PaywallSources.All"/>.</summary>
    public required string Source { get; set; }
    public int Views { get; set; }
}

/// <summary>The places of the app that open the Premium offer. Anything else is counted as <see cref="Other"/>.</summary>
public static class PaywallSources
{
    public const string Account = "account";
    public const string Plan = "plan";
    public const string Session = "session";
    public const string Onboarding = "onboarding";
    public const string Other = "other";
    public static readonly string[] All = [Account, Plan, Session, Onboarding, Other];

    public static string Normalize(string? source) =>
        source is not null && All.Contains(source.Trim().ToLowerInvariant()) ? source.Trim().ToLowerInvariant() : Other;
}
