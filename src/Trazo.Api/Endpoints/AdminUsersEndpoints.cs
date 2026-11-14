using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Api.Endpoints;

/// <summary>User management for the administration console. Every route requires the superadmin role.</summary>
public static class AdminUsersEndpoints
{
    private static readonly string[] Roles = ["user", "admin", "superadmin"];
    private const string SuperadminRole = "superadmin";

    public static IEndpointRouteBuilder MapAdminUsersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/users")
            .WithTags("Admin users")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy);

        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPatch("/{id:guid}", UpdateAsync);
        group.MapPost("/{id:guid}/disable", DisableAsync);
        group.MapPost("/{id:guid}/enable", EnableAsync);
        group.MapPost("/{id:guid}/revoke-sessions", RevokeSessionsAsync);
        group.MapPost("/{id:guid}/unlock", UnlockAsync);
        group.MapPost("/{id:guid}/verify-email", VerifyEmailAsync);
        group.MapDelete("/{id:guid}", DeleteAsync).WithSummary("Permanently deletes the account and all its data. The body must repeat the account email.");
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        TrazoDbContext db,
        string? q,
        string? role,
        string? status,
        string? sort,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1 || pageSize is < 1 or > 100) errors["paging"] = ["page must be positive and pageSize between 1 and 100."];
        if (q?.Length > 100) errors["q"] = ["The search text is too long."];
        if (!string.IsNullOrWhiteSpace(role) && !Roles.Contains(role)) errors["role"] = ["Unknown role."];
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "disabled" or "unverified"))
            errors["status"] = ["Use active, disabled or unverified."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        IQueryable<Trazo.Domain.Identity.TrazoUser> query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var upper = q.Trim().ToUpperInvariant();
            var lower = q.Trim().ToLowerInvariant();
            query = query.Where(x => x.NormalizedEmail.Contains(upper) || x.DisplayName.ToLower().Contains(lower));
        }
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(x => x.Role == role);
        query = status switch
        {
            "active" => query.Where(x => x.DisabledAt == null),
            "disabled" => query.Where(x => x.DisabledAt != null),
            "unverified" => query.Where(x => x.EmailVerifiedAt == null),
            _ => query
        };

        var totalCount = await query.CountAsync(cancellationToken);
        query = sort == "created"
            ? query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            : query.OrderBy(x => x.NormalizedEmail).ThenBy(x => x.Id);

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id, x.Email, x.DisplayName, x.Role, x.EmailVerifiedAt, x.TwoFactorEnabled,
                HasPassword = x.PasswordHash != null, HasGoogle = x.GoogleSubject != null,
                x.DisabledAt, x.LockoutEnd, x.CreatedAt, x.LastLoginAt
            })
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var items = rows.Select(r => new AdminUserSummary(
            r.Id, r.Email, r.DisplayName, r.Role, r.EmailVerifiedAt != null, r.TwoFactorEnabled,
            r.HasPassword, r.HasGoogle, r.DisabledAt != null, r.LockoutEnd is { } l && l > now,
            r.CreatedAt, r.LastLoginAt)).ToList();

        return Results.Ok(new AdminUserPage(items, page, pageSize, totalCount, (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    private static async Task<IResult> GetAsync(Guid id, TrazoDbContext db, CancellationToken cancellationToken)
    {
        var detail = await BuildDetailAsync(db, id, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateAdminUserRequest request,
        ClaimsPrincipal principal,
        TrazoDbContext db,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        var name = request.DisplayName?.Trim();
        if (name is not null && name.Length is < 2 or > 100) errors["displayName"] = ["Use between 2 and 100 characters."];
        if (request.Role is not null && !Roles.Contains(request.Role)) errors["role"] = ["Unknown role."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var actorId = ActorId(principal);
        if (request.Role is not null && request.Role != user.Role)
        {
            if (user.Id == actorId) return Conflict("You cannot change your own role.");
            if (user.Role == SuperadminRole && await IsLastActiveSuperadminAsync(db, user.Id, cancellationToken))
                return Conflict("At least one active superadmin must remain.");
            Log(loggerFactory, actorId, $"changed role {user.Role} -> {request.Role} of", user.Id);
            user.Role = request.Role;
        }
        if (name is not null && name != user.DisplayName)
        {
            user.DisplayName = name;
            Log(loggerFactory, actorId, "renamed", user.Id);
        }
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id, [Microsoft.AspNetCore.Mvc.FromBody] DeleteAdminUserRequest request, ClaimsPrincipal principal, TrazoDbContext db,
        Trazo.Infrastructure.Accounts.IAccountService accounts, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var email = await db.Users.AsNoTracking().Where(x => x.Id == id).Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);
        if (email is null) return Results.NotFound();
        if (id == ActorId(principal)) return Conflict("You cannot delete your own account here; use the account deletion of the app.");
        // Explicit confirmation: the superadmin has to type the email of the account being erased.
        if (!string.Equals(request.ConfirmEmail?.Trim(), email, StringComparison.OrdinalIgnoreCase))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["confirmEmail"] = ["Type the exact email of the account to confirm."] });
        var result = await accounts.DeleteAsync(id, cancellationToken);
        if (!result.Deleted) return result.Blocked == Trazo.Infrastructure.Accounts.AccountDeletionBlock.NotFound ? Results.NotFound() : Conflict("At least one active superadmin must remain.");
        AccountEndpoints.Audit(loggerFactory, "superadmin " + ActorId(principal), result);
        return Results.Ok(new { deleted = true, rowsDeleted = result.RowsDeleted, playSubscriptionNotCancelled = result.PlaySubscriptionNotCancelled });
    }

    private static async Task<IResult> DisableAsync(
        Guid id, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return Results.NotFound();
        var actorId = ActorId(principal);
        if (user.Id == actorId) return Conflict("You cannot disable your own account.");
        if (user.Role == SuperadminRole && await IsLastActiveSuperadminAsync(db, user.Id, cancellationToken))
            return Conflict("At least one active superadmin must remain.");

        var now = DateTimeOffset.UtcNow;
        user.DisabledAt ??= now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await RevokeAllSessionsAsync(db, id, now, cancellationToken);
        Log(loggerFactory, actorId, "disabled", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> EnableAsync(
        Guid id, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return Results.NotFound();
        user.DisabledAt = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Log(loggerFactory, ActorId(principal), "enabled", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> RevokeSessionsAsync(
        Guid id, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(x => x.Id == id, cancellationToken)) return Results.NotFound();
        await RevokeAllSessionsAsync(db, id, DateTimeOffset.UtcNow, cancellationToken);
        Log(loggerFactory, ActorId(principal), "revoked the sessions of", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> UnlockAsync(
        Guid id, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return Results.NotFound();
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Log(loggerFactory, ActorId(principal), "unlocked", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    private static async Task<IResult> VerifyEmailAsync(
        Guid id, ClaimsPrincipal principal, TrazoDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return Results.NotFound();
        var now = DateTimeOffset.UtcNow;
        user.EmailVerifiedAt ??= now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        Log(loggerFactory, ActorId(principal), "marked the email as verified for", id);
        return Results.Ok(await BuildDetailAsync(db, id, cancellationToken));
    }

    /// <summary>True when no other active superadmin exists, so changing this account would lock everyone out.</summary>
    private static async Task<bool> IsLastActiveSuperadminAsync(TrazoDbContext db, Guid userId, CancellationToken cancellationToken) =>
        !await db.Users.AnyAsync(x => x.Role == SuperadminRole && x.DisabledAt == null && x.Id != userId, cancellationToken);

    private static Task<int> RevokeAllSessionsAsync(TrazoDbContext db, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(x => x.UserId == userId && x.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), cancellationToken);

    private static async Task<AdminUserDetail?> BuildDetailAsync(TrazoDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null) return null;
        var now = DateTimeOffset.UtcNow;
        var tokens = await db.RefreshTokens.AsNoTracking()
            .Where(x => x.UserId == id && x.RevokedAt == null)
            .ToListAsync(cancellationToken);
        var sessions = tokens
            .Where(t => t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new AdminUserSession(t.DeviceName, t.CreatedAt, t.ExpiresAt))
            .ToList();
        return new AdminUserDetail(
            user.Id, user.Email, user.DisplayName, user.Role, user.EmailVerifiedAt != null, user.TwoFactorEnabled,
            user.PasswordHash != null, user.GoogleSubject != null, user.DisabledAt != null,
            user.LockoutEnd is { } l && l > now, user.FailedLoginAttempts, user.CreatedAt, user.LastLoginAt, user.DisabledAt,
            sessions.Count, sessions.Take(10).ToList());
    }

    private static Guid? ActorId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("sub") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private static IResult Conflict(string message) => Results.Conflict(new { error = message });

    private static void Log(ILoggerFactory loggerFactory, Guid? actor, string action, Guid target) =>
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation("Superadmin {Actor} {Action} user {Target}", actor, action, target);
}

public sealed record AdminUserSummary(
    Guid Id, string Email, string DisplayName, string Role, bool EmailVerified, bool TwoFactorEnabled,
    bool HasPassword, bool HasGoogle, bool Disabled, bool LockedOut, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

public sealed record AdminUserPage(IReadOnlyList<AdminUserSummary> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record AdminUserSession(string? DeviceName, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record AdminUserDetail(
    Guid Id, string Email, string DisplayName, string Role, bool EmailVerified, bool TwoFactorEnabled,
    bool HasPassword, bool HasGoogle, bool Disabled, bool LockedOut, int FailedLoginAttempts,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, DateTimeOffset? DisabledAt,
    int ActiveSessionCount, IReadOnlyList<AdminUserSession> Sessions);

public sealed record DeleteAdminUserRequest(string? ConfirmEmail);

public sealed record UpdateAdminUserRequest(string? DisplayName, string? Role);
