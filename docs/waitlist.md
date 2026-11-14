# Waitlist (public landing form)

The landing page at the root of the site collects e-mail addresses of people who want to be told when the app opens.

## Public endpoint

`POST /api/v1/waitlist` (anonymous, same origin as the landing, **no CORS policy**).

```json
{ "email": "ana@example.com", "language": "es", "consent": true }
```

| Rule | Behaviour |
| --- | --- |
| Validation | `email` must be a valid address (max 254), `language` is `es` or `en`, `consent` must be `true`; otherwise `400` with the field errors. |
| Success | `202 {"accepted": true}`. |
| Address already on the list | **Identical** `202` answer and no change to the stored row, so the form cannot be used to find out who signed up. |
| Rate limit | 5 requests per minute per client IP (`429` beyond that). Fixed, like the sign-in limiter. The IP comes from `X-Forwarded-For` through the trusted reverse proxy. |
| Logging | Nothing logs the address. Insertion is a single `INSERT ... ON CONFLICT DO NOTHING`, so a duplicate or a race is not a database exception (its text would quote the address and reach the log). |

## Storage

Table `waitlist_entries` (migration `20261115100000_Waitlist`): `Id`, `Email` (trimmed and lower-cased, **unique**),
`Language`, `ConsentAt` (when the box was ticked) and `CreatedAt`. It has no foreign key to `users`.

## Administration

Superadmin only (`SuperadminPolicy` in the API, `superadminGuard` in the admin web), menu entry **Waitlist**:

- `GET /api/v1/admin/waitlist?q=&page=&pageSize=` – list (newest first), with `overall` counts (total / es / en).
- `GET /api/v1/admin/waitlist/export.csv` – CSV (`email,language,consent_at,created_at`, UTF-8 with BOM; cells that
  start with `= + - @` are prefixed with `'` so spreadsheets do not run them as formulas).
- `DELETE /api/v1/admin/waitlist/{id}` – removes one address.

## Erasure (RGPD)

- A person on the list can ask to be removed: the superadmin deletes the row from the console (or runs
  `WaitlistService.DeleteByEmailAsync`, which normalises the address first).
- The table is **independent of user accounts**: it is not removed by the cascade of `users`, because the person may
  never have registered, and the address is a separate piece of data. Deleting an account therefore must also call
  `WaitlistService.DeleteByEmailAsync(db, user.Email, ct)`; there is no account-deletion flow in the API yet, so add that
  call when it exists (and, if the account used another address, tell the person to ask for that one too).
- Retention: the entries are kept until the person is told the app has opened or asks for removal; purge the list after
  the launch announcement.
