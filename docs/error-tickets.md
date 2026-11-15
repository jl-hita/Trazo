# Error tickets

Every unexpected error of the API and every crash of the Android app becomes an **anonymous ticket** with the complete
stack trace. Tickets are read in the administration console (**Error tickets**, superadmin only: a trace can reveal how
the system is built). Equal errors share one ticket and only count their occurrences.

## What is reported

| Source | What | How |
|---|---|---|
| API | An exception that ends a request | `ErrorTicketExceptionHandler` (`UseExceptionHandler`) reports it and answers a plain `500` problem (`title: server_error`) without the exception text. Malformed requests (`BadHttpRequestException`) and aborted requests are not reported |
| API | Any error the code logs with an exception (`LogError(ex, ...)`, background services) | `ErrorTicketLoggerProvider` queues level Error/Critical entries that carry an exception |
| App | Uncaught exceptions (crashes), uncaught coroutine exceptions, and responses the app cannot read | The app stores the trace and sends it to `POST /api/v1/diagnostics/errors` the next time it can |

Reports go through a bounded in-memory queue (1,000) and are stored by a background writer, so reporting never slows or
fails a request. While a report is being stored, errors logged by the storing itself (for example a database that is down)
are not reported again, which would loop.

## Anonymity

- A ticket has no user, no IP, no device and no request data. The public endpoint stores none of them.
- Before storing, the trace is **scrubbed** (`ErrorTicketService.Scrub`): emails, JWTs and `Bearer` tokens, GUIDs, IPv4
  addresses, URL query strings, long hex/base64 strings and home folders are replaced by placeholders. It is applied to the
  API's own traces and to the app's, in one place.
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
- At most 200 **new** tickets per hour (repeats of a known error are always counted) and at most 10 notice emails per hour.

## Notification

New and reopened tickets send one email to every active, verified superadmin (the notice says the error type and where to
read it). A failure to send is logged and ignored: the ticket is in the console.

## API

`POST /api/v1/diagnostics/errors` `{ "appVersion": "1.0.3", "stackTrace": "..." }` (anonymous).

Superadmin:

| Route | |
|---|---|
| `GET /api/v1/admin/error-tickets` | `status` (`new`, `seen`, `resolved`), `source` (`backend`, `android`), `q` (type or trace), `page`, `pageSize` (≤100). Answers `items`, paging and `overall` counts per status |
| `GET /api/v1/admin/error-tickets/{id}` | The ticket with its `stackTrace` |
| `PUT /api/v1/admin/error-tickets/{id}/status` | `{ "status": "seen" }` |
| `DELETE /api/v1/admin/error-tickets/{id}` | |
| `POST /api/v1/admin/error-tickets/test-exception` | Throws on purpose (HTTP 500) to check the whole chain; the console has a *Trigger test error* button |

Table `error_tickets` (migration `ErrorTickets`).
