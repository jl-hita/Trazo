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
production silently. Existing images are never overwritten: an image replaced in the console wins over the seed.

## Console

Training library, editor, **Images**: upload, replace or remove each slot. The editor keeps unsaved text edits while
images change.

## API

| Method | Route | Purpose |
|---|---|---|
| PUT | `/api/v1/admin/exercises/{id}/images/{slot}` | Body is the raw image; `400` if it is not a supported image, `413` over 1 MB |
| DELETE | `/api/v1/admin/exercises/{id}/images/{slot}` | Remove a slot |

Both return the updated exercise detail and require the `superadmin` role.

## Illustration generator

The 32 exercises added to the curated seed ship with two schematic illustrations each (start and end position,
512 x 512 WebP, flat style, side view). They are drawn by `tools/exercise-illustrations`, which renders a figure with
two-bone inverse kinematics (hands and feet are placed, elbows and knees are solved) plus simple equipment shapes.
They are deliberately simpler than the RepDB photos and can be replaced from the console at any time.

```bash
cd tools/exercise-illustrations
npm install
node generate.mjs --sheet            # contact sheets in ./out to review the poses
node generate.mjs                    # writes Seeds/exercise-images/{id}/{0,1}.webp
node append-catalog.mjs              # adds the metadata of catalog-entries.mjs missing from the seed
```

To add an exercise: describe its two frames in `exercises.mjs`, its metadata in `catalog-entries.mjs` (instructions in
Spanish, at least three steps) and its stimulus decision in `TrainingExerciseStimulus`; `ExerciseSeedAuditTests`
checks that all three agree.
