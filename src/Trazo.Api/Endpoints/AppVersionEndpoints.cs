using System.Security.Claims;
using Trazo.Infrastructure.Settings;

namespace Trazo.Api.Endpoints;

public sealed record AppVersionRequest(int MinimumAndroidVersionCode, int RecommendedAndroidVersionCode);

/// <summary>
/// The oldest version of the Android app that may still be used, and the one users are invited to update to. The app asks when it
/// opens and shows the notice itself: no request is rejected here, so nothing queued on a phone is ever lost. 0 means off.
/// See docs/configuration.md.
/// </summary>
public static class AppVersionEndpoints
{
    public static IEndpointRouteBuilder MapAppVersionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Public: it must answer before anyone signs in, and it holds nothing private.
        endpoints.MapGet("/api/v1/app/version-policy", async (IRuntimeSettings s, CancellationToken ct) =>
                Results.Ok(Body(await s.GetAppVersionAsync(ct))))
            .WithTags("App").AllowAnonymous().RequireRateLimiting(ReferralEndpoints.PublicRateLimitPolicy);

        var admin = endpoints.MapGroup("/api/v1/admin/settings/app-version").WithTags("Settings")
            .RequireAuthorization(SetupEndpoints.SuperadminPolicy).RequireRateLimiting(SetupEndpoints.SetupPolicy);
        admin.MapGet("", async (IRuntimeSettings s, CancellationToken ct) => Results.Ok(Body(await s.GetAppVersionAsync(ct))));
        admin.MapPut("", SaveAsync);
        return endpoints;
    }

    private static async Task<IResult> SaveAsync(
        AppVersionRequest request, ISettingsWriter writer, IRuntimeSettings settings, ClaimsPrincipal principal,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var before = await settings.GetAppVersionAsync(ct);
        var input = new AppVersionConfig(request.MinimumAndroidVersionCode, request.RecommendedAndroidVersionCode);
        var errors = await writer.UpdateAppVersionAsync(input, ct);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        loggerFactory.CreateLogger("Trazo.Admin").LogInformation(
            "Superadmin {Actor} changed the Android version policy: minimum {MinimumBefore} -> {Minimum}, recommended {RecommendedBefore} -> {Recommended}.",
            OperationsSettingsEndpoints.Actor(principal), before.MinimumAndroidVersionCode, input.MinimumAndroidVersionCode,
            before.RecommendedAndroidVersionCode, input.RecommendedAndroidVersionCode);
        return Results.Ok(Body(input));
    }

    private static object Body(AppVersionConfig c) => new
    {
        minimumAndroidVersionCode = c.MinimumAndroidVersionCode,
        recommendedAndroidVersionCode = c.RecommendedAndroidVersionCode
    };
}
