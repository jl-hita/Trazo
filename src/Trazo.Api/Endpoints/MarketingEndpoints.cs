using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Trazo.Infrastructure.Marketing;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

public sealed record InstallCampaignRequest(string? Campaign);

/// <summary>
/// The marketing panel for the superadmin and the commercial role, plus the two public entry points of a campaign: its tracking
/// link and the install report the app sends after reading the Google Play Install Referrer. The panel only ever returns aggregates.
/// </summary>
public static partial class MarketingEndpoints
{
    public const string ReferrerKey = "trazo_cmp";

    [GeneratedRegex("bot|crawler|spider|preview|facebookexternalhit|whatsapp|telegram|slack|discord|skype|embedly|curl|wget|python-requests|headless",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Robots();

    public static IEndpointRouteBuilder MapMarketingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Public and per-IP limited: they only count events and hold no personal data.
        endpoints.MapGet("/c/{slug}", OpenLinkAsync).AllowAnonymous().RequireRateLimiting(ReferralEndpoints.PublicRateLimitPolicy).ExcludeFromDescription();
        endpoints.MapPost("/api/v1/campaigns/install-referrer", InstallAsync).WithTags("Campaigns")
            .AllowAnonymous().RequireRateLimiting(ReferralEndpoints.PublicRateLimitPolicy);

        var g = endpoints.MapGroup("/api/v1/admin/marketing").WithTags("Admin marketing")
            .RequireAuthorization(SetupEndpoints.MarketingPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);

        g.MapGet("/options", () => Results.Ok(new
        {
            channels = Trazo.Domain.Marketing.CampaignChannels.All, statuses = Trazo.Domain.Marketing.CampaignStatuses.All,
            minReliable = MarketingAnalytics.MinReliable, retentionWeeks = MarketingAnalytics.RetentionWeeks
        }));
        g.MapGet("/summary", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.SummaryAsync(f, t, ct)); }));
        g.MapGet("/strategies", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.StrategiesAsync(f, t, ct)); }));
        g.MapGet("/strategies.csv", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            Run(async () =>
            {
                var (f, t) = Period(from, to, clock);
                var csv = await a.StrategiesCsvAsync(f, t, ct);
                return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", $"trazo-strategies-{f:yyyyMMdd}-{t:yyyyMMdd}.csv");
            }));
        g.MapGet("/funnel", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, string? segment, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.FunnelAsync(f, t, segment, ct)); }));
        g.MapGet("/retention", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, string? segment, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.RetentionAsync(f, t, segment, ct)); }));
        g.MapGet("/paywall", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.PaywallAsync(f, t, ct)); }));
        g.MapGet("/economics", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.EconomicsAsync(f, t, ct)); }));
        g.MapGet("/downloads", (IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.DownloadsAsync(f, t, ct)); }));
        g.MapPut("/downloads", (DownloadsInput body, IDownloadsService s, ClaimsPrincipal user, CancellationToken ct) =>
            Run(async () => Results.Ok(await s.SaveAsync(body, OperationsSettingsEndpoints.Actor(user), ct))));
        g.MapDelete("/downloads/{week}", (DateOnly week, IDownloadsService s, CancellationToken ct) =>
            Run(async () => { await s.DeleteAsync(week, ct); return Results.NoContent(); }));
        g.MapGet("/waitlist", (IMarketingAnalytics a, CancellationToken ct) => Run(async () => Results.Ok(await a.WaitlistAsync(ct))));
        g.MapGet("/campaigns", (ICampaignService s, HttpContext http, IRuntimeSettings settings, CancellationToken ct) =>
            Run(async () =>
            {
                var baseUrl = await ReferralEndpoints.BaseUrlAsync(http, settings, ct);
                return Results.Ok(new { items = (await s.ListAsync(ct)).Select(c => new { campaign = c, link = $"{baseUrl}/c/{c.Slug}" }) });
            }));
        g.MapPost("/campaigns", (CampaignInput body, ICampaignService s, ClaimsPrincipal user, HttpContext http, IRuntimeSettings settings, CancellationToken ct) =>
            Run(async () =>
            {
                var created = await s.CreateAsync(body, OperationsSettingsEndpoints.Actor(user), ct);
                return Results.Ok(new { campaign = created, link = $"{await ReferralEndpoints.BaseUrlAsync(http, settings, ct)}/c/{created.Slug}" });
            }));
        g.MapPut("/campaigns/{id:guid}", (Guid id, CampaignInput body, ICampaignService s, HttpContext http, IRuntimeSettings settings, CancellationToken ct) =>
            Run(async () =>
            {
                var updated = await s.UpdateAsync(id, body, ct);
                return Results.Ok(new { campaign = updated, link = $"{await ReferralEndpoints.BaseUrlAsync(http, settings, ct)}/c/{updated.Slug}" });
            }));
        g.MapGet("/campaigns/{id:guid}", (Guid id, IMarketingAnalytics a, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
            Run(async () => { var (f, t) = Period(from, to, clock); return Results.Ok(await a.CampaignAsync(id, f, t, ct)); }));
        g.MapPost("/campaigns/{id:guid}/spend", (Guid id, SpendInput body, ICampaignService s, ClaimsPrincipal user, CancellationToken ct) =>
            Run(async () => Results.Ok(await s.AddSpendAsync(id, body, OperationsSettingsEndpoints.Actor(user), ct))));
        g.MapDelete("/campaigns/{id:guid}/spend/{spendId:guid}", (Guid id, Guid spendId, ICampaignService s, CancellationToken ct) =>
            Run(async () => { await s.DeleteSpendAsync(id, spendId, ct); return Results.NoContent(); }));
        return endpoints;
    }

    /// <summary>Default period: the last 90 days. At most two years, so that a request cannot read the whole history at once.</summary>
    private static (DateTimeOffset From, DateTimeOffset To) Period(DateTimeOffset? from, DateTimeOffset? to, TimeProvider clock)
    {
        var end = to ?? clock.GetUtcNow().AddDays(1);
        var start = from ?? end.AddDays(-90);
        if (start >= end) throw new MarketingException(400, "validation", "period: 'from' must be before 'to'.");
        if (end - start > TimeSpan.FromDays(731)) throw new MarketingException(400, "validation", "period: at most two years.");
        return (start, end);
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (MarketingException e) { return Results.Json(new { error = e.Code, message = e.Message }, statusCode: e.Status); }
    }

    // ------------------------------------------------------------------ public

    /// <summary>
    /// The tracking link of a campaign. Counts the opening (link-preview robots excluded) and sends the visitor to Google Play with a
    /// referrer carrying the campaign: Google Play hands it to the app on first launch. The same referrer also carries standard
    /// utm_* values so that Google Play Console's own acquisition reports agree with this panel.
    /// </summary>
    private static async Task<IResult> OpenLinkAsync(
        string slug, HttpContext http, ICampaignService campaigns, IRuntimeSettings settings, CancellationToken ct)
    {
        var package = (await settings.GetPlayBillingAsync(ct)).PackageName;
        if (string.IsNullOrWhiteSpace(package)) package = ReferralEndpoints.DefaultPackage;
        var store = $"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(package)}";
        var agent = http.Request.Headers.UserAgent.ToString();
        var robot = string.IsNullOrWhiteSpace(agent) || Robots().IsMatch(agent);
        var campaign = robot ? await campaigns.FindAsync(slug, ct) : await campaigns.RecordClickAsync(slug, ct);
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers.CacheControl = "no-store";
        if (campaign is null) return Results.Redirect(store);
        var referrer = $"utm_source={campaign.Channel}&utm_medium=trazo&utm_campaign={campaign.Slug}&{ReferrerKey}={campaign.Slug}";
        return Results.Redirect($"{store}&referrer={Uri.EscapeDataString(referrer)}");
    }

    /// <summary>The app read a campaign in the Play Install Referrer on first launch: one more attributable install.</summary>
    private static async Task<IResult> InstallAsync(InstallCampaignRequest? request, ICampaignService campaigns, CancellationToken ct)
    {
        await campaigns.RecordInstallAsync(request?.Campaign, ct);
        return Results.NoContent();
    }
}
