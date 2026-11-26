# Administration: exercise library API

Routes under `/api/v1/admin/exercises`, all restricted to the `superadmin` role. They power the
**Training library** page of the administration console.

| Method | Route | Purpose |
|---|---|---|
| GET | `/` | List exercises. Query: `q` (name or identifier), `movementPattern`, `difficulty`, `experience`, `equipment`, `muscle`, `source`, `status` (`active`, `inactive`), `sort` (`priority`, `updated`; default is name), `page`, `pageSize` (max 100) |
| GET | `/options` | The accepted vocabularies (patterns, levels, equipment, muscles, goals...) and the sources in use, offered by the editor |
| GET | `/{id}` | Full exercise, with image slots and curation state |
| POST | `/` | Create an exercise (source `trazo`). The identifier is generated from the English name when omitted |
| PUT | `/{id}` | Replace every editable field of an exercise |
| POST | `/{id}/deactivate`, `/activate` | Hide or show an exercise in the public catalog |
| POST | `/{id}/release-curation` | Let the next DietoExpress import overwrite an exercise again |

## Interaction with the DietoExpress import

- Editing an imported exercise marks it as **locally curated**. The importer skips curated exercises, so edits are
  never overwritten. `release-curation` removes the mark.
- Exercises created in TRAZO have no import record, so imports never touch them.
- Hiding an exercise (`IsActive = false`) is kept across imports; new imported exercises are visible by default.
- Hidden exercises disappear from `GET /api/v1/exercises` and `GET /api/v1/exercises/{id}`, so they are neither shown
  in the apps nor available for plan generation. Their images remain stored.

## Not included yet

Bulk edits and deleting exercises. Deleting is deliberately left out: hiding keeps history intact for plans that may
reference an exercise.

## Seed exercises

Summaries and details carry `fromSeed`. Exercises of the seed are read only: update, activate, deactivate and
release-curation answer `409`. Their images can be uploaded and removed.

## Validation

Creating, editing and showing (`activate`) an exercise, the seed and the DietoExpress import share one set of rules
(`ExerciseMetadataValidator`): closed vocabularies for pattern, muscles, goals, experience, difficulty, mechanic, force type,
body part and required equipment (listed by `GET /options`); at least one primary muscle and one goal; no muscle both primary
and secondary; at least three instructions; equipment required unless the exercise is bodyweight; single-leg exercises are
unilateral. Errors are returned per field.

Planning metadata, which the engine reads instead of deducing anything from the identifier:

| Field | Meaning |
|---|---|
| `effectiveSecondaryMuscles` | Secondary muscles that count a set (subset of `secondaryMuscles`) |
| `emphasis` | Part of a muscle the plan reserves direct work for (`LateralDeltoid`, primary deltoids) |
| `limitationAreas` | `Elbow`, `Knee`, `LowerBack`, `Shoulder`, `Wrist`: avoided for users with a limitation there |
| `metadataReviewed` | Only a reviewed exercise is planned automatically; it must state `limitationAreas`, even empty. Any limitation avoids an unreviewed one |

A DietoExpress import does not carry these fields: it leaves the exercises it adds or changes unreviewed. An import with any invalid record writes nothing.

## Seed report

`GET /api/v1/admin/exercises/seed-report` (read only) compares the database with the exercise seed: exercises missing,
differences in planning metadata, in other content and in translations, exercises outside the seed, invalid rows and how much of
each language the seed covers. The API logs a warning on startup when the database differs. The console shows it with the
**Comprobar seed** button of the Training library; opening the URL in a browser answers `401`, because the request carries no token.
