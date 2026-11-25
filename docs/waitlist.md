# Waitlist and closed-test invitations (public landing form)

The landing page at the root of the site collects the e-mail addresses of people who want to be told when the app opens
**and** to be invited to the closed test on Google Play. The form asks for the address of the person's **Google account**
(Google Play grants access to the test by that account). One consent covers both purposes (launch notice and invitation);
its text and the privacy policy say so.

## Flow

1. The person submits the form → the address is stored **unconfirmed** and a confirmation e-mail is sent.
2. The person opens the link, presses **Confirm** (page `/waitlist/confirm`) → the address is confirmed (double opt-in).
   Until then nothing else is ever sent to it.
3. The superadmin, in the console (**Waitlist**), copies the confirmed addresses that have not been invited, pastes them
   in Play Console (closed test → testers) and saves, then presses **Send invitations**. The invitation holds the Play
   opt-in link (`Waitlist:TestInviteUrl`, default `https://play.google.com/apps/testing/com.trazo.android`) and the
   person's unsubscribe link. The invitation goes **only to the addresses that were copied** (the console remembers
   them), so an address that confirmed meanwhile is not invited before it is in the Play list.
4. The person unsubscribes from any invitation with its link (page `/waitlist/unsubscribe`), which deletes the row.

Play Console tester lists are managed by hand: the Play Developer API only manages Google Groups, which needs Google
Workspace or Cloud Identity. If a domain is registered later, step 3 can be replaced by adding the address to a group.

## Public endpoints

All anonymous, same origin as the landing, **no CORS policy**, 5 requests per minute per client IP (`429` beyond that;
the IP comes from `X-Forwarded-For` through the trusted reverse proxy). Nothing logs an address.

`POST /api/v1/waitlist`

```json
{ "email": "ana@example.com", "language": "es", "consent": true }
```

| Rule | Behaviour |
| --- | --- |
| Validation | `email` must be a valid address (max 254), `language` is one the API has texts for (`es`, `en` and every language with a translation file), `consent` must be `true`; otherwise `400` with the field errors. |
| Success | `202 {"accepted": true}`. |
| Address already on the list | **Identical** `202` answer and no change to the stored row, so the form cannot be used to find out who signed up. |
| Confirmation e-mail | Sent in the background (the response time does not reveal anything) to an address that is **not confirmed yet**, with a fresh link each time (the previous link stops working). At most 3 e-mails per address per hour (`DeletionRequestThrottle`, key prefix `waitlist\|`). A confirmed address gets nothing. If the SMTP is not configured or fails, the answer is still `202`; the log keeps the exception type only. |
| Insertion | A single `INSERT ... ON CONFLICT DO NOTHING`, so a duplicate or a race is not a database exception (its text would quote the address and reach the log). |

`POST /api/v1/waitlist/confirm` `{ "token": "..." }` → `200 {"confirmed": true}`, or `400` when the link is invalid, expired
(48 hours) or already used. The pages behind the e-mail links show a button first and post only when pressed, so a mail
scanner that merely opens the link cannot confirm (or remove) anybody.

`POST /api/v1/waitlist/unsubscribe` `{ "token": "..." }` → `200 {"unsubscribed": true}` (also when the row is already gone) or
`400`. The token is the entry id protected with the application's data-protection keys (`DataProtection:KeysPath`); if
those keys are lost, old unsubscribe links stop working and the person has to ask for the removal.

## Storage

Table `waitlist_entries`: `Id`, `Email` (trimmed and lower-cased, **unique**), `Language`, `ConsentAt` (when the box was
ticked), `CreatedAt`, `ConfirmedAt`, `ConfirmTokenHash` + `ConfirmTokenExpiresAt` (SHA-256 of the pending confirmation token;
the token itself is never stored; cleared once used) and `InvitedAt` (invitation e-mailed). Migrations
`20261115100000_Waitlist` and `20261116100000_WaitlistTestInvites`. It has no foreign key to `users`.

## Administration

Superadmin only (`SuperadminPolicy` in the API, `superadminGuard` in the admin web), menu entry **Waitlist**:

- `GET /api/v1/admin/waitlist?q=&language=&status=&page=&pageSize=` – list (newest first); `status` is `pending` (not
  confirmed), `confirmed` (confirmed, not invited) or `invited`. `overall` has the counts over the whole list (total, es, en,
  pending, confirmed, invited).
- `POST /api/v1/admin/waitlist/invite` `{ "ids": [...] }` (1 to 50 ids) – sends the invitation to those entries that are
  confirmed and not invited yet, in their language, and marks each one that was sent. Answer:
  `{ requested, invited, failed, skipped }` (`skipped`: unknown, not confirmed or already invited). One failure does not stop
  the rest; failed ones stay "confirmed" and can be sent again.
- `GET /api/v1/admin/waitlist/export.csv` – CSV (`email,language,consent_at,created_at,confirmed_at,invited_at`, UTF-8 with
  BOM; cells that start with `= + - @` are prefixed with `'` so spreadsheets do not run them as formulas).
- `DELETE /api/v1/admin/waitlist/{id}` – removes one address.

The e-mails go through the SMTP configured in Settings (Gmail, see the privacy policy); the links use
`Account:PublicBaseUrl` (default `https://trazostrength.duckdns.org`). The invitation link comes from
`Waitlist:TestInviteUrl` (an `https` URL; anything else falls back to the default).

## Erasure (RGPD)

- A person can remove themselves with the unsubscribe link, or ask for it: the superadmin deletes the row from the console
  (or runs `WaitlistService.DeleteByEmailAsync`, which normalises the address first).
- The table is **independent of user accounts**: the person may never have registered. Account deletion
  (`AccountService`) also removes the row whose normalised address equals the account's.
- Retention: the entries are kept until the person is told the app has opened or unsubscribes; purge the list after the
  launch announcement. Unconfirmed entries are not purged automatically yet: they get no further messages, and can be
  deleted from the console (filter **Not confirmed**).
