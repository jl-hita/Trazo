# Exercise catalog audit (2026-11-23)

Scope: `Seeds/exercise-catalog.json` (the only source; the published artifact is generated from the database) and how
`TrainingProgramGenerator` consumes it. Method: every earlier finding was re-checked against the data, then the generator was run
on real equipment/level/day profiles to see what it actually selects.

## What the engine reads

Only `movementPattern`, `requiredEquipment`, `suitableGoals`, `minimumExperience`, `selectionPriority` and the primary/secondary
muscles drive generation. `equipment`, `difficulty`, `forceType`, `mechanic`, `bodyPart`, `isUnilateral` and `isBodyweight` are
display/filter metadata (app, admin panel). A wrong value there is a data-quality problem, not a planning bug.
`ExerciseEquipmentRequirements.Overrides` is a second source of truth for `requiredEquipment` and wins over the seed for 33 ids.

## Earlier findings, verified

| Finding | Result |
| --- | --- |
| 14 `difficulty` vs `minimumExperience` gaps | 26 today, by design: `difficulty` is the intrinsic RepDB rating, `minimumExperience` is the product gate (seed v3 opened guided lifts to beginners). Not changed. |
| 10 `equipment` vs `requiredEquipment` gaps | Mostly vocabulary (`leg_press` vs `leg-press-machine`). Real: `bent-knee-inverted-row` (said pull-up bar, needs barbell + rack) and `lat-pulldown` (said cable). Fixed. |
| 5 Hinge exercises with `forceType: push` | Confirmed (hip thrust and bridge family). It is the RepDB convention for hip extension against a load and nothing reads it. Not changed. |
| `step-ups` SingleLeg but not unilateral | Confirmed, fixed (new test enforces it for every SingleLeg exercise). |
| Null `equipment` with a required implement | Confirmed for 4 ids, fixed (`step`, `bench`, `captains_chair`). |
| `Accessory` too generic | True but harmless: the generator ranks accessories by weekly muscle deficit using the primary muscles, not by category. Splitting it needs an engine and migration change for no selection gain. Not done. |
| Core priorities too high | Core is already excluded from plans unless a professional prioritizes the abdominal wall (`PlanSessionPatterns`), and 72 generated plans contained no core exercise. The real issue was the order *inside* core: holds tied with loadable work at 75. Fixed. |

Also found: `isBodyweight` was false for pull-up, chin-up and hanging raises but true for the negative pull-up; `dumbbell-romanian-deadlift`
had `bodyPart: back` while the barbell version has `upper_legs`.

## Selection strategy behind the priority changes

Priority only breaks ties between candidates of one pattern, so each change states which tie it resolves:

- Strength-style lifts must not outrank the hypertrophy staple of their pattern: `pendlay-row` 97 → 85, `rack-pull` 97 → 80 (by the scoring arithmetic it
  would tie or beat the deadlift in strength plans), `weighted-pull-up` 97 → 80 (the weight is a dumbbell between the feet).
- Evidence-led accessory order: `cable-overhead-tricep-extension` 76 and `overhead-tricep-extension` 66 above the pushdown
  (long-head stretch), `seated-leg-curl` 66 above the lying curl (hamstring growth at long muscle length).
- Low-value accessories only when nothing better fits: `shrug` 50 and `dumbbell-shrug` 48 (rows already load the traps),
  `reverse-nordic-curl` 42 (bodyweight-only quad work with high knee demand; same tier as the other niche bodyweight curls).
- Core order: `cable-crunch` 80, `machine-seated-crunch` 78, hanging/captain's-chair knee raises 72, `ab-wheel-rollout` 68
  (lumbar demand), holds and carries 62 (they are general-fitness goals, never offered in hypertrophy plans).

## Catalog changes

- Added `dumbbell-bent-over-row` (dumbbell only, beginner-level gate, priority 62): a dumbbell-only profile had **no** horizontal pull
  and failed with "no compatible exercise for HorizontalPull" at every level. It now generates.
- Added `smith-machine-squat` (priority 85): the Squat pattern had no option for gyms without a free rack, unlike benching.
- Retired `standing-calf-raise` (`isActive: false`): same machine token and movement as `machine-calf-raise`, never selected.
- Kept deliberately: `pec-deck` / `machine-chest-fly`, `leg-press` / `horizontal-leg-press` and `lat-pulldown` / `v-bar-lat-pulldown` look
  alike but are distinct machines or attachments, so removing one would leave a gym without a valid option.

## Translations

Spanish was normalised to Spain gym usage and kept consistent between equivalent exercises:

- Latin-American forms: "Acuéstate/Recuéstate" → "Túmbate", "Párate" → "Ponte de pie", "jalar" → "tirar", "pantorrillas" → "gemelos", "máquina de cable" → "polea".
- One word per concept: "polea" (not "cable"), "pesa rusa" (not "kettlebell"), "Flexiones" for the whole push-up family, plural "Elevaciones Laterales", "Dominadas".
- Misleading names: `band-pull-apart` was "Aperturas con Banda" (a fly), `tricep-pushdown` was "Jalón de Tríceps" (it is a push), `machine-seated-crunch` "Abdominales en Máquina".
- Wording that could mislead: the step-up descent, "hasta que los muslos queden paralelos" at the top of a hip thrust (torso and thighs should align), "Desrackea", "Bisagra en las caderas".
- English: "Bicep/Tricep" → "Biceps/Triceps", hyphenated "Pull-Up", "Step-Up", "Knee Push-Up".

Not done: the model has no English instructions or tips (the seeder creates English draft rows with the name only). Completing them is
a content task for the admin translation workflow, not a seed change.

## Proposed generator work (not implemented)

The engine is shared with DietoExpress, so these should be ported together:

1. `GetDirectIsolationGap` recognises lateral raises by the substring `lateral-raise` in the id, against the "never from names" rule. Use
   a reviewed flag (for example a `LateralDeltoid` stimulus entry) instead.
2. `TrainingExerciseStimulus` is a hand-kept id table and a test forces an entry for every curated exercise. Move the decision into
   catalog data so a new exercise cannot be forgotten and the DietoExpress copy cannot drift.
3. Accessory choice repeats the same lat/chest isolation in every upper session (`straight-arm-pulldown`, `machine-chest-fly`);
   a per-week cap per muscle for isolation fillers would add variety.
4. Equal-priority isolation (lateral raises, curls, calves) is resolved only by the plan seed. Tiering by evidence, as done above for
   triceps and hamstrings, is the lever; the remaining ties are acceptable because equipment availability decides them.
5. `requiredEquipment` overrides in code duplicate the seed; fold them into the seed so there is one source.
