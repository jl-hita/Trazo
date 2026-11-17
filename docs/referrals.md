# Referral programme

> Invite a friend. If they end up paying their first Premium subscription, you earn 30 days of Premium for free.

The reward length, the welcome period and both on/off switches are superadmin settings (Settings → Promotions,
`GET/PUT /api/v1/admin/settings/promotions`). **Every economic decision is taken by the backend** from purchases it verified with
Google Play; the app only shows data and sends the code typed or received by the invited person.

## Rules

- Every user has one permanent, unique code (8 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789`: no 0/O/1/I/L), created on first use.
- A code is attached to a new account **only while the account is created** (registration form or first Google sign-in), through
  `referralCode`/`referralSource` (`link`, `install_referrer`, `manual`). Nothing attaches a code afterwards, so existing accounts can
  never become referrals and nobody can change referrer (`ReferredUserId` is unique).
- Rejected silently (registration still succeeds, no attribution): unknown/malformed code, own code, a second account on the same mailbox
  (Gmail dots and `+tag` aliases are folded), a disabled referrer, a second attribution, or the programme switched off.
- **Conversion** = the first payment actually taken, as reported by Google Play to the backend (`subscriptions.PaidAt`, never set for a
  free trial or a pending purchase). The monthly or annual plan are both valid. A trial starting, a renewal, a duplicate notification or
  a restored purchase never produces a second reward: the invitation leaves `registered` once, atomically, and the reward is a ledger row
  with a unique key (the invitation id).
- One reward per invited user; no cap on invitations or accumulated days; no expiry.
- Refund/void of the first payment within 60 days cancels the reward when it can still be cancelled (see below); later refunds keep it
  (`late_refund_kept`, visible to the administrator).

## How the reward is applied (Google Play)

The reward is a row of `premium_grants` (kind `referral_reward`) and never alters `subscriptions`.

| Referrer | Result |
|---|---|
| No subscription, or one that already ended | `active` promotional window starting now, chained after any other promotional time. |
| Subscription that will not renew (cancelled, not auto-renewing) | `active` window that starts when the paid period ends. |
| Auto-renewing, **paid** (`ACTIVE`, not a free trial) | The renewal is deferred with `purchases.subscriptionsv2.defer`. The reward becomes `deferred` **only if Google confirms** the new expiry. |
| Auto-renewing in a free trial or grace period | `pending`: waits for the first payment, then is deferred. |
| Deferral refused, fails, or Google's answer does not move the date | Stays `pending`, error stored in `LastDeferError`, retried on the next notification and every ~30 minutes by the reconciler. |

A `pending` reward becomes a promotional window the moment the subscription stops renewing (cancelled, expired, on hold), starting
after the paid period, so it is **never lost and never overlaps a period the referrer is paying for**. The backend never marks a reward as
deferred without reading the date back from Google.

### What the Google API allows (checked against the reference, not assumed)

`POST androidpublisher/v3/applications/{package}/purchases/subscriptionsv2/tokens/{token}:defer` with
`{"deferralContext": {"etag", "deferDuration": "<seconds>s", "validateOnly"}}`; response `itemExpiryTimeDetails` (new expiry per item).

- The `etag` must be the latest from `subscriptionsv2.get`; otherwise the call fails. The backend reads the purchase right before deferring.
- `deferDuration` applies to all items of the subscription. Google documents deferring from one day up to one year **per call**; several
  calls can be chained (no cumulative cap was found). Monthly and annual plans are treated the same: the next renewal moves by N days.
- `validateOnly: true` is a dry run. The backend always does one first, then the real call.
- Which subscription states can be deferred is **not stated** in the reference. The backend only tries `SUBSCRIPTION_STATE_ACTIVE`, auto-renewing,
  not in a free trial; anything else is treated as "not now". If Google refuses, the reward stays pending (see above) instead of guessing.
- A deferral cannot be undone through the API. If the first payment is later refunded after the reward was `deferred`, the days stay, the
  relationship gets the incident `reward_not_reversible` and shows in the admin incident list.
- Ambiguous outcome (timeout after Google applied it): the backend stores the expiry and order id **before** calling. On retry it reads the
  purchase; if the expiry is already `before + days` under the same order, it confirms without deferring again. A normal renewal in
  between changes the order id, so it is never mistaken for the reward.
- The Real-time developer notification for a deferral (`SUBSCRIPTION_DEFERRED`) is handled like every notification: the body is not
  trusted, the backend re-reads the purchase from Google.
- Not verified against a real purchase: the sandbox/license-tester flow must be run once in Play Console before activating payments (see
  "Before going live").

## API

| Route | Auth | Purpose |
|---|---|---|
| `GET /api/v1/referrals/me` | user | `{ enabled, rewardDays, code, link, stats }`; `stats` = invited, pending, converted, rewards, daysEarned, daysPending, daysDeferred. |
| `GET /api/v1/referrals/invitations?status=` | user | Own invitations: date, status, plan, reward days/state. **No name, e-mail or id of the invited person.** |
| `GET /api/v1/referrals/rewards` | user | Own ledger (welcome period and rewards). |
| `POST /api/v1/referrals/shared` | user | Counts a share (the app cannot know if anyone received it). |
| `POST /api/v1/analytics/events` | user | `onboarding_completed` only; money events are rejected. |
| `POST /api/v1/referrals/install-referrer` | public, per-IP limit | Counts an install whose Play referrer carried a valid code. |
| `GET /invite/{code}` | public, per-IP limit | Invitation page (strict CSP, no JS): counts the opening and links to Google Play with `referrer=trazo_ref=CODE`. |
| `GET /.well-known/assetlinks.json` | public | Android App Links file; served when `AppLinks:Sha256Fingerprints` is set. |
| `GET /api/v1/admin/referrals/summary?from&to` | superadmin | Totals and rates of the programme. |
| `GET /api/v1/admin/referrals?status&incidents&page` | superadmin | Invitations with both accounts and the reward state. |
| `GET /api/v1/admin/referrals/users/{id}/ledger` | superadmin | Full Premium-time history of one user. |
| `POST /api/v1/admin/referrals/grants/{id}/void` | superadmin | Cancels a reward; a reason (5–300 chars) is required and recorded with the actor. |
| `POST /api/v1/admin/referrals/grants/{id}/retry`, `/reconcile` | superadmin | Re-attempt applying a reward / run the safety-net sweep. |
| `GET /api/v1/admin/analytics/funnel?from&to&channel&reference` | superadmin | Signup → … → retention funnel for **all** accounts (filter `referral`/`organic`). |

Administrators cannot edit economic data: they can only void (with a reason) or retry. Registration accepts `referralCode` and
`referralSource`; the app sends `X-App-Version` (optional) for per-version analysis.

## Attribution: what is and is not possible

| Situation | Outcome |
|---|---|
| App installed, user taps the link | App Link opens the app, which keeps the code until registration (needs `assetlinks.json` served and verified). |
| Not installed | The link page sends the user to Google Play with a `referrer` parameter; the app reads it on first launch with the Play Install Referrer API and keeps the code. The Install Referrer is delivered for a limited time after the click (Google documents it as available for 90 days) and only through Google Play installs. |
| Link opened several times, registration finished later | The app keeps the **first** code until registration or until it is used; the server keeps the first referrer forever. |
| Code typed manually | Field at registration (source `manual`). |
| Installed from elsewhere (sideload, other store, web search), link opened in an in-app browser that does not hand over to Play, or a preinstalled/cleared app | Cannot be attributed automatically; the user can still type the code. |
| Sign-up on a different device from the click | Not attributable without fingerprinting, which is deliberately **not** done (privacy). |

## Funnel events and analytics

Event catalogue v1 (`growth_events`, see `docs/subscriptions.md`): `signup`, `email_verified`, `onboarding_completed` (app),
`signup_trial_granted`, `trial_started`, `first_purchase`, `renewed`, `canceled`, `expired`, `refunded` (backend, verified purchases) and
`referral_code_created`, `referral_shared`, `referral_link_opened`, `referral_install_attributed`, `referral_attributed`,
`referral_reward_granted`, `referral_reward_voided`. Events of a referred user carry `channel=referral` and `reference=<code>`. Plan
(`first_purchase.plan`), app version (`signup.appVersion`) and an unused `variant` column are there for cohorts and future experiments.
First plan and first workout are derived from plans and logs, so they also cover older accounts. Breakdowns with fewer than 3 users are
folded into "other". No e-mail, IP, device id or free text is stored; the user id is cleared when the account is deleted.

## Before going live

1. Play Console: licence testers and a sandbox purchase of a monthly and an annual plan; confirm `subscriptionsv2.defer` on a test
   subscription (the service account needs the permission to manage orders and subscriptions) and note which states Google accepts.
2. Serve `/.well-known/assetlinks.json` from the API host: set `AppLinks:Sha256Fingerprints` (Play App Signing certificate **and** the
   upload certificate if side-loading) and make sure the invitation host is the one in `Email:ActionBaseUrl`. Check with the Digital Asset
   Links tester.
3. Decide whether the Google Play free-trial offer stays: it is independent from the 30-day welcome period (both can stack).
4. Review the terms/privacy text for the programme (reward rules, refunds, abuse).
