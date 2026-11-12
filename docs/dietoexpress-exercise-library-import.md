# DietoExpress exercise-library import

TRAZO accepts a private ZIP transfer from the DietoExpress superadmin dashboard at:

`POST /api/internal/exercise-catalog/import`

The endpoint is not intended for browser users or public uploads. It requires the `X-Trazo-Import-Key` header and accepts only `application/zip`.

## Configuration

Set `DietoExpressImport__ApiKey` in the TRAZO server environment. Configure the same random secret in DietoExpress as `TrazoTransfer__ApiKey`, and set DietoExpress' `TrazoTransfer__Endpoint` to the TRAZO API base URL. Use HTTPS except for loopback development.

## Import behavior

The receiver validates the archive schema, exercise approval state, stable identifiers, image paths, content signatures, sizes and SHA-256 hashes before opening a database transaction. It imports only approved and reviewed global exercises. Pending/rejected and professional-owned records are never accepted.

The effective reviewed fields populate the catalog consumed by the training engine. The original DietoExpress record, review provenance and RepDB attribution are retained in `exercise_catalog_import_metadata`. Images are stored in `exercise_catalog_images` and served by the TRAZO API at `/api/v1/exercises/{id}/images/{slot}`.

The operation is idempotent. Existing TRAZO entries without DietoExpress import provenance are preserved. Imported entries marked as locally curated are also protected from future source refreshes. Image rows are refreshed only when their existing provenance is DietoExpress. All database writes happen in one transaction; checksum or validation errors abort before writes.

RepDB data/assets must remain inside the private applications. Do not expose this endpoint as a public bulk export/download facility or redistribute the imported catalogue.
