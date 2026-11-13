# Planning engine (`Trazo.Training`)

The training-planning engine is a pure, deterministic class library ported from DietoExpress. It has no
dependency on ASP.NET, EF Core or the database, so it can be tested in isolation and reused by the API.

## Provenance

- Source: DietoExpress `Anguloso.Server/Logica/Training*.cs`, commit `c730f6c3` (2026-11-13).
- The files were copied **as they are**; only the namespace changed (`Anguloso.Server.Logica` to `Trazo.Training`).
  Class names, behavior and Spanish messages are unchanged, so later improvements in either repository can be
  compared and ported deliberately, with tests (see `docs/architecture.md`: no runtime or package dependency
  between the products).
- Design notes and the safety rules behind the engine live in the DietoExpress documents
  `TRAINING_GENERATOR_AUDIT_AND_DESIGN.md`, `TRAINING_PROGRESSION_ALGORITHM.md`,
  `TRAINING_VOLUME_ALLOCATION_0_6.md` and `TRAINING_DELOAD_POLICY.md`.

## What is included

| Area | Files |
|---|---|
| Program generation | `TrainingProgramGenerator` (split selection, exercises, sets, reps, RIR, rest, progression), `TrainingProgramVolumeAllocator`, `TrainingSessionExerciseOrderer`, `TrainingExerciseAlternativeFinder` |
| Periodization | `TrainingMacrocycleDraftBuilder` (accumulation / intensification / optional deload phases) |
| Contracts and validation | `TrainingPrescription` (structured prescription, muscle groups, progression rules, validator) |
| Analysis | `TrainingProgramQualityAnalyzer` (weekly volume and frequency per muscle, alerts) |
| Progression and adaptation | `TrainingExerciseProgressionEngine`, `TrainingAdaptationPolicy`, `TrainingAutoAdaptationPolicy`, `TrainingAdaptationPrescriptionAdjuster`, volume and muscle feedback adapters and mappers, `TrainingLoadEstimator` |

`tests/Trazo.UnitTests/Training` holds the 329 DietoExpress unit tests that exercise these classes; they pass
unchanged.

## What was left out on purpose

- Persistence-bound code: `TrainingAutoAdaptationApplier`, `TrainingExerciseProgressionService` and the
  `*Schema` bootstrap classes (they use DietoExpress tables and EF Core). TRAZO will have its own persistence.
- DietoExpress-only concepts: the RepDB curation list (`RepDbExerciseCuration`) and the tests that read
  controller source files. Two tests that referenced those were removed. The audit that every curated catalog id
  has an explicit stimulus decision was recreated against the TRAZO seed (`ExerciseSeedAuditTests`).

## Things to know when integrating

- **Only reviewed exercises are selected.** The generator uses candidates with `IsReviewed` (or
  `IsOwnedByCurrentProfessional`). The TRAZO adapter must set `IsReviewed` for exercises that are active and
  curated in the catalog; the importer only brings approved and reviewed exercises, and administrators create
  the rest.
- **Secondary-muscle stimulus is keyed by exercise id** (`TrainingExerciseStimulus`). Exercises created in
  TRAZO without an entry keep the conservative behaviour: their secondary muscles count as exposures, not as full
  sets. Every exercise of the curated seed has an explicit decision (a unit test fails when one is added without
  it): either the secondary muscles that count as a set, or "reviewed, nothing extra counts".
- **Selection order is by `selectionPriority`.** The curated additions use 55-70 so they do not displace the
  RepDB exercises (72-96); they are chosen when equipment is restricted (calves with only a barbell, upper back
  with dumbbells, ...). Three advanced exercises are the exception and use 97 because they close real gaps:
  `pendlay-row`, `rack-pull` and `weighted-pull-up`. Measured on the real catalog (advanced, 4 days, all
  equipment): effective sets in week 1 went from 123 to 130, upper-back and calves warnings from 2 to 0, and the
  main lifts (bench press, deadlift) stay in the plan. Raising all 15 advanced exercises, or the 10 loaded ones,
  gave no further benefit and removed the conventional lifts from advanced plans, so they keep 55-60. Intermediate
  profiles are unaffected (these exercises require Advanced).
- **Weighted pull-ups and dips** require only `pull-up-bar` and `dip-station`; the load is a dumbbell between the
  feet or a weighted vest, so no `weight-belt` token exists.
- **Beginner and low-equipment coverage.** Added `bodyweight-squat`, `bodyweight-reverse-lunge`,
  `bent-knee-inverted-row` (RepDB's `inverted-row` needs a barbell and is intermediate), `negative-pull-up`,
  `band-row`, `band-lat-pulldown`, `band-shoulder-press`, `elevated-pike-push-up` and
  `single-leg-glute-bridge`. A beginner with only a pull-up bar, only bands, or both now gets a program. A beginner
  who lists no equipment at all still gets none: there is no pulling movement that needs nothing.
- **Seed ids must not collide with the imported catalog.** The seeder skips ids that already exist, and the image
  seeder fills empty slots of an existing id, so a colliding id would silently attach a TRAZO illustration to someone
  else's exercise. Check new ids against the catalog before adding them.
- The automatic split accepts 2-4 training days only (engine rule).
- The generator returns a draft for professional or user review. It is not a clinical prescription.

## Next steps

1. Catalog adapter: `ExerciseCatalogItem` to `TrainingExerciseCandidate` (muscle names, equipment, goals).
2. Administration preview: generate a program from a profile with the real catalog and review the quality report.
3. Persistence and user-facing API for plans (profile, macrocycle, mesocycles, weeks, sessions, execution records).
4. Android screens.
