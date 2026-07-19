using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Owns gravity/collapse/refill: dropping existing symbols into empty space below them, then
/// spawning new symbols in from above to fill whatever's left - rolling frozen-tile and Madness
/// spawn chances on each new symbol along the way. Extracted from Board.CollapseAndRefill.
///
/// Doesn't own any config of its own - everything it needs (fall behavior of locked tiles,
/// frozen/Madness spawn rolls) already lives on LockingSystem/MadnessSystem, so this class just
/// orchestrates them plus SymbolSpawner in the right order.
///
/// Takes a `gridToWorld` delegate rather than duplicating Board's origin/cellSize math, so
/// changing the board's world-space layout only ever needs to happen in one place (Board.GridToWorld).
/// </summary>
public class GravityController
{
    private readonly GridModel grid;
    private readonly SymbolSpawner spawner;
    private readonly LockingSystem lockingSystem;
    private readonly MadnessSystem madnessSystem;
    private readonly float fallDuration;
    private readonly System.Func<int, int, Vector3> gridToWorld;

    public GravityController(GridModel grid, SymbolSpawner spawner, LockingSystem lockingSystem,
        MadnessSystem madnessSystem, float fallDuration, System.Func<int, int, Vector3> gridToWorld)
    {
        this.grid = grid;
        this.spawner = spawner;
        this.lockingSystem = lockingSystem;
        this.madnessSystem = madnessSystem;
        this.fallDuration = fallDuration;
        this.gridToWorld = gridToWorld;
    }

    public IEnumerator Collapse()
    {
        var sequence = DOTween.Sequence();
        bool anyMovement = false;

        for (int x = 0; x < grid.Width; x++)
        {
            int writeY = 0;
            for (int y = 0; y < grid.Height; y++)
            {
                if (grid[x, y].IsEmpty) continue;

                var occ = grid[x, y].Occupant;

                if (occ.IsLocked && !lockingSystem.LockedTilesFallWithGravity)
                {
                    // Locked/frozen tiles don't react to gravity - they stay exactly where
                    // they are and act as a floor for the segment above them. Nothing below
                    // this row will ever be reached by tiles above it while it holds.
                    writeY = y + 1;
                    continue;
                }

                if (writeY != y)
                {
                    grid[x, writeY].Occupant = occ;
                    grid[x, y].Occupant = null;
                    occ.GridPosition = new Vector2Int(x, writeY);
                    sequence.Join(occ.MoveTo(gridToWorld(x, writeY), fallDuration));
                    anyMovement = true;
                }
                writeY++;
            }

            for (int y = writeY; y < grid.Height; y++)
            {
                var type = spawner.RandomType();
                var spawnHeight = grid.Height + (y - writeY); // spawn above the visible board and fall in
                var instance = spawner.Spawn(x, y, type, SpecialType.None, gridToWorld(x, spawnHeight));
                if (instance == null) continue;

                if (lockingSystem.ShouldSpawnFrozenTileOnRefill(x, y))
                {
                    var option = lockingSystem.PickWeightedLockOption();
                    if (option != null) instance.SetLock(option.layers, option.behavior, option.movesPerLayer);
                }

                if (madnessSystem.ShouldSpawnOnRefill())
                {
                    var madnessDef = madnessSystem.PickWeightedOption();
                    if (madnessDef != null)
                    {
                        instance.InitializeMadness(madnessDef);
                        madnessSystem.FireEffects(madnessDef.onSpawnedEffects, instance, new Vector2Int(x, y), chainCount: 0);
                    }
                }

                sequence.Join(instance.MoveTo(gridToWorld(x, y), fallDuration));
                anyMovement = true;
            }
        }

        if (anyMovement) yield return sequence.WaitForCompletion();

        lockingSystem.TryFreezeExistingSymbols();
    }
}
