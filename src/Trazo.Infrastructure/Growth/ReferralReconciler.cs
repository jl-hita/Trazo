using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trazo.Domain.Billing;
using Trazo.Domain.Growth;
using Trazo.Infrastructure.Billing;
using Trazo.Infrastructure.Persistence;

namespace Trazo.Infrastructure.Growth;

/// <summary>Reacts to verified subscription changes: converts referrals, cancels rewards on refunds, applies pending rewards.</summary>
public sealed class ReferralSubscriptionObserver(IReferralRewardService rewards) : ISubscriptionObserver
{
    public async Task OnChangedAsync(SubscriptionChange change, CancellationToken ct)
    {
        if (change.Revoked) await rewards.HandleRevocationAsync(change.Row, ct);
        else if (change.FirstPaid) await rewards.ProcessConversionAsync(change.Row, ct);
        // Whatever happened to this user's own subscription (cancelled, paid, expired) may let a pending reward be applied.
        await rewards.ResolvePendingAsync(change.Row.UserId, ct);
    }
}

public sealed record ReconcileResult(int Conversions, int PendingResolved, int PendingWaiting);

/// <summary>
/// Safety net for everything the event-driven path may have missed (a crash between steps, a transient Google error): converts
/// invitations whose invited user is already stored as paid, and retries pending rewards. Idempotent: running it twice is harmless.
/// </summary>
public sealed class ReferralReconciler(TrazoDbContext db, IReferralRewardService rewards, TimeProvider clock, ILogger<ReferralReconciler> logger)
{
    public static readonly TimeSpan RetryEvery = TimeSpan.FromMinutes(30);

    public async Task<ReconcileResult> RunOnceAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var conversions = 0;
        var invited = await db.ReferralRelationships.AsNoTracking().Where(r => r.Status == ReferralStatuses.Registered)
            .Select(r => r.ReferredUserId).ToListAsync(ct);
        foreach (var userId in invited)
        {
            var paid = (await db.Subscriptions.AsNoTracking().Where(s => s.UserId == userId && s.PaidAt != null).ToListAsync(ct))
                .Where(s => s.Status is not (SubscriptionStatus.Revoked or SubscriptionStatus.Pending)).OrderBy(s => s.PaidAt).FirstOrDefault();
            if (paid is null) continue;
            await rewards.ProcessConversionAsync(paid, ct);
            conversions++;
        }

        var resolved = 0;
        var waiting = 0;
        var pending = (await db.PremiumGrants.AsNoTracking().Where(g => g.Status == PremiumGrantStatuses.Pending).ToListAsync(ct))
            .Where(g => now - g.UpdatedAt >= RetryEvery || g.DeferAttempts == 0).ToList();
        foreach (var grant in pending)
        {
            var outcome = await rewards.ResolveAsync(grant.Id, ct);
            if (outcome is DeferOutcome.Deferred or DeferOutcome.Activated) resolved++; else waiting++;
        }
        if (conversions + resolved > 0) logger.LogInformation("Referral reconciliation: {Conversions} conversions, {Resolved} rewards applied, {Waiting} still pending.", conversions, resolved, waiting);
        return new ReconcileResult(conversions, resolved, waiting);
    }
}

public sealed class ReferralReconcilerWorker(IServiceScopeFactory scopes, ILogger<ReferralReconcilerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ReferralReconciler>().RunOnceAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Google being down or the database being busy pass by themselves: try again next round. Anything else is a defect that
                    // would repeat every round without anybody noticing, so it is logged as an error (an error ticket).
                    if (ex is System.Net.Http.HttpRequestException or TimeoutException or System.Data.Common.DbException or PlayBillingException)
                        logger.LogWarning(ex, "Referral reconciliation failed; it will run again.");
                    else logger.LogError(ex, "Referral reconciliation failed; it will run again.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }
}
