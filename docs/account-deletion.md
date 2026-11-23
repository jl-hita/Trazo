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
| `device_tokens` (push), `refresh_tokens`, `auth_challenges`, `auth_recovery_codes`, `email_action_tokens` | sessions, 2FA challenges, recovery codes, email links |
| `subscriptions` | Google Play purchase (including the encrypted purchase token) |
| `training_plans`, `training_plan_phases`, `training_mesocycles`, `planned_sessions` | profile (limitations, equipment) and the plan |
| `workout_logs`, `workout_set_logs` | logged sessions: loads, repetitions, RIR, recovery, **pain reported, free notes** |
| `plan_adaptations` | changes the engine made to the plan |
| `muscle_week_observations` | per muscle and week observations (deleted before the plans; an anonymous copy of the counters is kept, see "Anonymous engine research") |
| `longitudinal_decisions` | audit log of every volume decision of longitudinal learning (deleted before the plans; an anonymous copy of the structured fields is kept) |
| `frequency_proposals` | proposals and decisions to reorganise the training days (deleted before the plans) |
| `waitlist_entries` | the landing waitlist row with the same email as the account (not linked by id; matched by the normalised email) |
| `plan_review_requests`, `plan_review_messages` | manual plan reviews: the user's conversations (may contain health data) are deleted; when an administrator is deleted, their reviews go back to the queue and only the reference to them is cleared |

A test (`EveryTableOfTheModelIsEitherDeletedWithTheAccountOrKnownNotToBePersonal`) walks the EF model and fails if a table exists that is
neither in `AccountData.UserOwned` (deleted) nor in `AccountData.NotPersonal` (catalog, settings, anonymous research). Add new user tables to `AccountService` too.
A second test walks the two research tables and fails if they gain an id of a person or plan, a foreign key, a date, a free-text column or an unexpected type.

## What is kept, and why

- **Nothing that identifies the user is kept in TRAZO**, except what the next section describes, which is anonymous and is not about the
  user any more. Subscription payments are processed by Google Play, which is the merchant of record and
  keeps the legally required billing records; TRAZO holds only a verified cache of the purchase, which is deleted with the account.
  No anonymised subscription record is retained (it would add no legal value and would keep a purchase fingerprint).
- **Audit log:** one line `Account deleted: subject <16 hex of a SHA-256>, initiated by self|web-link|superadmin <id>, N rows removed.`
  The subject tag is not reversible and there is no email or name. (For admin deletions the acting superadmin's id is logged.)
- **Backups:** if the server database is backed up, deleted data disappears from backups when they rotate out. State the backup retention in the policy.

## Anonymous engine research (kept on purpose)

**Status: needs legal review before the privacy policy that mentions it is published.** The design below is meant to be anonymous in the
sense of recital 26 GDPR (nobody can single the person out with means reasonably likely to be used); that is a legal judgement, not only
a technical one, and it has not been made by a lawyer or a DPO yet.

When an account is deleted, `AccountService.DeleteAsync` first copies a few allowed fields into two tables **in the same transaction** as
the deletes (so a failed deletion rolls the copy back and leaves no orphan rows), and then deletes everything else as described above.
The copy is built by `EngineResearchSampler`, which works by allow-list.

| Table | What it holds | Source |
|---|---|---|
| `engine_research_observations` | per muscle and plan period: `Period`, `MesocycleSequence`, `IsDeload`, `Muscle`, `Quality`, `Reason`, prescribed/performed sets, sessions planned/done, exposures, completed and below-range sets, performance change %, performance samples, fatigue signals, feedback / low-stimulus / discomfort answers, pain sessions, `ReturnFromAbsence` | `muscle_week_observations` |
| `engine_research_decisions` | `MesocycleSequence`, `Muscle`, `Action`, baseline / previous / proposed sets, analysed period range, `PolicyVersion` | `longitudinal_decisions` |

Both tables also repeat a **generalised plan profile**: goal, experience, split, readiness (`Regular`, `New`, `Returning`), engine version,
sessions per period (capped at 7) and plan length rounded **up to a multiple of 4** weeks (capped at 52).

### What is deliberately NOT copied

- Any user id, plan id or original row id; nothing maps the new random row ids back to the old ones, and no mapping is stored anywhere
  (a stored mapping would make it pseudonymisation, which is still personal data).
- Dates and times of any kind (`ProcessedThrough`, `UpdatedAt`, `CreatedAt`, plan start date, time zone, training days, `PerformedAt`).
- `LongitudinalDecision.Reason` (generated prose) and `SignalsJson` (unversioned JSON): they could carry anything, so they are dropped.
  `MuscleWeekObservation.Reason` is kept only if it is one of the closed codes the observer writes (`deload`, `preparation`, `pain`,
  `return_from_absence`, `date_unreliable`, `no_valid_exposure`, `low_completion`); anything else becomes null.
- From the plan: `Limitations`, `MusclePriorities`, `AvailableEquipment`, `ExcludedExerciseIds`, `Seed`, `TrainingDays`, exact `TotalWeeks`,
  `StartDate`, macrocycle texts. Text fields that are copied (goal, experience, split, muscle, versions) must match a strict token pattern
  (letters, digits, `_`, `-`, at most 30-40 characters) or are dropped; a row whose muscle or action is not a valid token is not exported.
- Workout logs (notes, pain, dates), exact weights, reviews and messages, emails, names, anything free-text. These are deleted as before.

### Consent: `ResearchConsent`

`users.ResearchConsent` (default **false**), set by `PUT /api/v1/account/research-consent` `{ "consent": true|false }` and reported in
`GET /api/v1/account` as `researchConsent`. It is optional, revocable at any time and never a condition of using the app (the Android
client has no screen for it yet; the backend is ready).

- **Without consent (default):** the rows are written independent of each other (`SampleGroupId` is null). Each row is one
  muscle-week counter plus a generalised profile; rows of the same person cannot be linked to each other.
- **With consent:** the rows of one deleted plan share a `SampleGroupId`, a **new random Guid per deletion** (not derived from the plan id
  or anything else, not stored elsewhere). It lets the engine study sequences (what happened after a volume decrease). This is the
  riskier of the two modes, because a long, unusual sequence can start to single someone out; that is why it needs the opt-in.

### Anonymisation criteria applied

1. No direct identifier and no foreign key (a test fails if the tables get a `UserId`/`PlanId`/email/name/text/date column or any FK).
2. No stored mapping to the original ids.
3. Allow-list copy: numbers, flags, closed vocabularies, tokens; never free text.
4. Generalisation of quasi-identifiers (plan length in bands, sessions capped, no dates, no limitations or equipment).
5. Linkage only with explicit opt-in, through a random id.

### Known limits (for the legal / DPO review)

- No k-anonymity threshold is enforced: a deleted account with a very unusual profile still produces rows. If the review asks for one,
  suppress rows whose (goal, experience, split, band, engine version) combination has fewer than k members, at write or at analysis time.
- The pain counters (`PainSessions`, `DiscomfortAnswers`, `Reason = pain`) are health-related in origin. They are counters with no
  person, date or body area attached, but this is the part to look at first under art. 9 GDPR.
- Rows cannot be erased on request after deletion (nobody can be identified), which the privacy policy states.
- The two tables are not part of `AccountData.UserOwned`; they are in `AccountData.NotPersonal`, with the reasoning in a comment there.
- Database backups taken before a deletion still contain the original rows until they rotate out.

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
- **Migrations:** `20261114140000_UserTimeZone` adds `users."TimeZoneId"`; `20261203100000_EngineResearchSamples` adds `users."ResearchConsent"` and the two research tables.
