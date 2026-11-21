using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trazo.Application.Localization;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Growth;
using Trazo.Infrastructure.Persistence;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

public sealed record SharedRequest(string? Channel);
public sealed record InstallReferrerRequest(string? Code);
public sealed record VoidGrantRequest(string? Reason);
public sealed record ReferralProgrammeSettingsRequest(bool Enabled, int RewardDays);
public sealed record ClientEventRequest(string? Name);
public sealed record ReferralDiagnosticsRequest(int HttpStatus, string? ContentType, long ContentLength, string? Preview, string? AppVersion);

/// <summary>
/// The referral programme: the signed-in user's code, link, statistics and rewards; the public invitation page and Android App
/// Links file; and the commercial/superadmin administration views. Attribution and every economic decision are made here, never by the app.
/// </summary>
public static class ReferralEndpoints
{
    public const string RateLimitPolicy = "referrals";
    public const string PublicRateLimitPolicy = "referrals-public";
    public const string ManagementPolicy = "referral-management";
    public const string DefaultPackage = "com.trazo.android";

    public static IEndpointRouteBuilder MapReferralEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/v1/referrals").WithTags("Referrals").RequireAuthorization().RequireRateLimiting(RateLimitPolicy);
        me.MapGet("/me", MeAsync);
        me.MapGet("/invitations", InvitationsAsync);
        me.MapPost("/invitations/diagnostics", ReferralDiagnosticsAsync);
        me.MapGet("/rewards", RewardsAsync);
        me.MapPost("/shared", SharedAsync);

        // Public, per-IP limited. They only count events (no personal data) and show the invitation page.
        endpoints.MapPost("/api/v1/referrals/install-referrer", InstallReferrerAsync).WithTags("Referrals")
            .AllowAnonymous().RequireRateLimiting(PublicRateLimitPolicy);
        endpoints.MapGet("/invite/{code}", InvitePageAsync).AllowAnonymous().RequireRateLimiting(PublicRateLimitPolicy).ExcludeFromDescription();
        endpoints.MapGet("/.well-known/assetlinks.json", AssetLinksAsync).AllowAnonymous().ExcludeFromDescription();

        var admin = endpoints.MapGroup("/api/v1/admin/referrals").WithTags("Admin referrals")
            .RequireAuthorization(ManagementPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);
        admin.MapGet("/settings", async (IRuntimeSettings settings, CancellationToken ct) =>
        {
            var current = await settings.GetPromotionsAsync(ct);
            return Results.Ok(new { referralsEnabled = current.ReferralsEnabled, referralRewardDays = current.ReferralRewardDays, maxDays = PromotionsConfig.MaxDays });
        });
        admin.MapPut("/settings", async (ReferralProgrammeSettingsRequest request, ISettingsWriter writer, IRuntimeSettings settings,
            System.Security.Claims.ClaimsPrincipal principal, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var before = await settings.GetPromotionsAsync(ct);
            var input = new PromotionsConfig(before.SignupTrialEnabled, before.SignupTrialDays, request.Enabled, request.RewardDays);
            var errors = await writer.UpdatePromotionsAsync(input, ct);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
                "Commercial/admin {Actor} changed referral programme settings: {EnabledBefore} -> {EnabledAfter}, reward {DaysBefore}d -> {DaysAfter}d.",
                OperationsSettingsEndpoints.Actor(principal), before.ReferralsEnabled, input.ReferralsEnabled, before.ReferralRewardDays, input.ReferralRewardDays);
            return Results.Ok(new { referralsEnabled = input.ReferralsEnabled, referralRewardDays = input.ReferralRewardDays, maxDays = PromotionsConfig.MaxDays });
        });
        admin.MapGet("/summary", AdminSummaryAsync);
        admin.MapGet("", AdminListAsync);
        admin.MapGet("/users/{id:guid}/ledger", AdminLedgerAsync);
        admin.MapPost("/grants/{id:guid}/void", AdminVoidAsync);
        admin.MapPost("/grants/{id:guid}/retry", AdminRetryAsync);
        admin.MapPost("/reconcile", AdminReconcileAsync);
        endpoints.MapGet("/api/v1/admin/analytics/funnel", FunnelAsync).WithTags("Admin referrals")
            .RequireAuthorization(ManagementPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);
        return endpoints;
    }

    public static string? AppVersion(HttpContext http)
    {
        var value = http.Request.Headers["X-App-Version"].ToString().Trim();
        return value.Length is > 0 and <= 40 ? value : null;
    }


    private static IResult ReferralDiagnosticsAsync(
        ReferralDiagnosticsRequest? request, ILoggerFactory loggerFactory)
    {
        if (request is null || request.HttpStatus is < 100 or > 599 ||
            request.ContentLength < -1 || request.Preview is null || request.Preview.Length > 8192 ||
            (request.ContentType?.Length ?? 0) > 120 || (request.AppVersion?.Length ?? 0) > 40)
            return Results.BadRequest(new { error = "invalid_diagnostic" });

        string preview;
        try
        {
            using var document = JsonDocument.Parse(request.Preview);
            preview = JsonSerializer.Serialize(SanitizeReferralDiagnostic(document.RootElement));
        }
        catch (JsonException)
        {
            preview = "<non-JSON preview>";
        }

        loggerFactory.CreateLogger("Trazo.Api.ReferralDiagnostics").LogInformation(
            "Referral invitations response observed by Android: HTTP {HttpStatus}; ContentType {ContentType}; ContentLength {ContentLength}; AppVersion {AppVersion}; Preview {Preview}",
            request.HttpStatus, request.ContentType, request.ContentLength, request.AppVersion, preview);
        return Results.NoContent();
    }

    private static object? SanitizeReferralDiagnostic(JsonElement element)
    {
        const string redacted = "<redacted>";
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                p => p.Name,
                p => new[] { "id", "userid", "code", "link", "email", "name", "token", "accesstoken", "refreshtoken" }
                    .Contains(new string(p.Name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()))
                    ? (object?)redacted : SanitizeReferralDiagnostic(p.Value)),
            JsonValueKind.Array => element.EnumerateArray().Select(SanitizeReferralDiagnostic).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static Guid? UserId(System.Security.Claims.ClaimsPrincipal principal) => SubscriptionEndpoints.UserId(principal);

    internal static async Task<string> BaseUrlAsync(HttpContext http, IRuntimeSettings settings, CancellationToken ct)
    {
        var configured = (await settings.GetEmailAsync(ct)).ActionBaseUrl?.Trim().TrimEnd('/');
        return string.IsNullOrEmpty(configured) ? $"{http.Request.Scheme}://{http.Request.Host}" : configured;
    }

    // ------------------------------------------------------------------ the signed-in user

    private static async Task<IResult> MeAsync(
        HttpContext http, System.Security.Claims.ClaimsPrincipal principal, IReferralService referrals, IRuntimeSettings settings, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var promotions = await settings.GetPromotionsAsync(ct);
        if (!promotions.ReferralsEnabled)
            return Results.Ok(new { enabled = false, rewardDays = promotions.ReferralRewardDays, code = (string?)null, link = (string?)null, stats = (object?)null });
        var code = await referrals.GetOrCreateCodeAsync(userId, ct);
        var link = $"{await BaseUrlAsync(http, settings, ct)}/invite/{code.Code}";
        return Results.Ok(new { enabled = true, rewardDays = promotions.ReferralRewardDays, code = code.Code, link, stats = await referrals.StatsAsync(userId, ct) });
    }

    private static async Task<IResult> InvitationsAsync(
        System.Security.Claims.ClaimsPrincipal principal,
        IReferralService referrals,
        ILoggerFactory loggerFactory,
        Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> jsonOptions,
        string? status,
        CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        if (status is not null && status is not (ReferralStatuses.Registered or ReferralStatuses.Rewarded or ReferralStatuses.Voided or ReferralStatuses.Skipped))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] });

        var response = new { items = await referrals.InvitationsAsync(userId, status, ct) };
        // Serialize once with the same HTTP JSON options used by ASP.NET Core, log those exact bytes,
        // and return that same JSON so the diagnostic cannot drift from the response sent to Android.
        var responseJson = JsonSerializer.Serialize(response, jsonOptions.Value.SerializerOptions);
        loggerFactory.CreateLogger("Trazo.Api.ReferralResponses").LogInformation(
            "GET /api/v1/referrals/invitations response: HTTP 200; UserId {UserId}; StatusFilter {StatusFilter}; Body {ResponseBody}",
            userId, status, responseJson);
        return Results.Content(responseJson, "application/json; charset=utf-8");
    }

    private static async Task<IResult> RewardsAsync(System.Security.Claims.ClaimsPrincipal principal, IReferralService referrals, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        return Results.Ok(new { items = await referrals.GrantsAsync(userId, ct) });
    }

    /// <summary>The app reports that the user opened the share sheet. Only a counter: the backend cannot know whether anyone received it.</summary>
    private static async Task<IResult> SharedAsync(
        HttpContext http, System.Security.Claims.ClaimsPrincipal principal, SharedRequest? request, IReferralService referrals,
        IGrowthEvents events, CancellationToken ct)
    {
        if (UserId(principal) is not { } userId) return Results.Unauthorized();
        var code = await referrals.GetOrCreateCodeAsync(userId, ct);
        var channel = request?.Channel is { Length: > 0 and <= 30 } c ? c : null;
        await events.RecordAsync(GrowthEventNames.ReferralShared, userId,
            new GrowthEventInfo(Channel: channel ?? "referral", Reference: code.Code, AppVersion: AppVersion(http)), ct);
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ public

    private static async Task<IResult> InstallReferrerAsync(InstallReferrerRequest? request, TrazoDbContext db, IGrowthEvents events, CancellationToken ct)
    {
        var code = ReferralService.NormalizeCode(request?.Code);
        if (code is null || !await db.ReferralCodes.AsNoTracking().AnyAsync(x => x.Code == code, ct)) return Results.NoContent();
        await events.RecordAsync(GrowthEventNames.ReferralInstallAttributed, null, new GrowthEventInfo(Channel: "referral", Reference: code), ct);
        return Results.NoContent();
    }

    private const string PageCss = "body{margin:0;font:16px/1.55 system-ui,sans-serif;background:#10151c;color:#e8edf5}main{max-width:30rem;margin:0 auto;padding:2.5rem 1.2rem}"
        + "h1{font-size:1.6rem}a.b{display:block;margin:.8rem 0;padding:.85rem 1rem;border-radius:8px;text-align:center;font-weight:700;text-decoration:none;background:#7fb0ee;color:#0b1220}"
        + "a.s{background:transparent;border:2px solid #34465b;color:#e8edf5}p{color:#a9b6c7}code{font-size:1.2rem;color:#e8edf5;letter-spacing:.12em}";
    private static readonly string CssHash = "sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(PageCss)));

    private static async Task<IResult> InvitePageAsync(
        string code, HttpContext http, TrazoDbContext db, IGrowthEvents events, IRuntimeSettings settings, RequestLanguage language, CancellationToken ct)
    {
        var normalized = ReferralService.NormalizeCode(code);
        var valid = normalized is not null && (await settings.GetPromotionsAsync(ct)).ReferralsEnabled &&
                    await db.ReferralCodes.AsNoTracking().AnyAsync(x => x.Code == normalized, ct);
        if (valid) await events.RecordAsync(GrowthEventNames.ReferralLinkOpened, null, new GrowthEventInfo(Channel: "referral", Reference: normalized), ct);

        var package = (await settings.GetPlayBillingAsync(ct)).PackageName;
        if (string.IsNullOrWhiteSpace(package)) package = DefaultPackage;
        var en = language.Current == AppLanguage.En;
        string E(string s) => WebUtility.HtmlEncode(s);
        // Google Play passes the "referrer" value to the app on first launch (Play Install Referrer API).
        var store = valid
            ? $"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(package)}&referrer={Uri.EscapeDataString("trazo_ref=" + normalized)}"
            : $"https://play.google.com/store/apps/details?id={Uri.EscapeDataString(package)}";
        var title = en ? "You have been invited to TRAZO" : "Te han invitado a TRAZO";
        var body = valid
            ? (en ? "Create your account with this invitation code and your friend can earn free Premium time when you subscribe."
                  : "Crea tu cuenta con este código de invitación; tu amistad podrá ganar Premium gratis cuando te suscribas.")
            : (en ? "This invitation is not valid, but you can still install TRAZO." : "Esta invitación no es válida, pero puedes instalar TRAZO igualmente.");
        var html = $"<!doctype html><html lang=\"{(en ? "en" : "es")}\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">"
            + $"<meta name=\"robots\" content=\"noindex\"><title>TRAZO</title><style>{PageCss}</style></head><body><main><h1>{E(title)}</h1><p>{E(body)}</p>"
            + (valid ? $"<p>{(en ? "Invitation code" : "Código de invitación")}: <code>{E(normalized!)}</code></p>" : "")
            + $"<a class=\"b\" href=\"{E(store)}\">{(en ? "Get it on Google Play" : "Descargar en Google Play")}</a></main></body></html>";
        http.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; style-src '{CssHash}'; base-uri 'none'; frame-ancestors 'none'";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        http.Response.Headers.CacheControl = "no-store";
        return Results.Content(html, "text/html; charset=utf-8");
    }

    /// <summary>Digital Asset Links for Android App Links. Needs the SHA-256 fingerprint(s) of the signing certificate in AppLinks:Sha256Fingerprints.</summary>
    private static async Task<IResult> AssetLinksAsync(IConfiguration configuration, IRuntimeSettings settings, CancellationToken ct)
    {
        var fingerprints = (configuration["AppLinks:Sha256Fingerprints"] ?? "")
            .Split([',', ';', '\n', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToArray();
        if (fingerprints.Length == 0) return Results.NotFound();
        var package = (await settings.GetPlayBillingAsync(ct)).PackageName;
        if (string.IsNullOrWhiteSpace(package)) package = DefaultPackage;
        var doc = new[]
        {
            new
            {
                relation = new[] { "delegate_permission/common.handle_all_urls" },
                target = new { @namespace = "android_app", package_name = package, sha256_cert_fingerprints = fingerprints }
            }
        };
        return Results.Json(doc);
    }

    // ------------------------------------------------------------------ superadmin audit

    private static (DateTimeOffset From, DateTimeOffset To) Period(DateTimeOffset? from, DateTimeOffset? to, TimeProvider clock)
    {
        var end = to ?? clock.GetUtcNow().AddDays(1);
        return (from ?? end.AddDays(-91), end);
    }

    private static async Task<IResult> AdminSummaryAsync(IGrowthAnalytics analytics, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var (f, t) = Period(from, to, clock);
        if (f >= t) return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["'from' must be before 'to'."] });
        return Results.Ok(await analytics.ReferralsAsync(f, t, ct));
    }

    private static async Task<IResult> FunnelAsync(
        IGrowthAnalytics analytics, TimeProvider clock, DateTimeOffset? from, DateTimeOffset? to, string? channel, string? reference, CancellationToken ct)
    {
        var (f, t) = Period(from, to, clock);
        if (f >= t) return Results.ValidationProblem(new Dictionary<string, string[]> { ["period"] = ["'from' must be before 'to'."] });
        if (channel is not null && channel is not ("referral" or "organic"))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["channel"] = ["Use referral or organic."] });
        return Results.Ok(await analytics.FunnelAsync(f, t, channel, ReferralService.NormalizeCode(reference), ct));
    }

    private static async Task<IResult> AdminListAsync(TrazoDbContext db, string? status, bool? incidents, DateTimeOffset? from, DateTimeOffset? to,
        int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || pageSize is < 1 or > 100) errors["paging"] = ["page must be positive and pageSize between 1 and 100."];
        if (from.HasValue && to.HasValue && from >= to) errors["period"] = ["'from' must be before 'to'."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var rels = db.ReferralRelationships.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) rels = rels.Where(r => r.Status == status);
        if (incidents == true) rels = rels.Where(r => r.Incident != null);
        if (from.HasValue) rels = rels.Where(r => r.AttributedAt >= from.Value);
        if (to.HasValue) rels = rels.Where(r => r.AttributedAt < to.Value);
        var all = await rels.ToListAsync(ct);
        var total = all.Count;
        var slice = all.OrderByDescending(r => r.AttributedAt).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        var userIds = slice.SelectMany(r => new[] { r.ReferrerUserId, r.ReferredUserId }).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, ct);
        var grantIds = slice.Where(r => r.RewardGrantId != null).Select(r => r.RewardGrantId!.Value).ToList();
        var grants = await db.PremiumGrants.AsNoTracking().Where(g => grantIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);
        return Results.Ok(new
        {
            total, page, pageSize,
            items = slice.Select(r => new
            {
                r.Id, r.Code, r.Source, r.Status, r.AttributedAt, r.ConvertedAt, plan = r.ConversionPlan, r.RewardDays, r.Incident, r.AppVersion,
                referrerId = r.ReferrerUserId, referrerEmail = names.GetValueOrDefault(r.ReferrerUserId),
                referredId = r.ReferredUserId, referredEmail = names.GetValueOrDefault(r.ReferredUserId),
                rewardGrantId = r.RewardGrantId, rewardStatus = r.RewardGrantId is { } g && grants.TryGetValue(g, out var gr) ? gr.Status : null,
                lastDeferNote = r.RewardGrantId is { } g2 && grants.TryGetValue(g2, out var gr2) ? gr2.LastDeferError : null
            })
        });
    }

    private static async Task<IResult> AdminLedgerAsync(Guid id, TrazoDbContext db, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == id, ct)) return Results.NotFound();
        var grants = (await db.PremiumGrants.AsNoTracking().Where(g => g.UserId == id).ToListAsync(ct)).OrderByDescending(g => g.GrantedAt);
        return Results.Ok(new
        {
            items = grants.Select(g => new
            {
                g.Id, g.Kind, g.SourceKey, g.Days, g.Status, g.GrantedAt, g.StartsAt, g.EndsAt, g.AppliedAt, g.VoidedAt, g.VoidReason, g.VoidedBy,
                g.OriginUserId, g.OriginSubscriptionId, g.DeferredSubscriptionId, g.GoogleExpiryBefore, g.GoogleExpiryAfter, g.DeferAttempts, g.LastDeferError
            })
        });
    }

    /// <summary>Cancels a reward. The superadmin must say why; the reason and the actor stay on the ledger row and in the log.</summary>
    private static async Task<IResult> AdminVoidAsync(
        Guid id, VoidGrantRequest request, IPremiumGrantService grants, TrazoDbContext db, System.Security.Claims.ClaimsPrincipal principal,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length is < 5 or > 300)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["A reason of 5 to 300 characters is required."] });
        var actor = OperationsSettingsEndpoints.Actor(principal);
        var outcome = await grants.VoidAsync(id, reason, actor, ct);
        loggers.CreateLogger("Trazo.Admin").LogInformation("Superadmin {Actor} voided premium grant {GrantId}: {Outcome}. Reason: {Reason}", actor, id, outcome, reason);
        if (outcome == VoidOutcome.NotFound) return Results.NotFound();
        if (outcome == VoidOutcome.NotReversible)
            return Results.Conflict(new { error = "not_reversible", message = "Google already deferred the renewal for this reward; it cannot be taken back." });
        if (outcome == VoidOutcome.Voided)
        {
            var now = DateTimeOffset.UtcNow;
            await db.ReferralRelationships.Where(r => r.RewardGrantId == id)
                .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, ReferralStatuses.Voided).SetProperty(r => r.Incident, "voided_by_admin").SetProperty(r => r.UpdatedAt, now), ct);
        }
        return Results.Ok(new { outcome = outcome.ToString() });
    }

    private static async Task<IResult> AdminRetryAsync(Guid id, IReferralRewardService rewards, System.Security.Claims.ClaimsPrincipal principal, ILoggerFactory loggers, CancellationToken ct)
    {
        var outcome = await rewards.ResolveAsync(id, ct);
        loggers.CreateLogger("Trazo.Admin").LogInformation("Superadmin {Actor} retried applying premium grant {GrantId}: {Outcome}", OperationsSettingsEndpoints.Actor(principal), id, outcome);
        return Results.Ok(new { outcome = outcome.ToString() });
    }

    private static async Task<IResult> AdminReconcileAsync(ReferralReconciler reconciler, System.Security.Claims.ClaimsPrincipal principal, ILoggerFactory loggers, CancellationToken ct)
    {
        var result = await reconciler.RunOnceAsync(ct);
        loggers.CreateLogger("Trazo.Admin").LogInformation("Superadmin {Actor} ran the referral reconciliation: {Result}", OperationsSettingsEndpoints.Actor(principal), JsonSerializer.Serialize(result));
        return Results.Ok(result);
    }
}
