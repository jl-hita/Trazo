# Exercise catalog bootstrap

## First-run behavior

Before the API starts accepting requests, startup runs these steps in order:

1. Connect to the dedicated TRAZO PostgreSQL database.
2. Apply all pending Entity Framework Core migrations.
3. Load `Seeds/exercise-catalog-additions.json`.
4. Validate every seed entry and insert missing exercises.
5. Preserve existing exercise rows so administrator curation is never overwritten.

The seed runs on every startup, but inserts only missing stable exercise IDs. A PostgreSQL transaction-level advisory lock prevents two API instances from racing during initial seeding. A missing or invalid seed is a startup error rather than silently leaving an empty catalog.

## What is included in this seed

The file is the first-party curated additions seed currently used by DietoExpress, copied into TRAZO as an independent product-owned file. It contains the extra exercises that are not part of the upstream RepDB catalogue.

This is not a bundled copy of the RepDB dataset. DietoExpress obtains that source catalogue from its upstream dataset endpoint at runtime; the upstream dataset and its licensing conditions must be handled separately before implementing the equivalent TRAZO importer. Do not copy or redistribute the upstream dataset or its images as a repository seed.

The separate curated image seed is not imported by this initial catalog bootstrap. Image ingestion will be added as a distinct step with checksum and content validation.

## Database provisioning

The PostgreSQL database and a dedicated login are provisioned once by the server administrator. The configured login needs schema migration permissions in the TRAZO database. No manual table creation or SQL script is required: EF Core migrations create and evolve the application schema automatically.

## Updating the seed

Add new curated exercises to the JSON file with stable IDs and increment the entry version when changing its seed definition. Existing database rows are intentionally not overwritten. Changes to already-imported exercises should be delivered through an explicit data migration or a reviewed administrative operation, not by replaying the seed over live data.
