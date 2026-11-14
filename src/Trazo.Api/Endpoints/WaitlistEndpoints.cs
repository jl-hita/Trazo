using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Trazo.Domain.Waitlist;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Waitlist;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The waitlist of the public landing page. The public POST is anonymous and limited per IP; the answer is the same
/// whether or not the address was already there, so it cannot be used to find out who signed up. The addresses are
/// personal data: they are never logged, and the administration routes require the superadmin role.
/// </summary>
public static class WaitlistEndpoints
{
    public const string RateLimitPolicy = "waitlist";
    private const int MaxPageSize = 200;

    public sealed record JoinWaitlistRequest(string? Email, string? Language, bool Consent);

    public static IEndpointRouteBuilder MapWaitlistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The landing is served from the same origin as the API, so the public route has no CORS policy at all.
        endpoints.MapPost("/api/v1/waitlist", JoinAsync)
            .WithTags("Waitlist")
            .AllowAnonymous()
            .WithMetadata(new Microsoft.AspNetCore.Cors.DisableCorsAttribute())
            .RequireRateLimiting(RateLimitPolicy);

        var admin = endpoints.MapGroup("/api/v1/admin/waitlist")
            .WithTags("Admin waitlist")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        admin.MapGet("", ListAsync);
        admin.MapGet("/export.csv", ExportAsync);
        admin.MapDelete("/{id:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> JoinAsync(JoinWaitlistRequest? request, TrazoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!WaitlistService.IsValidEmail(request?.Email)) errors["email"] = ["A valid email address is required."];
        var language = request?.Language?.Trim().ToLowerInvariant();
        if (language is null || !WaitlistService.Languages.Contains(language)) errors["language"] = ["Use 'es' or 'en'."];
        if (request?.Consent != true) errors["consent"] = ["Consent is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // Whether the address was new is deliberately dropped: the answer must be identical in both cases.
        await WaitlistService.AddAsync(db, request!.Email!, language!, clock.GetUtcNow(), ct);
        return Results.Accepted(value: new { accepted = true });
    }

    private static async Task<IResult> ListAsync(TrazoDbContext db, string? q, string? language, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["page must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        if (q?.Length > 100) errors["q"] = ["The search text is too long."];
        if (!string.IsNullOrEmpty(language) && !WaitlistService.Languages.Contains(language)) errors["language"] = ["Use 'es' or 'en'."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var query = db.WaitlistEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(language)) query = query.Where(x => x.Language == language);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(x => x.Email.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Email, x.Language, x.ConsentAt, x.CreatedAt })
            .ToListAsync(ct);

        // Counts over the whole list, independent of the filters.
        var byLanguage = await db.WaitlistEntries.AsNoTracking().GroupBy(x => x.Language)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return Results.Ok(new
        {
            items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize),
            overall = new
            {
                total = byLanguage.Sum(x => x.Count),
                es = byLanguage.FirstOrDefault(x => x.Key == "es")?.Count ?? 0,
                en = byLanguage.FirstOrDefault(x => x.Key == "en")?.Count ?? 0
            }
        });
    }

    private static async Task<IResult> ExportAsync(TrazoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var rows = await db.WaitlistEntries.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var csv = new StringBuilder("email,language,consent_at,created_at\r\n");
        foreach (var r in rows)
            csv.Append(CsvCell(r.Email)).Append(',').Append(r.Language).Append(',')
               .Append(r.ConsentAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append(',')
               .Append(r.CreatedAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
        var name = $"trazo-waitlist-{clock.GetUtcNow():yyyyMMdd}.csv";
        // UTF-8 with BOM so spreadsheets open accented addresses correctly.
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", name);
    }

    /// <summary>Quotes a cell and defuses spreadsheet formulas (an address may start with + or -).</summary>
    internal static string CsvCell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static async Task<IResult> DeleteAsync(Guid id, TrazoDbContext db, CancellationToken ct)
    {
        var removed = await db.WaitlistEntries.Where(x => x.Id == id).ExecuteDeleteAsync(ct);
        return removed == 0 ? Results.NotFound() : Results.NoContent();
    }
}
