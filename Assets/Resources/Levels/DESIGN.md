# Xandria Gem Jam — 250-Level Design

`tools/level-gen/generate_levels.py` is the deterministic source for this catalog. It uses
seed `20260822`, preserves the authored records for IDs 0–4, and writes the pretty-printed
`levels.json` catalog with `"version": 1`.

## Move-budget model

Every generated level has a computed perfect-play lower bound:

```
minimum moves = ceil(sum(objective amounts) / 3)
              + ceil(sum(obstacle health × obstacle-cell count) / 2)
              + shape penalty
```

The shape penalty is zero through 20 excluded cells, +1 for 21–35, and +2 above 35.
This recognizes that narrow boards reduce the number of useful adjacent clears without
making a board cut count as a full objective. `maxMoves` is then set exactly from that
minimum, rather than from a linear level-number ramp:

| Tier | Level band | Move budget |
| --- | --- | --- |
| Tutorial | 0–10 | minimum + 15 |
| Easy | 11–55 | minimum + 12 |
| Medium | 56–110 | minimum + 8 |
| Hard | 111–180 | minimum + 5 |
| Hardest | 181–249 | minimum + 3 |

Levels 0–4 are legacy tutorial seeds and remain byte-for-byte data-equivalent to the
original authored records. They are validated against the lower-bound rule but retain their
existing move values. The resulting catalog has 11 tutorial, 45 easy, 55 medium, 70 hard,
and 69 hardest levels.

## Mechanic progression

- **0–4:** Existing tutorial ramp: basic gem goal followed by the initial obstacle examples.
- **5–15:** Single gem objectives only; count and color rotate without obstacle pressure.
- **16–30:** `Bubble` enters alone at health 1–2, alongside one familiar gem objective.
- **31–50:** `Ice` (health 2) arrives, first on its own and then with familiar bubbles.
- **51–75:** `Grass` (health 1) arrives in contiguous clusters; two-color objectives begin.
- **76–110:** `WitheredVine` (health 3) appears as lines. Diamond, hourglass, cross, L,
  staircase, and split-board cutouts are interleaved with rectangular recovery boards.
- **111–150:** Two obstacle types are combined on every level; objective sizes move to the
  hard band and the buffer closes to five moves.
- **151–200:** Every level combines at least three obstacle types. Recurring 12-level and
  25-level boss beats add a fourth type or larger obstacle patch.
- **201–249:** Four obstacle types appear in every boss level; recurring milestones use all
  five. Three hardest-band gem objectives and tight `minimum + 3` budgets define the finale.

All placements are on unique playable cells, each obstacle group has 15 or fewer cells, and
no level exceeds 60 total obstacle cells. Grass uses clusters and vines/ice favor linear
layouts so obstacle silhouettes remain readable at a glance.

## Milestone levels (every 25th)

- **0 — First Match:** Existing CircleGem goal; establishes the basic match-to-collect loop.
- **25 — Bubble Check:** A single health-2 bubble cluster adds the first localized clear
  problem while the objective remains easy-band.
- **50 — Frozen Pair:** Ice and familiar bubbles share a board; it is the capstone of the
  Ice introduction before grass changes the board-reading problem.
- **75 — Garden Gate:** Two medium-band gem goals and a grass cluster require intentional
  color routing, not just obstacle clearing.
- **100 — Vine Works:** A shaped board, a vine line, grass, and a mushroom boss patch test
  chaining around constrained lanes.
- **125 — Combination Drill:** The hard move window begins in earnest: a large vine/bubble
  pair and high gem requirements demand efficient adjacent clears.
- **150 — Two-Tool Finale:** The final focused two-obstacle encounter pairs ice with
  mushrooms and carries three hard-band objectives.
- **175 — Four-Front Push:** A boss variation adds a fourth obstacle type to the three-type
  hard-game mix, with an L-shaped board increasing routing pressure.
- **200 — Pre-Finale Siege:** Three 60/59/60-scale hardest objectives face four dense
  obstacle types under the `minimum + 3` budget.
- **225 — Five-Obstacle Crown:** All five obstacle types appear together with three
  maximum-scale objectives; it is the catalog's heaviest recurring boss beat.

## Rebuild and validation

Run:

```bash
python tools/level-gen/generate_levels.py
```

The generator serializes the catalog, parses it back with Python's `json` module, validates
IDs, names, asset strings, dimensions, playable coordinates, non-overlap, obstacle-cell
limits, mechanic gates, and move budgets, then prints:

```
250 levels, all ids sequential, all maxMoves >= computed min
```
