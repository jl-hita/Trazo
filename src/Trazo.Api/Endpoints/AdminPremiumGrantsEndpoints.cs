using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Trazo.Api.Security;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Growth;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Endpoints;

/// <summary>Manual Premium grants are a superadmin-only adjustment, separate from store purchases and referral rewards.</summary>
public static class AdminPremiumGrantsEndpoints
{
    public static IEndpointRouteBuilder MapAdminPremiumGrantsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/users/{userId:guid}/premium-grants")
            .WithTags("Admin users")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);

        group.MapGet("", ListAsync).WithSummary("Lists the Premium grant ledger for a user.");
        group.MapPost("", GrantAsync).WithSummary("Grants a user a finite or permanent Premium period.");
        group.MapPost("/{grantId:guid}/revoke", RevokeAsync).WithSummary("Revokes one manual Premium grant without changing store purchases.");
        return endpoints;
    }

    private static async Task<IResult> ListAsync(Guid userId, TrazoDbContext db, TimeProvider clock, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, ct)) return Results.NotFound();
        var now = clock.GetUtcNow();
        var rows = await db.PremiumGrants.AsNoTracking().Where(x => x.UserId == userId)
            .OrderByDescending(x => x.GrantedAt).ToListAsync(ct);
        var history = rows.Select(x => new AdminPremiumGrantView(
            x.Id, x.Kind, x.Status, x.Days, x.IsPermanent, x.StartsAt, x.EndsAt, x.GrantReason, x.GrantedBy,
            x.GrantedAt, x.VoidedAt, x.VoidReason, x.VoidedBy,
            x.Status == PremiumGrantStatuses.Active && x.StartsAt <= now && (x.IsPermanent || x.EndsAt > now))).ToArray();
        return Results.Ok(history);
    }

    private static async Task<IResult> GrantAsync(
        Guid userId, AdminPremiumGrantRequest request, ClaimsPrincipal principal, TrazoDbContext db,
        IPremiumGrantService grants, TimeProvider clock, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == userId, ct)) return Results.NotFound();
        var reason = request.Reason?.Trim();
        var errors = new Dictionary<string, string[]>();
        if (reason is null || reason.Length is < 5 or > 300)
            errors["reason"] = ["Explain the reason using between 5 and 300 characters."];
        if (request.Permanent)
        {
            if (request.Days is not null) errors["days"] = ["Do not send days for a permanent grant."];
        }
        else if (request.Days is null or < 1 or > 3650)
            errors["days"] = ["Choose between 1 and 3650 days, or select a permanent grant."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var grantId = Guid.NewGuid();
        var actor = ActorId(principal);
        var result = await grants.GrantAsync(new GrantRequest(
            userId, PremiumGrantKinds.AdminGrant, $"admin:{grantId:N}", request.Permanent ? 0 : request.Days!.Value,
            Status: PremiumGrantStatuses.Active, IsPermanent: request.Permanent, Reason: reason, GrantedBy: actor?.ToString()), ct);

        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} granted {Days} days (permanent: {Permanent}) of Premium to user {Target}. Reason: {Reason}",
            actor, request.Permanent ? null : request.Days, request.Permanent, userId, reason);

        var row = result.Grant;
        var now = clock.GetUtcNow();
        return Results.Created($"/api/v1/admin/users/{userId}/premium-grants",
            new AdminPremiumGrantView(row.Id, row.Kind, row.Status, row.Days, row.IsPermanent, row.StartsAt,
                row.EndsAt, row.GrantReason, row.GrantedBy, row.GrantedAt, row.VoidedAt, row.VoidReason, row.VoidedBy,
                row.Status == PremiumGrantStatuses.Active && row.StartsAt <= now &&
                (row.IsPermanent || row.EndsAt > now)));
    }

    private static async Task<IResult> RevokeAsync(
        Guid userId, Guid grantId, AdminPremiumGrantRevokeRequest request, ClaimsPrincipal principal,
        TrazoDbContext db, IPremiumGrantService grants, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var grant = await db.PremiumGrants.SingleOrDefaultAsync(x => x.Id == grantId && x.UserId == userId, ct);
        if (grant is null) return Results.NotFound();
        if (grant.Kind != PremiumGrantKinds.AdminGrant)
            return Results.Conflict(new { error = "Only a manual administrative grant can be revoked here." });
        var reason = request.Reason?.Trim();
        if (reason is null || reason.Length is < 5 or > 300)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Explain the revocation using between 5 and 300 characters."] });

        var outcome = await grants.VoidAsync(grantId, reason, ActorId(principal)?.ToString() ?? "superadmin", ct);
        if (outcome == VoidOutcome.NotFound) return Results.NotFound();
        if (outcome == VoidOutcome.NotReversible)
            return Results.Conflict(new { error = "This grant was already applied as a Google Play renewal deferral and cannot be reversed." });
        if (outcome == VoidOutcome.AlreadyVoided)
            return Results.Conflict(new { error = "This grant has already been revoked." });

        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} revoked manual Premium grant {GrantId} for user {Target}. Reason: {Reason}",
            ActorId(principal), grantId, userId, reason);
        return Results.Ok(new { revoked = true, grantId });
    }

    private static Guid? ActorId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public sealed record AdminPremiumGrantRequest(int? Days, bool Permanent, string? Reason);
public sealed record AdminPremiumGrantRevokeRequest(string? Reason);
public sealed record AdminPremiumGrantView(
    Guid Id, string Kind, string Status, int Days, bool Permanent, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    string? Reason, string? GrantedBy, DateTimeOffset GrantedAt, DateTimeOffset? RevokedAt, string? RevocationReason, string? RevokedBy,
    bool IsCurrent);
