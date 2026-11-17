using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Analytics;
using Trazo.Domain.Billing;
using Trazo.Domain.Growth;
using Trazo.Domain.Marketing;
using Trazo.Domain.Planning;
using Trazo.Infrastructure.Fiscal;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Marketing;

public interface IMarketingAnalytics
{
    Task<SummaryReport> SummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<StrategiesReport> StrategiesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<MarketingFunnel> FunnelAsync(DateTimeOffset from, DateTimeOffset to, string? segment, CancellationToken ct);
    Task<RetentionReport> RetentionAsync(DateTimeOffset from, DateTimeOffset to, string? segment, CancellationToken ct);
    Task<PaywallReport> PaywallAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<WaitlistReport> WaitlistAsync(CancellationToken ct);
    Task<CampaignDetail> CampaignAsync(Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<string> StrategiesCsvAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>
/// Marketing analytics for the commercial role: which acquisition strategy brings people who sign up, train, start a trial, pay and
/// stay. Everything is an aggregate over accounts created in a period (a "cohort"): no name, e-mail or id of any person ever leaves
/// this class. A person counts in exactly one strategy: the referral code wins over a campaign, a campaign wins over "organic".
/// Economic events come from purchases verified with Google Play; test purchases and promotional time are left out.
/// </summary>
public sealed class MarketingAnalytics(TrazoDbContext db, IFiscalService fiscal, TimeProvider clock) : IMarketingAnalytics
{
    /// <summary>A strategy needs at least this many signups before its rates are called reliable (the interval shows the uncertainty anyway).</summary>
    public const int MinReliable = 20;
    public const int RetentionWeeks = 8;
    private const int ChunkSize = 1000;

    // ------------------------------------------------------------------ loading

    private sealed class Person
    {
        public Guid Id;
        public DateTimeOffset SignupAt;
        public string Channel = CampaignChannels.Organic;
        public Guid? CampaignId;
        public string? Slug;
        public bool Verified, Onboarded, HasPlan, Trial, Paid, Canceled, Refunded;
        public int Renewals;
        public List<DateTimeOffset> Workouts = [];
        public List<SaleObservation> Sales = [];
        public DateTimeOffset? FirstWorkout => Workouts.Count == 0 ? null : Workouts.Min();
    }

    private sealed record Loaded(List<Person> People, List<MarketingCampaign> Campaigns, FiscalProfile Profile);

    private static async Task<List<T>> InChunksAsync<T>(IReadOnlyList<Guid> ids, Func<List<Guid>, Task<List<T>>> query)
    {
        var result = new List<T>();
        for (var i = 0; i < ids.Count; i += ChunkSize) result.AddRange(await query(ids.Skip(i).Take(ChunkSize).ToList()));
        return result;
    }

    public static Guid? SubscriptionOf(string? dedupeKey)
    {
        var parts = dedupeKey?.Split(':');
        return parts is { Length: >= 2 } && Guid.TryParse(parts[1], out var id) ? id : null;
    }

    /// <summary>Subscriptions whose events must not count: test purchases and promotional time.</summary>
    private async Task<HashSet<Guid>> ExcludedSubscriptionsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var rows = await InChunksAsync(ids, chunk => db.Subscriptions.AsNoTracking()
            .Where(s => chunk.Contains(s.Id) && (s.IsTestPurchase || s.Source != SubscriptionSources.GooglePlay)).Select(s => s.Id).ToListAsync(ct));
        return rows.ToHashSet();
    }

    private async Task<Loaded> LoadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var signups = (await db.GrowthEvents.AsNoTracking().Where(e => e.Name == GrowthEventNames.Signup && e.UserId != null)
                .Select(e => new { UserId = e.UserId!.Value, e.OccurredAt }).ToListAsync(ct))
            .Where(e => e.OccurredAt >= from && e.OccurredAt < to).GroupBy(e => e.UserId).Select(g => new { UserId = g.Key, At = g.Min(x => x.OccurredAt) }).ToList();
        var campaigns = await db.MarketingCampaigns.AsNoTracking().ToListAsync(ct);
        var profile = await fiscal.GetProfileAsync(ct);
        var people = signups.ToDictionary(s => s.UserId, s => new Person { Id = s.UserId, SignupAt = s.At });
        if (people.Count == 0) return new Loaded([], campaigns, profile);
        var ids = people.Keys.ToList();

        var referred = (await InChunksAsync(ids, chunk => db.ReferralRelationships.AsNoTracking()
            .Where(r => chunk.Contains(r.ReferredUserId)).Select(r => r.ReferredUserId).ToListAsync(ct))).ToHashSet();
        var bySlug = campaigns.ToDictionary(c => c.Id);
        var attributions = await InChunksAsync(ids, chunk => db.CampaignAttributions.AsNoTracking()
            .Where(a => chunk.Contains(a.UserId)).Select(a => new { a.UserId, a.CampaignId }).ToListAsync(ct));
        foreach (var a in attributions)
            if (bySlug.TryGetValue(a.CampaignId, out var c)) { people[a.UserId].Channel = c.Channel; people[a.UserId].CampaignId = c.Id; people[a.UserId].Slug = c.Slug; }
        foreach (var id in referred) { var p = people[id]; p.Channel = CampaignChannels.Referral; p.CampaignId = null; p.Slug = null; }

        string[] names = [GrowthEventNames.EmailVerified, GrowthEventNames.OnboardingCompleted, GrowthEventNames.TrialStarted, GrowthEventNames.FirstPurchase,
            GrowthEventNames.Renewed, GrowthEventNames.Canceled, GrowthEventNames.Refunded];
        var events = await InChunksAsync(ids, chunk => db.GrowthEvents.AsNoTracking()
            .Where(e => e.UserId != null && chunk.Contains(e.UserId.Value) && names.Contains(e.Name))
            .Select(e => new { UserId = e.UserId!.Value, e.Name, e.OccurredAt, e.DedupeKey }).ToListAsync(ct));
        var excluded = await ExcludedSubscriptionsAsync(events.Select(e => SubscriptionOf(e.DedupeKey)).OfType<Guid>().Distinct().ToList(), ct);
        var realEvents = events.Where(e => SubscriptionOf(e.DedupeKey) is not { } s || !excluded.Contains(s)).ToList();

        var subIds = realEvents.Select(e => SubscriptionOf(e.DedupeKey)).OfType<Guid>().Distinct().ToList();
        var spans = (await InChunksAsync(subIds, chunk => db.Subscriptions.AsNoTracking().Where(s => chunk.Contains(s.Id))
            .Select(s => new { s.Id, s.StartedAt, s.ExpiresAt }).ToListAsync(ct))).ToDictionary(s => s.Id, s => (s.ExpiresAt - s.StartedAt).TotalDays);

        foreach (var e in realEvents)
        {
            var p = people[e.UserId];
            switch (e.Name)
            {
                case GrowthEventNames.EmailVerified: p.Verified = true; break;
                case GrowthEventNames.OnboardingCompleted: p.Onboarded = true; break;
                case GrowthEventNames.TrialStarted: p.Trial = true; break;
                case GrowthEventNames.FirstPurchase: p.Paid = true; break;
                case GrowthEventNames.Renewed: p.Renewals++; break;
                case GrowthEventNames.Canceled: p.Canceled = true; break;
                case GrowthEventNames.Refunded: p.Refunded = true; break;
            }
            if (e.Name is GrowthEventNames.FirstPurchase or GrowthEventNames.Renewed or GrowthEventNames.Refunded && SubscriptionOf(e.DedupeKey) is { } sub)
                p.Sales.Add(new SaleObservation(sub, e.Name, e.OccurredAt, spans.GetValueOrDefault(sub)));
        }

        // Derived from the data itself, so it also covers accounts created before the events existed.
        foreach (var id in await InChunksAsync(ids, chunk => db.TrainingPlans.AsNoTracking().Where(p => chunk.Contains(p.UserId)).Select(p => p.UserId).Distinct().ToListAsync(ct)))
            people[id].HasPlan = true;
        var logs = await InChunksAsync(ids, chunk => db.WorkoutLogs.AsNoTracking()
            .Where(l => chunk.Contains(l.UserId) && l.Outcome != WorkoutOutcome.NotCompleted).Select(l => new { l.UserId, l.PerformedAt }).ToListAsync(ct));
        foreach (var l in logs) people[l.UserId].Workouts.Add(l.PerformedAt);
        return new Loaded(people.Values.ToList(), campaigns, profile);
    }

    // ------------------------------------------------------------------ rows

    private static double? Rate(int part, int whole) => whole == 0 ? null : Math.Round(part / (double)whole, 4);
    private static decimal? Per(decimal spend, int count) => spend > 0 && count > 0 ? Math.Round(spend / count, 2) : null;

    /// <summary>Wilson 95% interval: how far a rate measured on few people can be trusted.</summary>
    public static (double? Low, double? High) Wilson(int hits, int total)
    {
        if (total == 0) return (null, null);
        const double z = 1.96;
        var p = hits / (double)total;
        var denom = 1 + z * z / total;
        var centre = (p + z * z / (2 * total)) / denom;
        var half = z * Math.Sqrt(p * (1 - p) / total + z * z / (4.0 * total * total)) / denom;
        return (Math.Round(Math.Max(0, centre - half), 4), Math.Round(Math.Min(1, centre + half), 4));
    }

    private static decimal Revenue(IEnumerable<Person> people, FiscalProfile profile)
    {
        var sales = people.SelectMany(p => p.Sales).ToList();
        return sales.Count == 0 ? 0 : FiscalEstimator.Estimate(sales, DateTimeOffset.MinValue, DateTimeOffset.MaxValue, profile).Income;
    }

    private SegmentRow Row(string kind, string key, string name, string? channel, string? status, IReadOnlyList<Person> people,
        int clicks, int installs, decimal spend, FiscalProfile profile)
    {
        var now = clock.GetUtcNow();
        var signups = people.Count;
        var paid = people.Count(p => p.Paid);
        var trials = people.Count(p => p.Trial);
        var renewed = people.Count(p => p.Renewals > 0);
        var activated = people.Count(p => p.Workouts.Count > 0);
        var d7Eligible = people.Where(p => p.SignupAt.AddDays(14) <= now).ToList();
        var d7Active = d7Eligible.Count(p => p.Workouts.Any(w => w >= p.SignupAt.AddDays(7) && w < p.SignupAt.AddDays(14)));
        var d30Eligible = people.Where(p => p.SignupAt.AddDays(37) <= now).ToList();
        var d30Active = d30Eligible.Count(p => p.Workouts.Any(w => w >= p.SignupAt.AddDays(30) && w < p.SignupAt.AddDays(37)));
        var revenue = Revenue(people, profile);
        var (low, high) = Wilson(paid, signups);
        var trialAndPaid = people.Count(p => p.Trial && p.Paid);
        return new SegmentRow(kind, key, name, channel, status,
            clicks, installs, signups, people.Count(p => p.Verified), people.Count(p => p.Onboarded), activated, trials, paid, renewed,
            people.Count(p => p.Canceled), people.Count(p => p.Refunded), d7Eligible.Count, d7Active, d30Eligible.Count, d30Active, spend, revenue,
            Rate(installs, clicks), Rate(signups, installs), Rate(activated, signups), Rate(trials, signups), Rate(paid, signups), low, high,
            Rate(trialAndPaid, trials), Rate(renewed, paid), Rate(d7Active, d7Eligible.Count), Rate(d30Active, d30Eligible.Count),
            Per(spend, installs), Per(spend, signups), Per(spend, trials), Per(spend, paid),
            spend > 0 ? Math.Round((double)((revenue - spend) / spend), 4) : null, signups >= MinReliable);
    }

    private sealed record Traffic(Dictionary<Guid, int> Clicks, Dictionary<Guid, int> Installs, Dictionary<Guid, decimal> Spend);

    private async Task<Traffic> TrafficAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var a = DateOnly.FromDateTime(from.UtcDateTime);
        var b = DateOnly.FromDateTime(to.UtcDateTime);
        var stats = await db.CampaignDailyStats.AsNoTracking().Where(s => s.Day >= a && s.Day < b).ToListAsync(ct);
        var spend = await db.CampaignSpends.AsNoTracking().Where(s => s.Day >= a && s.Day < b).Select(s => new { s.CampaignId, s.Amount }).ToListAsync(ct);
        return new Traffic(
            stats.GroupBy(s => s.CampaignId).ToDictionary(g => g.Key, g => g.Sum(x => x.Clicks)),
            stats.GroupBy(s => s.CampaignId).ToDictionary(g => g.Key, g => g.Sum(x => x.Installs)),
            spend.GroupBy(s => s.CampaignId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)));
    }

    public static string ChannelName(string channel) => channel switch
    {
        CampaignChannels.Organic => "Organic (no campaign)", CampaignChannels.Referral => "Referral programme",
        "ads" => "Paid ads", "social" => "Social media", "influencer" => "Influencers", "email" => "E-mail", "partner" => "Partners",
        "content" => "Content / SEO", "press" => "Press", "event" => "Events", _ => "Other"
    };

    public async Task<StrategiesReport> StrategiesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var data = await LoadAsync(from, to, ct);
        var traffic = await TrafficAsync(from, to, ct);
        int Sum(Dictionary<Guid, int> d, IEnumerable<Guid> ids) => ids.Sum(i => d.GetValueOrDefault(i));
        decimal SumM(IEnumerable<Guid> ids) => ids.Sum(i => traffic.Spend.GetValueOrDefault(i));

        var allIds = data.Campaigns.Select(c => c.Id).ToList();
        var overall = Row("overall", "all", "All accounts", null, null, data.People, Sum(traffic.Clicks, allIds), Sum(traffic.Installs, allIds), SumM(allIds), data.Profile);

        var channels = new List<SegmentRow>
        {
            Row("channel", CampaignChannels.Organic, ChannelName(CampaignChannels.Organic), CampaignChannels.Organic, null,
                data.People.Where(p => p.Channel == CampaignChannels.Organic).ToList(), 0, 0, 0, data.Profile),
            Row("channel", CampaignChannels.Referral, ChannelName(CampaignChannels.Referral), CampaignChannels.Referral, null,
                data.People.Where(p => p.Channel == CampaignChannels.Referral).ToList(), 0, 0, 0, data.Profile)
        };
        foreach (var channel in CampaignChannels.All)
        {
            var ids = data.Campaigns.Where(c => c.Channel == channel).Select(c => c.Id).ToList();
            var people = data.People.Where(p => p.Channel == channel).ToList();
            if (ids.Count == 0 && people.Count == 0) continue;
            channels.Add(Row("channel", $"channel:{channel}", ChannelName(channel), channel, null, people, Sum(traffic.Clicks, ids), Sum(traffic.Installs, ids), SumM(ids), data.Profile));
        }

        var campaigns = new List<SegmentRow>();
        foreach (var c in data.Campaigns)
        {
            var people = data.People.Where(p => p.CampaignId == c.Id).ToList();
            var clicks = traffic.Clicks.GetValueOrDefault(c.Id);
            var spend = traffic.Spend.GetValueOrDefault(c.Id);
            if (c.Status == CampaignStatuses.Archived && people.Count == 0 && clicks == 0 && spend == 0) continue;
            campaigns.Add(Row("campaign", $"campaign:{c.Slug}", c.Name, c.Channel, c.Status, people, clicks, traffic.Installs.GetValueOrDefault(c.Id), spend, data.Profile));
        }
        campaigns = campaigns.OrderByDescending(r => r.Paid).ThenByDescending(r => r.Signups).ToList();
        return new StrategiesReport(from, to, MinReliable, overall, channels, campaigns, Insights(overall, channels, campaigns));
    }

    // ------------------------------------------------------------------ insights

    private static string P(double? v) => v is null ? "n/a" : (v.Value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    private static string E(decimal? v) => v is null ? "n/a" : v.Value.ToString("0.00", CultureInfo.InvariantCulture) + " €";

    public static List<Insight> Insights(SegmentRow overall, IReadOnlyList<SegmentRow> channels, IReadOnlyList<SegmentRow> campaigns)
    {
        var list = new List<Insight>();
        var comparable = channels.Where(c => c.Key is CampaignChannels.Organic or CampaignChannels.Referral).Concat(campaigns).ToList();
        var reliable = comparable.Where(r => r.Reliable).ToList();
        if (overall.Signups == 0)
        {
            list.Add(new Insight("info", "No signups in the period", "Widen the date range, or publish a campaign link (Campaigns tab) to start measuring where people come from."));
            return list;
        }
        if (reliable.Count == 0)
            list.Add(new Insight("info", "Not enough data to rank strategies yet",
                $"A strategy needs at least {MinReliable} signups in the period before its rates mean much. Rates below are shown with their confidence range; do not decide on them yet."));

        var bestPaid = reliable.Where(r => r.Paid > 0).OrderByDescending(r => r.SignupToPaid).FirstOrDefault();
        if (bestPaid is not null && reliable.Count > 1)
            list.Add(new Insight("good", $"Best at turning signups into payers: {bestPaid.Name}",
                $"{P(bestPaid.SignupToPaid)} of its {bestPaid.Signups} signups paid (range {P(bestPaid.SignupToPaidLow)}–{P(bestPaid.SignupToPaidHigh)}), against {P(overall.SignupToPaid)} for everybody."));

        var bestKeep = reliable.Where(r => r.D30Eligible >= MinReliable).OrderByDescending(r => r.D30).FirstOrDefault();
        if (bestKeep is not null && reliable.Count > 1)
            list.Add(new Insight("good", $"Best at keeping people training: {bestKeep.Name}",
                $"{P(bestKeep.D30)} of its people trained again 30 days after signing up (overall {P(overall.D30)}). Quality of the audience matters more than volume."));

        var cheapest = comparable.Where(r => r.CostPerPaid is not null).OrderBy(r => r.CostPerPaid).FirstOrDefault();
        if (cheapest is not null)
            list.Add(new Insight("good", $"Cheapest paying customer: {cheapest.Name}",
                $"{E(cheapest.CostPerPaid)} per paying customer ({E(cheapest.CostPerSignup)} per signup). Estimated return on spend: {P(cheapest.ReturnOnSpend)}."));

        foreach (var leak in campaigns.Where(r => r.Spend > 0 && r.Paid == 0 && r.Signups >= MinReliable))
            list.Add(new Insight("warn", $"Spend without payers: {leak.Name}",
                $"{E(leak.Spend)} spent and {leak.Signups} signups, none has paid. Review the audience or the offer before spending more."));

        foreach (var weak in campaigns.Where(r => r.Clicks >= 50 && r.Signups > 0 && r.ClickToInstall is < 0.1))
            list.Add(new Insight("warn", $"Few clicks become installs: {weak.Name}",
                $"Only {P(weak.ClickToInstall)} of {weak.Clicks} link openings ended in an attributable install. Check the landing of the ad and that the link opens Google Play."));

        foreach (var trial in reliable.Where(r => r.Trials >= 10 && r.TrialToPaid is not null && overall.TrialToPaid is not null && r.TrialToPaid < overall.TrialToPaid / 2))
            list.Add(new Insight("warn", $"Trials that do not convert: {trial.Name}",
                $"Only {P(trial.TrialToPaid)} of its {trial.Trials} trials paid (overall {P(overall.TrialToPaid)}). The audience may like the free period more than the product."));

        var refundHeavy = reliable.Where(r => r.Paid >= 5 && r.Refunded > 0 && r.Refunded / (double)r.Paid >= 0.2).ToList();
        foreach (var refund in refundHeavy)
            list.Add(new Insight("warn", $"High refund rate: {refund.Name}", $"{refund.Refunded} of {refund.Paid} payers asked for a refund."));
        return list;
    }

    // ------------------------------------------------------------------ funnel and retention

    private static string SegmentName(string key, IReadOnlyList<MarketingCampaign> campaigns) => key switch
    {
        "all" => "All accounts",
        CampaignChannels.Organic => ChannelName(CampaignChannels.Organic),
        CampaignChannels.Referral => ChannelName(CampaignChannels.Referral),
        _ when key.StartsWith("channel:", StringComparison.Ordinal) => ChannelName(key[8..]),
        _ when key.StartsWith("campaign:", StringComparison.Ordinal) => campaigns.FirstOrDefault(c => c.Slug == key[9..])?.Name ?? key[9..],
        _ => key
    };

    /// <summary>"all", "organic", "referral", "channel:ads" or "campaign:spring-ads"; anything else is rejected.</summary>
    public static string NormalizeSegment(string? key)
    {
        var k = string.IsNullOrWhiteSpace(key) ? "all" : key.Trim().ToLowerInvariant();
        if (k is "all" or CampaignChannels.Organic or CampaignChannels.Referral) return k;
        if (k.StartsWith("channel:", StringComparison.Ordinal) && CampaignChannels.All.Contains(k[8..])) return k;
        if (k.StartsWith("campaign:", StringComparison.Ordinal) && CampaignService.NormalizeSlug(k[9..]) is not null) return k;
        throw new MarketingException(400, "validation", "segment: use all, organic, referral, channel:<type> or campaign:<link name>.");
    }

    private static bool InSegment(Person p, string key) => key switch
    {
        "all" => true,
        CampaignChannels.Organic or CampaignChannels.Referral => p.Channel == key,
        _ when key.StartsWith("channel:", StringComparison.Ordinal) => p.Channel == key[8..],
        _ => p.Slug == key[9..]
    };

    public async Task<MarketingFunnel> FunnelAsync(DateTimeOffset from, DateTimeOffset to, string? segment, CancellationToken ct)
    {
        var key = NormalizeSegment(segment);
        var data = await LoadAsync(from, to, ct);
        var people = data.People.Where(p => InSegment(p, key)).ToList();
        var stages = new (string Name, int Count)[]
        {
            (GrowthEventNames.Signup, people.Count), (GrowthEventNames.EmailVerified, people.Count(p => p.Verified)),
            (GrowthEventNames.OnboardingCompleted, people.Count(p => p.Onboarded)), (GrowthEventNames.FirstPlan, people.Count(p => p.HasPlan)),
            (GrowthEventNames.FirstWorkout, people.Count(p => p.Workouts.Count > 0)), (GrowthEventNames.TrialStarted, people.Count(p => p.Trial)),
            (GrowthEventNames.FirstPurchase, people.Count(p => p.Paid)), (GrowthEventNames.Renewed, people.Count(p => p.Renewals > 0)),
            (GrowthEventNames.Canceled, people.Count(p => p.Canceled)), (GrowthEventNames.Refunded, people.Count(p => p.Refunded))
        };
        var rows = new List<FunnelStageRow>();
        var previous = people.Count;
        foreach (var (name, count) in stages)
        {
            rows.Add(new FunnelStageRow(name, count, Rate(count, previous), Rate(count, people.Count)));
            if (name is not (GrowthEventNames.Canceled or GrowthEventNames.Refunded)) previous = count;
        }
        var paid = people.Count(p => p.Paid);
        var trials = people.Count(p => p.Trial);
        var (low, high) = Wilson(paid, people.Count);
        return new MarketingFunnel(from, to, key, SegmentName(key, data.Campaigns), people.Count, rows, Rate(paid, people.Count), low, high,
            Rate(people.Count(p => p.Trial && p.Paid), trials), Rate(people.Count(p => p.Renewals > 0), paid));
    }

    private static string Week(DateTimeOffset when)
    {
        var date = when.UtcDateTime.Date;
        return date.AddDays(-(((int)date.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public async Task<RetentionReport> RetentionAsync(DateTimeOffset from, DateTimeOffset to, string? segment, CancellationToken ct)
    {
        var key = NormalizeSegment(segment);
        var data = await LoadAsync(from, to, ct);
        var people = data.People.Where(p => InSegment(p, key)).ToList();
        var now = clock.GetUtcNow();

        double?[] Active(IReadOnlyList<Person> group) => Enumerable.Range(0, RetentionWeeks + 1).Select(k =>
        {
            var eligible = group.Where(p => p.SignupAt.AddDays(7 * (k + 1)) <= now).ToList();
            var active = eligible.Count(p => p.Workouts.Any(w => w >= p.SignupAt.AddDays(7 * k) && w < p.SignupAt.AddDays(7 * (k + 1))));
            return Rate(active, eligible.Count);
        }).ToArray();

        var cohorts = people.GroupBy(p => Week(p.SignupAt)).OrderBy(g => g.Key)
            .Select(g => new RetentionCohort(g.Key, g.Count(), g.Count(p => p.Paid), g.Count(p => p.Renewals > 0), Active(g.ToList()))).ToList();
        return new RetentionReport(from, to, key, SegmentName(key, data.Campaigns), RetentionWeeks, people.Count, Active(people), cohorts);
    }

    // ------------------------------------------------------------------ summary

    private async Task<List<(string Name, DateTimeOffset At)>> RealEventsAsync(string[] names, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = (await db.GrowthEvents.AsNoTracking().Where(e => names.Contains(e.Name)).Select(e => new { e.Name, e.OccurredAt, e.DedupeKey }).ToListAsync(ct))
            .Where(e => e.OccurredAt >= from && e.OccurredAt < to).ToList();
        var excluded = await ExcludedSubscriptionsAsync(rows.Select(e => SubscriptionOf(e.DedupeKey)).OfType<Guid>().Distinct().ToList(), ct);
        return rows.Where(e => SubscriptionOf(e.DedupeKey) is not { } s || !excluded.Contains(s)).Select(e => (e.Name, e.OccurredAt)).ToList();
    }

    public async Task<SummaryReport> SummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var span = to - from;
        var previousFrom = from - span;
        string[] names = [GrowthEventNames.Signup, GrowthEventNames.TrialStarted, GrowthEventNames.FirstPurchase, GrowthEventNames.Renewed,
            GrowthEventNames.Canceled, GrowthEventNames.Refunded];
        var current = await RealEventsAsync(names, from, to, ct);
        var previous = await RealEventsAsync(names, previousFrom, from, ct);
        int N(List<(string Name, DateTimeOffset At)> list, string name) => list.Count(e => e.Name == name);
        var kpis = new List<Kpi>
        {
            new("signups", "New accounts", N(current, GrowthEventNames.Signup), N(previous, GrowthEventNames.Signup)),
            new("trials", "Trials started", N(current, GrowthEventNames.TrialStarted), N(previous, GrowthEventNames.TrialStarted)),
            new("paid", "New paying customers", N(current, GrowthEventNames.FirstPurchase), N(previous, GrowthEventNames.FirstPurchase)),
            new("renewals", "Renewals", N(current, GrowthEventNames.Renewed), N(previous, GrowthEventNames.Renewed)),
            new("canceled", "Cancellations", N(current, GrowthEventNames.Canceled), N(previous, GrowthEventNames.Canceled)),
            new("refunds", "Refunds", N(current, GrowthEventNames.Refunded), N(previous, GrowthEventNames.Refunded))
        };

        var data = await LoadAsync(from, to, ct);
        var paid = data.People.Count(p => p.Paid);
        var trials = data.People.Count(p => p.Trial);
        var mix = data.People.GroupBy(p => p.Channel).Select(g => new MixRow(g.Key, ChannelName(g.Key), g.Count(), g.Count(p => p.Paid)))
            .OrderByDescending(m => m.Signups).ToList();

        var a = DateOnly.FromDateTime(from.UtcDateTime);
        var b = DateOnly.FromDateTime(to.UtcDateTime);
        var paywall = await db.PaywallViews.AsNoTracking().Where(v => v.Day >= a && v.Day < b).ToListAsync(ct);
        var paywallByWeek = paywall.GroupBy(v => Week(new DateTimeOffset(v.Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))).ToDictionary(g => g.Key, g => g.Sum(v => v.Views));
        var weeks = current.GroupBy(e => Week(e.At)).Select(g => g.Key).Union(paywallByWeek.Keys).OrderBy(w => w)
            .Select(w =>
            {
                int C(string name) => current.Count(e => e.Name == name && Week(e.At) == w);
                return new WeekPoint(w, C(GrowthEventNames.Signup), C(GrowthEventNames.TrialStarted), C(GrowthEventNames.FirstPurchase),
                    C(GrowthEventNames.Renewed), C(GrowthEventNames.Canceled), C(GrowthEventNames.Refunded), paywallByWeek.GetValueOrDefault(w));
            }).ToList();

        var now = clock.GetUtcNow();
        var live = await db.Subscriptions.AsNoTracking().Where(s => !s.IsTestPurchase && s.Source == SubscriptionSources.GooglePlay)
            .Select(s => new { s.Status, s.ExpiresAt, s.PaidAt }).ToListAsync(ct);
        var subscribers = new SubscribersNow(
            live.Count(s => s.Status is SubscriptionStatus.Active or SubscriptionStatus.GracePeriod && s.ExpiresAt > now && s.PaidAt != null),
            live.Count(s => s.Status == SubscriptionStatus.Trial && s.ExpiresAt > now),
            live.Count(s => s.Status == SubscriptionStatus.Canceled && s.ExpiresAt > now),
            live.Count(s => s.Status is SubscriptionStatus.GracePeriod or SubscriptionStatus.OnHold));
        var confirmed = await db.WaitlistEntries.AsNoTracking().CountAsync(w => w.ConfirmedAt != null, ct);
        var active = data.Campaigns.Count(c => c.Status == CampaignStatuses.Active);
        return new SummaryReport(from, to, kpis, Rate(trials, data.People.Count), Rate(paid, data.People.Count),
            Rate(data.People.Count(p => p.Trial && p.Paid), trials), data.People.Count, paywall.Sum(v => v.Views), subscribers, mix, weeks, confirmed, active);
    }

    // ------------------------------------------------------------------ paywall, waitlist

    public async Task<PaywallReport> PaywallAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var a = DateOnly.FromDateTime(from.UtcDateTime);
        var b = DateOnly.FromDateTime(to.UtcDateTime);
        var rows = await db.PaywallViews.AsNoTracking().Where(v => v.Day >= a && v.Day < b).ToListAsync(ct);
        var total = rows.Sum(r => r.Views);
        var sources = PaywallSources.All.Select(s =>
        {
            var views = rows.Where(r => r.Source == s).Sum(r => r.Views);
            return new PaywallSourceRow(s, views, total == 0 ? 0 : Math.Round(views / (double)total, 4));
        }).OrderByDescending(s => s.Views).ToList();
        var weeks = rows.GroupBy(r => Week(new DateTimeOffset(r.Day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero))).OrderBy(g => g.Key)
            .Select(g => new PaywallWeekRow(g.Key, g.Sum(r => r.Views), g.GroupBy(r => r.Source).ToDictionary(x => x.Key, x => x.Sum(r => r.Views)))).ToList();
        var purchases = (await RealEventsAsync([GrowthEventNames.FirstPurchase], from, to, ct)).Count;
        return new PaywallReport(from, to, total, purchases, purchases == 0 ? null : Math.Round(total / (double)purchases, 1), sources, weeks);
    }

    public async Task<WaitlistReport> WaitlistAsync(CancellationToken ct)
    {
        var rows = await db.WaitlistEntries.AsNoTracking().Select(w => new { w.Language, w.CreatedAt, w.ConfirmedAt, w.InvitedAt }).ToListAsync(ct);
        var confirmed = rows.Count(r => r.ConfirmedAt != null);
        var invited = rows.Count(r => r.InvitedAt != null);
        var weeks = rows.GroupBy(r => Week(r.CreatedAt)).OrderByDescending(g => g.Key).Take(12).OrderBy(g => g.Key).Select(g => new WaitlistWeek(g.Key, g.Count())).ToList();
        return new WaitlistReport(rows.Count, confirmed, invited, Rate(confirmed, rows.Count), Rate(invited, confirmed),
            rows.GroupBy(r => r.Language).ToDictionary(g => g.Key, g => g.Count()), weeks);
    }

    // ------------------------------------------------------------------ one campaign, export

    public async Task<CampaignDetail> CampaignAsync(Guid id, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var campaign = await db.MarketingCampaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new MarketingException(404, "not_found", "Campaign not found.");
        var report = await StrategiesAsync(from, to, ct);
        var row = report.Campaigns.FirstOrDefault(r => r.Key == $"campaign:{campaign.Slug}")
            ?? Row("campaign", $"campaign:{campaign.Slug}", campaign.Name, campaign.Channel, campaign.Status, [], 0, 0, 0, FiscalProfile.Default);

        var a = DateOnly.FromDateTime(from.UtcDateTime);
        var b = DateOnly.FromDateTime(to.UtcDateTime);
        var stats = await db.CampaignDailyStats.AsNoTracking().Where(s => s.CampaignId == id && s.Day >= a && s.Day < b).ToListAsync(ct);
        var spend = await db.CampaignSpends.AsNoTracking().Where(s => s.CampaignId == id && s.Day >= a && s.Day < b).ToListAsync(ct);
        var signupDays = (await db.CampaignAttributions.AsNoTracking().Where(x => x.CampaignId == id).Select(x => x.AttributedAt).ToListAsync(ct))
            .Where(t => t >= from && t < to).GroupBy(t => DateOnly.FromDateTime(t.UtcDateTime)).ToDictionary(g => g.Key, g => g.Count());
        var days = stats.Select(s => s.Day).Union(spend.Select(s => s.Day)).Union(signupDays.Keys).OrderBy(d => d)
            .Select(d => new CampaignDay(d, stats.Where(s => s.Day == d).Sum(s => s.Clicks), stats.Where(s => s.Day == d).Sum(s => s.Installs),
                signupDays.GetValueOrDefault(d), spend.Where(s => s.Day == d).Sum(s => s.Amount))).ToList();

        var spendRows = await db.CampaignSpends.AsNoTracking().Where(s => s.CampaignId == id).ToListAsync(ct);
        var all = (await db.CampaignDailyStats.AsNoTracking().Where(s => s.CampaignId == id).ToListAsync(ct));
        var signups = await db.CampaignAttributions.AsNoTracking().CountAsync(x => x.CampaignId == id, ct);
        var view = new CampaignView(campaign.Id, campaign.Slug, campaign.Name, campaign.Channel, campaign.Status, campaign.StartsOn, campaign.EndsOn,
            campaign.Notes, campaign.CreatedAt, all.Sum(s => s.Clicks), all.Sum(s => s.Installs), signups, spendRows.Sum(s => s.Amount));
        var spendViews = spendRows.OrderByDescending(s => s.Day).ThenByDescending(s => s.CreatedAt)
            .Select(s => new SpendView(s.Id, s.Day, s.Amount, s.Note, s.CreatedBy)).ToList();
        return new CampaignDetail(view, row, days, spendViews);
    }

    public async Task<string> StrategiesCsvAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var report = await StrategiesAsync(from, to, ct);
        var sb = new StringBuilder();
        sb.AppendLine("kind,key,name,channel,status,clicks,installs,signups,verified,onboarded,activated,trials,paid,renewed,canceled,refunded,"
            + "signup_to_trial,signup_to_paid,signup_to_paid_low,signup_to_paid_high,trial_to_paid,paid_renewed,d7_retention,d30_retention,"
            + "spend_eur,estimated_net_revenue_eur,cost_per_signup_eur,cost_per_paid_eur,return_on_spend,reliable");
        foreach (var r in new[] { report.Overall }.Concat(report.Channels).Concat(report.Campaigns))
            sb.AppendLine(string.Join(',', new[]
            {
                Csv(r.Kind), Csv(r.Key), Csv(r.Name), Csv(r.Channel), Csv(r.Status), r.Clicks.ToString(), r.Installs.ToString(), r.Signups.ToString(),
                r.Verified.ToString(), r.Onboarded.ToString(), r.Activated.ToString(), r.Trials.ToString(), r.Paid.ToString(), r.Renewed.ToString(),
                r.Canceled.ToString(), r.Refunded.ToString(), Num(r.SignupToTrial), Num(r.SignupToPaid), Num(r.SignupToPaidLow), Num(r.SignupToPaidHigh),
                Num(r.TrialToPaid), Num(r.PaidRenewed), Num(r.D7), Num(r.D30), Num(r.Spend), Num(r.EstimatedRevenue), Num(r.CostPerSignup),
                Num(r.CostPerPaid), Num(r.ReturnOnSpend), r.Reliable ? "yes" : "no"
            }));
        return sb.ToString();
    }

    private static string Num(double? v) => v?.ToString("0.####", CultureInfo.InvariantCulture) ?? "";
    private static string Num(decimal? v) => v?.ToString("0.00", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Quotes a CSV field and defuses spreadsheet formulas (campaign names are typed by people).</summary>
    public static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var text = value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
        return text.Contains(',') || text.Contains('"') || text.Contains('\n') ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }
}
