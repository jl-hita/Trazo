# Account deletion, data export and retention

This page is the technical source for the privacy policy and the Google Play "Data deletion" form.
**Public deletion URL:** `https://trazostrength.duckdns.org/account/delete`

## Ways to delete an account

| Way | How |
|---|---|
| In the app | `GET /api/v1/account` tells the app what to ask for and what to warn about; then `DELETE /api/v1/account` (or `POST /api/v1/account/delete` for clients that cannot send a DELETE body) with recent re-authentication. |
| On the web, without the app | `GET /account/delete` (public page, Spanish) -> the user types their email -> `POST /api/v1/account/deletion-request` -> email with a link -> `GET /account/delete/confirm?token=...` shows an explicit confirmation -> the button posts to `POST /api/v1/account/deletion-request/confirm`, and only then the data is erased. |
| By an administrator | `DELETE /api/v1/admin/users/{id}` (superadmin only) with `{"confirmEmail": "<the account email>"}`. Not allowed on one's own account. The Users page of the console has a "Delete account permanently" box that asks to type the email. |

### Re-authentication (in the app)

Body of `DELETE /api/v1/account`: `{ "password": "..." }` **or** `{ "idToken": "<Google ID token>" }`, plus `"code"` when the account has
two-factor authentication (TOTP or an unused recovery code).

- Accounts with a password need the password. Google-only accounts need a Google ID token issued in the last 5 minutes whose
  subject is the linked Google account.
- Wrong answers count towards the sign-in lockout (5 failures -> 15 minutes). The route is also rate limited per IP.
- Answers: `200 {deleted, playSubscriptionNotCancelled, manageSubscriptionUrl}`, `403 reauthentication_failed`,
  `409 last_superadmin`, `401`.
- The last active superadmin can never be deleted (`GET /api/v1/account` reports `canDelete: false`).

### Web flow guarantees

- `POST /api/v1/account/deletion-request` (form or JSON) **always** answers the same (`202`), whether or not the email has an account,
  is malformed or is rate limited. The email is sent in the background so the response time does not reveal it either.
- Limits: 10 requests per hour per IP and 3 links per hour per email address (counted for any address, known or not).
- The link uses `EmailActionToken` (purpose `delete-account`): single use, valid for 30 minutes, stored only as a SHA-256 hash, and a new
  request invalidates the previous link. Opening the link deletes nothing; the confirmation page needs a click on a POST form. Accounts with
  two-factor authentication must also enter a code there.
- The link base is `Account:PublicBaseUrl` (default `https://trazostrength.duckdns.org`), not `Email:ActionBaseUrl` (which may be an app
  deep link) and never the request's `Host` header.
- The page is plain HTML with no JavaScript and no external resources. CSP: `default-src 'none'; style-src 'sha256-...'; form-action 'self';
  base-uri 'none'; frame-ancestors 'none'`; also `no-store`, `no-referrer`, `nosniff`, `X-Frame-Options: DENY`.

## What is deleted

Everything is removed in one database transaction (explicit deletes, children first; each step checks that no row is left, otherwise the
whole transaction is rolled back). All sessions end with it: refresh tokens are deleted and access tokens are rejected because the user no longer exists.

| Table | Content |
|---|---|
| `users` | email, name, password hash, Google subject, 2FA key, time zone |
| `refresh_tokens`, `auth_challenges`, `auth_recovery_codes`, `email_action_tokens` | sessions, 2FA challenges, recovery codes, email links |
| `subscriptions` | Google Play purchase (including the encrypted purchase token) |
| `training_plans`, `training_plan_phases`, `training_mesocycles`, `planned_sessions` | profile (limitations, equipment) and the plan |
| `workout_logs`, `workout_set_logs` | logged sessions: loads, repetitions, RIR, recovery, **pain reported, free notes** |
| `plan_adaptations` | changes the engine made to the plan |

A test (`EveryTableOfTheModelIsEitherDeletedWithTheAccountOrKnownNotToBePersonal`) walks the EF model and fails if a table exists that is
neither in `AccountData.UserOwned` (deleted) nor in `AccountData.NotPersonal` (catalog, settings). Add new user tables to `AccountService` too.

## What is kept, and why

- **Nothing about the user is kept in TRAZO.** Subscription payments are processed by Google Play, which is the merchant of record and
  keeps the legally required billing records; TRAZO holds only a verified cache of the purchase, which is deleted with the account.
  No anonymised subscription record is retained (it would add no legal value and would keep a purchase fingerprint).
- **Audit log:** one line `Account deleted: subject <16 hex of a SHA-256>, initiated by self|web-link|superadmin <id>, N rows removed.`
  The subject tag is not reversible and there is no email or name. (For admin deletions the acting superadmin's id is logged.)
- **Backups:** if the server database is backed up, deleted data disappears from backups when they rotate out. State the backup retention in the policy.

## Application logs (`app_logs`)

The application never writes emails or names to the log: only user ids (random GUIDs) in a few warnings (for example a rejected purchase),
and exception texts. On deletion, log lines containing the user's id are removed (best effort, PostgreSQL only). Everything else is purged
by the log retention (30 days by default, editable in Settings). Mail-sending failures are logged by exception type only, because SMTP errors can quote
the recipient. Reverse proxy access logs (nginx) hold IPs and URLs (the confirmation URL contains the single-use token) and rotate under the host's policy.

## Google Play subscription

Deleting the account does **not** cancel the Google Play subscription. The API does not cancel it on the user's behalf: the Play Developer API can
cancel or revoke a subscription, but it needs a service account with financial permissions, `revoke` refunds money, and a failure would leave a
deleted user still being billed without any warning. Instead:

- `GET /api/v1/account` -> `deletion.playSubscriptionNotCancelled`, `deletion.warning` and `deletion.manageSubscriptionUrl` so the app warns first;
- the deletion answer repeats `playSubscriptionNotCancelled` and `manageSubscriptionUrl`; the web page and the email say it too.

A Play notification (RTDN) for a purchase that is no longer linked is ignored.

## Export (access and portability)

`GET /api/v1/account/export` (signed in) returns a JSON file (`Content-Disposition: attachment`) with the account, plans (phases and sessions with their
prescription), logged workouts and sets (including pain and notes), adaptations, subscriptions (product, state, dates) and devices.
It never contains password or token hashes, the 2FA key, recovery codes, or purchase tokens.

## Operations

- **nginx:** the Angular fallback would swallow `/account/delete`. Add to the TRAZO server block (before `location /`) and reload nginx:

  ```nginx
  location ^~ /account/delete {
      proxy_pass http://127.0.0.1:5001;
      proxy_set_header Host $host;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto $scheme;
  }
  ```

  `deploy/nginx/trazo.conf` has it for reference; the live file is edited by hand because certbot changes it. Do not add a conflicting
  `Content-Security-Policy` in nginx for that path.
- **Email:** SMTP must be configured (Settings); otherwise the web flow answers normally but no link is sent.
- **Migration:** `20261114140000_UserTimeZone` adds `users."TimeZoneId"`; account deletion needs no schema change.
