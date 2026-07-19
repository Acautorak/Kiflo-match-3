using UnityEngine;

/// <summary>
/// Owns the grid's cell storage - what used to be Board's private `Cell[,] grid` field plus
/// the init loop in Awake(). Exposes an indexer so existing call-site syntax like
/// `grid[x, y].Occupant` and `grid[x, y].IsEmpty` keeps working unchanged wherever this
/// replaces the raw array - Cell is a reference type, so mutating through the indexer mutates
/// the same underlying cell that everyone else sees.
///
/// Deliberately minimal for this pass: no match-finding, no gravity, no spawn logic - those
/// stay owned by MatchFinder / GravityController / SymbolSpawner respectively and just read
/// or write through this. RawGrid is a temporary escape hatch for MatchFinder.FindMatchGroups
/// until that call is migrated to take a GridModel directly.
/// </summary>
public class GridModel
{
    private readonly Cell[,] cells;

    public int Width { get; }
    public int Height { get; }

    public GridModel(int width, int height)
    {
        Width = width;
        Height = height;
        cells = new Cell[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                cells[x, y] = new Cell();
    }

    public Cell this[int x, int y] => cells[x, y];
    public Cell this[Vector2Int p] => cells[p.x, p.y];

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    public bool InBounds(Vector2Int p) => InBounds(p.x, p.y);

    /// <summary>Escape hatch for APIs that specifically need a raw Cell[,] - currently just
    /// MatchFinder.FindMatchGroups. Prefer the indexer/InBounds helpers everywhere else.</summary>
    public Cell[,] RawGrid => cells;

    /// <summary>Empties every occupied cell, invoking `onEachRemoved` per symbol first (typically
    /// to Destroy its GameObject) before clearing the slot. Replaces Board.ClearExistingSymbols'
    /// loop body.</summary>
    public void ClearAll(System.Action<Symbol> onEachRemoved = null)
    {
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                var occ = cells[x, y].Occupant;
                if (occ == null) continue;

                onEachRemoved?.Invoke(occ);
                cells[x, y].Occupant = null;
            }
    }
}
