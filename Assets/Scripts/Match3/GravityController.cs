using System.Collections;
using System.Collections.Generic;
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

    /// <summary>
    /// True (default): a hole is a floor/ceiling - see GridModel.GetActiveSegments - splitting a
    /// column into independent falling segments, same as before board shapes existed for a
    /// full-rectangle board (which has no holes, so this never mattered there).
    /// False: a hole is transparent to gravity - see GridModel.GetActiveYs - symbols fall straight
    /// past it as if it weren't there, and it's simply a slot nothing can ever occupy. Whether
    /// one plays better than the other is a real game-feel question with no obviously-correct
    /// answer, hence this being a toggle rather than a hardcoded choice - see Board.ApplyStageRules
    /// (reads stage.holesBlockGravity) for how a stage picks one.
    /// </summary>
    public bool HolesBlockGravity { get; set; } = true;

    public IEnumerator Collapse()
    {
        var sequence = DOTween.Sequence();
        bool anyMovement = false;

        for (int x = 0; x < grid.Width; x++)
        {
            if (HolesBlockGravity)
            {
                var segments = grid.GetActiveSegments(x);
                for (int s = 0; s < segments.Count; s++)
                {
                    var (segStart, segEnd) = segments[s];
                    var ys = new List<int>(segEnd - segStart + 1);
                    for (int y = segStart; y <= segEnd; y++) ys.Add(y);

                    // Segments are ordered bottom-to-top, so only the LAST one is "open to the
                    // sky" and can receive refills from outside the board - see the class-level
                    // discussion in GridModel.GetActiveSegments.
                    bool topExposed = s == segments.Count - 1;
                    CollapseList(x, ys, topExposed, sequence, ref anyMovement);
                }
            }
            else
            {
                var ys = grid.GetActiveYs(x);
                if (ys.Count > 0) CollapseList(x, ys, topExposed: true, sequence, ref anyMovement);
            }
        }

        if (anyMovement) yield return sequence.WaitForCompletion();

        lockingSystem.TryFreezeExistingSymbols();
    }

    /// <summary>
    /// Collapses+refills one column's worth of active y-positions, given as an ascending list -
    /// either one contiguous segment (HolesBlockGravity true) or the whole column with holes
    /// omitted (HolesBlockGravity false). The two cases need no separate code: "the next active
    /// slot" is just the next entry in `ys` either way, so a hole in pass-through mode is
    /// skipped for free by simply not being in the list, exactly like it being outside a
    /// segment's range is skipped for free in blocking mode.
    /// </summary>
    private void CollapseList(int x, List<int> ys, bool topExposed, Sequence sequence, ref bool anyMovement)
    {
        int writeIndex = 0;
        for (int i = 0; i < ys.Count; i++)
        {
            int y = ys[i];
            if (grid[x, y].IsEmpty) continue;

            var occ = grid[x, y].Occupant;

            if (occ.IsLocked && !lockingSystem.LockedTilesFallWithGravity)
            {
                // Locked/frozen tiles don't react to gravity - they stay exactly where they are
                // and act as a floor for whatever's above them in this list.
                writeIndex = i + 1;
                continue;
            }

            int writeY = ys[writeIndex];
            if (writeY != y)
            {
                grid[x, writeY].Occupant = occ;
                grid[x, y].Occupant = null;
                occ.GridPosition = new Vector2Int(x, writeY);
                sequence.Join(occ.MoveTo(gridToWorld(x, writeY), fallDuration));
                anyMovement = true;
            }
            writeIndex++;
        }

        if (!topExposed) return; // sealed pocket (blocking mode only) - leftover empty slots just stay empty

        for (int i = writeIndex; i < ys.Count; i++)
        {
            int y = ys[i];
            var type = spawner.RandomType();
            // Staggered by rank (i - writeIndex), not raw y - in pass-through mode y can jump
            // across skipped holes, so basing the spawn-drop height on rank keeps the fall-in
            // stagger visually even regardless of gaps.
            var spawnHeight = grid.Height + (i - writeIndex);
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
                    Debug.Log($"[GravityController] Madness Symbol spawned: '{madnessDef.name}' at ({x},{y})");
                    EventBus.Publish(new MadnessSymbolSpawnedEvent(madnessDef, new Vector2Int(x, y)));
                }
            }

            sequence.Join(instance.MoveTo(gridToWorld(x, y), fallDuration));
            anyMovement = true;
        }
    }
}
