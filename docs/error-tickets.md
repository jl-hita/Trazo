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
- At most 200 **new** tickets per hour (repeats of a known error are always counted) and at most 10 notice emails per hour.

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
