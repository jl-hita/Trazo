# Log viewer

The administration console (**Logs**) shows the application's own log, stored in PostgreSQL so it can be filtered and
paged. It is superadmin only: logs can contain request details and exception traces.

## What is stored

A logger provider (`DatabaseLoggerProvider`) keeps every entry of level Information or above that passes the normal
`Logging:LogLevel` filters (framework categories such as `Microsoft.AspNetCore` and `Microsoft.EntityFrameworkCore`
stay at Warning, so SQL is not stored). Entries are queued in memory and written in batches by a background service
(`DatabaseLogWriter`), so logging never waits for the database. A failure to write drops the batch and prints at most
one line a minute to stderr; it never affects a request. When the queue is full (10,000 entries) the oldest are dropped.
The journal (`journalctl -u trazo`) still receives everything, as before.

Table `app_logs` (migration `AppLogs`): `id`, `timestamp_utc`, `level` (Microsoft `LogLevel`: 2 information,
3 warning, 4 error, 5 critical), `category`, `message` (up to 8,000 characters), `exception` (up to 16,000),
`trace_id`.

| Setting | Default | Meaning |
|---|---|---|
| `Logging__Database__Enabled` | `true` | `false` stops storing logs (the viewer then shows what was already stored) |
| `Logging__Database__RetentionDays` | `30` | Entries older than this are deleted (checked every 6 hours); `0` keeps everything |

## API

`GET /api/v1/admin/logs` (superadmin)

| Query | Meaning |
|---|---|
| `levels` | Comma-separated groups: `info`, `warning`, `error` (critical counts as error). Empty means all |
| `from`, `to` | Period; `from` is inclusive and `to` exclusive (ISO 8601 instants) |
| `q` | Text searched in the message, the category and the exception (case-insensitive, up to 100 characters) |
| `cursor` | The `nextCursor` of the previous page: returns the entries older than it |
| `pageSize` | 1 to 200, default 50 |

The answer has `items` (newest first), `nextCursor` (null on the last page) and `counts` (`info`, `warning`, `error`)
for the selected period and text, regardless of the level selection, so the console can show how many entries each
level has.

Paging is by cursor over `(timestamp, id)` instead of page numbers: new entries arriving while someone reads never shift
or repeat a page, and the last page costs the same as the first.

## Console

- Level chips (with the number of entries of each level in the period), search box, date range with previous/next day,
  *Today*, *Yesterday*, *Last 7 days* and *All dates*, page size, auto-refresh (first page only) and refresh.
- *Older* / *Newer* page through the period. Click an entry to see its full message, exception and trace id.
- Dates are the browser's local days; the API receives them as instants.
