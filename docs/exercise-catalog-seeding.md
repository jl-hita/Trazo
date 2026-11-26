# Exercise catalog seed

## The seed is the baseline

`Seeds/exercise-catalog.json` and `Seeds/translations/{language}.json` are the baseline exercise library. For the exercises
they hold, the seed **wins over the database**: their metadata, visibility (`isActive`) and texts are written from it on every
startup, whatever was changed before. To change one of those exercises, change the seed and deploy.

- The console marks them as seed exercises and does not allow editing, hiding or showing them (`409`). Their images are still
  managed in the console: the seed ships none.
- The DietoExpress import never changes them (it may still add images).
- Exercises the seed does not have, and translation languages it does not have, are left alone. The seed translates every
  exercise into every language of the app (a test enforces it); Spanish lives in the catalog file.
- Removing an exercise from the seed leaves its row in the database, editable again. To retire one, set `isActive: false`.

The seed covers 150 exercises (100 `repdb-free` and 50 `trazo-curated`; `standing-calf-raise` is retired). It is independent of
any runtime RepDB download, so a fresh installation reproduces the library.

## Startup

Before the API starts accepting requests, startup runs these steps in order:

1. Connect to the dedicated TRAZO PostgreSQL database.
2. Apply all pending Entity Framework Core migrations.
3. Load the seed and validate it: unique IDs, supported sources and the rules every exercise must satisfy (see "Validation"
   in `admin-exercises-api.md`). A missing or invalid seed is a startup error.
4. Insert the missing exercises and translations and rewrite the rows that differ from the seed. Nothing else is written, and a
   PostgreSQL transaction-level advisory lock serializes concurrent instances.
5. Log a warning if the database still differs from the seed.
6. Export the effective database-backed catalog to `/var/lib/trazo/artifacts/exercise-catalog.json`.

## Updating the seed

Edit the JSON files and review the diff, especially planning metadata and `requiredEquipment`. Raise the entry's `version` when
its content changes (it is recorded in plans as part of the catalog version). The artifact published by a deployment is a view
of the database, not the source of the seed.
