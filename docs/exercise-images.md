# Exercise images

An exercise can have up to four images (slots 0 to 3), stored in `exercise_catalog_images` and served by
`GET /api/v1/exercises/{id}/images/{slot}`. Images enter the catalog in three ways, all validated by
`ExerciseImageRules`: WebP, PNG or JPEG detected from the file signature (the declared type is not trusted),
at most 1 MB each.

| Origin | `source` / `origin_system` | Behavior |
|---|---|---|
| DietoExpress import | `repdb-free` or curated / `DietoExpress` | Refreshed by later imports, with RepDB attribution |
| Seed shipped with the API | `trazo-curated` / `TRAZO` | Inserted only when the slot is empty |
| Upload from the console | `trazo-upload` / `TRAZO` | Replaces any image and is never touched by imports |

## Seed

Put files under `src/Trazo.Infrastructure/Seeds/exercise-images/{exerciseId}/{slot}.webp`. They are published with the
API and loaded at startup after the exercise seed. Folders for exercises that do not exist are ignored (and logged); a
file that is not an image, is too large or is not named after a slot makes startup fail, so a bad seed never reaches
production silently. Existing images are never overwritten: an image replaced in the console wins over the seed. The reverse also holds: a
file removed from the seed removes the row the seed created (see below).

## Console

Training library, editor, **Images**: upload, replace or remove each slot. The editor keeps unsaved text edits while
images change.

## API

| Method | Route | Purpose |
|---|---|---|
| PUT | `/api/v1/admin/exercises/{id}/images/{slot}` | Body is the raw image; `400` if it is not a supported image, `413` over 1 MB |
| DELETE | `/api/v1/admin/exercises/{id}/images/{slot}` | Remove a slot |

Both return the updated exercise detail and require the `superadmin` role.

## Withdrawn generated illustrations

Schematic illustrations that were generated procedurally for 48 exercises were shipped in the seed and then withdrawn:
the figures were anatomically wrong and not good enough. The seed owns the rows it creates, so removing a file from
`Seeds/exercise-images` also removes the row it created in the database at the next startup (only rows with source
`trazo-curated` that the seed no longer ships; uploads and imported images are never touched). The seed directory is
currently empty.

`tools/exercise-illustrations` is the generator that produced them. It is kept only as a record and should not be
used to ship images again; real exercise images should come from photographs or illustrations made by a person,
added through the console or the seed.
