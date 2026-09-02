// ACT 2 ADDITION — gated behind player level 251+. See Act2SpecialTileManager for activation gate.
//
// L-shape / T-shape (intersection) match detection. This does NOT exist
// anywhere else in the codebase — Match3.FindMatches() (Assets/_Scripts/
// Match3.cs) only scans straight horizontal and vertical runs and has zero
// concept of shapes/intersections. This class is a fully separate, standalone
// detector called FROM FindMatches() via the Act2SpecialTileManager hook, but
// it is not merged into FindMatches() itself, per the task's isolation
// requirement.
//
// Algorithm: reuses the same straight-run groups Match3.FindMatches() already
// computed (passed in as horizontalGroups/verticalGroups) and looks for a
// horizontal run and a vertical run of the SAME gem color that share exactly
// one cell (their "corner"). If the combined, de-duplicated cell count of the
// pair is exactly 5, it's a standard L-shape or T-shape match:
//   - L-shape: the shared cell is at an END of both runs (a corner).
//   - T-shape: the shared cell is at an END of one run and in the MIDDLE of
//     the other (a "T" junction).
// Both cases are treated identically for spawning purposes (both produce a
// Wrapped tile) — the standard combo table does not distinguish L vs T
// outcomes, only the resulting 5-cell intersection shape.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Match3Game.Act2
{
    /// <summary>One detected L/T-shape match: the 5 cells involved and where they intersect.</summary>
    public readonly struct ShapeMatch
    {
        public readonly List<Vector2Int> cells;
        public readonly Vector2Int intersection;

        public ShapeMatch(List<Vector2Int> cells, Vector2Int intersection)
        {
            this.cells = cells;
            this.intersection = intersection;
        }
    }

    public static class ShapeMatchDetector
    {
        /// <summary>
        /// Finds every L-shape/T-shape match among the given straight-line run
        /// groups. Groups are the same (Vector2Int list) groups Match3.FindMatches()
        /// already builds while scanning rows/columns — pass them in rather than
        /// re-scanning the grid, so this stays a pure, independently-testable
        /// function with no grid/board dependency of its own.
        /// </summary>
        public static List<ShapeMatch> FindShapeMatches(
            List<List<Vector2Int>> horizontalGroups,
            List<List<Vector2Int>> verticalGroups)
        {
            var results = new List<ShapeMatch>();
            if (horizontalGroups == null || verticalGroups == null) return results;

            foreach (var hGroup in horizontalGroups)
            {
                var hSet = new HashSet<Vector2Int>(hGroup);

                foreach (var vGroup in verticalGroups)
                {
                    // Runs must share exactly one cell to form a valid corner/junction.
                    var shared = hGroup.Where(vGroup.Contains).ToList();
                    if (shared.Count != 1) continue;

                    Vector2Int corner = shared[0];

                    // Combine + de-duplicate (the shared cell is counted once).
                    var combined = new HashSet<Vector2Int>(hGroup);
                    combined.UnionWith(vGroup);

                    // Standard 5-tile L/T match: a 3-run + 3-run sharing one cell = 5 cells.
                    // Longer combined runs belong to bigger/rarer combos which Act 2 does not
                    // define an outcome for yet, so we only spawn Wrapped for exactly 5.
                    if (combined.Count != 5) continue;

                    results.Add(new ShapeMatch(combined.ToList(), corner));
                }
            }

            return results;
        }

        /// <summary>
        /// True if `corner` sits at either end of `run` (an L-shape corner)
        /// rather than in the middle (a T-shape junction). Useful for VFX/debug
        /// only — both cases spawn the same Wrapped tile.
        /// </summary>
        public static bool IsCornerAtRunEnd(List<Vector2Int> run, Vector2Int corner)
        {
            if (run == null || run.Count == 0) return false;
            return run[0] == corner || run[run.Count - 1] == corner;
        }
    }
}
