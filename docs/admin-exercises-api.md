# Administration: exercise library API

Routes under `/api/v1/admin/exercises`, all restricted to the `superadmin` role. They power the
**Training library** page of the administration console.

| Method | Route | Purpose |
|---|---|---|
| GET | `/` | List exercises. Query: `q` (name or identifier), `movementPattern`, `difficulty`, `experience`, `equipment`, `muscle`, `source`, `status` (`active`, `inactive`), `sort` (`priority`, `updated`; default is name), `page`, `pageSize` (max 100) |
| GET | `/options` | Values already used in the catalog (patterns, levels, equipment, muscles, goals...), used as suggestions in the editor |
| GET | `/{id}` | Full exercise, with image slots and curation state |
| POST | `/` | Create an exercise (source `trazo`). The identifier is generated from the English name when omitted |
| PUT | `/{id}` | Replace every editable field of an exercise |
| POST | `/{id}/deactivate`, `/activate` | Hide or show an exercise in the public catalog |
| POST | `/{id}/release-curation` | Let the next DietoExpress import overwrite an exercise again |

## Required fields

`nameEs`, `nameEn`, `descriptionEs`, at least one entry in `instructionsEs`, `movementPattern`, `difficulty`,
`minimumExperience`, at least one `primaryMuscles` and at least one `suitableGoals`. `selectionPriority` is 0-100
(default 50). Validation errors are returned as a standard problem document keyed by field name.

## Interaction with the DietoExpress import

- Editing an imported exercise marks it as **locally curated**. The importer skips curated exercises, so edits are
  never overwritten. `release-curation` removes the mark.
- Exercises created in TRAZO have no import record, so imports never touch them.
- Hiding an exercise (`IsActive = false`) is kept across imports; new imported exercises are visible by default.
- Hidden exercises disappear from `GET /api/v1/exercises` and `GET /api/v1/exercises/{id}`, so they are neither shown
  in the apps nor available for plan generation. Their images remain stored.

## Not included yet

Uploading or replacing images, bulk edits and deleting exercises. Deleting is deliberately left out: hiding keeps
history intact for plans that may reference an exercise.
