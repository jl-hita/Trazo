using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Marketing;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Marketing;

public sealed record DownloadsInput(DateOnly? Week, int? Downloads, string? Note);

public interface IDownloadsService
{
    /// <summary>Stores (or replaces) the downloads of the week that contains the given day.</summary>
    Task<DownloadWeekEntry> SaveAsync(DownloadsInput input, string actor, CancellationToken ct);
    Task DeleteAsync(DateOnly week, CancellationToken ct);
}

public sealed record DownloadWeekEntry(DateOnly Week, int Downloads, string? Note);

public sealed class DownloadsService(TrazoDbContext db, TimeProvider clock) : IDownloadsService
{
    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    public async Task<DownloadWeekEntry> SaveAsync(DownloadsInput input, string actor, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (input.Week is not { } day || day > today.AddDays(7) || day < today.AddYears(-3))
            throw new MarketingException(400, "validation", "week: a date from the last three years up to today.");
        if (input.Downloads is not { } downloads || downloads is < 0 or > 10_000_000)
            throw new MarketingException(400, "validation", "downloads: a whole number from 0 to 10,000,000.");
        if (input.Note is { Length: > 200 }) throw new MarketingException(400, "validation", "note: at most 200 characters.");
        var week = MondayOf(day);
        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        var by = actor.Length > 60 ? actor[..60] : actor;
        var row = await db.StoreDownloads.SingleOrDefaultAsync(x => x.Week == week, ct);
        if (row is null) db.StoreDownloads.Add(new StoreDownloads { Week = week, Downloads = downloads, Note = note, UpdatedBy = by, UpdatedAt = clock.GetUtcNow() });
        else { row.Downloads = downloads; row.Note = note; row.UpdatedBy = by; row.UpdatedAt = clock.GetUtcNow(); }
        await db.SaveChangesAsync(ct);
        return new DownloadWeekEntry(week, downloads, note);
    }

    public async Task DeleteAsync(DateOnly week, CancellationToken ct)
    {
        if (await db.StoreDownloads.Where(x => x.Week == week).ExecuteDeleteAsync(ct) == 0)
            throw new MarketingException(404, "not_found", "No downloads were entered for that week.");
    }
}
