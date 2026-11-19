# Planning engine

TRAZO generates, serves and adapts personalised strength-training plans on the server. This document is the inventory of
the source engine (DietoExpress), the scope of what TRAZO took from it, the decisions behind the implementation and the
endpoints of the plan API.

- [1. Inventory of the source engine](#1-inventory-of-the-source-engine)
- [2. MVP scope](#2-mvp-scope)
- [3. Architecture](#3-architecture)
- [4. Decisions](#4-decisions)
- [5. Endpoints](#5-endpoints)
- [6. Adaptation rules](#6-adaptation-rules)
- [7. Tests](#7-tests)
- [8. Known limits and next steps](#8-known-limits-and-next-steps)

## 1. Inventory of the source engine

Source: DietoExpress `Anguloso.Server/Logica/Training*.cs` and `Anguloso.Server/Controllers/TrainingPeriodization/*`
(commit `c730f6c3`, 2026-11-13). The repository is read-only for TRAZO: nothing was modified there and TRAZO has no runtime or
package dependency on it (see `docs/architecture.md`).

### 1.1 Pure logic (ported as the `Trazo.Training` library)

Deterministic classes with no database, HTTP or user concept. They were copied as they are (namespace
`Anguloso.Server.Logica` to `Trazo.Training`), so later improvements in either repository can be compared and ported on purpose.

| Piece | What it does | Input to output |
|---|---|---|
| `TrainingProgramGenerator` | Builds ONE block (1-52 weeks): picks the weekly split, one exercise per movement pattern and the accessories for the weekly muscle deficit, then sets, repetition range, RIR, rest and progression rule | `TrainingProgramGenerationRequest` + candidate exercises to `TrainingProgramPrescription` (or explained errors) |
| `TrainingProgramVolumeAllocator` | Distributes working sets towards weekly volume targets per muscle under time and per-exercise limits | sessions + goal + level + time to sessions with sets |
| `TrainingSessionExerciseOrderer` | Orders exercises inside a session (big lifts first, fatigue-aware) | exercises to the same exercises with `Order` |
| `TrainingMacrocycleDraftBuilder` | Splits a macrocycle into accumulation / intensification / optional deload phases | goal, level, weeks, deload policy to phases |
| `TrainingPrescription` | Structured prescription contract, muscle vocabulary, progression rules, structural validator, and the per-exercise stimulus table (`TrainingExerciseStimulus`) | - |
| `TrainingProgramQualityAnalyzer` | Weekly volume and frequency per muscle plus review warnings | prescription to report |
| `TrainingExerciseProgressionEngine` | Double progression per exercise: add a repetition, confirm the top of the range, raise the load by one increment, hold, reduce after repeated failure, stagnation review. Pain always wins | prescription + last exposures to recommendation |
| `TrainingLoadEstimator` | Suggests a working load from the last performance (Epley-type estimate, capped change) | last sets to load |
| `TrainingAdaptationPolicy` | Session-level reading of recovery, effort, completion and pain | recent session feedback to decision |
| `TrainingExerciseVolumeAdapter` + `TrainingExerciseVolumeFeedbackMapper` | Per-exercise fatigue detection: remove or give back one set | exposures to recommendation |
| `TrainingMuscleFeedbackAdapter` + `TrainingMuscleFeedbackMapper` | Adds or removes a set per muscle from post-exercise answers (stimulus, pre-fatigue, discomfort) | questionnaire exposures to decisions |
| `TrainingAutoAdaptationPolicy` | Which proposals are applied automatically and the cooldown that stops changes stacking | proposal + recent applications to decision |
| `TrainingAdaptationPrescriptionAdjuster` | Edits a stored prescription (reduce/restore sets, +1 RIR, deload, set load, swap exercise) | JSON to JSON |
| `TrainingExerciseAlternativeFinder` | Reviewed replacements for one exercise by pattern and shared muscles, respecting goal, level and equipment | exercise + catalog to alternatives |

### 1.2 Bound to DietoExpress tables (re-implemented, not ported)

| Piece | Why it does not travel |
|---|---|
| `TrainingAutoAdaptationApplier`, `TrainingExerciseProgressionService` | Raw SQL over `training_sessions`, `training_session_records`, `training_adaptation_proposals` (PostgreSQL JSONB, client ids). Their *rules* are kept: TRAZO's `TrainingPlanService` and `AdaptationPlanner` implement the same flow over TRAZO's own tables |
| `TrainingMacrocycleSchema`, `TrainingPlanSplitSchema`, `TrainingTemplatesSchema` | Table bootstrap code for DietoExpress |
| `RepDbExerciseCuration` and tests that read controller source | DietoExpress catalog curation |
| `TrainingPeriodizationController.*` (Plans, Cycles, Generation, Sessions, Adaptation, Templates, Collaborations, Support, DTOs, about 3,000 lines) | Controllers coupled to DietoExpress identity (professional / client), its tables and DTOs |

### 1.3 Not needed in a single-user mobile app

| Source feature | Decision |
|---|---|
| Collaborations (professional-client invitations, shared plans) | Out. TRAZO has one user per plan |
| Professional portal: approval workflow for adaptation proposals, review notes | Out. The engine applies the small, bounded, reversible changes by itself and records each one in `plan_adaptations`; pain and safety signals are never automated, they are reported to the user |
| Administrative / professional templates (`Templates`) | Out |
| Manual plan editing (cycles CRUD, manual volume analysis) | Out for the MVP. The generator is the only author of a plan |
| Muscle questionnaire (`TrainingMuscleFeedback*`) | Library kept, not wired: it needs a post-exercise questionnaire screen the Android app does not have yet |
| Ownership of exercises by a professional (`IsOwnedByCurrentProfessional`) | Not used: every active TRAZO catalog exercise is `IsReviewed` |

## 2. MVP scope

In:

1. **Generate** a personalised macrocycle and first mesocycle from goal, level, days per week, session length, equipment,
   exclusions and limitations (injury areas).
2. **Serve the current week** with sessions and prescription: sets, repetition range, RIR, rest, warm-up, load target.
3. **Log sessions** (sets, loads, repetitions, RIR, recovery, effort, pain) with feedback.
4. **Adapt** loads, volume and intensity of upcoming sessions from what was logged.
5. **Suggest alternative exercises** and swap them.
6. A summary of the macrocycle (phases), never the sessions of future weeks.

Out (and why): see 1.3, plus: multi-user / sharing, manual editing, nutrition integration, calendar sync, push
notifications, exporting a plan, free-text exercise creation by users, the muscle questionnaire. They are product decisions
for later, not engine limits.

## 3. Architecture

```
Trazo.Training        pure engine (copied from DietoExpress + one fix, see 4.9). Since 0.11.0 it also owns the
                      split rotations (`TrainingSessionRotation`). No EF, no ASP.NET.
Trazo.Domain          Planning/: TrainingPlan, TrainingPlanPhase, TrainingMesocycle, PlannedSession,
                      WorkoutLog, WorkoutSetLog, PlanAdaptation (plain entities, enums as text).
Trazo.Application     Planning/: pure orchestration
                        PlanProfileParser      validates and normalises the user's profile
                        LimitationRules        injury area -> exercises to avoid
                        PlanSchedule           macrocycle -> blocks, dates, current week
                        MesocycleGenerator     one block, deterministic for (profile, seed, block, catalog, history)
                        LoadMetrics            real dose per seven days, exposures and observed pace from the workouts
                        PrescriptionJson       the stored form of a session prescription
                        AdaptationPlanner      logged history -> advice + automatic changes
                        LogSessionValidator    shape and ranges of a session log
                        ExerciseCandidateMapper  catalog row -> engine candidate
Trazo.Infrastructure  Planning/TrainingPlanService (EF Core), Persistence/Configurations/TrainingPlanConfiguration,
                      migration 20261114120000_TrainingPlans
Trazo.Api             Endpoints/PlansEndpoints (/api/v1/plans) and AdminPlanningEndpoints (/api/v1/admin/planning)
```

Tables (migration `20261114120000_TrainingPlans`, after `Subscriptions`): `training_plans`, `training_plan_phases`,
`training_mesocycles`, `planned_sessions`, `workout_logs`, `workout_set_logs`, `plan_adaptations`. Lists are stored as JSON
text so the same mapping works on PostgreSQL and on SQLite in tests. A partial unique index
(`ux_training_plans_one_active_per_user`) enforces one active plan per user in the database.

## 4. Decisions

1. **Blocks are generated lazily.** A plan stores the profile and the macrocycle *phases*. Sessions exist only for the
   blocks already reached: the first block (at most four weeks) is generated on creation, the next one when its first
   week arrives. This is what makes adaptation real (the next block starts from the loads the user really lifted, rotates
   equivalent exercises, and follows a deload) and means the full macrocycle never exists in the database.
2. **Anti-copy: the API serves the current week and a phase summary only.** There is no route that returns future
   sessions, several weeks, the whole macrocycle or an export (a test asserts that). `GET /current` shows phases (name,
   type, dates, objective) and never an exercise. The algorithm, its tables and its parameters live only on the server.
   A per-user rate limit (`plans`, 120 requests a minute) makes scraping slow.
3. **Subscription gate.** The group is `.RequireActiveSubscription()` (see `docs/subscriptions.md`): with subscriptions
   enforced, a user without access gets `402 subscription_required`; administrators skip the check; while the switch is
   off everyone signed in has access.
4. **Ownership.** Every call resolves the caller's single active plan first and looks sessions up *inside that plan*, so
   another user's session id is answered `404`, never `403` (no information leak). A user has one active plan; creating
   another returns `409 active_plan_exists` until the first is archived (`POST /current/archive`, history is kept).
5. **Determinism.** The generator is pure. The plan keeps a random `Seed` chosen at creation; selection rotation uses
   `seed % 1000 + block number`, so the same profile, seed and history always give the same plan, and different users with
   the same profile can get different (equivalent) exercises.
6. **Limitations are an exclusion list, not a diagnosis.** The catalog has no contraindication metadata, so
   `LimitationRules` maps five areas (`Knee`, `Shoulder`, `LowerBack`, `Wrist`, `Elbow`) to exercises to avoid. Each area
   combines structural rules read from reviewed metadata (pattern, primary muscles, equipment: `Shoulder` excludes every
   vertical press and every direct deltoid raise whatever the implement, `LowerBack` barbell squats and unsupported
   barbell rows, `Wrist` bodyweight pressing on the hands) with id tokens for movements the metadata cannot describe
   (`dip`, `skull-crusher`, `lunge`...). They narrow the choice; the plan states it is not medical advice. Since engine
   0.10.0 an area never makes a plan impossible by itself: when overhead pressing or single-leg work is left without
   exercises the session keeps its sibling pattern (horizontal press, hinge) or receives it as a substitute, and a
   warning says so. Squat, hinge, horizontal press and row are never dropped: a profile left without one of them still
   fails with the engine's own explanation (`422 plan_generation_failed`). Users can also exclude exercises by id.
7. **Equipment.** Tokens are normalised (lower case, hyphens). An empty list means *everything is available*; the engine
   treats `machine` as interchangeable with `*-machine`.
8. **Calendar.** A plan week is the seven days starting at the plan's start date (the day it was created by default).
   Each training day falls once in each week. "Today" is the UTC date. A session can be used during its own week and the
   week after, to log a late workout; anything else answers `409 session_out_of_window`.
9. **A deliberate change to the engine** (see 12 for the rest). The generator could put the same exercise twice in one session when equipment
   or level left a single candidate for two movement patterns (for example one row serving both the vertical and the
   horizontal pull). Property tests found it; `TrainingProgramGenerator` now skips a pattern whose only candidate is already
   in the session. The 329 ported tests still pass.
10. **Stored prescription = the adjuster's JSON.** `PlannedSession.PrescriptionJson` is `{"exercises":[...]}` in
    camelCase, the shape `TrainingAdaptationPrescriptionAdjuster` edits, so the engine's adaptation code is used unchanged.
    Names and catalog versions are snapshots: later catalog edits do not rewrite history.
11. **Catalog.** The existing seeded catalog already carries everything the engine needs (pattern, primary/secondary muscles,
    level, goals, equipment, priority), so the seeder was not changed. `GET /plans/options` lists the equipment tokens
    actually present in the catalog for the app's equipment picker.

12. **Engine 0.10.0: coaching-quality corrections.** A battery of 22 profiles judged as a personal trainer would
    (not by the invariants the tests already checked) showed eight serious faults. These are deliberate changes to the
    ported engine; every figure is an internal product heuristic. `EngineCoachingQualityTests` pins each one.
    - **Selection is ranked by the engine, not by the raw catalog priority** (`TrainingExerciseSelection`). The curated
      `SelectionPriority` stays the base and the engine corrects it with reviewed metadata: an entry-level bodyweight
      exercise or a hand-held squat loses points for a trained user (and in strength), a barbell lift gains them in
      strength. Ties are broken with a stable hash of the plan seed, never alphabetically. The rotation pool of a
      pattern takes the best exercise of each implement family first (barbell, machine, cable, free weight,
      bodyweight), so three near-identical machines cannot keep the barbell squat out. "A different exercise on each
      day of the week" only chooses among candidates within 20 points of the best: repeating the dumbbell press is
      better than prescribing knee push-ups to an intermediate. *Why in the engine and not in a catalog migration:*
      the production catalog is not this repository's seed (the seeder never overwrites rows and administrators edit
      priorities), and the right order depends on the profile (goal, level), which a single stored number cannot
      express. One fault is data and stays open: `bench-press` and `db-bench-press` are `Intermediate` in production,
      so beginners never get them; the engine must not override a minimum level (see section 8).
    - **The hinge slot trains the hamstrings.** When the profile has a hinge with hamstrings as a primary muscle, glute
      or upper-back variants (bridge, hip thrust, rack pull) are not candidates for that slot.
    - **Strength is built around a main lift.** Each session opens with a lift that accepts high loads (barbell, trap
      bar, Smith or machine; never a pulldown, a goblet or dumbbell squat or a push-up when one exists), and the
      sessions of the week open with different patterns (squat, press, deadlift). Its dose comes from the lift, not
      from the muscle accounting: 3 sets for beginners, 4 for intermediates, 5 for advanced, limited by session time,
      at 3-6 repetitions for non-beginners (5-8 otherwise). The remaining exercises keep the three-set ceiling.
      Strength selection ignores block rotation: every block of a plan trains the same lifts on the same days.
    - **Volume balance.** Hamstrings: 75 % of the central target (was 50 %) with the same allocation weight as any
      muscle, up to two leg-curl exposures a week, the first accessory slot that may train them reserved while they are
      under target. Calves: half the central target, no reserved slot except one exercise a week in hypertrophy
      sessions longer than 45 minutes, none in strength or in beginner plans under 180 weekly minutes. Single-leg work
      and accessories start at two sets. The allocator gives sets to compound lifts before accessories.
    - **Ceilings.** An exercise is stopped by the weekly ceiling of its primary muscles only; synergists (triceps in a
      press, glutes in a squat) no longer freeze the lift, and glutes have a wider margin. Nothing takes a muscle past
      20 weekly effective sets unless the professional set an explicit target. Strength and general fitness, which had
      no margin above the target, now have one (strength up to twice the target, general fitness up to three times it
      and at most eight sets more).
    - **Time.** While a session stays under 70 % of the time offered, its compound lifts (never the accessories) take
      one more set at a time up to those ceilings. Sessions of advanced six-day plans still end well under 90 minutes:
      the ceilings, not the clock, are the limit.
    - **Core.** No plan carries direct core work by default (engine 0.11.2): the squat, the hinge, the presses and the rows, free weights and
      cables, make the transversus stabilize, so it is trained indirectly and spending time on it would be wasted. Only an explicit
      professional priority puts core in every session.
    - **Ported tests updated on purpose** (10 cases of 329): the targets of hamstrings and calves
      (`TrainingProgramVolumeAllocatorTests`), default core and leg-curl count and the accessory-distribution scenario
      (`TrainingProgramGeneratorTests`), the engine version (`TrainingGeneratorEvaluationReportTests`) and the shared
      catalog of `TrainingBlockProgressionTests`, whose compound lifts now declare a barbell.

## 5. Endpoints

All under `/api/v1/plans`, JWT bearer, `RequireActiveSubscription`, rate limit `plans`. Enums are returned by name.
Errors are RFC 7807 problems: `title` carries a stable code (`no_active_plan`, `active_plan_exists`, `session_not_found`,
`session_already_logged`, `session_out_of_window`, `plan_generation_failed`, `invalid_log`, `not_allowed`...) and validation
errors list the offending fields. All routes are in the OpenAPI document with their response types.

| Method | Route | Purpose | Success | Errors |
|---|---|---|---|---|
| GET | `/options` | Allowed goals, levels, splits, days, limitation areas, equipment tokens and numeric limits | 200 | 401 |
| POST | `/` | Create the plan from a profile (macrocycle + first block). Generated plans need Premium | 201 `PlanView` | 400 fields, 401, 402, 409, 422 |
| GET | `/current` | Profile and macrocycle phases, no sessions | 200 `PlanView` | 404 |
| GET | `/current/week` | Sessions of the current week with prescription (generates the block when first needed). `locked` is true for a generated plan without Premium: it is read-only and no block is generated | 200 `WeekView` | 404 |
| GET | `/current/history?take=30` | Logged sessions, newest first (1-100) | 200 | 404 |
| GET | `/current/load` | Weekly volume in sets for every plan week: `plannedSets` (null while that block is not generated), `completedSets` (null for weeks not reached yet), phase and deload flag. Read-only, also for a plan locked by a lapsed Premium. `periodKind` (`Week`/`Rotation`) says what a period is; `rolling` is the real dose of the last seven days (see "One engine, two scheduling policies") | 200 `PlanLoadView` | 404 |
| POST | `/current/archive` | Close the active plan | 204 | 404 |
| POST | `/current/sessions/{id}/log` | Record a session; with Premium it also adapts the upcoming ones (`adaptation.locked` is true otherwise). Generated plans without Premium answer 402 `premium_required` | 200 `LogResult` | 400, 402, 404, 409 |
| GET | `/current/sessions/{id}/exercises/{exerciseId}/alternatives?limit=5` | Replacement options (1-10) | 200 | 402, 404 |
| POST | `/current/sessions/{id}/exercises/{exerciseId}/swap` | Replace one exercise (optionally in the rest of the block) | 200 `SessionView` | 400, 402, 404 |
| POST | `/current/sessions/{id}/exercises` | Add an exercise to one pending session (`exerciseId`, optional `sets`, `minRepetitions`, `maxRepetitions`, `targetRir`, `restSeconds`). Marked `isExtra`; it is logged and counts for history, progress and load, but is never copied to other sessions or blocks. Not allowed if already in the session or ruled out by the profile | 200 `SessionView` | 400, 402, 404 |

### Deload weeks

`includeDeloadPhases` (default true) turns deload weeks on. With `deloadEveryTrainingWeeks` omitted or null, TRAZO places
them (`MacrocyclePlanner` / `DeloadPlacement`, in the Application layer): plans of 6 weeks or less get none; otherwise no run
of training weeks is longer than four, the training weeks are spread evenly between the deloads (longer runs first) and the
last week is never a deload. 12 weeks give training 1-4, deload 5, training 6-8, deload 9, training 10-12. An explicit
interval (4-8) goes to the engine unchanged. The plan stores 0 in `DeloadEveryTrainingWeeks` for "automatic".

### Create

```json
POST /api/v1/plans
{
  "goal": "Hypertrophy",              // GeneralFitness | Hypertrophy | Strength
  "experience": "Intermediate",       // Beginner | Intermediate | Advanced
  "startDate": "2026-11-16",          // optional; default today (UTC); 7 days back to 60 days ahead
  "totalWeeks": 12,                   // 4-52, default 12
  "trainingDays": ["Monday", "Tuesday", "Thursday", "Friday"],   // 2-6 distinct days
  "maxSessionMinutes": 75,            // 35-180, default 60
  "availableEquipment": ["dumbbell", "bench", "cable"],          // empty = everything
  "excludedExerciseIds": [],          // up to 50
  "limitations": ["Knee"],            // Knee | Shoulder | LowerBack | Wrist | Elbow
  "split": "Automatic",               // Automatic (2-4 days) | FullBody | UpperLower | PushPullLegs
  "includeDeloadPhases": true,        // default true
  "deloadEveryTrainingWeeks": null    // omit/null = TRAZO places the deloads; or 4-8 for a fixed interval
}
```

### Current week (shape)

```json
{
  "planId": "...", "weekNumber": 1, "weekStart": "2026-11-16", "weekEnd": "2026-11-22", "timeline": "InProgress",
  "mesocycle": { "sequence": 1, "phaseType": "accumulation", "isDeload": false, "startWeek": 1, "weeks": 4, "weekInBlock": 1 },
  "sessions": [{
    "id": "...", "weekNumber": 1, "date": "2026-11-16", "day": "Monday", "title": "Tren superior A",
    "estimatedDurationMinutes": 51, "status": "Planned",
    "exercises": [{
      "exerciseId": "db-bench-press", "name": "...", "order": 1, "sets": 3, "minRepetitions": 8, "maxRepetitions": 12,
      "targetRir": 3, "restSeconds": 120, "progressionRule": "DoubleProgression",
      "targetLoadKg": 22.5, "loadBasis": "...", "warmupSets": 1, "warmupNote": "...",
      "primaryMuscles": ["Chest"], "secondaryMuscles": ["Triceps"], "swappedFromExerciseId": null
    }],
    "log": null
  }]
}
```

`timeline` is `NotStarted` (before the start date: week 1 is shown), `InProgress`, or `Finished` (the plan is then marked
`Completed` and no sessions are returned).

### Log

```json
POST /api/v1/plans/current/sessions/{id}/log
{
  "outcome": "Completed",                 // Completed | Partial | NotCompleted
  "nonCompletionReason": "None",          // None | Time | Fatigue | Equipment | Motivation | Illness | Other
  "recovery": "Good",                     // Unknown | Good | Partial | Insufficient
  "sessionRpe": 7,                        // 0-10
  "painReported": false,
  "notes": "...",                         // up to 1000 chars
  "sets": [{ "exerciseId": "db-bench-press", "setNumber": 1, "weightKg": 22.5, "repetitions": 10, "rir": 2,
             "completed": true, "skippedReason": null, "painReported": false }]
}
```

Sets must belong to the session's exercises (`400 invalid_log` otherwise), at most 60, a set number 1-12 once per exercise.
A session can be logged once (`409 session_already_logged`). The response carries the updated session and the
**adaptation report**: per-exercise advice (`action`, `explanation`, `nextLoadKg`, suggested repetitions), the session-level
reading (`sessionAction`), the automatic changes applied (`kind`, exercises, sessions changed) and notes (why nothing was
applied, or the pain warning).

## 6. Adaptation rules

Applied by `AdaptationPlanner` (decisions) and `TrainingPlanService` (writes), over the engine's classes. All thresholds are
the engine's internal product values, not clinical figures.

| Signal | Reading | What happens |
|---|---|---|
| Reps inside the range, effort as planned | `AddRepetitions` | Next load stays; suggestion of +1 rep per set |
| Top of the range twice in a row at the same load | `IncreaseLoad` | One increment (1 kg below 20 kg, otherwise 2.5 kg), never above the 5 % cap; written into the next session that has the exercise as `targetLoadKg` |
| Top of the range once | `ConfirmTopOfRange` | Same load again |
| Same load failed twice (sets or minimum reps) | `ReduceLoad` | Load lowered with the estimator (capped), written into the next session |
| Pain in a set or in the session | pause | Progression held, load not pushed, warning in `notes`. **Never automated** |
| Fatigue repeated in an exercise (RIR well under target or sets skipped for fatigue, 2 of 3 sessions) | reduce one set | One set less in that exercise for the next 3 sessions (cooldown 14 days) |
| Three clean sessions at a reduced dose | restore | One set given back, never above the original |
| Low recovery in 2 of the last 3 sessions | +1 RIR | Next 3 sessions ask for one more repetition in reserve (cooldown 14 days) |
| Under 80 % of planned sets completed, or fatigue/illness stop | hold | Load progression paused for 7 days |
| Three consecutive stressed sessions with two kinds of signal | deload | Next 7 days: half the sets, +2 RIR, no load progression (cooldown 28 days) |
| Session in a deload block or after an automatic deload | not progressable | Never raises the load |

Every change is stored in `plan_adaptations` (kind, exercises, sessions changed, explanation, time) and returned to the user.

## 7. Tests

`tests/Trazo.UnitTests`:

- `Training/` - the 329 DietoExpress unit tests of the ported classes, passing (they are the derived tests; the source
  repository has no engine tests of its own besides these: its `Tests` folder only covers security). Ten cases were
  updated for the deliberate changes of engine 0.10.0 (decision 12 lists them).
- `Planning/EngineCoachingQualityTests` - what a trainer would check, as properties over the test catalog: no limitation or
  pair of limitations breaks a full-gym plan and each one really excludes its exercises; every strength session opens
  with a heavy compound of 3-5 sets and keeps its lifts from block to block; the barbell basics compete, ties are not
  alphabetical and nobody is regressed to bodyweight drills when a loadable exercise exists; hamstrings get direct work
  and never less than the calves; no lone sets; no muscle past 20 weekly sets; no direct core unless a professional prioritizes it;
  sessions use about half or more of the time offered and the shortest session still produces a plan.
- `Planning/MesocycleGeneratorPropertyTests` - over a grid of goal x level x days x equipment x seed: sets, reps, RIR and rest
  inside limits; session length within the limit; no repeated exercise in a session; no exercise above the user's level
  or needing equipment they do not have; weekly volume per muscle bounded; excluded and limitation exercises never appear;
  determinism for a seed; weekly work non-decreasing inside a block; a deload lighter than a training week; impossible
  profiles fail with an explanation.
- `Planning/PlanLogicTests` - profile validation, limitation rules, calendar, prescription JSON round trip with the engine's
  adjuster, every adaptation rule of section 6, and a property test over random training histories (a load never jumps
  more than 10 %, never rises after a failed or painful session, is always positive).
- `Planning/PlansApiTests` - the API end to end on SQLite: authentication, no plan, creation, only the current week is served
  (and no route returns more), validation, unsatisfiable profile, one active plan, ownership across users, logging,
  load carried into the next session, deload and its audit, pain, invalid logs, alternatives and swaps, and the
  Premium rules (generated plans and adaptation need Premium, manual plans are free, admins exempt).
- `ModelSnapshotTests` guards the migration against the EF model.

## 8. Known limits and next steps

- The plan is a generated draft; it is not a clinical prescription. The disclaimer travels in `PlanView`.
- The week follows UTC. A device-timezone parameter can be added when the Android app needs it.
- Intensification blocks (strength plans for non-beginners, second half) change the prescription of the heavy lifts: see "Intensification blocks" below.
- The muscle questionnaire (`TrainingMuscleFeedback*`) is available in the library but not exposed.
- Catalog data the engine cannot fix: `bench-press` and `db-bench-press` are `Intermediate` in the production catalog, so
  beginners never receive them. It needs a data migration or an administrator edit (the seeder never overwrites rows);
  it is a product decision and is not done.
- Advanced plans of five or six long sessions stay well under the time offered: the weekly ceilings per muscle (20 sets)
  are reached first. Automatic splits for five and six days, accessory variety between days and rotation between blocks
  outside strength are unchanged.
- Android screens consume these endpoints (next roadmap item); the admin preview (`docs/admin-planning-preview.md`) keeps
  evaluating the engine against the live catalog and shares the same mapper and generator.

## Week order, time zone and retries (API contract)

- `GET /api/v1/plans/options`: `days` lists the day ids **Monday first** (ISO week, the Spanish convention). The ids are the English names used
  everywhere else in the API and never change; `dayOptions` adds `{ id, isoNumber (Monday = 1), label, shortLabel }` in Spanish and `weekStartsOn` is `Monday`.
  `days` used to start on Sunday (the .NET enum order); clients that render it as received now show Monday first, the values are unchanged.
- A plan week is seven days counted from the plan's start date (not a calendar week).
- "Today" and the current week use the user's IANA time zone, resolved in this order: the `X-Time-Zone` header of the request (invalid values are ignored),
  the zone saved in the profile (`PUT /api/v1/account/time-zone`, or the optional `timeZone` field when creating a plan, which is also saved), and `Europe/Madrid`.
  The app should send `X-Time-Zone: <TimeZone.getDefault().id>` on every plan request.
- `POST /plans/current/sessions/{id}/log` is idempotent: repeating a request whose log is already saved with exactly the same data answers `200` with the saved
  session and `"replayed": true`. A different log for an already logged session still answers `409 session_already_logged`.

## Planned vs performed vs registered (flexible registration)

A session has an **identity and a place in the program** (`PlannedSession.Id`, `sequenceIndex`, 1-based, unique in the plan, in planned-date order);
the planned `date` is only a suggestion. A log records **when the workout happened** and **when it was registered**, separately:

| Field | Meaning |
|---|---|
| `performedAt` (log) | Instant of the workout. Defaults to the registration time. Chronology of all evidence (progression, adaptation, history, "last time"). |
| `performedOn` (log) | Local day of `performedAt` in the user's zone (`timeZoneId` is stored with it, so a later zone change does not rewrite history). |
| `loggedAt` (log) | When it was registered in TRAZO (server clock). |
| `clientRequestId` (request, optional) | Idempotency key: the same key always answers the saved result (`replayed: true`), even if the body differs; the same key on another session is `400`. |

Rules (see `docs/engine-async-split-audit-and-design.md` in the Android repo for the design this belongs to):

- `POST .../log` accepts `performedAt` (optional). It cannot be more than 5 minutes in the future (a smaller drift is clamped to now) nor before the plan's start day (`400`, field `performedAt`).
- There is **no registration window** any more: a session of an active plan can be registered however late and in any order. `session_out_of_window` is still
  returned by swap/alternatives of sessions out of the current and previous plan weeks, not by logging.
- Logging never changes a session's identity or planned date.
- Adaptation reads the history ordered by `performedAt`, with `now` = the newest `performedAt`; cooldowns use the adaptation's `EvidenceAt` (the `performedAt` of the workout that caused it).
- Only **pending sessions after the furthest resolved `sequenceIndex`** can be adapted. Sessions already resolved are never rewritten, and forgotten pending sessions before that point never use up an adaptation.
- A workout **older than another one already saved** is stored and enters the history, but does not apply automatic changes (the report says so in `notes`): the plan was already adapted with newer data.
- Muscle feedback treats every log as its own exposure even when several happen on the same local day.

Migration `20261117100000_WorkoutPerformedAtAndSessionSequence`: `PerformedAt = LoggedAt`, `PerformedOn` = day of `LoggedAt` in `Europe/Madrid`, `TimeZoneId = 'Europe/Madrid'` for old logs;
`SequenceIndex` numbered per plan by (`Date`, `Title`, `Id`); drops the now-redundant `IX_workout_logs_UserId` (covered by the new `(UserId, ClientRequestId)` index). Clients that do not send `performedAt` keep working unchanged.

## Last performance and proposed load

Each exercise of `GET /plans/current/week` carries `lastPerformance` (null when the user never logged it): the date and the completed,
pain-free sets of the latest session with that exercise in any of the user's plans, plus `suggestedLoadKg` and `suggestionBasis`.
The load repeats the heaviest weight of that day (`repeat`) or adds one increment, 1 kg below 20 kg and 2.5 kg above, when every
set at that weight reached the top of the repetition range (`increase`). `targetLoadKg` from the engine, when present, wins on the client.

## Progress (`GET /api/v1/plans/progress`)

Read-only, built from every completed set the user ever logged (all plans), so it also works with a lapsed Premium and with no plan.
Returns `summary` (sessions total, last 30 days and this week, weekly goal = training days of the active plan, streak of consecutive
weeks with a workout, sets and volume), `exercises` (per exercise up to 30 workouts: heaviest load, reps at it, Epley estimated
one-repetition maximum up to 12 repetitions, volume; `bestEstimatedOneRepMaxKg`, `recordDate` and `newRecord` when the latest
workout beat all earlier ones) and `muscles` with `weekStarts` (last 8 Monday-based weeks; a primary muscle counts 1 per set, a secondary one 0.5).
Weeks and dates use the user's time zone.

## Current block (`GET /api/v1/plans/current/block`)

Returns the whole current mesocycle (at most four weeks, about a month) as `weeks[]` with each week's sessions, prescription and logs,
and `isCurrent` for today's week. It lets the app show the month ahead (volume and intensity per week). Later blocks are still never
served: they do not exist until their first week arrives, so the anti-copy rule keeps holding. Showing the full paid period to annual
subscribers is a product decision still open (it would mean generating blocks ahead, which gives up adaptation between blocks).

## Supersets (manual plans)

A manual-plan exercise may carry `supersetGroup` (a letter A-F). Two or three consecutive exercises with the same letter form a superset:
the app does one set of each in turn and rests after the last. The group is stored beside the engine's fields in the prescription JSON
(like `swappedFromExerciseId`), so the adjuster keeps it; it is returned as `supersetGroup` in each exercise of the week and the block.
Generated plans do not use supersets.

Free accounts (and lapsed Premium) get `lastPerformance` and a proposed load that repeats the last load; the proposed **increase**
(`suggestionBasis: increase`) is automatic progression and is Premium only.

## Editing a manual plan (`PUT /api/v1/plans/current/manual`)

Body as in creation (name, notes, days with exercises and optional `supersetGroup`); `totalWeeks` and `startDate` are ignored because the
plan keeps its length. Only plans with origin `Manual` can be edited (`400 not_allowed` otherwise, `404 no_active_plan` without a plan).
Logged sessions and past days stay as they are; every pending session from today on is replaced by the new routines, so loads
written by the adaptation into those sessions are lost (the user's edit wins). Days dropped from the routine disappear from the
future weeks; a new weekday creates sessions from today on. Free for every account.

The plan keeps its way of scheduling. A **sequence** (asynchronous) plan has no dates or "today": every pending session is replaced,
`days[].day` is ignored and `sessionsPerPeriod` becomes the number of routines sent. Each logged session keeps its place in its
period; the new routines fill the positions of that period that are still free, and all sessions are renumbered (`sequenceIndex`)
in program order. A calendar plan whose request lacks a weekday for a day answers `400`.

## Post-exercise questionnaire (muscle feedback)

`POST .../log` accepts `exerciseFeedback`: an object by exercise id with `stimulus` (`low|adequate|high`), `discomfort` (`none|mild|strong`) and
`preFatigue` (`recovered|loaded`); every field is optional and invalid values give `400`. It is saved with the log for every account
(`workout_logs.ExerciseFeedbackJson`, migration `WorkoutExerciseFeedback`). Only Premium adapts from it, with the engine's
`TrainingMuscleFeedbackAdapter`: one set more for an exercise after two sessions with low stimulus, recovered and no discomfort (below the 20-set
weekly ceiling and 5 sets per exercise); one set less after repeated high stimulus with fatigue. Each change applies to the next sessions, has a
14-day cooldown per exercise and is audited as `muscle_add_set` / `muscle_reduce_set`. Discomfort is never automated: mild repeated or strong discomfort
only adds a note (swap or review suggestion) to the adaptation report.

## Intensification blocks

For a block whose phase is `intensification` (only strength plans of non-beginners get one, in the second half), `IntensificationPhase` (Application layer, not the
shared engine) rewrites the generated prescription: lifts worked in 6 repetitions or fewer move to a lower range (floor 2; for example 3-6 becomes 2-4), the
target RIR drops by one (never below 1), rest grows 30 s (max 300 s) and the target load, when there is one, follows the range with the Epley ratio (capped at +8 %,
rounded to the load increment). Accessories keep their volume and range. Double progression and the adaptation work on the new range unchanged.

## Next session and recovery (`GET /api/v1/plans/current/next`)

The backend decides which session the user should do next; clients only show it.

- `session`: the first **pending** session in program order (`sequenceIndex`), whatever its planned date. A session logged out of order does not move the program. `null` when nothing is pending (plan finished) or the plan is locked (`locked: true`).
- `position`: `period` (plan week; in the asynchronous mode it will be the cycle), `sessionInPeriod`/`sessionsInPeriod` and the block (`mesocycle`).
- `overdue`: pending sessions whose planned date has passed. They can still be registered (there is no window) and are never skipped automatically.
- `schedulingMode`: `Calendar` or `Sequence`. `IPlanTimeline` (`CalendarTimeline`, `SequenceTimeline`) answers "which is next / what is late"; the engine itself (generation, prescription, progression, adaptation) is shared by every mode.
- `availability` (**Premium**; without it `availabilityLocked: true` and `availability: null`): `state` `Ready | Caution | Wait`, `availableAt`, `reasons` (Spanish, naming the muscles) and `alternatives` (later pending sessions that are ready now). It is advice: it never blocks logging.

`RecoveryAdvisor` (application layer, not the shared engine) works per muscle, not with a fixed rest: effective sets (primary and meaningful secondary muscles 1, incidental ones 0: the engine's own definition, the same as the generator and the load chart) over the last 6 days of the user's workouts, weighted by proximity to failure (RIR), scaled by experience, and lengthened by insufficient/partial recovery, session RPE >= 9, pain and "loaded" pre-fatigue answers. Window = `24 h + 5.5 h x weighted sets`, capped at 120 h; a muscle with fewer than 2 effective sets is not counted, and a pending session only counts muscles it trains with at least 3 effective sets. `Caution` = at most 12 h short. The coefficients are product values (constants in `RecoveryAdvisor`) to be tuned with real data.

## Asynchronous split (`schedulingMode: "Sequence"`)

A plan advances either by calendar weeks (`Calendar`, the default and every existing plan) or by the order of its sessions (`Sequence`): the user trains on whatever days suit them and the program continues from the session that follows the last one resolved. It is not a calendar plan without days: the **same engine** (generation, selection, prescription, progression, adaptation, recovery) runs for both, and only the answer to "where am I / what is next" changes (`IPlanTimeline`: `CalendarTimeline`, `SequenceTimeline`).

- **Creation**: `POST /plans` with `schedulingMode: "Sequence"` and `sessionsPerPeriod` (2-6, the sessions of one rotation); `trainingDays` must not be sent. Manual plans (`POST /plans/manual`, free) accept `schedulingMode: "Sequence"` too: the routines are a rotation in the order given and `day` is not needed. `GET /plans/options` announces `schedulingModes` and the allowed `sessionsPerPeriod`. The mode is chosen at creation and does not change afterwards (archive and create another).
- **Period**: the plan's unit of progress is the *period* (`weekNumber` in the responses): a week in a calendar plan, a **cycle** (one pass through the rotation) in a sequence plan. `totalWeeks` is the number of periods. Phases and blocks (mesocycles of at most 4 periods) are ranges of periods (`startPeriod`/`endPeriod` in `phases`), so the macrocycle structure is identical in both modes; the dates exist only in calendar plans.
- **No dates**: in a sequence plan `date`, `day`, `weekStart`, `weekEnd`, `endDate` and the phase dates are `null`; `sequenceIndex` is the identity and order of every session. Clients must treat those fields as optional.
- **Position**: the current period is the one of the first pending session; the plan is finished only when every period has been generated and every session resolved (done, partial or skipped). The clock never moves it. A block is generated when the previous one is resolved.
- **Deloads and blocks follow progress**: a deload cycle is reached by resolving the cycles before it, never because weeks passed. The reactive deload (fatigue) still exists; its window is the next rotation of sessions (`sessionsPerPeriod`) instead of 7 days.
- **Registration**: identical to the calendar mode (see "Planned vs performed vs registered"): any pending session can be logged at any time and in any order; a log older than newer ones already saved never changes the plan.
- **Next session and recovery**: `GET /plans/current/next` returns the first pending session of the rotation, never any `overdue`, and (Premium) the recovery advice with ready alternatives, exactly as in the calendar mode. `reentry` appears after 21 days without training: the plan stays where it was and suggests easing back in (informational; it does not rewrite the plan).
- **Compatibility**: migration `20261117150000_AsynchronousSplit` (existing plans become `Calendar`; `sessionsPerPeriod` from their training days; phase `startPeriod`/`endPeriod` back-filled from the dates; dates become nullable). `PlanView`, `SessionView`, `WeekView` and friends only gain fields or turn some dates nullable (always present in calendar plans). Android must read the new fields before offering the mode.
- Fixed on the way: a block generated for an already tracked plan was saved as an UPDATE of an existing row and failed, so the second block of any plan could not be generated; it is now added explicitly.

Not yet: the calendar plan's "recolocar plan desde hoy" after a week without logs, and an automatic lighter first session on re-entry.

## One engine, two scheduling policies (engine 0.11.0)

*Audit that led here.* The first sequence mode (PR #77-#80) reused the calendar engine with synthetic weekdays: every period was generated as an independent week, so the split **restarted at each period** (with 3 sessions of upper/lower the user would have trained upper, lower, upper on every period), only the combinations a calendar week supports were allowed (no upper/lower in 3 or 5 sessions, no push/pull/legs in 4, no full body in 5 or 6, no automatic split in 5 or 6), and the plan report and the load chart called a group of sessions "a week". A week, a rotation and seven days were three different things sharing one name. The engine itself (selection, prescription, progression, adaptation) was already common and stays so; what changed is what it plans *over*.

### The generation unit

The engine plans one **unit** of sessions at a time and then lays it out over the periods of the block (`GenerationUnit`, `TrainingSessionRotation` in `Trazo.Training/Engine`):

| | Calendar plan | Sequence plan |
|---|---|---|
| Unit | one week: the split's list for the training days | one **cycle**: the closed list of K sessions of the split (full body A/B = 2, upper/lower A/B = 4, push/pull/legs A/B = 6) |
| Nominal weeks of the unit | 1 | K / N (N = sessions per period = the user's habitual sessions in seven days) |
| Laying out | the unit repeats every week | session number `p` of the plan is `cycle[p mod K]`; the period only groups N consecutive sessions |
| Start | week 1 | `StartPosition` = number of sessions the plan already has, read from what is persisted (`SequenceIndex`), never from a stored counter or the clock |

The calendar case is the sequence machinery with K = N and one week per unit, so there is **one code path**. `ClassicalPlanGoldenTests` pins 1,188 classical plans (every goal, level, supported split and day count, three equipment sets, block / deload / second block / limitations) by hash: the refactor changed none of them. The classical restrictions are untouched (`IsSupportedWeekly`: full body 2-4, upper/lower 2 or 4, push/pull/legs 3, 5 or 6, automatic 2-4).

Sequence plans accept full body 2-6, upper/lower 2-6, push/pull/legs 3-6 and automatic 2-6 (`IsSupportedSequence`, announced by `GET /plans/options` in `splitSupport` so clients do not rebuild it). Automatic picks full body up to 3 sessions, upper/lower for 4 or 5 and push/pull/legs for 6 (a product choice). When a period holds exactly one split (full body 2, upper/lower 4, push/pull/legs 6, automatic 2 or 4) the sequence plan is identical to the calendar plan (tested).

Odd divisions simply fall out of the rotation: upper/lower in 3 sessions reads `U L U | L U L | U L U` across periods; push/pull/legs in 4 sessions ends its first block on "Empuje B" and the next block (the deload) starts with "Tracción B". There is no restart at a period, a block or a deload.

### Volume: five different things

The allocator, the accessory selection and the quality report plan against **weekly** targets. A cycle spans K/N weeks, so the allocator measures the volume of the unit divided by `weeksPerGroup` (K/N) and one added set counts for N/K of it; with one week per unit nothing changes. Every ceiling stays weekly (20 effective sets, per-exercise limits, time per session). When the minimum dose of the exercises alone exceeds a ceiling (full body six times a week) the plan carries the usual review warning instead of hiding it.

| Quantity | Where | Meaning |
|---|---|---|
| Volume per session | prescription / log | sets of one session |
| Volume per rotation (period) | `weeks[]` of `GET /plans/current/load` | sets of the sessions of a period; `periodKind` says `Week` or `Rotation` |
| Exposure | `LoadMetrics` | a workout that works a muscle with at least 2 effective sets |
| Dose per seven days, prescribed | `rolling.prescribedPer7Days` | the current block divided by the nominal weeks it spans |
| Dose per seven days, real | `rolling.actual` | effective sets and exposures of the workouts of the last seven real days (`performedAt`) |

Five sessions done in six days and the same five done in ten days have the same volume per rotation and a different real dose: the dose is computed from instants, never from a period number (`LoadMetricsTests`). `rolling.observedSessionsPer7Days` is the pace between the user's workouts of the last 28 days and is **null** with fewer than four workouts or less than five days between the first and the last: the evidence does not support a number, so none is shown. It describes the recent past; it does not say the user is still training. Sets are counted as the generator counts them (the engine's effective muscles: primary plus meaningful secondary, each at 1), so the planned and the real dose are comparable and the 20-set review ceiling applies to both; it is a planning convention and not a measurement of stimulus.

Why not a moving window for the *plan*: the plan has to be generated before it is executed and the engine's targets are weekly, so the plan is balanced per cycle at the nominal cadence; the real dose is measured after the fact in a window of seven real days. Rotations were rejected as the unit of *measurement* (they are not time); exposures alone were rejected (they ignore sets).

### Recovery: hard constraints, soft advice

- Hard (the engine enforces them while generating, in both policies): session time limit, per-exercise set limits, the weekly ceilings, level, equipment, exclusions, limitations, one exercise at most once per session, the classical split and day combinations.
- Soft (never prevents logging): `RecoveryAdvisor` reads the **instants** of the workouts, the effective sets per muscle weighted by proximity to failure and by what the user reported, and answers `Ready / Caution / Wait` with `availableAt` and alternatives. A period number is never a substitute for elapsed time, and the user can alter the order whenever they want. The coefficients are product values to tune with real data; no physiological model is claimed.
- The cadence does not change the rule: it already depends on real elapsed time, so a user who trains every other day and one who trains daily get different answers from the same rule.

### Progression

Progression is per exposure: the double progression, the load references of the next block and the adaptation rules read the logged sets ordered by `performedAt` and never the period or the week of the calendar. A sequence plan's blocks, deloads and end are reached by resolving sessions, so progression cannot advance because time passed. A calendar plan keeps its documented behaviour (the next block is generated when its first week arrives). A skipped session is resolved without being an exposure; sessions logged out of order enter the history without applying automatic changes that newer data already superseded (see "Planned vs performed vs registered").

### Execution state

No new table: the state is derived from persisted facts. The last resolved session, the next pending one (`/current/next`), the rotation position (`(sequenceIndex - 1) mod K`), the real instants and the exposures come from `planned_sessions` + `workout_logs`; a stored copy would only be a second source of truth that can drift (a counter on the plan would break as soon as a session is edited or a block is regenerated). The generator receives the position as an argument.

### Compatibility

No schema change and no migration. Plans already created keep their sessions untouched; a sequence plan created with the previous version continues its rotation from the number of sessions it has (for a split whose period held a whole split nothing changes). Contract changes are additive: `PlanOptions.splitSupport`, `PlanLoadView.periodKind` and `.rolling`. `WeekNumber` keeps its column and name and means *period* (documented since the asynchronous split). Android reads `periodKind` to say "cycle" instead of "week" in the load chart; older apps ignore the new fields.

### Alternatives considered

- *One generation per period with the cycle offset* (each period an independent week of N sessions of the rotation): rejected, because the weekly volume targets would be judged on `U L U` and `L U L` separately and always miss.
- *Allocate a whole block as one group*: rejected, it moves sets between periods arbitrarily and breaks the intra-block ramp.
- *A persisted rotation index on the plan*: rejected (see Execution state).
- *Scale every target and ceiling by K/N inside the allocator*: rejected for the same result with many more edits; normalising the measured volume leaves every weekly constant as it is.
- *Keep two generators*: rejected by the brief; the calendar plan is the K = N special case.

### Known limits

- A cycle of K sessions at N sessions per seven days assumes the user trains about N times a week. A user who trains much less gets a lower real dose (visible in `rolling`) but the plan is not rewritten: no correction factor is invented. Adapting the *prescription* to the observed pace is the next step and needs real data.
- Sequence plans of full body in 5 or 6 sessions per period work the same muscles very frequently; the minimum dose of the exercises can exceed 20 weekly effective sets, which the plan reports in its warnings.
- The calendar allocator breaks ties by session order and a Sunday sorts first (enum order). This predates the refactor, is pinned by the golden test and was left alone on purpose.
- `TrainingProgramQualityReport.WeeksPerGroup` was added to the engine: DietoExpress does not have it yet (see `docs/shared-engine-package.md`).

## Coming back after time away: reschedule and re-entry

**Reschedule from today** (calendar plans). When the pending program is at least a week behind the calendar (the first week that still has something to do is before the current week), `GET /plans/current/next` returns `reschedule: { weeksBehind }`. `POST /api/v1/plans/current/reschedule` then moves the **pending** sessions forward by whole weeks, so every session keeps its weekday and the first pending week becomes the current one: `{ weeksShifted, sessionsMoved, startDate, endDate }`. The plan's start date, the phase dates and the end date move with it (the plan lasts the same number of weeks; it ends later). Sessions already done are never rewritten. The days moved are stored in `ScheduleShiftDays`, so a workout of the skipped days can still be registered (`performedAt` may be as early as the original start). Repeating the call changes nothing (`weeksShifted: 0`). Sequence plans have no calendar: `400 not_allowed`. Every move is stored in the audit (`reschedule`).

**Lighter first session** (all plans, Premium; adaptation is Premium; since the strategies below it is graded, see "Sessions not done"). After 21 days without training (since the newest real workout, not an omitted session), reading `GET /plans/current/next` makes the next session lighter on its own: one set less in every exercise and one more repetition in reserve (the engine's own `TryReduceSets` and `TryRaiseReserve`, so the usual rules give the sets back after clean sessions). It happens **once per absence**: the audit row `reentry` has `EvidenceAt` = the last workout and a unique index lets only one request apply it, even if two reads arrive at once. A deload session is left alone. `reentry` in the response says `applied` and carries the message; without Premium the message advises it and nothing is changed. As with generating a block when the week is read, this read has that effect on purpose, so the session the app shows is the session stored.

## Sessions not done: shift, omit, ease back, rebuild

A session not done is not a volume problem. Four different things can happen and the engine keeps them apart; `AdjustmentAdvisor` (pure, `Trazo.Application/Planning`) picks the one that fits the real state and `GET /plans/current/next` returns it as `adjustment` (`strategy`, `reason` code, `message`, `easeBackSessions`, `canReschedule`, `suggestSkip`, `canSkip`). Nothing is stored for it: it is read from the sessions, the logs and the clock.

| Strategy | When | What happens |
|---|---|---|
| **Continue / shift the queue** (default) | A one-off miss, any cause | The late session stays pending and is the next one; every later session keeps its order and prescription; nothing is marked done, nothing is duplicated or lost. In a sequence plan this is the whole mechanism (no dates, the week never restarts it). In a calendar plan the late sessions are done first; `reschedule` (below) moves the dates once the program is a week behind. |
| **Shift the calendar** (`Shift`) | Calendar plan, pending program at least one whole week behind | `canReschedule`: `POST /plans/current/reschedule` (unchanged). The plan ends later; no session is lost. |
| **Omit** | Only the user decides, `POST /plans/current/sessions/{id}/skip` `{reason, notes}` | See below. The advisor only *suggests* it (`suggestSkip`: a calendar plan with 2 or more late sessions inside the current week, where recovering the oldest no longer makes sense). |
| **Ease back** (`EaseBack`) | 21 to 41 days without training: the first session back is lighter; 42 to 83: the first two | One set less in every exercise and one more repetition in reserve (Premium; free users get the advice). Once per absence and session: each session carries `easedBackFor` (the instant of the last real workout), so reading `/next` again, or an omission in between, never eases twice. The row `reentry` of the audit holds the number of sessions eased. The absence growing from 21 to 42 days eases the second session too, without touching the first again. |
| **Rebuild** (`Rebuild`) | 84 days or more without training, or 3 omitted out of the last 5 resolved sessions | A suggestion, never an automatic rewrite: create a new plan with the current profile (archive and create). The first sessions back are still eased for whoever keeps the plan. |

**Omitting** (`skip`) is free. The session is resolved as `Skipped` with a log without sets (the history keeps it), so it is **not an exposure** (no progression reads it, the double progression and the load references ignore it), it is **not training** (the absence clock reads the newest workout that was not `NotCompleted`, so omitting yesterday does not hide a three-week absence) and it does not touch the rest of the program. `reason` is Time, Fatigue, Equipment, Motivation, Illness or Other (empty = Other). A reason of agenda (Time, Equipment, Motivation, Other) changes nothing else; a **physical** one (Fatigue, Illness) goes through the usual adaptation (Premium), exactly like a session logged as not completed for that reason. Every omission writes an audit row `skip` with its reason. A session already resolved answers `409`; a second identical request cannot omit twice.

Decisions and limits:

- **Recovery stays advice.** `availability` (Premium) says whether the next session is `Ready`, `Caution` or `Wait` and offers ready alternatives; no strategy blocks logging, and a late session is never reordered mechanically. Free accounts get the queue but not the recovery advice.
- **No redistribution of the missed sets.** The adaptation after every logged session already rebalances with conservative rules; moving the sets of a session not done into others would add volume that nobody planned, so it is deliberately not offered.
- **No fixed deadline.** A plan has no end date the user must hit, so "the block must end on a date" is not a case: rescheduling lengthens the plan and omitting is the way to shorten the queue.
- The thresholds (21, 42, 84 days; 3 of 5 omissions; 2 late sessions) are product values (constants of `AdjustmentAdvisor`) to tune with real data.
- Load targets are not reduced after an absence: the lighter sessions rely on one set less and one more repetition in reserve, and the usual rules restore them.

## What the engine adjusted, in view (`GET /api/v1/plans/current/adaptations`)

The plan is recalculated **after every logged session**, not at the end of the block: `AdaptAsync` rewrites the pending sessions after the last one resolved (up to 40, in the same block and the next ones already generated). Automatic adaptation is Premium; omissions and moves are for everyone. Everything the engine decides is stored in `plan_adaptations` and shown:

- `entries` (newest first): `kind`, `summary` (one line with the numbers: "Una serie menos en Press banca en las próximas 3 sesiones."), `explanation` (the engine's reason), `exercises` (id and name), `sessionsChanged`, `changed`, and the session that caused it (`sourceSession`, `sourceDate`). Kinds: `reduction_sets`, `muscle_reduce_set`, `muscle_add_set`, `restore_sets`, `raise_reserve`, `deload`, `hold_progression`, `load_update`, `reentry`, `reschedule`, `skip`, `review`.
- `review` is a session the engine read and **left as it was** (`changed: false`): the user sees that it looked and why it kept the plan. It is written only for the newest evidence (a late log never decides anything).
- `load_update` records every new target load (`Carga objetivo de la próxima sesión: 62.5 kg. ...`), which used to be written silently into the session.
- Totals: `reviews`, `changes` (decisions that changed sessions), `sessionsAdjusted`; `automatic` is false when the account has no automatic adaptation (then only omissions and moves appear).
- Texts follow `Accept-Language`; every new sentence has its entry in `EnglishCatalog`.
- **How long they last.** The adjustments belong to the plan: archiving it or creating another leaves them out of the screen (they stay stored for the history and to tune the engine). Inside a plan, `scope=block` (default) shows the block in progress (the one of the first pending session) and `scope=plan` every block; `planChanges` and `hasEarlier` tell the client there is more. An adjustment is in a block when the workout that caused it is; the ones with no workout (absences, moves) belong to the block when they happened after the last workout of an earlier block.

**Proposed load with several sessions** (`lastPerformance.suggestionBasis: "estimate"`, `basedOnSessions`; Premium). `LoadEstimation` takes the best estimated maximum (Epley with the reserve reported) of up to the last three sessions of the exercise, weighted 3:2:1 from the latest, converts it to the load of the middle of the repetition range at the prescribed reserve, rounds it to the increment (1 kg below 20 kg, otherwise 2.5 kg) and bounds it around the last load (down 10 %, up 5 %). It needs two sessions with usable sets; with one it stays on `repeat`, and `increase` (every set at the top of the range) keeps priority. Limit: the estimate is per exercise, so a new exercise has no proposal even if an equivalent one has history.

## Language (`Accept-Language`)

The plan API (`/api/v1/plans/**`, including the review conversation) answers in the language of the request: **Spanish by default**, English when the first language the client asks for (by quality) is `en`. The app should send `Accept-Language: <device language>`; anything missing, malformed or unsupported is Spanish, so no existing client changes. There is no stored preference yet.

What is localized: error `detail` and validation `errors`; the disclaimers; the titles, phase names and objectives of **generated** plans (the words of a manual plan are the user's and are never translated); the exercise names (English from the catalog's `NameEn`, read per request); the adaptation reports (explanations, applied changes and notes, including the engine's); the recovery reasons and the re-entry message; the day labels of the options.

How: the Spanish text is the key (`Localizer` + `EnglishCatalog`, sentence by sentence, with `{0}` placeholders for interpolated values), so `Trazo.Training` — the engine shared with DietoExpress — is **not touched** and keeps writing Spanish; its messages are translated at the boundary. Recovery reasons are written natively in both languages (`RecoveryAdvisor`). A text with no entry stays Spanish (never an error); the API tests of the English flows fail if any Spanish leaks (accents and common words), so **when a message changes, add its entry to `EnglishCatalog`**.

**Every app-facing endpoint** now follows the same language, not only the plans API:
- A middleware sets the language of every request. `ProblemDetails` (all `Results.Problem` / `Results.ValidationProblem`, through `AddProblemDetails`) and the `{ error, message }` bodies (`ApiErrors.BadRequest/Conflict/Json`) are put in it on the way out. Texts were written in Spanish in some places and in English in others (the authentication errors), so the catalog works **in both directions**: a Spanish request gets Spanish for the English-written ones too. A text with no entry is returned as written.
- E-mails (verification, reset, invitation, account deletion, SMTP test) are built with the language of the request; the account-deletion web pages follow the browser's `Accept-Language`; the exercise filter labels (`GET /exercises/options`, which now varies on `Accept-Language`) come in both languages.

**The user can choose the language** (`PUT /api/v1/account/language` with `es`, `en`, or empty/`auto`; stored in `users.Language`, reported in `GET /account` and `GET /auth/me` as `language`). On a signed-in request the chosen language **wins over the header** (one small lookup per authenticated request); with `auto` the header decides, as before. E-mails to a known user (verification, reset, account deletion) and the review notifications (push title/body and e-mail) go out in the language they chose; with no choice, e-mails use the language of the request and notifications Spanish (the app also translates a notification from its `kind`). A user who changes language on one device changes it for all.

Not localized: the admin console texts (operator-only: overview insights, admin plan-review messages), the audit trail (`PlanAdaptation.Explanation`), the stored `SelectionRationale`, engine messages that do not reach the app, and push notifications (they need the user's own language, not a request's).

## Engine 0.11.1: chest/back balance and one definition of "effective set"

A review of 20 plans generated from the real exercise library (calendar and sequence) found the chest at the bottom of its range while the back was above it (upper/lower: about 10 chest sets against 16 lats and 13 upper back). The cause was in accessory selection: the lats had a direct accessory reserved when they were under target and the chest had none, and a press is limited to five sets, so nothing else could raise the chest. The chest now gets the same first call (`prioritizeChestAccessory`). In that plan the chest reaches 13 effective sets (its target) and no other muscle moves out of range. It changes 698 of the 1,188 pinned classical plans (every goal; one accessory per week where the chest was under target); `ClassicalPlanGoldenTests` was regenerated on purpose and `PushPullBalanceTests` judges the result against the real catalog (chest at least 10 sets and at least 0.65 of the lats; it fails on 3 of 10 layouts without the change).

`LoadMetrics` now counts a set for every muscle the engine itself considers stimulated (`TrainingStimulus.EffectiveMuscles`: primary plus meaningful secondary, each at 1) instead of weighting every secondary muscle at 0.5. The old weighting inflated shoulders and upper back (24-33 "weighted" sets) because shrugs and every press were counted; planned and real dose now use the same definition as the generator and the same 20-set review ceiling.

Reviewed and left as they were, with the reason: the selection of exercises (Smith press, leg press, hex-bar deadlift) comes from the catalog's curated priority, which the engine corrects but does not override; shorter leg sessions are the result of reaching the weekly targets with fewer exercises; rest periods of 120 s for 8-12 repetition compounds are at the lower end of what is recommended and raising them lengthens every session estimate; the knee limitation stays an exclusion list of lunges and single-leg work because squats cannot be dropped (see decision 6).

## Muscle priorities (Emphasize / Grow / Maintain)

A generated hypertrophy plan accepts a `musclePriorities` map at creation (`POST /api/v1/plans`) and a replacement through
`PUT /api/v1/plans/current/muscle-priorities`. A muscle that is not listed is Grow (the default dose). They are stored in
`training_plans.MusclePriorities` as `"Muscle:Priority"` entries and returned in `PlanView.musclePriorities`.

- **Emphasize**: weekly target at the top of the default range of the level (default + tolerance), never above 18 effective sets; the
  allocator keeps its usual +/-2 sets around it. At most 3 muscles can be emphasized.
- **Maintain**: half of the default dose, never below 2 sets. A maintained muscle does not lift the absolute ceiling of the
  compounds that also train it, so a maintenance priority never raises that muscle's volume.
- Configurable muscles: chest, lats, upper back, deltoids, biceps, triceps, quadriceps, hamstrings, glutes, calves. Only hypertrophy.
- Editing applies to the blocks generated from then on; the block in progress is not rewritten. The change is recorded as a
  `muscle_priorities` adaptation.
- Emphasis only has effect where accessories and set ceilings leave room: a muscle already saturated by compounds does not grow.
- The numbers (half the dose, top of range, 18/20 sets) are internal product heuristics, not validated physiological thresholds.
