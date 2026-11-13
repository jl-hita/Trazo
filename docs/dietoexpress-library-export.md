# Sending exercises from TRAZO to DietoExpress

The reverse of `docs/dietoexpress-exercise-library-import.md`. Exercises **created in TRAZO** (source `trazo` or
`trazo-curated`), with the images uploaded or seeded in TRAZO, can be sent to DietoExpress from the Training library
page (**Send to DietoExpress**, with a confirmation click). Exercises that came from DietoExpress are never sent back.

## Keys

The same shared secret is used in both directions:

| Side | Setting | Meaning |
|---|---|---|
| TRAZO | `DietoExpressImport__ApiKey` | Secret accepted for imports from DietoExpress **and** sent when exporting |
| TRAZO | `DietoExpressTransfer__Endpoint` | Base URL of DietoExpress, for example `https://jlhitap.duckdns.org/` (HTTPS, or HTTP only on loopback) |
| DietoExpress | `TrazoTransfer__ApiKey` | The same secret, checked on the inbound endpoint |

The export posts the archive to `{endpoint}/api/internal/trazo-exercise-library/import` with the header
`X-Trazo-Transfer-Key`. If the endpoint or key is missing the page reports that the transfer is not configured (`503`).

## Archive (schemaVersion 1)

- `manifest.json`: source `TRAZO`, counts, SHA-256 of `exercises.json`, and every image (path, size, SHA-256).
- `exercises.json`: entries in the same format as DietoExpress `exercise-catalog-additions.json`. The `version` grows with
  each edit, so DietoExpress can refresh its copy of an exercise that TRAZO changed.
- `images/{exerciseId}/{slot}.{webp|png|jpg}`.

An exercise is skipped, and listed in the result, when its identifier is not lowercase letters, digits and hyphens, or
when it has fewer than three instruction steps (DietoExpress requires both). Hidden exercises are not sent.

## Behavior on the DietoExpress side

New exercises are inserted as approved, reviewed global exercises (`dietoexpress-curated`). An exercise that already
exists in DietoExpress is not replaced unless it was itself created by an earlier TRAZO transfer with a lower version;
exercises from RepDB or edited by a person in DietoExpress are never touched. Images are only added to empty slots.
