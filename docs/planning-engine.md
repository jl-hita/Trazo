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
Trazo.Training        pure engine (copied from DietoExpress + one fix, see 4.9). No EF, no ASP.NET.
Trazo.Domain          Planning/: TrainingPlan, TrainingPlanPhase, TrainingMesocycle, PlannedSession,
                      WorkoutLog, WorkoutSetLog, PlanAdaptation (plain entities, enums as text).
Trazo.Application     Planning/: pure orchestration
                        PlanProfileParser      validates and normalises the user's profile
                        LimitationRules        injury area -> exercises to avoid
                        PlanSchedule           macrocycle -> blocks, dates, current week
                        MesocycleGenerator     one block, deterministic for (profile, seed, block, catalog, history)
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
    - **Core.** General-fitness and hypertrophy plans carry one direct core exercise a week (two sets) by default;
      strength plans none; an explicit professional priority still puts core in every session.
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
| GET | `/current/load` | Weekly volume in sets for every plan week: `plannedSets` (null while that block is not generated), `completedSets` (null for weeks not reached yet), phase and deload flag. Read-only, also for a plan locked by a lapsed Premium | 200 `PlanLoadView` | 404 |
| POST | `/current/archive` | Close the active plan | 204 | 404 |
| POST | `/current/sessions/{id}/log` | Record a session; with Premium it also adapts the upcoming ones (`adaptation.locked` is true otherwise). Generated plans without Premium answer 402 `premium_required` | 200 `LogResult` | 400, 402, 404, 409 |
| GET | `/current/sessions/{id}/exercises/{exerciseId}/alternatives?limit=5` | Replacement options (1-10) | 200 | 402, 404 |
| POST | `/current/sessions/{id}/exercises/{exerciseId}/swap` | Replace one exercise (optionally in the rest of the block) | 200 `SessionView` | 400, 402, 404 |

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
  and never less than the calves; no lone sets; no muscle past 20 weekly sets; one core exercise a week outside strength;
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
- Intensification phases are scheduled in the macrocycle summary, but the generator does not yet change its prescription for
  them (the source engine does not either: only the deload is a generator mode).
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
