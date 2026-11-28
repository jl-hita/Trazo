# Engine scorecard

What "7 out of 10" means for the plan generator and for the adaptation, written **before** changing either of them (the
thresholds below were fixed first and the baseline measured on engine 0.17.0).

Score = `10 × criteria passed / criteria`. A **7** needs a score of at least 7.0 **and** every mandatory criterion (M).

These are product criteria, checked on generated plans and on simulated people. They say that the engine behaves as intended;
they do **not** show that people grow, get stronger, keep training or like the plans. That, and whether the questions are
answered honestly in real life, can only be established with testers. No figure here is a validated physiological threshold;
the volume, frequency, repetition and effort ranges follow the planning ranges already agreed for the engine and the public
guidance of Renaissance Periodization (2017–2018).

## Generation

Matrix: 3 goals × 3 levels × 2–6 training days × 45, 60 and 90 minutes × 5 equipment sets (full gym; barbell garage;
dumbbells, bench and pull-up bar; dumbbells and bench; bands and pull-up bar) × 2 seeds, on the real seed catalog. Each plan
is generated as a first block of four weeks, its deload and the following block of four weeks. "Gym cells" are the full gym and
the garage.

| | Criterion | Passes when |
|---|---|---|
| G1 M | Integrity: material, exclusions, limitations, level, no exercise twice in a session, session within the time | 0 violations |
| G2 M | A plan comes out | ≥ 99 % of profiles with at least dumbbells, bench and bar; ≥ 90 % with the two smallest sets |
| G3 M | Blocks ramp: weekly sets never fall, mean reserve never rises, the deload has ≤ 70 % of the peak sets | every hypertrophy and strength block |
| G4 | Weekly dose (hypertrophy, intermediate and advanced, 3+ days, 60+ min, gym): chest, back, quadriceps, hamstrings, glutes, shoulders, biceps and triceps get 8–22 effective sets in the peak week | ≥ 95 % of plan and muscle |
| G5 | Direct work, same plans: chest, back, quadriceps, hamstrings, shoulders, biceps, triceps and calves get ≥ 4 sets as a primary muscle | ≥ 90 % of plan and muscle |
| G6 | Frequency (hypertrophy, 60+ min, gym): those eight muscles are trained in ≥ 2 sessions of the week | ≥ 95 % of plan and muscle |
| G7 | Session dose: no muscle gets more than 12 effective sets in one session | ≥ 99 % of sessions |
| G8 | Repetition ranges (hypertrophy, intermediate and advanced, 60+ min, gym): a week has a range topping at ≤ 10, one topping at ≥ 15 and at least three different ranges | ≥ 90 % of plans |
| G9 | Continuity between the two training blocks (gym): ≥ 60 % of the compound lifts stay, and something changes | ≥ 90 % of plans |
| G10 | Balance (gym and dumbbells with bar): pulling sets ≥ 0.8 × pushing sets, hamstring sets ≥ 0.5 × quadriceps sets | ≥ 95 % of plans |
| G11 | Time used (hypertrophy, ≤ 60 min, gym): a session fills ≥ 65 % of the time offered | ≥ 85 % of sessions |
| G12 | Strength (gym): the first exercise of every session is a compound lift of ≤ 6 repetitions and ≥ 3 sets, and it is the same in the next block | ≥ 90 % of sessions |

## Adaptation

Whole twelve-week plans lived through the API by simulated people whose strength the engine never sees
(`EngineSimulationTests`), four plan kinds × 2 seeds. The **gap** is the mean distance, at the end, between the load a person
lifts and the load at which they would reach the middle of the range with the reserve asked for. An exercise is **under** below
80 % of it and **over** above 120 %.

| | Criterion | Passes when |
|---|---|---|
| A1 M | Safety: no increase after pain, no jump over 10 % (or one step), no invalid advice, no failed request, no target far beyond reach | 0 findings |
| A2 M | Regular people (steady, noisy, stalled) | gap ≤ 10 %; under + over ≤ 5 % of exercises |
| A3 | Starting far too light (60 %) | gap ≤ 12 %; under ≤ 10 % |
| A4 | Starting too heavy (130 %) | gap ≤ 10 %; over ≤ 5 % |
| A5 | Never writing the reserve | gap ≤ 12 %; loads go up in every plan |
| A6 | Writing the reserve wrong (two or three too many, one too few) | over ≤ 5 %; sets under the range ≤ 8 %; gap ≤ 15 % |
| A7 | Novice gaining 2 % a week | gap ≤ 12 % |
| A8 | Volume follows the person: losing performance takes sets away; a low-reserve habit costs ≤ 2 per plan; "recovered, little work" about every muscle adds a set; "not recovered, too much" takes one | all four, in every plan |
| A9 | Irregular people (skipping sets, tiring, bad weeks, pain, ignoring the loads) | no findings; gap ≤ 15 % |
| A10 M | The app that no longer asks about recovery in the finish dialog keeps its protections: bad days told only through the muscle questions pause progression and can bring a deload, as the old session answer did | both seen, in every plan kind where the old answer brings them |

Not covered by any criterion, and so by no score: custom exercises, load steps per piece of equipment, carrying volume from
one block to the next (calculated in shadow only), and everything that needs real people.

## Results (2026-11-28)

| | Engine 0.17.0 (before) | Engine 0.19.0 |
|---|---|---|
| Generation | **3.3** (4/12), mandatory G2 fails | **8.3** (10/12), all mandatory pass |
| Adaptation | **7.0** (7/10), mandatory A10 fails | **9.0** (9/10), all mandatory pass |

How to read them:

- The adaptation criteria proved lenient: the engine before already passed 7 of them. A first version of the A10 check was also
  looser than its text (it did not ask for the deloads) and scored the baseline 8.0; the figures here use the check as written.
- With five days the baseline was measured asking for push, pull and legs and the result asking for the hybrid week, which is
  what the app sends in each case: part of G6 comes from that change in the app.
- Baseline of generation measured first without asking for any split with five or six days gave 5.0; that was a mistake of the
  measurement (the app never does that) and is not used.
- Still failing: **G10** (89 %, needs 95: two-day strength plans keep the main lift's dose over the quadriceps/hamstrings
  balance), **G11** (68 % of sessions use 65 % of the time, needs 85: short beginner sessions reach their volume early) and
  **A5** (without the reserve written, four of eight twelve-week plans see no load increase, as the gap stays at 8 %; before the
  generator change it was none).
- Worse than before: with calibration, someone having bad weeks falls under the range in more sets (about 13 % to 16 %).

## Running it

```
TRAZO_SCORECARD=<file> dotnet test Trazo.sln -c Release --filter "FullyQualifiedName~Scorecard"
```
