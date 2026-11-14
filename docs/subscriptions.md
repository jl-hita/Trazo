# Subscriptions (Google Play Billing)

TRAZO is paid, with a free trial of about 14 days configured as an offer on the base plan in Google Play.
Payments are handled **only by Google Play Billing**. The backend is the source of truth for access: the
Android app never decides on its own whether the user has paid.

## Flow

1. The app starts the purchase with Play Billing, setting the TRAZO user id (the JWT `sub`) as the
   *obfuscated account id*.
2. The app sends the result to `POST /api/v1/subscription/verify` with `productId` and `purchaseToken`.
3. The API asks Google (`purchases.subscriptionsv2.get`), checks the product, links the token to the user,
   acknowledges the purchase (`purchases.subscriptions.acknowledge`; Google refunds unacknowledged purchases
   after three days) and returns the state.
4. Google Pub/Sub sends real-time developer notifications (RTDN) to `POST /api/v1/subscription/notifications`.
   The notification body is **not trusted**: the API only takes the purchase token from it and asks Google again.
   Voided purchases (refunds) revoke access at once.
5. The app calls `GET /api/v1/subscription` to learn the state and whether the user has access.

## Endpoints

| Route | Auth | Purpose |
|---|---|---|
| `POST /api/v1/subscription/verify` | user | Validate a purchase and link it to the caller. `200` state, `400` unknown purchase/wrong product, `409` token already linked to another account or bought by another account, `502/503` Google problem. |
| `GET /api/v1/subscription` | user | `{ hasAccess, status, productId, startedAt, expiresAt, autoRenewing, source }`. `status` is `none` without a subscription. |
| `POST /api/v1/subscription/notifications` | Pub/Sub OIDC token or `?secret=` | RTDN webhook. Always `200` for messages that can be ignored (test, unknown token, other package, malformed); `503` when Google cannot be reached so Pub/Sub redelivers; `401` without valid credentials. |
| `GET /api/v1/admin/subscriptions` | superadmin | Paged list. Filters: `q` (name/email), `status`, `source`, `user`, `expiresFrom`, `expiresTo`; `sort` = `expires`, `started` (default: last change). |
| `GET /api/v1/admin/subscriptions/{id}` | superadmin | Detail (token fingerprint only, never the token). |
| `POST /api/v1/admin/subscriptions/{id}/refresh` | superadmin | Re-read the purchase from Google. |
| `GET/PUT /api/v1/admin/settings/play-billing`, `POST …/test` | superadmin | Configuration and connection test. |

## Statuses and access

`pending`, `trial`, `active`, `grace_period`, `on_hold`, `canceled`, `expired`, `revoked`.

Access (`SubscriptionAccess.Grants`) requires a **future expiry** and one of `trial`, `active`,
`grace_period` or `canceled`. A *cancelled* subscription still has access until its end date, because the
user already paid (or is in the trial) for that period. `on_hold`, `pending`, `expired` and `revoked` have none.

Google state mapping: `ACTIVE` → `active` (or `trial` while the offer phase is a free trial),
`IN_GRACE_PERIOD` → `grace_period`, `ON_HOLD` / `PAUSED` → `on_hold`, `PENDING` → `pending`,
`CANCELED` → `canceled` (or `expired` once past its end), `EXPIRED` / `PENDING_PURCHASE_CANCELED` → `expired`.

One purchase token belongs to exactly one user: `purchase_token_hash` has a unique index, and a second user
sending the same token gets `409`. A user can have several rows over time (re-subscribing creates a new
token); the API reports the one that grants access.

## Protecting endpoints

`RequireActiveSubscription()` (`Trazo.Api.Security`) is an endpoint filter for any route or group:

```csharp
var plans = app.MapGroup("/api/v1/plans").RequireActiveSubscription();
```

It answers `401` without a user, `402 { "error": "subscription_required" }` without access, and lets
`admin` and `superadmin` through. Administrators therefore reach the planning-preview endpoints under
`/api/v1/admin/...` as before.

**Which endpoints to protect.** Nothing existing is closed: the exercise catalog
(`/api/v1/exercises/...`) is public today and the code does not mark it as paid content. The endpoints that
should use the filter are the future planning ones (plan/mesocycle generation and the current week), serving only
the current week/mesocycle so the value cannot be downloaded in bulk by a lapsed subscriber. Whether the
catalogue itself becomes paid is a product decision to take when the app consumes it.

## Configuration

Saved by a superadmin in the administration console (Settings → Google Play Billing), stored in
`app_settings`; the service-account key and the notification secret are encrypted with Data Protection and are
never returned by the API:

| Setting | Meaning |
|---|---|
| Package name | e.g. `com.trazo.android`; notifications for other packages are ignored. |
| Service-account key (JSON) | Linked in Play Console with access to financial data/orders. |
| OIDC audience (+ optional sender email) | Pub/Sub push authentication (recommended): create the push subscription with a service account and this audience. |
| Shared secret | Alternative: append `?secret=<value>` to the push endpoint URL (URLs may appear in access logs). |

At least one of audience/secret is required for the webhook; otherwise it answers `503`. "Test connection"
queries Google with a fake token: an "unknown purchase" answer proves the credentials and package name work.
`app_settings.Value` was widened to 16000 characters to hold the encrypted key.

## Not covered

- The wizard on first start is unchanged; Play Billing is configured afterwards in Settings.
- There is no periodic reconciliation job yet: rows only change on verify, on a notification, or on an admin
  refresh. Expiry is still enforced on every access check from `ExpiresAt`.
