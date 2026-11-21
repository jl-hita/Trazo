# Error tickets

Every unexpected error of the API and every crash of the Android app becomes an **anonymous ticket** with the complete
stack trace. Tickets are read in the administration console (**Error tickets**, superadmin only: a trace can reveal how
the system is built). Equal errors share one ticket and only count their occurrences.

## What is reported

| Source | What | How |
|---|---|---|
| API | An exception that ends a request | `ErrorTicketExceptionHandler` (`UseExceptionHandler`) reports it and answers a `500` problem (see [Error contract](#error-contract)) without the exception text. Malformed requests (`BadHttpRequestException`) and aborted requests are not reported |
| API | An exception a `catch` block turns into an answer | `ErrorResults.Failure(exception, status, code, detail)`: reports it and answers the same contract. Used by the endpoints that must keep answering something specific |
| API | Work an endpoint starts but does not wait for (confirmation emails, notices to the administrators) | `IBackgroundWork` runs it in a service with a scope of its own; a failure is logged by type and reported |
| API | Any error the code logs with an exception (`LogError(ex, ...)`, background services) | `ErrorTicketLoggerProvider` queues level Error/Critical entries that carry an exception. Entity Framework's own log is ignored: it logs every failed command, including the unique-key races the code handles |
| App | Uncaught exceptions (crashes), uncaught coroutine exceptions, and responses the app cannot read | The app stores the trace and sends it to `POST /api/v1/diagnostics/errors` the next time it can |

One exception is reported **once**, however many of those places see it: the reporter marks the exception object
(`ErrorMarks`), so a `LogError(ex)` followed by a rethrow, or a `catch` that reports and then throws, is one ticket.

## Reliable delivery (outbox)

```
 handler / catch block / app endpoint ──► error_report_outbox (PostgreSQL, before the answer; 2 s at most)
 logger provider / database down ───────► in-memory queue (1,000) ─► writer ─► outbox ─► ticket (one per fingerprint)
```

A report is **appended to the outbox table first** and removed only once its ticket is stored. What can still go wrong:

| Risk | What happens |
|---|---|
| Process restart or crash | Accepted reports are already in the table and are stored at the next start. Reports still in memory are moved to the table on a normal shutdown |
| Queue full | The oldest are dropped, but counted: the writer files a ticket (`DroppedReportsException`) saying how many were lost |
| Database down | The report waits in memory (bounded) and is appended when the database answers. The writer retries with growing delays and never stops |
| Failure while storing a ticket | The row stays with a later attempt time (5 s, 10 s... up to 15 min). After 10 failures it is discarded with a warning |
| Hourly budget of new tickets spent (200) | The row waits (retried every 5 min) instead of being dropped; discarded only after 24 h. Repeats of a known error never need the budget |
| The ticket system's own errors | While a report is being stored, errors logged by the storing are not reported again (no loop) |

## Anonymity

- A ticket has no user, no IP, no device and no request data. The public endpoint stores none of them.
- Android crash traces are first passed through R8 Retrace when their exact app version has a mapping file. Retrace runs
  **before** scrubbing because the obfuscated class/method names are needed for the lookup. The readable trace is shown by
  default; the scrubbed original obfuscated trace is retained as well and can be switched to in the admin console.
  Both traces are scrubbed (`ErrorTicketService.Scrub`): emails, JWTs and `Bearer` tokens, GUIDs, IPv4 addresses, URL
  query strings, long hex/base64 strings and home folders are replaced by placeholders.
- Historical tickets without an app version, releases without an uploaded mapping, or installations without the Retrace
  executable are **not retraced**. Their original obfuscated trace is scrubbed and stored as-is; a Retrace failure never
  prevents a ticket from being recorded.
- The email notice names the error **type** only, never the trace.
- Tickets hold no personal data, so account deletion does not touch them (`AccountData.NotPersonal`).
- The privacy policy should mention that crash reports without identifiers are collected.

## Grouping

The fingerprint is a SHA-256 of the source, the outer and the innermost (`Caused by` / `--->`) exception types and the first
six frames without line numbers or file paths. A repeat increments `Occurrences` and `LastSeenAt`; a **resolved** ticket
that happens again is reopened (`new`) and announced again.

## Limits

- `POST /api/v1/diagnostics/errors`: anonymous, no CORS, 10 per minute per IP, body up to 64 KB, `stackTrace` up to
  20,000 characters; the stored trace is cut at 16,000 after scrubbing. The answer is always `202` and never says whether the
  error was already known.
- At most 200 **new** tickets per hour (repeats of a known error are always counted; the rest wait, see above) and at most 10 notice emails per hour.

## Notification

New and reopened tickets send one email to every active, verified superadmin (the notice says the error type and where to
read it). A failure to send is logged and ignored: the ticket is in the console.

## API

`POST /api/v1/diagnostics/errors` `{ "appVersion": "0.2.2+30029", "stackTrace": "..." }` (anonymous).
The Android app version must include both versionName and versionCode (`versionName+versionCode`) for exact mapping
selection. Older tickets with a missing or unmatched version remain obfuscated.

The Android release workflow uploads `mapping.txt` after every minified build to
`POST /api/internal/diagnostics/mappings?appVersion=0.2.2%2B30029`, using the
`X-Trazo-Diagnostics-Key` header. The API returns the SHA-256 checksum of the stored mapping in the accepted response. Configure these server settings:

- `Diagnostics:MappingUploadKey`: long random secret; set the matching GitHub Actions secret
  `TRAZO_DIAGNOSTICS_UPLOAD_KEY`. It is optional for publishing, but without it mappings are not uploaded.
- `Diagnostics:MappingsPath`: private writable directory outside the web root (for example
  `/var/lib/trazo/r8-mappings`); grant access only to the API service account and administrators.
- `Diagnostics:RetraceExecutable`: path to Android SDK command-line tools' `retrace` executable, or simply
  `retrace` if it is on the service PATH. The host needs Java 11+ and Android command-line tools installed.

Mappings are stored outside the public web root, named by the validated app version, and replaced atomically on re-upload.
Retrace is time-limited; if its executable or mapping is unavailable, the ticket falls back to the original obfuscated trace.

Superadmin:

| Route | |
|---|---|
| `GET /api/v1/admin/error-tickets` | `status` (`new`, `seen`, `resolved`), `source` (`backend`, `android`), `q` (type or trace), `page`, `pageSize` (≤100). Answers `items`, paging and `overall` counts per status |
| `GET /api/v1/admin/error-tickets/{id}` | The ticket with its `stackTrace` (readable by default) and nullable `originalStackTrace` (obfuscated version when Retrace succeeded) |
| `PUT /api/v1/admin/error-tickets/{id}/status` | `{ "status": "seen" }` |
| `DELETE /api/v1/admin/error-tickets/{id}` | |
| `POST /api/v1/admin/error-tickets/test-exception` | Throws on purpose (HTTP 500) to check the whole chain; the console has a *Trigger test error* button |

Table `error_tickets` (migration `ErrorTickets`).

## Error contract

Every problem answer of the API (`application/problem+json`, RFC 9457) has `type`, `title`, `status`, `detail` (a text that is
safe to show), `traceId` and a stable `code`. `title` already was the stable code where one exists (`no_active_plan`,
`server_error`; the Android app reads it), so it keeps that role and `code` repeats it for new clients; validation problems
keep their sentence as title and get `code: validation_error`. Server-side answers in the older `{ error, message }` shape
also carry `traceId`. Nothing from the exception (message, trace, SQL, addresses) is ever sent.

`traceId` is the W3C trace id of the request. The same value is in the server log next to the line that says whether a ticket was
opened (`Error ticket <id> Created/Repeated for trace <traceId>`), so a person who quotes the reference can be matched with
the ticket. The Angular console and the Android app show it (shortened to 12 characters) after server-side errors.

| Situation | Answer | Ticket |
|---|---|---|
| Validation | 400 `validation_error` | no |
| Not signed in / no permission | 401 / 403 | no |
| Missing resource | 404 | no |
| Business conflict | 409 | no |
| Expected condition (mail server not answering, mail not configured, Firebase unreachable, Google Play not configured) | 503 with a code (`email_unavailable`, `service_unavailable`...) | no |
| Real fault with a known answer (database not answering) | 503 `database_unavailable` | yes |
| Unexpected exception | 500 `server_error` | yes |

## Decisions: what is *not* a defect

An exception is an **expected condition** (`.AsExpected()`; no ticket) when waiting or an administrator action fixes it and the
overview already shows it: the mail server not answering or refusing (`SmtpEmailSender` marks transport failures, and "not
configured"), Firebase not configured / credentials refused / not reachable, Google Play not configured or not answering.
Everything else that goes wrong while doing those things (a bug building a message, a failing query) is unexpected.

- Push: `check`, `test` and `broadcast` keep answering `200 { ok: false, message }` (the console and its tests rely on it). A
  Firebase *error status* (a `PushException` without an inner network exception) is a provider fault: reported, and the
  answer carries its `traceId`. "No devices registered" is a normal result.
- Waitlist invitations answer `200` with `failed`: a batch is a partial result. Each unexpected failure is reported.
- Connectivity tests (SMTP, Google, DietoExpress) answer `200 { ok: false }` for "no" and for an invalid address typed by the
  administrator; any other exception reaches the handler.
- `catch (DbUpdateException)` is only for unique-key races (`DbErrors.IsUniqueViolation`): an outage is no longer taken for
  "somebody else did it".
- `FiscalException`, `MarketingException`, malformed JSON/form bodies, invalid JWTs and the like are typed or client-caused
  business answers and never tickets.
