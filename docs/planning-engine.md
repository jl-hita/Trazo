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
  controller source files. Two tests that referenced those were removed; one of them audited that every curated
  catalog id has an explicit stimulus decision and should be recreated against the TRAZO catalog.

## Things to know when integrating

- **Only reviewed exercises are selected.** The generator uses candidates with `IsReviewed` (or
  `IsOwnedByCurrentProfessional`). The TRAZO adapter must set `IsReviewed` for exercises that are active and
  curated in the catalog; the importer only brings approved and reviewed exercises, and administrators create
  the rest.
- **Secondary-muscle stimulus is keyed by exercise id** (`TrainingExerciseStimulus`). Exercises created in
  TRAZO have no entry, so their secondary muscles count as exposures, not as full sets. This is conservative.
- **The catalog has no Advanced exercises yet** (only Beginner and Intermediate), so programs for advanced
  lifters will use intermediate exercises until the catalog is extended.
- The generator returns a draft for professional or user review. It is not a clinical prescription.

## Next steps

1. Catalog adapter: `ExerciseCatalogItem` to `TrainingExerciseCandidate` (muscle names, equipment, goals).
2. Administration preview: generate a program from a profile with the real catalog and review the quality report.
3. Persistence and user-facing API for plans (profile, macrocycle, mesocycles, weeks, sessions, execution records).
4. Android screens.
