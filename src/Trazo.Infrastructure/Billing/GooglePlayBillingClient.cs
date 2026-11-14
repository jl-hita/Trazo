using System.Net;
using Google;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Trazo.Infrastructure.Settings;

namespace Trazo.Infrastructure.Billing;

/// <summary>Google Play Developer API client (androidpublisher v3). Reads its configuration on every call.</summary>
public sealed class GooglePlayBillingClient(IRuntimeSettings settings) : IPlayBillingClient
{
    public async Task<PlaySubscription> GetSubscriptionAsync(string purchaseToken, CancellationToken ct)
    {
        var (service, package) = await CreateServiceAsync(ct);
        using var _ = service;
        SubscriptionPurchaseV2 purchase;
        try
        {
            purchase = await service.Purchases.Subscriptionsv2.Get(package, purchaseToken).ExecuteAsync(ct);
        }
        catch (GoogleApiException ex)
        {
            throw Translate(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PlayBillingException(PlayBillingFailure.Unavailable, "Google Play could not be reached.", ex);
        }

        var item = purchase.LineItems?.FirstOrDefault()
            ?? throw new PlayBillingException(PlayBillingFailure.InvalidPurchase, "The purchase has no subscription items.");
        // With several items (add-ons) the subscription lasts as long as the latest item.
        var expiry = purchase.LineItems!.Select(x => x.ExpiryTimeDateTimeOffset).Where(x => x is not null).Max()
            ?? throw new PlayBillingException(PlayBillingFailure.InvalidPurchase, "The purchase has no expiry time.");
        return new PlaySubscription(
            purchase.SubscriptionState ?? "",
            item.ProductId ?? "",
            purchase.StartTimeDateTimeOffset ?? DateTimeOffset.UtcNow,
            expiry,
            item.AutoRenewingPlan?.AutoRenewEnabled == true,
            item.OfferPhase?.FreeTrial is not null,
            purchase.AcknowledgementState == "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
            item.LatestSuccessfulOrderId,
            purchase.ExternalAccountIdentifiers?.ObfuscatedExternalAccountId,
            purchase.TestPurchase is not null);
    }

    public async Task AcknowledgeAsync(string productId, string purchaseToken, CancellationToken ct)
    {
        var (service, package) = await CreateServiceAsync(ct);
        using var _ = service;
        try
        {
            await service.Purchases.Subscriptions
                .Acknowledge(new SubscriptionPurchasesAcknowledgeRequest(), package, productId, purchaseToken)
                .ExecuteAsync(ct);
        }
        catch (GoogleApiException ex)
        {
            throw Translate(ex);
        }
        catch (HttpRequestException ex)
        {
            throw new PlayBillingException(PlayBillingFailure.Unavailable, "Google Play could not be reached.", ex);
        }
    }

    public async Task CheckConnectionAsync(CancellationToken ct)
    {
        try
        {
            await GetSubscriptionAsync("trazo-connection-check", ct);
        }
        catch (PlayBillingException ex) when (ex.Failure == PlayBillingFailure.InvalidPurchase)
        {
            // Google authenticated the service account and looked the (fake) token up: credentials work.
        }
    }

    private async Task<(AndroidPublisherService Service, string Package)> CreateServiceAsync(CancellationToken ct)
    {
        var config = await settings.GetPlayBillingAsync(ct);
        if (!config.IsComplete)
            throw new PlayBillingException(PlayBillingFailure.NotConfigured,
                "Google Play Billing is not configured. A superadmin must save the package name and service account.");
        GoogleCredential credential;
        try
        {
            credential = CredentialFactory.FromJson<ServiceAccountCredential>(config.ServiceAccountJson!)
                .ToGoogleCredential()
                .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
        {
            throw new PlayBillingException(PlayBillingFailure.NotConfigured, "The saved service-account key is not valid.", ex);
        }
        var service = new AndroidPublisherService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "TRAZO"
        });
        return (service, config.PackageName);
    }

    private static PlayBillingException Translate(GoogleApiException ex) => ex.HttpStatusCode switch
    {
        HttpStatusCode.NotFound or HttpStatusCode.BadRequest or HttpStatusCode.Gone =>
            new PlayBillingException(PlayBillingFailure.InvalidPurchase, "Google Play does not recognise this purchase.", ex),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new PlayBillingException(PlayBillingFailure.Unauthorized,
                "Google Play rejected the service account. Check it is linked in Play Console with access to subscriptions.", ex),
        _ => new PlayBillingException(PlayBillingFailure.Unavailable, "Google Play returned an error. Try again later.", ex)
    };
}
