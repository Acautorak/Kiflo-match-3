using System.Collections.Generic;

namespace Match3.Grid
{
    public class MatchGroup
    {
        public readonly List<(int x, int y)> Cells = new List<(int, int)>();
        public int ColorId;

        /// 4+ in a straight line — a natural point to spawn a line-clear special tile.
        public bool IsLine => Cells.Count >= 4;
    }

    public static class MatchFinder
    {
        /// <summary>Scans the whole board and returns every run of 3+ same-color tiles.</summary>
        public static List<MatchGroup> FindAllMatches(BoardModel board)
        {
            var groups = new List<MatchGroup>();

            // Horizontal runs
            for (int y = 0; y < board.Height; y++)
            {
                int runStart = 0;
                for (int x = 1; x <= board.Width; x++)
                {
                    bool sameAsPrev = x < board.Width &&
                        !board.Get(x, y).IsEmpty && !board.Get(x - 1, y).IsEmpty &&
                        board.Get(x, y).ColorId == board.Get(x - 1, y).ColorId;

                    if (sameAsPrev) continue;

                    int runLength = x - runStart;
                    if (runLength >= 3)
                    {
                        var g = new MatchGroup { ColorId = board.Get(runStart, y).ColorId };
                        for (int k = runStart; k < x; k++) g.Cells.Add((k, y));
                        groups.Add(g);
                    }
                    runStart = x;
                }
            }

            // Vertical runs
            for (int x = 0; x < board.Width; x++)
            {
                int runStart = 0;
                for (int y = 1; y <= board.Height; y++)
                {
                    bool sameAsPrev = y < board.Height &&
                        !board.Get(x, y).IsEmpty && !board.Get(x, y - 1).IsEmpty &&
                        board.Get(x, y).ColorId == board.Get(x, y - 1).ColorId;

                    if (sameAsPrev) continue;

                    int runLength = y - runStart;
                    if (runLength >= 3)
                    {
                        var g = new MatchGroup { ColorId = board.Get(x, runStart).ColorId };
                        for (int k = runStart; k < y; k++) g.Cells.Add((x, k));
                        groups.Add(g);
                    }
                    runStart = y;
                }
            }

            return groups;
        }
    }
}
