using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the grid's cell storage - what used to be Board's private `Cell[,] grid` field plus
/// the init loop in Awake(). Exposes an indexer so existing call-site syntax like
/// `grid[x, y].Occupant` and `grid[x, y].IsEmpty` keeps working unchanged wherever this
/// replaces the raw array - Cell is a reference type, so mutating through the indexer mutates
/// the same underlying cell that everyone else sees.
///
/// Also owns the board's SHAPE - a fixed Width x Height bounding box (see Board's discussion
/// with the user about deferring true variable-size-per-stage support) with a per-cell active
/// mask on top. An inactive cell is a permanent hole: never populated, never spawned into, and
/// never treated as anything other than an empty cell by MatchFinder (a hole's Occupant is
/// always null, and MatchFinder already breaks/boundary-checks runs on null occupants - so
/// holes need zero changes there). Only gravity needs to know about holes specially, since a
/// hole can split a column into independent falling segments - see GetActiveSegments and
/// GravityController.Collapse.
///
/// Deliberately minimal for this pass: no match-finding, no gravity, no spawn logic - those
/// stay owned by MatchFinder / GravityController / SymbolSpawner respectively and just read
/// or write through this. RawGrid is a temporary escape hatch for MatchFinder.FindMatchGroups
/// until that call is migrated to take a GridModel directly.
/// </summary>
public class GridModel
{
    private readonly Cell[,] cells;
    private bool[,] active;

    public int Width { get; }
    public int Height { get; }

    public GridModel(int width, int height)
    {
        Width = width;
        Height = height;
        cells = new Cell[width, height];
        active = new bool[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                cells[x, y] = new Cell();
                active[x, y] = true;
            }
    }

    public Cell this[int x, int y] => cells[x, y];
    public Cell this[Vector2Int p] => cells[p.x, p.y];

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;
    public bool InBounds(Vector2Int p) => InBounds(p.x, p.y);

    /// <summary>Escape hatch for APIs that specifically need a raw Cell[,] - currently just
    /// MatchFinder.FindMatchGroups. Prefer the indexer/InBounds helpers everywhere else.</summary>
    public Cell[,] RawGrid => cells;

    /// <summary>True when (x,y) is part of this stage's playable shape. False means a
    /// permanent hole - the cell's Occupant is always null and it's skipped by populate/refill.</summary>
    public bool IsActive(int x, int y) => active[x, y];
    public bool IsActive(Vector2Int p) => IsActive(p.x, p.y);

    /// <summary>Number of active (non-hole) cells - just for logging/diagnostics.</summary>
    public int ActiveCellCount
    {
        get
        {
            int count = 0;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    if (active[x, y]) count++;
            return count;
        }
    }

    /// <summary>
    /// Applies a shape mask for a fresh stage. Pass null (or an all-true mask) to reset to a
    /// full rectangle - what a stage without a hand-authored/procedural shape gets. Doesn't
    /// touch existing occupants; callers should ClearAll first if the board might be populated
    /// (Board.ResetForStage already clears before repopulating, so this is safe to call there).
    /// </summary>
    public void ApplyShape(bool[,] mask)
    {
        if (mask == null)
        {
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    active[x, y] = true;
            return;
        }

        if (mask.GetLength(0) != Width || mask.GetLength(1) != Height)
        {
            Debug.LogWarning($"[GridModel] Shape mask size {mask.GetLength(0)}x{mask.GetLength(1)} " +
                              $"doesn't match grid size {Width}x{Height} - ignoring and using a full rectangle instead.");
            ApplyShape(null);
            return;
        }

        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                active[x, y] = mask[x, y];
    }

    /// <summary>
    /// Every active y in column x, ascending, with holes simply omitted from the list rather
    /// than splitting it into separate runs - the pass-through-gravity counterpart to
    /// GetActiveSegments. Use this when GravityController.HolesBlockGravity is false: a hole
    /// stops being a floor/ceiling and just becomes a slot nothing can ever occupy, while
    /// symbols above it fall straight past it to whatever's below.
    /// </summary>
    public List<int> GetActiveYs(int x)
    {
        var ys = new List<int>();
        for (int y = 0; y < Height; y++)
            if (active[x, y]) ys.Add(y);
        return ys;
    }

    /// <summary>
    /// Contiguous runs of active y-values in column x, ordered bottom-to-top as (start, end)
    /// inclusive pairs. A hole splits a column into independent segments: GravityController
    /// collapses/refills each one on its own, since a hole is both a floor (nothing below it
    /// falls through) and a ceiling (nothing above a hole can drop into what's below it). A
    /// full-height column with no holes always yields exactly one segment, (0, Height-1) - so
    /// this is a strict generalization of the old single-loop-per-column behavior, not a
    /// special case bolted on top of it.
    /// </summary>
    public List<(int start, int end)> GetActiveSegments(int x)
    {
        var segments = new List<(int start, int end)>();
        int segStart = -1;
        for (int y = 0; y < Height; y++)
        {
            if (active[x, y])
            {
                if (segStart == -1) segStart = y;
            }
            else if (segStart != -1)
            {
                segments.Add((segStart, y - 1));
                segStart = -1;
            }
        }
        if (segStart != -1) segments.Add((segStart, Height - 1));
        return segments;
    }

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
