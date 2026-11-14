namespace Trazo.Api.Overview;

/// <summary>
/// The administration overview: operational state and actionable alerts in one response. It holds aggregates only, never
/// health data, emails, log messages or stack traces.
/// </summary>
public sealed record AdminOverview(
    DateTimeOffset GeneratedAt, int CacheSeconds, OverviewHealth Health, OverviewLogs Logs,
    OverviewConfiguration Configuration, OverviewSummary Summary, IReadOnlyList<OverviewAlert> Alerts);

public sealed record OverviewSystem(string Version, string? Commit, string Environment);

public sealed record OverviewHealth(
    bool DatabaseOk, long DatabaseLatencyMs, string Version, string? Commit, string Environment,
    int MigrationsApplied, int MigrationsTotal, IReadOnlyList<string> PendingMigrations);

public sealed record OverviewErrorSource(string Category, int Count, DateTimeOffset LastAt, string Link);

public sealed record OverviewLogs(
    int Errors24h, int Warnings24h, int ErrorsPrevious24h, IReadOnlyList<OverviewErrorSource> TopErrorSources,
    string ErrorsLink, string WarningsLink);

public sealed record OverviewSmtpSend(bool Succeeded, DateTimeOffset At, string? Error);

public sealed record OverviewMissingSetting(string Id, string Label, string Link);

public sealed record OverviewConfiguration(
    bool SmtpConfigured, OverviewSmtpSend? LastSmtpSend, bool GoogleConfigured, bool PlayBillingConfigured,
    bool PlayRtdnConfigured, bool SubscriptionsEnforced, IReadOnlyList<OverviewMissingSetting> Missing);

public sealed record OverviewCount(string Name, int Count);

public sealed record OverviewSummary(
    int UsersTotal, int UsersNew7d, int PlansCreated7d, int WorkoutsLogged7d, int SubscriptionsTotal,
    IReadOnlyList<OverviewCount> SubscriptionsByStatus, bool SubscriptionsEnforced,
    int ExercisesActive, int ExercisesWithoutImages, int ExercisesUnusable, int ExercisesUsable);

/// <param name="Severity">"critical", "warning" or "info".</param>
/// <param name="Link">An administration route, optionally with a query string (for example <c>/logs?levels=error&amp;range=24h</c>).</param>
public sealed record OverviewAlert(string Id, string Severity, string Title, string Detail, string Link);

/// <summary>The database does not answer: there is nothing reliable to summarise.</summary>
public sealed class OverviewUnavailableException(Exception inner)
    : Exception("The database is not available.", inner);
