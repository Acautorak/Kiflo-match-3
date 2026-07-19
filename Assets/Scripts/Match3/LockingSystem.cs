using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns locked/frozen-tile rules: initial level-authored placements, per-move temporary-lock
/// melting, refill-time/freeze-existing spawn rolls and their weighted option pool, and the
/// public LockCell/UnlockCell/IsCellLocked API. Extracted from Board's two "Locking / Freezing"
/// header regions.
///
/// Config is exposed as settable properties rather than constructor-only values because
/// Board.ApplyStageRules mutates several of these per stage (and PlayerRunStats nudges
/// LockSpawnChance at the same time) - Board still owns the [SerializeField] Inspector fields
/// for authoring and copies them in once at construction; ApplyStageRules now writes here
/// instead of to Board's own fields.
///
/// NOTE: SpawnLocksOnRefill is carried over for parity with the original field, but - same as
/// in the pre-refactor code - ShouldSpawnFrozenTileOnRefill doesn't actually check it, only
/// FrozenTileSpawnMode gates refill spawning. That looks like a pre-existing gap rather than
/// something introduced here; flagging it in case it wasn't intentional.
/// </summary>
public class LockingSystem
{
    private readonly GridModel grid;

    public bool DestroySymbolWhenUnlocked { get; set; } = true;
    public bool LockedTilesFallWithGravity { get; set; } = false;
    public bool AllowSwappingLockedTiles { get; set; } = false;
    public int ScorePerLockHit { get; set; } = 5;
    public InitialLockPlacement[] InitialLockPlacements { get; set; }

    public bool SpawnLocksOnRefill { get; set; } = false;
    public float LockSpawnChance { get; set; } = 0.05f;
    public LockSpawnOption[] LockSpawnOptions { get; set; }

    public FrozenTileSpawnMode FrozenTileSpawnMode { get; set; } = FrozenTileSpawnMode.None;
    public int FrozenTileBottomRowCount { get; set; } = 0;
    public float FrozenTileOutsideBottomRowsChanceMultiplier { get; set; } = 0.25f;

    public LockingSystem(GridModel grid)
    {
        this.grid = grid;
    }

    public void ApplyInitialLockPlacements(InitialLockPlacement[] overridePlacements = null)
    {
        var placements = (overridePlacements != null && overridePlacements.Length > 0)
            ? overridePlacements
            : InitialLockPlacements;

        if (placements == null || placements.Length == 0) return;

        int applied = 0;
        foreach (var placement in placements)
        {
            var p = placement.position;
            if (!grid.InBounds(p))
            {
                Debug.LogWarning($"[LockingSystem] Initial lock placement {p} is out of bounds ({grid.Width}x{grid.Height}) - skipped.");
                continue;
            }

            var occ = grid[p].Occupant;
            if (occ == null)
            {
                Debug.LogWarning($"[LockingSystem] Initial lock placement {p} has no symbol to lock - skipped.");
                continue;
            }

            occ.SetLock(placement.layers, placement.behavior, placement.movesPerLayer);
            applied++;
        }
        Debug.Log($"[LockingSystem] Applied {applied}/{placements.Length} initial lock placement(s).");
    }

    /// <summary>Ticks every Temporary lock on the board once (called after each accepted player
    /// move). Returns true if any tile was fully destroyed as a result, so the caller knows to
    /// re-run gravity/refill before continuing.</summary>
    public bool MeltAllTemporaryLocks()
    {
        bool anyDestroyed = false;

        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || occ.LockBehaviorMode != LockBehavior.Temporary) continue;

                bool melted = occ.TickTemporaryLock();
                if (!melted) continue;

                var pos = new Vector2Int(x, y);
                bool fullyUnlocked = !occ.IsLocked;
                EventBus.Publish(new LockLayerRemovedEvent(pos, occ.LockLayers, triggeredByMatch: false, fullyUnlocked));

                if (fullyUnlocked && DestroySymbolWhenUnlocked)
                {
                    Object.Destroy(occ.gameObject);
                    grid[x, y].Occupant = null;
                    anyDestroyed = true;
                }
            }

        return anyDestroyed;
    }

    public bool ShouldSpawnFrozenTileOnRefill(int x, int y)
    {
        if (FrozenTileSpawnMode != FrozenTileSpawnMode.GenerateNewFrozenTiles &&
            FrozenTileSpawnMode != FrozenTileSpawnMode.Both)
            return false;

        return RollFrozenTileChance(y);
    }

    /// <summary>Chance check shared by both frozen-tile paths. Row 0 is the bottom of the board
    /// (gravity pulls tiles down to y = 0), so rows within the bottom FrozenTileBottomRowCount
    /// roll at the full LockSpawnChance; rows above roll at a reduced chance instead of being
    /// excluded outright, so "bottom N rows" reads as a priority region rather than a hard
    /// cutoff. Set FrozenTileBottomRowCount to 0 to disable the priority entirely.</summary>
    public bool RollFrozenTileChance(int y)
    {
        float chance = LockSpawnChance;
        if (FrozenTileBottomRowCount > 0 && y >= FrozenTileBottomRowCount)
            chance *= FrozenTileOutsideBottomRowsChanceMultiplier;

        return Random.value < chance;
    }

    /// <summary>FreezeExistingBottomRows / Both: gives already-placed unlocked tiles a chance to
    /// freeze in place after the board settles. Reuses the same bottom-row priority and weighted
    /// option pool as the refill path.</summary>
    public void TryFreezeExistingSymbols()
    {
        if (FrozenTileSpawnMode != FrozenTileSpawnMode.FreezeExistingBottomRows &&
            FrozenTileSpawnMode != FrozenTileSpawnMode.Both)
            return;

        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || occ.IsLocked) continue;
                if (!RollFrozenTileChance(y)) continue;

                var option = PickWeightedLockOption();
                if (option == null) continue;

                occ.SetLock(option.layers, option.behavior, option.movesPerLayer);
            }
    }

    public LockSpawnOption PickWeightedLockOption()
    {
        if (LockSpawnOptions == null || LockSpawnOptions.Length == 0) return null;

        float total = LockSpawnOptions.Sum(o => Mathf.Max(0f, o.weight));
        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        float cumulative = 0f;
        foreach (var o in LockSpawnOptions)
        {
            cumulative += Mathf.Max(0f, o.weight);
            if (roll <= cumulative) return o;
        }
        return LockSpawnOptions[LockSpawnOptions.Length - 1];
    }

    public void LockCell(int x, int y, int layers, LockBehavior behavior, int movesPerLayer = 3)
    {
        if (!grid.InBounds(x, y))
        {
            Debug.LogWarning($"[LockingSystem] LockCell({x},{y}) out of range.");
            return;
        }
        var occ = grid[x, y].Occupant;
        if (occ == null)
        {
            Debug.LogWarning($"[LockingSystem] LockCell({x},{y}) - no symbol occupies that cell.");
            return;
        }
        occ.SetLock(layers, behavior, movesPerLayer);
    }

    public void UnlockCell(int x, int y)
    {
        if (!grid.InBounds(x, y)) return;
        var occ = grid[x, y].Occupant;
        if (occ == null || !occ.IsLocked) return;

        occ.SetLock(0, LockBehavior.None);
        EventBus.Publish(new LockLayerRemovedEvent(new Vector2Int(x, y), 0, triggeredByMatch: false, fullyUnlocked: true));
    }

    public bool IsCellLocked(int x, int y) =>
        grid.InBounds(x, y) && grid[x, y].Occupant != null && grid[x, y].Occupant.IsLocked;

    /// <summary>Picks a random unlocked tile and locks it with 2 layers - backs Board's
    /// Inspector test buttons. Returns the position locked, or null if no unlocked tile was
    /// available.</summary>
    public Vector2Int? LockRandomTileForTesting(LockBehavior behavior)
    {
        var candidates = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ != null && !occ.IsLocked) candidates.Add(new Vector2Int(x, y));
            }

        if (candidates.Count == 0) return null;

        var pos = candidates[Random.Range(0, candidates.Count)];
        LockCell(pos.x, pos.y, 2, behavior, movesPerLayer: 3);
        return pos;
    }
}
