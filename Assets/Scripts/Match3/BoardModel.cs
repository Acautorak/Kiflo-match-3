using System.Collections.Generic;

namespace Match3.Grid
{
    /// <summary>
    /// Pure data/logic representation of the match-3 board. Deliberately has zero
    /// MonoBehaviour dependency so it can be unit tested, run headless to validate
    /// procedurally generated levels, or reused for an AI move-suggester.
    /// </summary>
    public class BoardModel
    {
        public int Width { get; }
        public int Height { get; }
        public int NumColors { get; }

        private readonly Tile[,] _cells;
        private readonly System.Random _rng;

        public BoardModel(int width, int height, int numColors, int seed)
        {
            Width = width;
            Height = height;
            NumColors = numColors;
            _rng = new System.Random(seed);
            _cells = new Tile[width, height];
        }

        public Tile Get(int x, int y) => _cells[x, y];
        public void Set(int x, int y, Tile t) => _cells[x, y] = t;
        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        /// <summary>
        /// Fills the board with random colors, guaranteeing no pre-existing match-3
        /// and at least one legal move so the player is never handed a dead board.
        /// </summary>
        public void FillRandomNoMatches()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int color;
                    int attempts = 0;
                    do
                    {
                        color = _rng.Next(0, NumColors);
                        attempts++;
                    } while (CreatesImmediateMatch(x, y, color) && attempts < 20);

                    _cells[x, y] = Tile.Normal(color);
                }
            }

            if (!HasPossibleMove())
                Shuffle();
        }

        private bool CreatesImmediateMatch(int x, int y, int color)
        {
            if (x >= 2 && _cells[x - 1, y].ColorId == color && _cells[x - 2, y].ColorId == color)
                return true;
            if (y >= 2 && _cells[x, y - 1].ColorId == color && _cells[x, y - 2].ColorId == color)
                return true;
            return false;
        }

        public void Swap(int x1, int y1, int x2, int y2)
        {
            (_cells[x1, y1], _cells[x2, y2]) = (_cells[x2, y2], _cells[x1, y1]);
        }

        /// <summary>Gravity pass: collapses tiles downward. Returns moves for animation driving.</summary>
        public List<(int x, int fromY, int toY)> Collapse()
        {
            var moves = new List<(int, int, int)>();
            for (int x = 0; x < Width; x++)
            {
                int writeY = 0;
                for (int y = 0; y < Height; y++)
                {
                    if (_cells[x, y].IsEmpty) continue;

                    if (writeY != y)
                    {
                        _cells[x, writeY] = _cells[x, y];
                        _cells[x, y] = Tile.Empty;
                        moves.Add((x, y, writeY));
                    }
                    writeY++;
                }
            }
            return moves;
        }

        /// <summary>Refills empty cells from the top with new random tiles.</summary>
        public List<(int x, int y, int colorId)> Refill()
        {
            var spawned = new List<(int, int, int)>();
            for (int x = 0; x < Width; x++)
            {
                for (int y = 0; y < Height; y++)
                {
                    if (!_cells[x, y].IsEmpty) continue;

                    int color = _rng.Next(0, NumColors);
                    _cells[x, y] = Tile.Normal(color);
                    spawned.Add((x, y, color));
                }
            }
            return spawned;
        }

        /// <summary>
        /// Randomizes the board (used on deadlock). Retries until the result has
        /// no free matches and at least one legal move.
        /// </summary>
        public void Shuffle()
        {
            int attempts = 0;
            do
            {
                var flat = new List<int>();
                foreach (var t in _cells) flat.Add(t.ColorId);

                for (int i = flat.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    (flat[i], flat[j]) = (flat[j], flat[i]);
                }

                int idx = 0;
                for (int y = 0; y < Height; y++)
                    for (int x = 0; x < Width; x++)
                        _cells[x, y] = Tile.Normal(flat[idx++]);

                attempts++;
            } while ((MatchFinder.FindAllMatches(this).Count > 0 || !HasPossibleMove()) && attempts < 50);
        }

        /// <summary>Deadlock check: is there any single swap that would create a match?</summary>
        public bool HasPossibleMove()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (x < Width - 1 && WouldSwapMatch(x, y, x + 1, y)) return true;
                    if (y < Height - 1 && WouldSwapMatch(x, y, x, y + 1)) return true;
                }
            }
            return false;
        }

        private bool WouldSwapMatch(int x1, int y1, int x2, int y2)
        {
            Swap(x1, y1, x2, y2);
            bool result = MatchFinder.FindAllMatches(this).Count > 0;
            Swap(x1, y1, x2, y2); // revert probe swap
            return result;
        }
    }
}
