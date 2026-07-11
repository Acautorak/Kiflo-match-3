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
    public static List<MatchGroup> FindMatchGroups(Cell[,] grid, int width, int height)
    {
        var lines = FindRuns(grid, width, height);
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

    /// <summary>Raw straight-line runs of 3+, before merging. Exposed in case you need unmerged data.</summary>
    public static List<List<Vector2Int>> FindRuns(Cell[,] grid, int width, int height)
    {
        var matches = new List<List<Vector2Int>>();

        // Horizontal runs
        for (int y = 0; y < height; y++)
        {
            int runStart = 0;
            for (int x = 1; x <= width; x++)
            {
                bool continuesRun = x < width
                    && !grid[x, y].IsEmpty
                    && !grid[runStart, y].IsEmpty
                    && grid[x, y].Occupant.Type == grid[runStart, y].Occupant.Type;

                if (!continuesRun)
                {
                    int runLength = x - runStart;
                    if (runLength >= 3 && !grid[runStart, y].IsEmpty)
                    {
                        var line = new List<Vector2Int>(runLength);
                        for (int k = runStart; k < x; k++) line.Add(new Vector2Int(k, y));
                        matches.Add(line);
                    }
                    runStart = x;
                }
            }
        }

        // Vertical runs
        for (int x = 0; x < width; x++)
        {
            int runStart = 0;
            for (int y = 1; y <= height; y++)
            {
                bool continuesRun = y < height
                    && !grid[x, y].IsEmpty
                    && !grid[x, runStart].IsEmpty
                    && grid[x, y].Occupant.Type == grid[x, runStart].Occupant.Type;

                if (!continuesRun)
                {
                    int runLength = y - runStart;
                    if (runLength >= 3 && !grid[x, runStart].IsEmpty)
                    {
                        var line = new List<Vector2Int>(runLength);
                        for (int k = runStart; k < y; k++) line.Add(new Vector2Int(x, k));
                        matches.Add(line);
                    }
                    runStart = y;
                }
            }
        }

        return matches;
    }
}

