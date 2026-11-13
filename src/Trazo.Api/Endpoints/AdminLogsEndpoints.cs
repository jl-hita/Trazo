using Trazo.Infrastructure.Logging;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Log viewer of the administration console. Logs can contain request details and exception traces, so every route
/// requires the superadmin role.
/// </summary>
public static class AdminLogsEndpoints
{
    public static IEndpointRouteBuilder MapAdminLogsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGroup("/api/v1/admin/logs")
            .WithTags("Admin logs")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy)
            .MapGet("", ListAsync);
        return endpoints;
    }

    /// <param name="levels">Comma-separated groups to include: info, warning, error. Empty means all three.</param>
    /// <param name="from">Include entries at or after this instant.</param>
    /// <param name="to">Include entries before this instant (exclusive).</param>
    /// <param name="q">Text to look for in the message, the category or the exception.</param>
    /// <param name="cursor">The <c>nextCursor</c> of the previous page: returns the entries older than it.</param>
    private static async Task<IResult> ListAsync(
        AppLogReader reader,
        string? levels,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? q,
        string? cursor,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (pageSize is < 1 or > AppLogReader.MaximumPageSize)
            errors["pageSize"] = [$"pageSize must be between 1 and {AppLogReader.MaximumPageSize}."];
        if (q?.Length > 100) errors["q"] = ["The search text is too long."];
        if (from is not null && to is not null && from >= to) errors["period"] = ["'from' must be before 'to'."];
        if (!string.IsNullOrEmpty(cursor) && !AppLogReader.TryDecodeCursor(cursor, out _, out _)) errors["cursor"] = ["The cursor is not valid."];

        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(levels))
            selected.UnionWith(AppLogReader.LevelGroups);
        else
            foreach (var level in levels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!AppLogReader.LevelGroups.Contains(level.ToLowerInvariant())) errors["levels"] = ["Use info, warning and error, separated by commas."];
                else selected.Add(level.ToLowerInvariant());
            }
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var page = await reader.ReadAsync(new AppLogFilter(selected, from, to, q, cursor, pageSize), cancellationToken);
        return Results.Ok(new
        {
            items = page.Items.Select(x => new
            {
                x.Id, timestamp = x.Timestamp, x.Level, x.Category, x.Message, x.Exception, x.TraceId
            }),
            nextCursor = page.NextCursor,
            counts = new { info = page.Counts.Info, warning = page.Counts.Warning, error = page.Counts.Error }
        });
    }
}
