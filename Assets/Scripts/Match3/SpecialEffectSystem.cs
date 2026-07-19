using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns what each special symbol effect (RowClear, ColumnClear, Bomb, ColorClear) actually
/// covers on the grid, and the "open event" publish when one activates. Extracted from Board's
/// ComputeAffectedCells / Compute*AffectedCells and ActivateSpecial / Activate* methods.
///
/// Pure grid geometry - doesn't touch scoring, locking, or clearing itself. Callers (currently
/// Board.ResolveMatches and the random-bonus-effect coroutines) still own deciding *when* an
/// effect fires and what to do with the affected cells (ClearCell, scoring, etc).
/// </summary>
public class SpecialEffectSystem
{
    private readonly GridModel grid;

    public SpecialEffectSystem(GridModel grid)
    {
        this.grid = grid;
    }

    /// <summary>Every grid position a given special effect would hit, from a given origin cell.</summary>
    public List<Vector2Int> ComputeAffectedCells(SpecialType type, Vector2Int origin, SymbolType colorForColorClear)
    {
        return type switch
        {
            SpecialType.RowClear => ComputeRowClearAffectedCells(origin),
            SpecialType.ColumnClear => ComputeColumnClearAffectedCells(origin),
            SpecialType.Bomb => ComputeBombAffectedCells(origin),
            SpecialType.ColorClear => ComputeColorClearAffectedCells(origin, colorForColorClear),
            _ => new List<Vector2Int>()
        };
    }

    public List<Vector2Int> ComputeRowClearAffectedCells(Vector2Int origin)
    {
        var affected = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++) affected.Add(new Vector2Int(x, origin.y));
        return affected;
    }

    public List<Vector2Int> ComputeColumnClearAffectedCells(Vector2Int origin)
    {
        var affected = new List<Vector2Int>();
        for (int y = 0; y < grid.Height; y++) affected.Add(new Vector2Int(origin.x, y));
        return affected;
    }

    public List<Vector2Int> ComputeBombAffectedCells(Vector2Int origin)
    {
        var affected = new List<Vector2Int>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = origin.x + dx, ny = origin.y + dy;
                if (grid.InBounds(nx, ny))
                    affected.Add(new Vector2Int(nx, ny));
            }

        return affected;
    }

    public List<Vector2Int> ComputeColorClearAffectedCells(Vector2Int origin, SymbolType colorForColorClear)
    {
        var affected = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
                if (!grid[x, y].IsEmpty && grid[x, y].Occupant.Type == colorForColorClear)
                    affected.Add(new Vector2Int(x, y));

        return affected;
    }

    /// <summary>
    /// Returns every grid position this special symbol clears, and publishes the open event -
    /// this is the hook point VFX/SFX/UI wire into via SpecialSymbolEventRelay. Collapses the
    /// original Board's four separate Activate* wrapper methods into one switch, since each just
    /// called its matching Compute*AffectedCells and converted to an array - same behavior,
    /// fewer near-identical methods.
    /// </summary>
    public Vector2Int[] ActivateSpecial(Symbol special)
    {
        var pos = special.GridPosition;
        var affected = special.Special switch
        {
            SpecialType.RowClear => ComputeRowClearAffectedCells(pos).ToArray(),
            SpecialType.ColumnClear => ComputeColumnClearAffectedCells(pos).ToArray(),
            SpecialType.Bomb => ComputeBombAffectedCells(pos).ToArray(),
            SpecialType.ColorClear => ComputeColorClearAffectedCells(pos, special.Type).ToArray(),
            _ => new Vector2Int[0]
        };

        EventBus.Publish(new SpecialSymbolMatchedEvent(special.Special, pos, affected));
        return affected;
    }
}
