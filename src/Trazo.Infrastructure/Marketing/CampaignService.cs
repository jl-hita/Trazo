using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Growth;
using Trazo.Domain.Marketing;
using Trazo.Infrastructure.Growth;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Marketing;

/// <summary>A rejected marketing request: the HTTP status, a stable code and a message for the console.</summary>
public sealed class MarketingException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed record CampaignInput(string? Slug, string? Name, string? Channel, string? Status, DateOnly? StartsOn, DateOnly? EndsOn, string? Notes);
public sealed record SpendInput(DateOnly? Day, decimal? Amount, string? Note);

public sealed record CampaignView(
    Guid Id, string Slug, string Name, string Channel, string Status, DateOnly? StartsOn, DateOnly? EndsOn, string? Notes,
    DateTimeOffset CreatedAt, int Clicks, int Installs, int Signups, decimal Spend);

public sealed record SpendView(Guid Id, DateOnly Day, decimal Amount, string? Note, string? CreatedBy);

public interface ICampaignService
{
    Task<IReadOnlyList<CampaignView>> ListAsync(CancellationToken ct);
    Task<CampaignView> CreateAsync(CampaignInput input, string actor, CancellationToken ct);
    Task<CampaignView> UpdateAsync(Guid id, CampaignInput input, CancellationToken ct);
    Task<IReadOnlyList<SpendView>> SpendAsync(Guid campaignId, CancellationToken ct);
    Task<SpendView> AddSpendAsync(Guid campaignId, SpendInput input, string actor, CancellationToken ct);
    Task DeleteSpendAsync(Guid campaignId, Guid spendId, CancellationToken ct);

    /// <summary>The campaign with that slug, or null when the text cannot be a slug or nothing uses it.</summary>
    Task<MarketingCampaign?> FindAsync(string? slug, CancellationToken ct);
    /// <summary>Counts one opening of the campaign link. Returns the campaign, or null when the slug is unknown.</summary>
    Task<MarketingCampaign?> RecordClickAsync(string? slug, CancellationToken ct);
    /// <summary>Counts one install that the Play referrer attributed to the campaign.</summary>
    Task<bool> RecordInstallAsync(string? slug, CancellationToken ct);
    /// <summary>Links a newly created account to the campaign it came from. Only called when the account is created.</summary>
    Task<bool> AttributeAsync(Guid newUserId, string? slug, string? source, CancellationToken ct);
}

public sealed partial class CampaignService(TrazoDbContext db, IGrowthEvents events, TimeProvider clock, ILogger<CampaignService> logger) : ICampaignService
{

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,30}[a-z0-9]$")]
    private static partial Regex SlugPattern();

    public static string? NormalizeSlug(string? raw)
    {
        var slug = raw?.Trim().ToLowerInvariant();
        return slug is not null && SlugPattern().IsMatch(slug) ? slug : null;
    }

    public async Task<IReadOnlyList<CampaignView>> ListAsync(CancellationToken ct)
    {
        var campaigns = await db.MarketingCampaigns.AsNoTracking().ToListAsync(ct);
        var stats = await db.CampaignDailyStats.AsNoTracking().GroupBy(s => s.CampaignId)
            .Select(g => new { Id = g.Key, Clicks = g.Sum(x => x.Clicks), Installs = g.Sum(x => x.Installs) }).ToListAsync(ct);
        var signups = await db.CampaignAttributions.AsNoTracking().GroupBy(a => a.CampaignId)
            .Select(g => new { Id = g.Key, Count = g.Count() }).ToListAsync(ct);
        var spend = (await db.CampaignSpends.AsNoTracking().Select(s => new { s.CampaignId, s.Amount }).ToListAsync(ct))
            .GroupBy(s => s.CampaignId).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        return campaigns.OrderBy(c => c.Status == CampaignStatuses.Archived).ThenByDescending(c => c.CreatedAt).Select(c =>
        {
            var s = stats.FirstOrDefault(x => x.Id == c.Id);
            return View(c, s?.Clicks ?? 0, s?.Installs ?? 0, signups.FirstOrDefault(x => x.Id == c.Id)?.Count ?? 0, spend.GetValueOrDefault(c.Id));
        }).ToList();
    }

    private static CampaignView View(MarketingCampaign c, int clicks, int installs, int signups, decimal spend) =>
        new(c.Id, c.Slug, c.Name, c.Channel, c.Status, c.StartsOn, c.EndsOn, c.Notes, c.CreatedAt, clicks, installs, signups, spend);

    private static Dictionary<string, string[]> Validate(CampaignInput i, bool creating)
    {
        var errors = new Dictionary<string, string[]>();
        if (creating && NormalizeSlug(i.Slug) is null)
            errors["slug"] = ["3 to 32 characters: lower-case letters, digits and dashes, not starting or ending with a dash."];
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Trim().Length is < 2 or > 80) errors["name"] = ["2 to 80 characters."];
        if (i.Channel is null || !CampaignChannels.All.Contains(i.Channel)) errors["channel"] = [$"One of: {string.Join(", ", CampaignChannels.All)}."];
        if (i.Status is not null && !CampaignStatuses.All.Contains(i.Status)) errors["status"] = [$"One of: {string.Join(", ", CampaignStatuses.All)}."];
        if (i.Notes is { Length: > 500 }) errors["notes"] = ["At most 500 characters."];
        if (i.StartsOn is { } s && i.EndsOn is { } e && e < s) errors["endsOn"] = ["The end cannot be before the start."];
        return errors;
    }

    private static MarketingException Invalid(Dictionary<string, string[]> errors) =>
        new(400, "validation", string.Join(" ", errors.Select(e => $"{e.Key}: {e.Value[0]}")));

    public async Task<CampaignView> CreateAsync(CampaignInput input, string actor, CancellationToken ct)
    {
        var errors = Validate(input, creating: true);
        if (errors.Count > 0) throw Invalid(errors);
        var slug = NormalizeSlug(input.Slug)!;
        if (await db.MarketingCampaigns.AnyAsync(c => c.Slug == slug, ct)) throw new MarketingException(409, "slug_taken", "That link name is already used by another campaign.");
        var now = clock.GetUtcNow();
        var row = new MarketingCampaign
        {
            Id = Guid.NewGuid(), Slug = slug, Name = input.Name!.Trim(), Channel = input.Channel!, Status = input.Status ?? CampaignStatuses.Active,
            StartsOn = input.StartsOn, EndsOn = input.EndsOn, Notes = Clean(input.Notes), CreatedBy = Cut(actor, 60), CreatedAt = now, UpdatedAt = now
        };
        db.MarketingCampaigns.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.Entry(row).State = EntityState.Detached;
            throw new MarketingException(409, "slug_taken", "That link name is already used by another campaign.");
        }
        return View(row, 0, 0, 0, 0);
    }

    public async Task<CampaignView> UpdateAsync(Guid id, CampaignInput input, CancellationToken ct)
    {
        var errors = Validate(input, creating: false);
        if (errors.Count > 0) throw Invalid(errors);
        var row = await db.MarketingCampaigns.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new MarketingException(404, "not_found", "Campaign not found.");
        // The slug is not editable: the link is already published.
        row.Name = input.Name!.Trim();
        row.Channel = input.Channel!;
        row.Status = input.Status ?? row.Status;
        row.StartsOn = input.StartsOn;
        row.EndsOn = input.EndsOn;
        row.Notes = Clean(input.Notes);
        row.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == id);
    }

    public async Task<IReadOnlyList<SpendView>> SpendAsync(Guid campaignId, CancellationToken ct) =>
        (await db.CampaignSpends.AsNoTracking().Where(s => s.CampaignId == campaignId).ToListAsync(ct))
            .OrderByDescending(s => s.Day).ThenByDescending(s => s.CreatedAt).Select(s => new SpendView(s.Id, s.Day, s.Amount, s.Note, s.CreatedBy)).ToList();

    public async Task<SpendView> AddSpendAsync(Guid campaignId, SpendInput input, string actor, CancellationToken ct)
    {
        if (!await db.MarketingCampaigns.AnyAsync(c => c.Id == campaignId, ct)) throw new MarketingException(404, "not_found", "Campaign not found.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (input.Day is not { } day || day > today.AddDays(1) || day < today.AddYears(-3)) throw new MarketingException(400, "validation", "day: a date from the last three years up to today.");
        if (input.Amount is not { } amount || amount <= 0 || amount > 1_000_000m) throw new MarketingException(400, "validation", "amount: more than 0 and at most 1,000,000 euros.");
        if (input.Note is { Length: > 200 }) throw new MarketingException(400, "validation", "note: at most 200 characters.");
        var row = new CampaignSpend
        {
            Id = Guid.NewGuid(), CampaignId = campaignId, Day = day, Amount = Math.Round(amount, 2), Note = Clean(input.Note),
            CreatedBy = Cut(actor, 60), CreatedAt = clock.GetUtcNow()
        };
        db.CampaignSpends.Add(row);
        await db.SaveChangesAsync(ct);
        return new SpendView(row.Id, row.Day, row.Amount, row.Note, row.CreatedBy);
    }

    public async Task DeleteSpendAsync(Guid campaignId, Guid spendId, CancellationToken ct)
    {
        var removed = await db.CampaignSpends.Where(s => s.Id == spendId && s.CampaignId == campaignId).ExecuteDeleteAsync(ct);
        if (removed == 0) throw new MarketingException(404, "not_found", "Spend entry not found.");
    }

    public async Task<MarketingCampaign?> FindAsync(string? slug, CancellationToken ct)
    {
        var normalized = NormalizeSlug(slug);
        return normalized is null ? null : await db.MarketingCampaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Slug == normalized, ct);
    }

    public async Task<MarketingCampaign?> RecordClickAsync(string? slug, CancellationToken ct)
    {
        var campaign = await FindAsync(slug, ct);
        if (campaign is not null) await BumpAsync(campaign.Id, clicks: 1, installs: 0, ct);
        return campaign;
    }

    public async Task<bool> RecordInstallAsync(string? slug, CancellationToken ct)
    {
        var campaign = await FindAsync(slug, ct);
        if (campaign is null) return false;
        await BumpAsync(campaign.Id, clicks: 0, installs: 1, ct);
        return true;
    }

    private async Task BumpAsync(Guid campaignId, int clicks, int installs, CancellationToken ct)
    {
        var day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        Task<int> Bump() => db.CampaignDailyStats.Where(x => x.CampaignId == campaignId && x.Day == day)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Clicks, x => x.Clicks + clicks).SetProperty(x => x.Installs, x => x.Installs + installs), ct);
        if (await Bump() > 0) return;
        try
        {
            db.CampaignDailyStats.Add(new CampaignDailyStat { CampaignId = campaignId, Day = day, Clicks = clicks, Installs = installs });
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // another request created today's row first: add to it
            await Bump();
        }
    }

    public async Task<bool> AttributeAsync(Guid newUserId, string? slug, string? source, CancellationToken ct)
    {
        var campaign = await FindAsync(slug, ct);
        if (campaign is null) return false;
        // Attribution belongs to account creation: a user older than a few minutes is an existing account.
        var createdAt = await db.Users.AsNoTracking().Where(u => u.Id == newUserId).Select(u => u.CreatedAt).SingleOrDefaultAsync(ct);
        if (createdAt == default || clock.GetUtcNow() - createdAt > TimeSpan.FromMinutes(10)) return false;
        if (await db.CampaignAttributions.AsNoTracking().AnyAsync(a => a.UserId == newUserId, ct)) return false; // the first campaign stays
        var row = new CampaignAttribution
        {
            UserId = newUserId, CampaignId = campaign.Id, Source = CampaignSources.Normalize(source), AttributedAt = clock.GetUtcNow()
        };
        db.CampaignAttributions.Add(row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            db.Entry(row).State = EntityState.Detached; // already attributed: the first campaign stays
            logger.LogDebug(ex, "Campaign attribution was not stored.");
            return false;
        }
        await events.RecordAsync(GrowthEventNames.CampaignAttributed, newUserId,
            new GrowthEventInfo(Channel: "campaign", Reference: campaign.Slug, DedupeKey: $"campaign:{newUserId:N}"), ct);
        return true;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Cut(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;
}
