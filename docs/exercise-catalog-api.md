# Exercise catalog API

The exercise catalog is exposed to the Android client and other first-party clients through the versioned REST API.

## List and search

`GET /api/v1/exercises`

Query parameters:

| Parameter | Meaning |
| --- | --- |
| `q` | Case-insensitive partial match against Spanish name, English name or Spanish description |
| `movementPattern` | Exact movement-pattern filter |
| `difficulty` | Exact difficulty filter |
| `experience` | Exact minimum-experience filter |
| `equipment` | Matches the primary equipment value or any required-equipment entry |
| `muscle` | Matches a primary or secondary muscle |
| `page` | 1-based page number; defaults to 1 |
| `pageSize` | Results per page; defaults to 30 and is capped at 100 |

Example:

```http
GET /api/v1/exercises?q=press&movementPattern=horizontal_push&page=1&pageSize=30
```

The response contains `items`, `page`, `pageSize`, `totalCount` and `totalPages`. Results are ordered by descending selection priority, then Spanish name and stable ID. Invalid pagination or overlong filter values return HTTP 400.

List items intentionally omit long instructions and tips. Use the detail endpoint when the user opens an exercise.

## Exercise detail

`GET /api/v1/exercises/{id}`

Returns the full exercise description, instructions, tips, equipment, movement pattern, muscle groups, suitability and selection priority. Returns HTTP 404 when the ID is not present.

## Images

The existing image endpoints remain separate so clients can lazy-load image slots:

- `GET /api/v1/exercises/{id}/images` — available slots and attribution.
- `GET /api/v1/exercises/{id}/images/{slot}` — image bytes.

RepDB attribution is returned when an image originates from RepDB.

## Access and trust boundary

These read-only catalog endpoints are currently public. They expose catalog data only; they do not expose user information or administrative operations. User-specific training data and all write operations must be protected by authentication and authorization before they are added.
