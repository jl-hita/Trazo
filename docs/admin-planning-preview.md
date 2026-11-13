# Administration: planning preview API

Superadmin-only routes under `/api/v1/admin/planning`. They run the planning engine (`Trazo.Training`, see
`docs/planning-engine.md`) against the live exercise catalog. **Nothing is saved**: the preview exists to evaluate
the engine and the catalog before plans are persisted for users.

| Method | Route | Purpose |
|---|---|---|
| POST | `/preview` | Generate a program block from a profile. Optionally returns a macrocycle draft |
| GET | `/catalog-readiness` | How many catalog exercises the engine can use, by pattern, level and goal, and which ones it skips and why |

## Preview request

```json
{
  "goal": "Hypertrophy",            // GeneralFitness | Hypertrophy | Strength
  "experience": "Intermediate",     // Beginner | Intermediate | Advanced
  "startDate": "2026-11-16",
  "durationWeeks": 4,
  "trainingDays": ["Monday", "Tuesday", "Thursday", "Friday"],
  "maxSessionMinutes": 75,          // 35 to 180
  "availableEquipment": [],         // empty = everything is available
  "excludedExerciseIds": [],
  "split": "Automatic",             // Automatic | FullBody | UpperLower | PushPullLegs
  "isDeload": false,
  "macrocycleWeeks": 12,            // optional: adds a macrocycle draft
  "includeDeloadPhases": true,
  "deloadEveryTrainingWeeks": 4     // 4 to 8
}
```

The response contains the structured `program` (sessions, exercises, sets, repetition range, RIR, rest, progression
rule, selection rationale and muscle groups), the engine's `qualityReport` (weekly volume and frequency per muscle plus
review notes), the optional `macrocycle` draft, the number of `usableExercises` and the `skipped` exercises.
Enums are returned by name. Validation problems and the engine's explanations (for example why no exercise fits a
movement pattern) come back as `400` with an `errors` array.

## Catalog mapping

`ExerciseCandidateMapper` converts catalog rows into engine candidates. The catalog uses the engine's vocabulary, so
values are parsed, never guessed:

- Hidden exercises, and those with a movement pattern the engine does not support (for example `Cardio`), an unknown
  experience level, no recognized goal or no recognized primary muscle, are skipped and reported.
- Unknown secondary muscles or goals are dropped.
- Every active catalog exercise is treated as reviewed: imported exercises were approved in DietoExpress and
  administrators curate the rest.
