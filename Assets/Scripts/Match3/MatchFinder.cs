using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// A single connected chain of matched cells. Usually one straight run, but if a
/// horizontal and vertical run share a cell (an L or T shape), they're merged into
/// one group here instead of being treated as two separate matches.
/// </summary>
public class MatchGroup
{
    public readonly HashSet<Vector2Int> Cells = new HashSet<Vector2Int>();
    public readonly List<List<Vector2Int>> Lines = new List<List<Vector2Int>>();

    /// <summary>True when this group is formed from 2+ runs crossing each other (L/T/plus shape).</summary>
    public bool IsIntersection => Lines.Count > 1;

    public int LongestRun => Lines.Max(l => l.Count);

    /// <summary>
    /// The best "seed" cell to turn into a special symbol: for an intersection, the shared
    /// cell where the runs cross; otherwise the middle of the single run.
    /// </summary>
    public Vector2Int GetSeedCell()
    {
        if (IsIntersection)
        {
            var counts = new Dictionary<Vector2Int, int>();
            foreach (var line in Lines)
                foreach (var p in line)
                    counts[p] = counts.TryGetValue(p, out var c) ? c + 1 : 1;

            foreach (var kv in counts)
                if (kv.Value > 1) return kv.Key;
        }
        var line0 = Lines[0];
        return line0[line0.Count / 2];
    }
}

/// <summary>
/// Scans the grid for runs of 3+ same-type symbols, horizontally and vertically, then
/// merges any runs that share a cell into a single connected MatchGroup - so an L or T
/// shaped match is recognized (and scored/specialed) as one chain rather than two overlapping ones.
/// </summary>
public static class MatchFinder
{
    /// <param name="treatMadnessAsWildcard">
    /// If true, cells whose Symbol.IsMadness is true act as wildcards - same as a Special symbol -
    /// and join whatever color run they're touching instead of requiring their own Type to match.
    /// Toggle lives on Board (see Board.treatMadnessSymbolsAsWildcards).
    /// </param>
    public static List<MatchGroup> FindMatchGroups(Cell[,] grid, int width, int height, bool treatMadnessAsWildcard = false)
    {
        var lines = FindRuns(grid, width, height, treatMadnessAsWildcard);
        var groups = new List<MatchGroup>();
        if (lines.Count == 0) return groups;

        // Union-find over line indices: two runs merge if they share at least one cell.
        int n = lines.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(int i) => parent[i] == i ? i : (parent[i] = Find(parent[i]));
        void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a != b) parent[a] = b;
        }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (lines[i].Any(p => lines[j].Contains(p)))
                    Union(i, j);

        var byRoot = new Dictionary<int, MatchGroup>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!byRoot.TryGetValue(root, out var group))
            {
                group = new MatchGroup();
                byRoot[root] = group;
            }
            group.Lines.Add(lines[i]);
            foreach (var p in lines[i]) group.Cells.Add(p);
        }

        return byRoot.Values.ToList();
    }

    /// <summary>
    /// Raw straight-line runs of 3+, before merging. A cell whose Special != None is a
    /// wildcard: it doesn't force its own SymbolType, it just extends whatever color run
    /// it's touching (so Red, Red, Bomb counts as a 3-run of Red; Bomb, Bomb, Bomb alone
    /// also counts, with no particular color required). When treatMadnessAsWildcard is true,
    /// Madness Symbols get the exact same wildcard treatment alongside Special ones.
    /// </summary>
    public static List<List<Vector2Int>> FindRuns(Cell[,] grid, int width, int height, bool treatMadnessAsWildcard = false)
    {
        var matches = new List<List<Vector2Int>>();

        for (int y = 0; y < height; y++)
        {
            int yCopy = y;
            matches.AddRange(ScanLine(width, x => grid[x, yCopy].IsEmpty ? null : grid[x, yCopy].Occupant, x => new Vector2Int(x, yCopy), treatMadnessAsWildcard));
        }

        for (int x = 0; x < width; x++)
        {
            int xCopy = x;
            matches.AddRange(ScanLine(height, y => grid[xCopy, y].IsEmpty ? null : grid[xCopy, y].Occupant, y => new Vector2Int(xCopy, y), treatMadnessAsWildcard));
        }

        return matches;
    }

    /// <summary>
    /// Scans one row or column (via the given accessors) and returns every run of 3+ cells
    /// that share a color, where special (wildcard) symbols extend a run regardless of their
    /// own underlying SymbolType. Trailing wildcards at a run boundary carry forward into the
    /// next run instead of being discarded, so a wildcard sandwiched between an invalid short
    /// run and a valid one on the other side still gets picked up (e.g. Strawberry, Clear,
    /// Chocolate, Chocolate -> the single Strawberry doesn't "use up" Clear; Clear joins the
    /// chocolates instead).
    /// </summary>
    private static List<List<Vector2Int>> ScanLine(int length, System.Func<int, Symbol> getSymbol, System.Func<int, Vector2Int> toCoord, bool treatMadnessAsWildcard)
    {
        var result = new List<List<Vector2Int>>();
        var current = new List<(Vector2Int coord, bool isWildcard)>();
        SymbolType? anchor = null;

        void Flush()
        {
            if (current.Count >= 3)
                result.Add(current.Select(c => c.coord).ToList());

            int carry = 0;
            while (carry < current.Count && current[current.Count - 1 - carry].isWildcard) carry++;
            current = current.GetRange(current.Count - carry, carry);
            anchor = null;
        }

        for (int i = 0; i < length; i++)
        {
            var symbol = getSymbol(i);
            if (symbol == null)
            {
                Flush();
                current.Clear(); // a genuine empty cell fully breaks the run, wildcards included
                continue;
            }

            bool isWildcard = symbol.Special != SpecialType.None
                || (treatMadnessAsWildcard && symbol.IsMadness);

            if (isWildcard)
            {
                // Wildcard: joins the current run no matter what color it's carrying.
                current.Add((toCoord(i), true));
                continue;
            }

            if (anchor == null)
            {
                anchor = symbol.Type;
                current.Add((toCoord(i), false));
            }
            else if (symbol.Type == anchor.Value)
            {
                current.Add((toCoord(i), false));
            }
            else
            {
                Flush();
                anchor = symbol.Type;
                current.Add((toCoord(i), false));
            }
        }

        if (current.Count >= 3) result.Add(current.Select(c => c.coord).ToList());

        return result;
    }
}

