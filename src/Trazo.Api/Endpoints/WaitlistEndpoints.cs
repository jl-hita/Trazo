using Trazo.Api.Support;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Domain.Waitlist;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Waitlist;

namespace Trazo.Api.Endpoints;

/// <summary>
/// The waitlist of the public landing page, which also feeds the closed test. The public POST is anonymous and limited per
/// IP; the answer is the same whether or not the address was already there, so it cannot be used to find out who signed up.
/// A new address gets a confirmation email (double opt-in) and nothing else until it confirms; the superadmin then adds the
/// confirmed addresses to the Play Console tester list and sends the invitations from the console. The addresses are
/// personal data: they are never logged, and the administration routes require the superadmin role.
/// </summary>
public static class WaitlistEndpoints
{
    public const string RateLimitPolicy = "waitlist";
    public const string DefaultTestInviteUrl = "https://play.google.com/apps/testing/com.trazo.android";
    public const int MaxInvitesPerCall = 50;
    private const int MaxPageSize = 200;
    private const string UnsubscribePurpose = "Trazo.Waitlist.Unsubscribe.v1";
    private static readonly string[] Statuses = ["pending", "confirmed", "invited"];

    public sealed record JoinWaitlistRequest(string? Email, string? Language, bool Consent);
    public sealed record WaitlistTokenRequest(string? Token);
    public sealed record InviteRequest(Guid[]? Ids);

    public static IEndpointRouteBuilder MapWaitlistEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The landing is served from the same origin as the API, so the public routes have no CORS policy at all.
        endpoints.MapPost("/api/v1/waitlist", JoinAsync)
            .WithTags("Waitlist")
            .AllowAnonymous()
            .WithMetadata(new Microsoft.AspNetCore.Cors.DisableCorsAttribute())
            .RequireRateLimiting(RateLimitPolicy);
        // The pages behind the email links post here; the person presses a button first, so a mail scanner that only opens
        // links cannot confirm (or remove) anybody.
        endpoints.MapPost("/api/v1/waitlist/confirm", ConfirmAsync)
            .WithTags("Waitlist").AllowAnonymous()
            .WithMetadata(new Microsoft.AspNetCore.Cors.DisableCorsAttribute())
            .RequireRateLimiting(RateLimitPolicy);
        endpoints.MapPost("/api/v1/waitlist/unsubscribe", UnsubscribeAsync)
            .WithTags("Waitlist").AllowAnonymous()
            .WithMetadata(new Microsoft.AspNetCore.Cors.DisableCorsAttribute())
            .RequireRateLimiting(RateLimitPolicy);

        var admin = endpoints.MapGroup("/api/v1/admin/waitlist")
            .WithTags("Admin waitlist")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);
        admin.MapGet("", ListAsync);
        admin.MapGet("/export.csv", ExportAsync);
        admin.MapPost("/invite", InviteAsync);
        admin.MapDelete("/{id:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> JoinAsync(
        JoinWaitlistRequest? request, TrazoDbContext db, TimeProvider clock, DeletionRequestThrottle throttle,
        IBackgroundWork background, IConfiguration configuration, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!WaitlistService.IsValidEmail(request?.Email)) errors["email"] = ["A valid email address is required."];
        var language = request?.Language?.Trim().ToLowerInvariant();
        if (language is null || !WaitlistService.Languages.Contains(language)) errors["language"] = ["Idioma no admitido."];
        if (request?.Consent != true) errors["consent"] = ["Consent is required."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // Whether the address was new is deliberately dropped: the answer must be identical in both cases.
        await WaitlistService.AddAsync(db, request!.Email!, language!, clock.GetUtcNow(), ct);

        // The confirmation goes out in the background so the response time does not depend on the address being new, and the
        // per-address limit keeps the form from being used to flood somebody's mailbox. A confirmed address gets nothing.
        var normalized = WaitlistService.Normalize(request.Email!);
        if (throttle.TryAcquire("waitlist|" + normalized))
        {
            var baseUrl = (configuration["Account:PublicBaseUrl"] ?? AccountEndpoints.DefaultPublicBaseUrl).TrimEnd('/');
            background.Enqueue("waitlist-confirmation", (services, token) => SendConfirmationAsync(services, normalized, language!, baseUrl, token));
        }
        return Results.Accepted(value: new { accepted = true });
    }

    /// <summary>
    /// Runs in the background service, which logs a failure by type only (a mail error can quote the address) and reports it as an error
    /// ticket unless the mail server was the one that did not answer.
    /// </summary>
    private static async Task SendConfirmationAsync(IServiceProvider services, string email, string language, string baseUrl, CancellationToken ct)
    {
        var db = services.GetRequiredService<TrazoDbContext>();
        var clock = services.GetRequiredService<TimeProvider>();
        var raw = await WaitlistService.IssueConfirmationAsync(db, email, clock.GetUtcNow(), ct);
        if (raw is null) return; // already confirmed (or gone): nothing to send
        var url = $"{baseUrl}/waitlist/confirm?token={Uri.EscapeDataString(raw)}&lang={language}";
        var (subject, body) = WaitlistMail.Confirmation(language, url);
        await services.GetRequiredService<IEmailSender>().SendAsync(email, subject, body, ct);
    }

    private static async Task<IResult> ConfirmAsync(WaitlistTokenRequest? request, TrazoDbContext db, TimeProvider clock, CancellationToken ct) =>
        await WaitlistService.ConfirmAsync(db, request?.Token, clock.GetUtcNow(), ct)
            ? Results.Ok(new { confirmed = true })
            : ApiErrors.BadRequest(new { error = "The link is invalid, expired or already used." });

    private static async Task<IResult> UnsubscribeAsync(WaitlistTokenRequest? request, TrazoDbContext db, IDataProtectionProvider protection, CancellationToken ct)
    {
        var id = ReadUnsubscribeToken(protection, request?.Token);
        if (id is null) return ApiErrors.BadRequest(new { error = "The link is invalid." });
        // Idempotent: opening the link again after the data is gone is still a success for the person.
        await WaitlistService.DeleteByIdAsync(db, id.Value, ct);
        return Results.Ok(new { unsubscribed = true });
    }

    /// <summary>The link in the invitation: the entry id, protected with the application's data-protection keys (no extra column).</summary>
    public static string UnsubscribeToken(IDataProtectionProvider protection, Guid id) =>
        protection.CreateProtector(UnsubscribePurpose).Protect(id.ToString("N"));

    private static Guid? ReadUnsubscribeToken(IDataProtectionProvider protection, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 300) return null;
        try { return Guid.TryParseExact(protection.CreateProtector(UnsubscribePurpose).Unprotect(token), "N", out var id) ? id : null; }
        catch (CryptographicException) { return null; }
    }

    private static async Task<IResult> ListAsync(TrazoDbContext db, string? q, string? language, string? status, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["page must be 1 or greater."];
        if (pageSize is < 1 or > MaxPageSize) errors["pageSize"] = [$"pageSize must be between 1 and {MaxPageSize}."];
        if (q?.Length > 100) errors["q"] = ["The search text is too long."];
        if (!string.IsNullOrEmpty(language) && !WaitlistService.Languages.Contains(language)) errors["language"] = ["Idioma no admitido."];
        if (!string.IsNullOrEmpty(status) && !Statuses.Contains(status)) errors["status"] = ["Use 'pending', 'confirmed' or 'invited'."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var query = db.WaitlistEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(language)) query = query.Where(x => x.Language == language);
        // pending: not confirmed yet; confirmed: confirmed, invitation not sent; invited: invitation sent.
        query = status switch
        {
            "pending" => query.Where(x => x.ConfirmedAt == null),
            "confirmed" => query.Where(x => x.ConfirmedAt != null && x.InvitedAt == null),
            "invited" => query.Where(x => x.InvitedAt != null),
            _ => query
        };
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLowerInvariant();
            query = query.Where(x => x.Email.Contains(term));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.Email, x.Language, x.ConsentAt, x.CreatedAt, x.ConfirmedAt, x.InvitedAt })
            .ToListAsync(ct);

        // Counts over the whole list, independent of the filters.
        var all = db.WaitlistEntries.AsNoTracking();
        var byLanguage = await all.GroupBy(x => x.Language).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var pending = await all.CountAsync(x => x.ConfirmedAt == null, ct);
        var confirmed = await all.CountAsync(x => x.ConfirmedAt != null && x.InvitedAt == null, ct);
        var invited = await all.CountAsync(x => x.InvitedAt != null, ct);
        return Results.Ok(new
        {
            items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize),
            overall = new
            {
                total = byLanguage.Sum(x => x.Count),
                es = byLanguage.FirstOrDefault(x => x.Key == "es")?.Count ?? 0,
                en = byLanguage.FirstOrDefault(x => x.Key == "en")?.Count ?? 0,
                pending, confirmed, invited
            }
        });
    }

    private static async Task<IResult> ExportAsync(TrazoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var rows = await db.WaitlistEntries.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var csv = new StringBuilder("email,language,consent_at,created_at,confirmed_at,invited_at\r\n");
        foreach (var r in rows)
            csv.Append(CsvCell(r.Email)).Append(',').Append(r.Language).Append(',')
               .Append(Stamp(r.ConsentAt)).Append(',').Append(Stamp(r.CreatedAt)).Append(',')
               .Append(r.ConfirmedAt is { } c ? Stamp(c) : "").Append(',').Append(r.InvitedAt is { } i ? Stamp(i) : "").Append("\r\n");
        var name = $"trazo-waitlist-{clock.GetUtcNow():yyyyMMdd}.csv";
        // UTF-8 with BOM so spreadsheets open accented addresses correctly.
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", name);
    }

    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Quotes a cell and defuses spreadsheet formulas (an address may start with + or -).</summary>
    internal static string CsvCell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    /// <summary>
    /// Sends the invitation to the closed test to confirmed addresses that have not had it. The superadmin calls this once the
    /// addresses are in the Play Console tester list (the link only works for them). One failed send does not stop the rest, and
    /// only a sent invitation is marked, so the failed ones can be tried again.
    /// </summary>
    private static async Task<IResult> InviteAsync(
        InviteRequest? request, HttpContext http, TrazoDbContext db, IEmailSender email, IDataProtectionProvider protection, IConfiguration configuration,
        TimeProvider clock, ILoggerFactory loggers, IErrorReporter reporter, CancellationToken ct)
    {
        var ids = request?.Ids?.Distinct().ToArray() ?? [];
        if (ids.Length == 0 || ids.Length > MaxInvitesPerCall)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = [$"Send between 1 and {MaxInvitesPerCall} ids."] });

        var entries = await db.WaitlistEntries.Where(x => ids.Contains(x.Id) && x.ConfirmedAt != null && x.InvitedAt == null).ToListAsync(ct);
        var configured = configuration["Waitlist:TestInviteUrl"];
        var testUrl = Uri.TryCreate(configured, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? configured! : DefaultTestInviteUrl;
        var baseUrl = (configuration["Account:PublicBaseUrl"] ?? AccountEndpoints.DefaultPublicBaseUrl).TrimEnd('/');
        var log = loggers.CreateLogger("Trazo.Waitlist");

        int invited = 0, failed = 0;
        foreach (var entry in entries)
        {
            try
            {
                var unsubscribe = $"{baseUrl}/waitlist/unsubscribe?token={Uri.EscapeDataString(UnsubscribeToken(protection, entry.Id))}&lang={entry.Language}";
                var (subject, body) = WaitlistMail.Invitation(entry.Language, testUrl, unsubscribe);
                await email.SendAsync(entry.Email, subject, body, ct);
                entry.InvitedAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
                invited++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                // The exception type only: mail errors can quote the recipient address.
                log.LogWarning("A waitlist invitation could not be sent ({Error}).", ex.GetType().Name);
                // A mail server that did not answer is expected (it is marked, and nothing is reported); anything else is a defect.
                await reporter.ReportAsync(ex, ErrorResults.TraceIdOf(http), ct);
            }
        }
        // "skipped": unknown ids, not confirmed yet, or already invited.
        return Results.Ok(new { requested = ids.Length, invited, failed, skipped = ids.Length - entries.Count });
    }

    private static async Task<IResult> DeleteAsync(Guid id, TrazoDbContext db, CancellationToken ct)
    {
        var removed = await WaitlistService.DeleteByIdAsync(db, id, ct);
        return removed == 0 ? Results.NotFound() : Results.NoContent();
    }
}
