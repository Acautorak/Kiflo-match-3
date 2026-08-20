using System.Linq;
using UnityEngine;

/// <summary>
/// Finds a valid move for the hint system: an adjacent pair of tiles that, if swapped, would
/// create a match. Deliberately reuses MatchFinder rather than reimplementing "is this a match"
/// logic separately - a candidate swap is applied to the real grid data temporarily (via
/// Symbol.SetType, the same call MadnessSystem's color-repaint effects already use), checked with
/// MatchFinder.FindRuns, then reverted - so a hint always agrees with whatever the real
/// match-resolution pipeline would actually do, including wildcard treatment.
/// </summary>
public static class PossibleMoveFinder
{
    private static readonly Vector2Int[] CheckDirs = { Vector2Int.right, Vector2Int.up };

    /// <summary>
    /// Scans every active, unlocked, occupied cell for an adjacent swap that would create a
    /// match. Only checks right/up per cell (not left/down too) since every adjacent pair in the
    /// grid gets covered exactly once that way. Returns the first one found - good enough for a
    /// hint (doesn't need to be the "best" move, just a real one) and cheap enough to re-run
    /// every time HintController wants a fresh pick.
    /// </summary>
    public static bool TryFindHint(GridModel grid, bool treatMadnessAsWildcard, out Vector2Int from, out Vector2Int to)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                if (!grid.IsActive(x, y)) continue;

                var pos = new Vector2Int(x, y);
                var occ = grid[pos].Occupant;
                if (occ == null || occ.IsLocked) continue;

                foreach (var dir in CheckDirs)
                {
                    var neighborPos = pos + dir;
                    if (!grid.InBounds(neighborPos) || !grid.IsActive(neighborPos.x, neighborPos.y)) continue;

                    var neighbor = grid[neighborPos].Occupant;
                    if (neighbor == null || neighbor.IsLocked) continue;

                    if (WouldSwapCreateMatch(grid, pos, neighborPos, treatMadnessAsWildcard))
                    {
                        from = pos;
                        to = neighborPos;
                        return true;
                    }
                }
            }
        }

        from = default;
        to = default;
        return false;
    }

    /// <summary>True if there's ANY valid move anywhere on the board - cheap wrapper around
    /// TryFindHint for callers that only care about the yes/no (e.g. deciding whether to
    /// reshuffle instead of showing a hint).</summary>
    public static bool HasAnyValidMove(GridModel grid, bool treatMadnessAsWildcard) =>
        TryFindHint(grid, treatMadnessAsWildcard, out _, out _);

    /// <summary>Temporarily swaps the two cells' Types, checks whether either position is now
    /// part of a 3+ run via MatchFinder, then reverts - non-destructive, safe to call for every
    /// candidate pair during a scan. Symbol.SetType is a plain data+visual update with no tween
    /// side effects (MadnessSystem's repaint effects already rely on that same assumption), so
    /// this doesn't cause any visible flicker even though it mutates and reverts within one call.
    /// </summary>
    private static bool WouldSwapCreateMatch(GridModel grid, Vector2Int a, Vector2Int b, bool treatMadnessAsWildcard)
    {
        var occA = grid[a].Occupant;
        var occB = grid[b].Occupant;
        var typeA = occA.Type;
        var typeB = occB.Type;

        occA.SetType(typeB);
        occB.SetType(typeA);

        var runs = MatchFinder.FindRuns(grid.RawGrid, grid.Width, grid.Height, treatMadnessAsWildcard);
        bool wouldMatch = runs.Any(run => run.Contains(a) || run.Contains(b));

        occA.SetType(typeA);
        occB.SetType(typeB);

        return wouldMatch;
    }
}
