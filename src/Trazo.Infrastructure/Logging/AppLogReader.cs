using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Logging;

/// <summary>Filters of the log viewer. <see cref="Levels"/> are the groups "info", "warning" and "error".</summary>
public sealed record AppLogFilter(
    IReadOnlySet<string> Levels, DateTimeOffset? From, DateTimeOffset? To, string? Text, string? Cursor, int PageSize);

public sealed record AppLogItem(
    long Id, DateTimeOffset Timestamp, string Level, string Category, string Message, string? Exception, string? TraceId);

public sealed record AppLogCounts(int Info, int Warning, int Error);

public sealed record AppLogCategoryCount(string Category, int Count, DateTimeOffset LastAt);

public sealed record AppLogPage(IReadOnlyList<AppLogItem> Items, string? NextCursor, AppLogCounts Counts);

/// <summary>
/// Reads <c>app_logs</c>, newest first. Pages are keyset pages over (timestamp, id): the cursor of a page is its last
/// entry, so paging stays correct while new entries keep arriving and costs the same on the first and the last page.
/// </summary>
public sealed class AppLogReader(TrazoDbContext db)
{
    public const int MaximumPageSize = 200;
    public static readonly IReadOnlyList<string> LevelGroups = ["info", "warning", "error"];

    // Microsoft.Extensions.Logging.LogLevel values: Information = 2, Warning = 3, Error = 4, Critical = 5.
    private static readonly Dictionary<string, int[]> GroupLevels = new()
    {
        ["info"] = [0, 1, 2], ["warning"] = [3], ["error"] = [4, 5]
    };

    public static string GroupOf(int level) => level >= 4 ? "error" : level == 3 ? "warning" : "info";

    public static string EncodeCursor(DateTimeOffset timestamp, long id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{timestamp.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id.ToString(CultureInfo.InvariantCulture)}"))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecodeCursor(string? cursor, out DateTimeOffset timestamp, out long id)
    {
        timestamp = default; id = 0;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 80) return false;
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                ticks < 0 || ticks > DateTimeOffset.MaxValue.Ticks) return false;
            timestamp = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException) { return false; }
    }

    public async Task<AppLogPage> ReadAsync(AppLogFilter filter, CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(filter.PageSize, 1, MaximumPageSize);

        // Everything but the level and the cursor: the level counts describe the whole selected period.
        var args = new List<object>();
        var baseWhere = new StringBuilder("1 = 1");
        if (filter.From is { } from) baseWhere.Append(" AND timestamp_utc >= {").Append(Add(args, from.ToUniversalTime())).Append('}');
        if (filter.To is { } to) baseWhere.Append(" AND timestamp_utc < {").Append(Add(args, to.ToUniversalTime())).Append('}');
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = "%" + EscapeLike(filter.Text.Trim().ToLowerInvariant()) + "%";
            var index = Add(args, pattern);
            baseWhere.Append(" AND (LOWER(message) LIKE {").Append(index).Append("} ESCAPE '\\' OR LOWER(category) LIKE {").Append(index)
                .Append("} ESCAPE '\\' OR LOWER(COALESCE(exception, '')) LIKE {").Append(index).Append("} ESCAPE '\\')");
        }

        var counts = await CountAsync(baseWhere.ToString(), args.ToArray(), cancellationToken);

        var selected = filter.Levels.Where(GroupLevels.ContainsKey).SelectMany(group => GroupLevels[group]).Distinct().ToArray();
        if (selected.Length == 0) return new AppLogPage([], null, counts);

        var pageArgs = new List<object>(args);
        var where = new StringBuilder(baseWhere.ToString()).Append(" AND level IN (").Append(string.Join(", ", selected)).Append(')');
        if (TryDecodeCursor(filter.Cursor, out var cursorTime, out var cursorId))
        {
            var t = Add(pageArgs, cursorTime);
            var i = Add(pageArgs, cursorId);
            where.Append(" AND (timestamp_utc < {").Append(t).Append("} OR (timestamp_utc = {").Append(t).Append("} AND id < {").Append(i).Append("}))");
        }

        var rows = await db.Database.SqlQueryRaw<Row>(
            $"""
            SELECT id AS "Id", timestamp_utc AS "Timestamp", CAST(level AS INTEGER) AS "Level", category AS "Category",
                   message AS "Message", exception AS "Exception", trace_id AS "TraceId"
            FROM app_logs WHERE {where}
            ORDER BY timestamp_utc DESC, id DESC
            LIMIT {pageSize + 1}
            """, pageArgs.ToArray()).ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var page = rows.Take(pageSize).ToList();
        var items = page.Select(r => new AppLogItem(r.Id, r.Timestamp, GroupOf(r.Level), r.Category, r.Message, r.Exception, r.TraceId)).ToList();
        return new AppLogPage(items, hasMore && page.Count > 0 ? EncodeCursor(page[^1].Timestamp, page[^1].Id) : null, counts);
    }

    /// <summary>Entries per level group in [from, to). Uses the timestamp index only (no text search).</summary>
    public Task<AppLogCounts> CountBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default) =>
        CountAsync("timestamp_utc >= {0} AND timestamp_utc < {1}", [from.ToUniversalTime(), to.ToUniversalTime()], cancellationToken);

    /// <summary>
    /// The categories (loggers) with most errors since <paramref name="since"/>. Only the category is returned, never the
    /// message or the exception: messages can carry request details and must not reach a summary page.
    /// </summary>
    public async Task<IReadOnlyList<AppLogCategoryCount>> TopErrorCategoriesAsync(
        DateTimeOffset since, int take, CancellationToken cancellationToken = default)
    {
        var rows = await db.Database.SqlQueryRaw<CategoryRow>(
            """
            SELECT category AS "Category", CAST(COUNT(*) AS INTEGER) AS "Count", MAX(timestamp_utc) AS "LastAt"
            FROM app_logs WHERE level >= 4 AND timestamp_utc >= {0}
            GROUP BY category ORDER BY COUNT(*) DESC, MAX(timestamp_utc) DESC
            LIMIT 
            """ + Math.Clamp(take, 1, 20), since.ToUniversalTime()).ToListAsync(cancellationToken);
        return rows.Select(r => new AppLogCategoryCount(r.Category, r.Count, r.LastAt)).ToList();
    }

    private async Task<AppLogCounts> CountAsync(string where, object[] args, CancellationToken cancellationToken)
    {
        var grouped = await db.Database.SqlQueryRaw<CountRow>(
            $"""SELECT CAST(level AS INTEGER) AS "Level", CAST(COUNT(*) AS INTEGER) AS "Count" FROM app_logs WHERE {where} GROUP BY level""",
            args).ToListAsync(cancellationToken);
        return new AppLogCounts(
            grouped.Where(x => GroupOf(x.Level) == "info").Sum(x => x.Count),
            grouped.Where(x => GroupOf(x.Level) == "warning").Sum(x => x.Count),
            grouped.Where(x => GroupOf(x.Level) == "error").Sum(x => x.Count));
    }

    private static int Add(List<object> args, object value) { args.Add(value); return args.Count - 1; }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record Row(long Id, DateTimeOffset Timestamp, int Level, string Category, string Message, string? Exception, string? TraceId);
    private sealed record CategoryRow(string Category, int Count, DateTimeOffset LastAt);
    private sealed record CountRow(int Level, int Count);
}
