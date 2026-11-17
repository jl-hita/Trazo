namespace Trazo.Infrastructure.Marketing;

/// <summary>
/// One row of the strategy comparison: everything measured about the people one acquisition strategy brought (a campaign, a channel
/// type, the referral programme or organic arrivals), from the link click to the paid renewal. It is an aggregate: nobody can be
/// identified from it.
/// </summary>
public sealed record SegmentRow(
    string Kind, string Key, string Name, string? Channel, string? Status,
    int Clicks, int Installs, int Signups, int Verified, int Onboarded, int Activated, int Trials, int Paid, int Renewed, int Canceled, int Refunded,
    int D7Eligible, int D7Active, int D30Eligible, int D30Active,
    decimal Spend, decimal EstimatedRevenue,
    double? ClickToInstall, double? InstallToSignup, double? SignupToActivated, double? SignupToTrial, double? SignupToPaid,
    double? SignupToPaidLow, double? SignupToPaidHigh, double? TrialToPaid, double? PaidRenewed, double? D7, double? D30,
    decimal? CostPerInstall, decimal? CostPerSignup, decimal? CostPerTrial, decimal? CostPerPaid, double? ReturnOnSpend, bool Reliable);

public sealed record Insight(string Kind, string Title, string Detail);

public sealed record StrategiesReport(
    DateTimeOffset From, DateTimeOffset To, int MinReliable, SegmentRow Overall,
    IReadOnlyList<SegmentRow> Channels, IReadOnlyList<SegmentRow> Campaigns, IReadOnlyList<Insight> Insights);

public sealed record FunnelStageRow(string Name, int Users, double? OfPrevious, double? OfCohort);

public sealed record MarketingFunnel(
    DateTimeOffset From, DateTimeOffset To, string Segment, string SegmentName, int Cohort, IReadOnlyList<FunnelStageRow> Stages,
    double? SignupToPaid, double? SignupToPaidLow, double? SignupToPaidHigh, double? TrialToPaid, double? PaidRenewed);

public sealed record RetentionCohort(string Week, int Size, int Paid, int Renewed, IReadOnlyList<double?> Active);

/// <summary>Share of each signup-week cohort that trained at least once in each week after signing up. Cells only count people whose week is already over.</summary>
public sealed record RetentionReport(
    DateTimeOffset From, DateTimeOffset To, string Segment, string SegmentName, int Weeks, int Cohort,
    IReadOnlyList<double?> Average, IReadOnlyList<RetentionCohort> Cohorts);

public sealed record Kpi(string Key, string Label, int Current, int Previous);
public sealed record WeekPoint(string Week, int Signups, int Trials, int FirstPurchases, int Renewals, int Cancellations, int Refunds, int PaywallViews);
public sealed record MixRow(string Key, string Name, int Signups, int Paid);
public sealed record SubscribersNow(int Paying, int Trialing, int CancelingAtPeriodEnd, int Billing);

public sealed record SummaryReport(
    DateTimeOffset From, DateTimeOffset To, IReadOnlyList<Kpi> Kpis, double? SignupToTrial, double? SignupToPaid, double? TrialToPaid,
    int Cohort, int PaywallViews, SubscribersNow Subscribers, IReadOnlyList<MixRow> Mix, IReadOnlyList<WeekPoint> Weeks,
    int WaitlistConfirmed, int ActiveCampaigns, int? Downloads, double? DownloadToSignup);

public sealed record PaywallSourceRow(string Source, int Views, double Share);
public sealed record PaywallWeekRow(string Week, int Total, IReadOnlyDictionary<string, int> BySource);
public sealed record PaywallReport(
    DateTimeOffset From, DateTimeOffset To, int Total, int FirstPurchases, double? ViewsPerPurchase,
    IReadOnlyList<PaywallSourceRow> Sources, IReadOnlyList<PaywallWeekRow> Weeks);

public sealed record WaitlistWeek(string Week, int Joined);
public sealed record WaitlistReport(int Total, int Confirmed, int Invited, double? ConfirmRate, double? InviteRate, IReadOnlyDictionary<string, int> ByLanguage, IReadOnlyList<WaitlistWeek> Weeks);

public sealed record CampaignDay(DateOnly Day, int Clicks, int Installs, int Signups, decimal Spend);
public sealed record CampaignDetail(CampaignView Campaign, SegmentRow Row, IReadOnlyList<CampaignDay> Days, IReadOnlyList<SpendView> Spend);

public sealed record DownloadWeek(string Week, int? Downloads, int Signups, double? DownloadToSignup, string? Note);
/// <summary>Store downloads (typed in from Google Play Console) against accounts created, week by week.</summary>
public sealed record DownloadsReport(
    IReadOnlyList<DownloadWeek> Weeks, int Downloads, int SignupsInThoseWeeks, double? DownloadToSignup, int UnregisteredInstalls,
    int WeeksWithData, int WeeksMissing);

public sealed record SurvivalPoint(int Months, double? Retained, int Eligible);

/// <summary>
/// Unit economics of one strategy: what a paying customer costs, brings per month and keeps paying for. Orientative: it needs enough
/// payers (see <see cref="Reliable"/>), uses Google Play prices of the tax settings and observed renewals, and caps the lifetime.
/// </summary>
public sealed record EconomicsRow(
    string Kind, string Key, string Name, int Signups, int Payers, decimal Spend, decimal NetRevenue, decimal PaidMonths,
    decimal? Cac, decimal? NetPerMonth, double? MonthlyChurn, double? LifetimeMonths, decimal? Ltv, double? LtvToCac, double? PaybackMonths,
    bool Reliable);

public sealed record EconomicsReport(
    DateTimeOffset From, DateTimeOffset To, int MinPayers, int LifetimeCapMonths, EconomicsRow Overall, IReadOnlyList<EconomicsRow> Rows,
    IReadOnlyList<SurvivalPoint> Survival, IReadOnlyList<Insight> Insights);
