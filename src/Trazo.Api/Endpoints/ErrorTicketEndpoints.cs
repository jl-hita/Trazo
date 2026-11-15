using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Support;
using Trazo.Domain.Support;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Support;

namespace Trazo.Api.Endpoints;

/// <summary>
/// Anonymous error tickets. The public POST receives the crashes and unexpected errors of the Android app: it takes no
/// credentials and stores no user, IP or device data; it is limited per IP and in size. The console routes are superadmin only,
/// because a stack trace can reveal how the system is built.
/// </summary>
public static class ErrorTicketEndpoints
{
    public const string RateLimitPolicy = "diagnostics";
    public const int MaxIncomingLength = 20_000;
    private const int MaxPageSize = 100;

    public sealed record ReportErrorRequest(string? AppVersion, string? StackTrace);
    public sealed record SetStatusRequest(string? Status);

    public static IEndpointRouteBuilder MapErrorTicketEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/diagnostics/errors", ReportAsync)
            .WithTags("Diagnostics")
            .AllowAnonymous()
            .WithMetadata(new Microsoft.AspNetCore.Cors.DisableCorsAttribute())
            .WithMetadata(new RequestSizeLimitMetadata(64 * 1024))
            .RequireRateLimiting(RateLimitPolicy);

        var admin = endpoints.MapGroup("/api/v1/admin/error-tickets")
            .WithTags("Admin error tickets")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        admin.MapGet("", ListAsync);
        admin.MapGet("/{id:guid}", GetAsync);
        admin.MapPut("/{id:guid}/status", SetStatusAsync);
        admin.MapDelete("/{id:guid}", DeleteAsync);
        // Lets the superadmin check the whole chain (handler, ticket, email) on demand.
        admin.MapPost("/test-exception", TestException);
        return endpoints;
    }

    /// <summary>Throws on purpose. The caller gets the plain 500 of the exception handler and a ticket appears in the console.</summary>
    private static IResult TestException() =>
        throw new InvalidOperationException("Test exception triggered on purpose from the administration console.");

    private sealed class RequestSizeLimitMetadata(long maxRequestBodySize) : IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize { get; } = maxRequestBodySize;
    }

    private static IResult ReportAsync(ReportErrorRequest? request, ErrorTicketQueue queue)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request?.StackTrace)) errors["stackTrace"] = ["A stack trace is required."];
        else if (request.StackTrace.Length > MaxIncomingLength) errors["stackTrace"] = [$"The stack trace is too long (maximum {MaxIncomingLength} characters)."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // Queued and stored off the request path; the answer never says whether the error was already known.
        queue.TryWrite(new ErrorReport(ErrorTicketSource.Android, request!.AppVersion, request.StackTrace!));
        return Results.Accepted(value: new { accepted = true });
    }

    private static async Task<IResult> ListAsync(TrazoDbContext db, string? status, string? source, string? q, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["page must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        if (q?.Length > 100) errors["q"] = ["The search text is too long."];
        if (!string.IsNullOrEmpty(status) && !ErrorTicketStatus.All.Contains(status)) errors["status"] = ["Use new, seen or resolved."];
        if (!string.IsNullOrEmpty(source) && !ErrorTicketSource.All.Contains(source)) errors["source"] = ["Use backend or android."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var query = db.ErrorTickets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        if (!string.IsNullOrEmpty(source)) query = query.Where(x => x.Source == source);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(x => x.ErrorType.ToLower().Contains(term) || x.StackTrace.ToLower().Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.LastSeenAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Source, x.AppVersion, x.ErrorType, x.Occurrences, x.FirstSeenAt, x.LastSeenAt, x.Status })
            .ToListAsync(ct);

        // Counts over the whole table, independent of the filters.
        var byStatus = await db.ErrorTickets.AsNoTracking().GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(string key) => byStatus.FirstOrDefault(x => x.Key == key)?.Count ?? 0;
        return Results.Ok(new
        {
            items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize),
            overall = new { total = byStatus.Sum(x => x.Count), @new = Count(ErrorTicketStatus.New), seen = Count(ErrorTicketStatus.Seen), resolved = Count(ErrorTicketStatus.Resolved) }
        });
    }

    private static async Task<IResult> GetAsync(Guid id, TrazoDbContext db, CancellationToken ct)
    {
        var ticket = await db.ErrorTickets.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.Source, x.AppVersion, x.ErrorType, x.Occurrences, x.FirstSeenAt, x.LastSeenAt, x.Status, x.StackTrace })
            .FirstOrDefaultAsync(ct);
        return ticket is null ? Results.NotFound() : Results.Ok(ticket);
    }

    private static async Task<IResult> SetStatusAsync(Guid id, SetStatusRequest? request, TrazoDbContext db, CancellationToken ct)
    {
        if (request?.Status is null || !ErrorTicketStatus.All.Contains(request.Status))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use new, seen or resolved."] });
        var changed = await db.ErrorTickets.Where(x => x.Id == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, request.Status), ct);
        return changed == 0 ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, TrazoDbContext db, CancellationToken ct)
    {
        var removed = await db.ErrorTickets.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return removed == 0 ? Results.NotFound() : Results.NoContent();
    }
}
