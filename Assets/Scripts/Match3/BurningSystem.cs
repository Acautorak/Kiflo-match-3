using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Owns the Burning status effect: rolling a chance to ignite a nearby tile after a match (see
/// TryIgniteNearby) or guaranteed-igniting every neighbor at once for effects that need that (see
/// IgniteAllNeighbors - e.g. a Madness Symbol that lights everything around it ablaze on death),
/// ticking every burning tile's countdown once per accepted player move, and collecting a tile
/// once its countdown reaches 0 - scored and despawned exactly like Tile Collector's tap-collect,
/// and (like a real match) publishes SymbolMatchedEvent so a Collect-type stage goal picks it up
/// too, on top of its own TileCollectedEvent.
///
/// Modeled directly on LockingSystem's shape (a per-tile status with its own per-move tick and a
/// "what happens when it runs out" behavior) rather than inventing a parallel pattern - compare
/// TryIgniteNearby/TickAllBurningTiles to LockingSystem's LockCell/MeltAllTemporaryLocks.
///
/// Needs Board (not just GridModel) for AddBonusScore - same coupling MadnessSystem already has
/// with Board for the same reason (see that class's doc comment for the fuller explanation of
/// why this isn't reworked into something grid-only).
/// </summary>
public class BurningSystem
{
    private readonly GridModel grid;
    private readonly SymbolSpawner spawner;
    private readonly Board board;
    private readonly PlayerRunStats playerRunStats;

    /// <summary>How many accepted player moves a newly-ignited tile burns for before it's
    /// auto-collected. Set by Board from its own Inspector field.</summary>
    public int BurnDurationMoves { get; set; } = 3;

    /// <summary>Score awarded when a burnt-out tile is auto-collected - same idea as Tile
    /// Collector's scorePerTile. Set by Board from its own Inspector field.</summary>
    public int ScorePerBurntTile { get; set; } = 15;

    /// <summary>Optional - a prefab that visibly flies from the matched tile to the tile about to
    /// ignite, arriving before it actually catches (see TryIgniteNearby/PlaySparkThenIgnite).
    /// Null (default) ignites instantly with no travel visual, same as before this existed.</summary>
    public GameObject SparkPrefab { get; set; }

    /// <summary>Parent transform for spawned sparks. Falls back to Board's own transform if left
    /// null. Only matters when SparkPrefab is assigned.</summary>
    public Transform SparkParent { get; set; }

    /// <summary>How long (seconds) a spark takes to travel from the matched tile to its ignite
    /// target. Only matters when SparkPrefab is assigned.</summary>
    public float SparkFlightDuration { get; set; } = 0.35f;

    /// <summary>How far (world units) the spark's flight path bows away from a straight line
    /// between the two tiles - 0 flies in a straight line, higher arcs more. The curve bows to a
    /// random side each flight so consecutive sparks don't all bend the same way. Only matters
    /// when SparkPrefab is assigned.</summary>
    public float SparkArcHeight { get; set; } = 0.75f;

    public BurningSystem(GridModel grid, SymbolSpawner spawner, Board board, PlayerRunStats playerRunStats)
    {
        this.grid = grid;
        this.spawner = spawner;
        this.board = board;
        this.playerRunStats = playerRunStats;
    }

    /// <summary>
    /// Rolls PlayerRunStats.IgniteOnMatchChance once (same once-per-matched-group granularity as
    /// MatchResolver's existing color heal-chance roll) and, if it hits, picks one random eligible
    /// tile from the 8 cells around `seedCell`. Eligible = occupied, not locked, and not already
    /// burning (an already-burning tile is simply not a candidate rather than having its
    /// countdown reset - see the design note in the class doc). No-op if the chance is 0
    /// (baseline - only present via a powerup) or nothing eligible is adjacent.
    ///
    /// If SparkPrefab is assigned, the tile doesn't ignite immediately - a spark visibly flies
    /// from seedCell to the target first (see PlaySparkThenIgnite) and the tile only actually
    /// catches once it arrives. Without a prefab assigned, ignition is still instant, exactly as
    /// before this existed.
    /// </summary>
    public void TryIgniteNearby(Vector2Int seedCell)
    {
        float chance = playerRunStats != null ? playerRunStats.IgniteOnMatchChance : 0f;
        if (chance <= 0f || Random.value >= chance) return;

        var candidates = GetEligibleNeighbors(seedCell);
        if (candidates.Count == 0) return;

        var targetPos = candidates[Random.Range(0, candidates.Count)];
        IgniteOrSpark(seedCell, targetPos);
    }

    /// <summary>
    /// Ignites EVERY eligible tile around `centerCell`, guaranteed (no chance roll) - for a
    /// Madness Symbol effect that lights all its neighbors ablaze on death, not the random-single-
    /// neighbor roll TryIgniteNearby does on a normal match. Eligible = occupied, not locked, and
    /// not already burning, same rules as TryIgniteNearby. Each eligible neighbor gets its own
    /// spark (if SparkPrefab is assigned) flying out from centerCell independently, so a Madness
    /// Symbol's death reads as a burst of embers scattering outward rather than one spark.
    /// </summary>
    public void IgniteAllNeighbors(Vector2Int centerCell)
    {
        var candidates = GetEligibleNeighbors(centerCell);
        foreach (var targetPos in candidates)
            IgniteOrSpark(centerCell, targetPos);
    }

    /// <summary>Shared by TryIgniteNearby and IgniteAllNeighbors - every occupied, unlocked,
    /// not-already-burning cell in the 8 around `center`.</summary>
    private List<Vector2Int> GetEligibleNeighbors(Vector2Int center)
    {
        var candidates = new List<Vector2Int>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                var p = new Vector2Int(center.x + dx, center.y + dy);
                if (!grid.InBounds(p)) continue;

                var occ = grid[p].Occupant;
                if (occ == null || occ.IsLocked || occ.IsBurning) continue;
                candidates.Add(p);
            }
        return candidates;
    }

    /// <summary>Routes a single ignite target through the spark flight (if SparkPrefab is
    /// assigned) or straight to IgniteTile otherwise - shared by TryIgniteNearby's single pick
    /// and IgniteAllNeighbors' loop over every eligible neighbor.</summary>
    private void IgniteOrSpark(Vector2Int fromCell, Vector2Int targetCell)
    {
        var targetSymbol = grid[targetCell].Occupant;

        if (SparkPrefab != null && board != null)
            board.StartCoroutine(PlaySparkThenIgnite(fromCell, targetCell, targetSymbol));
        else
            IgniteTile(targetCell, targetSymbol);
    }

    /// <summary>Spawns a spark at seedCell's world position, flies it along a curved path to
    /// targetCell's world position (see SparkArcHeight - a straight line bowed to a random side
    /// through a Catmull-Rom midpoint, rather than a direct DOMove), destroys it on arrival, then
    /// ignites - but only if the exact Symbol instance targeted at roll time is still sitting
    /// there, unlocked, and not already burning. The board can change during the flight (a
    /// cascade elsewhere could clear/replace this cell, or another ignite roll could have already
    /// caught it, before this spark lands) - re-checking here means a stale target silently does
    /// nothing instead of igniting the wrong symbol or double-firing.</summary>
    private IEnumerator PlaySparkThenIgnite(Vector2Int seedCell, Vector2Int targetCell, Symbol expectedTargetSymbol)
    {
        Vector3 startPos = board.GridToWorldPosition(seedCell.x, seedCell.y);
        Vector3 endPos = board.GridToWorldPosition(targetCell.x, targetCell.y);

        var parent = SparkParent != null ? SparkParent : board.transform;
        var spark = Object.Instantiate(SparkPrefab, startPos, Quaternion.identity, parent);

        Vector3 midpoint = Vector3.Lerp(startPos, endPos, 0.5f);
        if (SparkArcHeight > 0f)
        {
            // Perpendicular to the start->end line, bowed to a random side each flight so
            // consecutive sparks don't all curve the same way.
            Vector3 direction = endPos - startPos;
            Vector3 perpendicular = new Vector3(-direction.y, direction.x, 0f).normalized;
            midpoint += perpendicular * SparkArcHeight * (Random.value < 0.5f ? 1f : -1f);
        }

        var path = new[] { startPos, midpoint, endPos };
        yield return spark.transform.DOPath(path, Mathf.Max(0.05f, SparkFlightDuration), PathType.CatmullRom)
            .SetEase(Ease.Linear)
            .WaitForCompletion();

        Object.Destroy(spark);

        var current = grid.InBounds(targetCell) ? grid[targetCell].Occupant : null;
        if (current == null || current != expectedTargetSymbol || current.IsLocked || current.IsBurning) yield break;

        IgniteTile(targetCell, current);
    }

    private void IgniteTile(Vector2Int pos, Symbol symbol)
    {
        if (symbol == null) return;

        symbol.SetBurning(BurnDurationMoves);
        Debug.Log($"[BurningSystem] Ignited {pos} ({BurnDurationMoves} move(s) until it burns out).");
        EventBus.Publish(new TileIgnitedEvent(pos, BurnDurationMoves));
    }

    /// <summary>
    /// Ticks every burning tile down by one - call once per accepted player move, same cadence as
    /// LockingSystem.MeltAllTemporaryLocks. Returns true if any tile burnt out and was collected
    /// this call, so the caller knows to re-run gravity/refill before continuing - same contract
    /// as MeltAllTemporaryLocks.
    /// </summary>
    public bool TickAllBurningTiles()
    {
        bool anyBurntOut = false;

        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || !occ.IsBurning) continue;

                bool burntOut = occ.TickBurning();
                if (!burntOut) continue;

                CollectBurntTile(new Vector2Int(x, y), occ);
                anyBurntOut = true;
            }

        return anyBurntOut;
    }

    /// <summary>
    /// Collects a fully-burnt tile exactly like Tile Collector's tap-collect: scores it, despawns
    /// it, and empties its cell. Unlike TileCollectorController.CollectRoutine (which runs
    /// gravity/rescans per tap since it's driving its own standalone timed session), this only
    /// empties the cell - the caller (TickAllBurningTiles, from Board's per-move pipeline)
    /// collects every burnt tile from this move first and Board runs ONE gravity/refill/rescan
    /// pass afterward for all of them together, same contract as LockingSystem's melt-then-refill
    /// split.
    /// </summary>
    private void CollectBurntTile(Vector2Int pos, Symbol symbol)
    {
        var type = symbol.Type;

        EventBus.Publish(new TileCollectedEvent(type, pos));
        // Also a real SymbolMatchedEvent - a burnt-out tile counts toward a Collect-type stage
        // goal exactly like a real match would (StageManager.HandleSymbolMatched doesn't care
        // where the event came from, and this also picks up Disco Dance Disco's collect
        // multiplier for free, same as any other match).
        EventBus.Publish(new SymbolMatchedEvent(type, pos));

        board.AddBonusScore(ScorePerBurntTile);

        grid[pos.x, pos.y].Occupant = null;
        spawner.Despawn(symbol);
    }
}
