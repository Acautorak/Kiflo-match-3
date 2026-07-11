using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scans the grid for runs of 3+ same-type symbols, horizontally and vertically.
/// Returns each run as its own list (Board.cs merges overlapping runs when it
/// collects positions to clear, and inspects run length to decide special symbols).
/// </summary>
public static class MatchFinder
{
    public static List<List<Vector2Int>> FindAllMatches(Cell[,] grid, int width, int height)
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
