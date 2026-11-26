# Exercise catalog seed

## Complete, reproducible catalog

`Seeds/exercise-catalog.json` is the versioned bootstrap snapshot of the complete exercise library, not merely the TRAZO-curated additions. It is generated from the database-backed catalog artifact published by the latest successful deployment. The snapshot includes every exercise and its current metadata: source, Spanish and English names, Spanish description/instructions/tips, movement and muscle metadata, difficulty, activity state, selection priority, and both `Equipment` and `RequiredEquipment`.

The seed currently represents all 150 exercises in the library (100 `repdb-free` and 50 `trazo-curated`; one, `standing-calf-raise`, is retired with `isActive: false`). It is deliberately independent of a runtime RepDB download: a fresh installation can reproduce the library even if the upstream dataset changes or is unavailable.

## First-run behavior

Before the API starts accepting requests, startup runs these steps in order:

1. Connect to the dedicated TRAZO PostgreSQL database.
2. Apply all pending Entity Framework Core migrations.
3. Load `Seeds/exercise-catalog.json`.
4. Validate the complete snapshot: unique IDs, supported sources and the rules every exercise must satisfy (see "Validation" in `admin-exercises-api.md`).
5. Insert missing exercises and their Spanish-published / English-draft translation rows.
6. Apply a newer seed version only to rows from the same source that have never been edited locally. Administrator curation and records from other sources are preserved.
7. Add missing language rows without replacing existing translations.
8. Export the effective database-backed catalog to `/var/lib/trazo/artifacts/exercise-catalog.json`.

The seed runs on every startup. A PostgreSQL transaction-level advisory lock prevents two API instances from racing during bootstrap. A missing or invalid seed is a startup error rather than silently leaving an incomplete catalog.

## Updating the seed

Use the latest successful deployment's `exercise-catalog-*` GitHub Actions artifact as the source snapshot. The workflow that refreshes the repository seed maps the complete artifact to the seed schema and increments the seed version. Review the resulting JSON diff, especially exercise metadata and `RequiredEquipment`, before merging.

Existing database rows are never overwritten merely because the JSON changed: the entry version must be higher, the source must match, and the row must remain untouched (`UpdatedAt == CreatedAt`). Changes made by an administrator therefore remain authoritative.
