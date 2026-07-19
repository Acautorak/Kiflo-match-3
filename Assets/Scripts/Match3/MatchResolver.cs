using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns the match-resolution cascade: turning MatchGroups into cleared cells, newly-created
/// special symbols, score, and repeated collapse/refill passes until nothing's left to resolve -
/// plus the two "random bonus effect" coroutines that piggyback on the same clear/score/collapse
/// machinery outside of a real match. Extracted from Board's "Matching / Cascades" region.
///
/// This is the most tangled piece pulled out so far - ClearCell alone reaches into locking,
/// Madness, and scoring together. A few bits of Board's own transient state (grace period,
/// isStageClearing, ShouldSkipRefillGeneration) legitimately belong to Board, not here, so
/// they're passed in as delegates rather than duplicated - Board stays the single source of
/// truth for them and this class just reads through.
///
/// Deliberately does NOT own the post-cascade save - Board wraps every call into this class with
/// its own saveIO.TrySave(...), since that needs several Board-only fields (moveCount,
/// IsSafeToSave) that have nothing to do with match resolution.
/// </summary>
public class MatchResolver
{
    private readonly GridModel grid;
    private readonly GravityController gravityController;
    private readonly SpecialEffectSystem specialEffectSystem;
    private readonly MadnessSystem madnessSystem;
    private readonly LockingSystem lockingSystem;
    private readonly ScoreTracker scoreTracker;
    private readonly SymbolSpawner symbolSpawner;
    private readonly PlayerHealth playerHealth;
    private readonly PlayerRunStats playerRunStats;
    private readonly MadnessBoardModifiers madnessBoardModifiers;
    private readonly GameManager gameManager;
    private readonly System.Func<int, int, Vector3> gridToWorld;
    private readonly System.Func<bool> shouldSkipRefillGeneration;
    private readonly System.Func<bool> isStageClearing;
    private readonly System.Func<bool> isGraceActive;
    private readonly System.Func<int> graceMovesRemaining;
    private readonly System.Func<float> graceRandomSpecialChance;

    public bool IntersectionsCreateBombs { get; set; } = true;
    public SpecialType[] EligibleRandomSpecialTypes { get; set; }
    public int MaxConsecutiveRandomTriggers { get; set; } = 3;
    public float RandomSpecialTriggerChance { get; set; } = 0.05f;
    public bool EnableRandomSpecialOnGravity { get; set; } = false;

    public MatchResolver(GridModel grid, GravityController gravityController, SpecialEffectSystem specialEffectSystem,
        MadnessSystem madnessSystem, LockingSystem lockingSystem, ScoreTracker scoreTracker, SymbolSpawner symbolSpawner,
        PlayerHealth playerHealth, PlayerRunStats playerRunStats, MadnessBoardModifiers madnessBoardModifiers,
        GameManager gameManager, System.Func<int, int, Vector3> gridToWorld, System.Func<bool> shouldSkipRefillGeneration,
        System.Func<bool> isStageClearing, System.Func<bool> isGraceActive, System.Func<int> graceMovesRemaining,
        System.Func<float> graceRandomSpecialChance)
    {
        this.grid = grid;
        this.gravityController = gravityController;
        this.specialEffectSystem = specialEffectSystem;
        this.madnessSystem = madnessSystem;
        this.lockingSystem = lockingSystem;
        this.scoreTracker = scoreTracker;
        this.symbolSpawner = symbolSpawner;
        this.playerHealth = playerHealth;
        this.playerRunStats = playerRunStats;
        this.madnessBoardModifiers = madnessBoardModifiers;
        this.gameManager = gameManager;
        this.gridToWorld = gridToWorld;
        this.shouldSkipRefillGeneration = shouldSkipRefillGeneration;
        this.isStageClearing = isStageClearing;
        this.isGraceActive = isGraceActive;
        this.graceMovesRemaining = graceMovesRemaining;
        this.graceRandomSpecialChance = graceRandomSpecialChance;
    }

    public IEnumerator Resolve(List<MatchGroup> initialGroups)
    {
        var currentGroups = initialGroups;
        int chainCount = 0;
        gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);

        while (currentGroups.Count > 0)
        {
            if (isStageClearing()) break;

            chainCount++;
            Debug.Log($"[MatchResolver] Cascade step {chainCount}: {currentGroups.Count} group(s) - " +
                       string.Join(" | ", currentGroups.Select(g =>
                           $"cells={g.Cells.Count} intersection={g.IsIntersection} longestRun={g.LongestRun} seed={g.GetSeedCell()}")));

            var allPositions = new HashSet<Vector2Int>();
            var specialsToCreate = new Dictionary<Vector2Int, (SpecialType special, SymbolType type)>();

            foreach (var group in currentGroups)
            {
                foreach (var p in group.Cells) allPositions.Add(p);
                RegisterSpecialsFromMatchGroup(group, specialsToCreate);

                // Color-targeted "heal on match" powerups roll their chance once per matched
                // group here, before any clearing happens below, while the seed cell's Occupant
                // is still valid. Baseline chance is 0 - only present if a powerup added to it.
                if (playerRunStats != null)
                {
                    var seed = group.GetSeedCell();
                    var seedOcc = grid[seed.x, seed.y].Occupant;
                    if (seedOcc != null)
                    {
                        float healChance = playerRunStats.GetColorHealChance(seedOcc.Type);
                        if (healChance > 0f && Random.value < healChance)
                        {
                            int healAmount = playerRunStats.GetColorHealAmount(seedOcc.Type);
                            if (healAmount > 0) playerHealth?.Heal(healAmount);
                        }
                    }
                }
            }

            // Any special symbols caught inside this match activate and pull in extra cells.
            var extraCleared = new HashSet<Vector2Int>();
            bool anySpecialActivated = false;
            foreach (var pos in allPositions)
            {
                var occ = grid[pos.x, pos.y].Occupant;
                if (occ != null && occ.Special != SpecialType.None)
                {
                    if (!anySpecialActivated)
                    {
                        anySpecialActivated = true;
                        gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);
                    }
                    foreach (var a in specialEffectSystem.ActivateSpecial(occ)) extraCleared.Add(a);
                }
            }
            foreach (var p in extraCleared) allPositions.Add(p);
            if (anySpecialActivated) gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);

            // --- Publish events for this cascade step ---
            foreach (var pos in allPositions)
            {
                var occ = grid[pos.x, pos.y]?.Occupant;
                if (occ != null) EventBus.Publish(new SymbolMatchedEvent(occ.Type, pos));
            }
            EventBus.Publish(new ChainMatchedEvent(currentGroups.Sum(g => g.Cells.Count), chainCount, allPositions.ToArray()));

            // Clear matched cells (locked tiles take a hit instead of clearing until their lock
            // breaks). Cells reserved for becoming a special are skipped here UNLESS they're
            // still locked - in that case they take a hit too, and special creation there is
            // deferred (removed from specialsToCreate) until a future pass finds it unlocked.
            int scoreDelta = 0;
            foreach (var pos in allPositions)
            {
                bool isSpecialSeed = specialsToCreate.ContainsKey(pos);
                var occBefore = grid[pos.x, pos.y].Occupant;
                if (isSpecialSeed && (occBefore == null || !occBefore.IsLocked)) continue;

                var (destroyed, delta) = ClearCell(pos, chainCount);
                scoreDelta += delta;
                if (isSpecialSeed && !destroyed) specialsToCreate.Remove(pos);
            }
            scoreTracker.AddScore(scoreDelta);

            foreach (var (pos, info) in specialsToCreate)
            {
                var existing = grid[pos.x, pos.y].Occupant;
                if (existing != null) Object.Destroy(existing.gameObject);
                symbolSpawner.Spawn(pos.x, pos.y, info.type, info.special, gridToWorld(pos.x, pos.y));
                EventBus.Publish(new SpecialSymbolCreatedEvent(info.special, pos));
            }

            if (isStageClearing()) break;

            if (shouldSkipRefillGeneration())
            {
                Debug.Log("[MatchResolver] Stage clear grace active - skipping refill generation.");
                currentGroups = new List<MatchGroup>();
                break;
            }

            yield return gravityController.Collapse();
            yield return TryRandomSpecialOnGravity(chainCount);
            gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);
            currentGroups = MatchFinder.FindMatchGroups(grid.RawGrid, grid.Width, grid.Height, madnessSystem.TreatMadnessSymbolsAsWildcards);
        }
    }

    private void RegisterSpecialsFromMatchGroup(MatchGroup group,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        if (group.IsIntersection && IntersectionsCreateBombs)
        {
            var seed = group.GetSeedCell();
            RegisterSpecialSeed(seed, SpecialType.Bomb, specialsToCreate);
            return;
        }

        // Not treating this as a bomb (either a straight run, or intersections-as-bomb
        // is disabled) - let each constituent run create its own special independently.
        foreach (var line in group.Lines)
        {
            if (line.Count < 4) continue;
            RegisterSpecialFromLine(line, specialsToCreate);
        }
    }

    private void RegisterSpecialFromLine(List<Vector2Int> line,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        var seed = line[line.Count / 2];
        var special = line.Count >= 5
            ? SpecialType.ColorClear
            : (line[0].y == line[1].y ? SpecialType.RowClear : SpecialType.ColumnClear);

        RegisterSpecialSeed(seed, special, specialsToCreate);
    }

    private void RegisterSpecialSeed(Vector2Int seed, SpecialType special,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        var seedType = grid[seed.x, seed.y].Occupant?.Type ?? SymbolType.Red;
        specialsToCreate[seed] = (special, seedType);
    }

    /// <summary>
    /// Attempts to clear a single matched/affected cell. If it's locked, this reduces the lock
    /// by one layer instead of destroying it - unless that exact hit breaks the last layer and
    /// LockingSystem.DestroySymbolWhenUnlocked is true, in which case it clears immediately on
    /// the same hit. Returns whether the cell actually emptied, and the score this hit is worth.
    /// </summary>
    private (bool destroyed, int scoreDelta) ClearCell(Vector2Int pos, int chainCount)
    {
        var occ = grid[pos.x, pos.y].Occupant;
        if (occ == null) return (false, 0);

        if (occ.IsLocked)
        {
            bool fullyUnlocked = occ.RemoveLockLayer();
            EventBus.Publish(new LockLayerRemovedEvent(pos, occ.LockLayers, triggeredByMatch: true, fullyUnlocked));

            if (!fullyUnlocked) return (false, lockingSystem.ScorePerLockHit);
            if (!lockingSystem.DestroySymbolWhenUnlocked) return (false, lockingSystem.ScorePerLockHit);
            // else: the same hit that broke the lock also clears the tile - fall through
        }

        var color = occ.Type;

        if (occ.IsMadness)
        {
            madnessSystem.FireEffects(occ.MadnessDefinition.onClearedEffects, occ, pos, chainCount);
            EventBus.Publish(new MadnessSymbolClearedEvent(occ.MadnessDefinition, pos, occ.MadnessMovesSurvived));
        }

        Object.Destroy(occ.gameObject);
        grid[pos.x, pos.y].Occupant = null;

        int baseScore = 10 * chainCount;
        float colorMultiplierBonus = 0f;
        int colorFlatBonus = 0;

        if (playerRunStats != null)
        {
            colorMultiplierBonus += playerRunStats.GetColorScoreMultiplierBonus(color);
            colorFlatBonus += playerRunStats.GetColorFlatScoreBonus(color);
        }
        if (madnessBoardModifiers != null)
        {
            colorMultiplierBonus += madnessBoardModifiers.GetColorScoreMultiplierBonus(color);
            int igniteDamage = madnessBoardModifiers.GetColorDamagePerMatch(color);
            if (igniteDamage > 0) playerHealth?.TakeDamage(igniteDamage);
        }

        if (colorMultiplierBonus != 0f)
            baseScore = Mathf.RoundToInt(baseScore * (1f + colorMultiplierBonus));
        baseScore += colorFlatBonus;

        return (true, baseScore);
    }

    /// <summary>
    /// Rolls a chance for a random tile to spontaneously trigger a random special effect during
    /// the stage-clear grace period, exactly as if it had been matched. Called from Board after
    /// each accepted move while grace is active.
    /// </summary>
    public IEnumerator TryRandomSpecialOnGraceMove()
    {
        if (!isGraceActive() || graceMovesRemaining() <= 0) yield break;
        if (EligibleRandomSpecialTypes == null || EligibleRandomSpecialTypes.Length == 0) yield break;
        float chance = graceRandomSpecialChance();
        if (chance <= 0f || Random.value >= chance) yield break;

        var candidates = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ != null && !occ.IsLocked) candidates.Add(new Vector2Int(x, y));
            }

        if (candidates.Count == 0) yield break;

        gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);

        var origin = candidates[Random.Range(0, candidates.Count)];
        var originSymbol = grid[origin.x, origin.y].Occupant;
        var effectType = EligibleRandomSpecialTypes[Random.Range(0, EligibleRandomSpecialTypes.Length)];
        var affected = new HashSet<Vector2Int>(specialEffectSystem.ComputeAffectedCells(effectType, origin, originSymbol.Type)) { origin };

        Debug.Log($"[MatchResolver] Grace-period bonus: {effectType} at {origin} - clearing {affected.Count} cell(s)");

        foreach (var pos in affected)
        {
            var occ = grid[pos.x, pos.y]?.Occupant;
            if (occ != null) EventBus.Publish(new SymbolMatchedEvent(occ.Type, pos));
        }

        EventBus.Publish(new SpecialSymbolMatchedEvent(effectType, origin, affected.ToArray()));
        EventBus.Publish(new ChainMatchedEvent(affected.Count, 1, affected.ToArray()));

        int scoreDelta = 0;
        foreach (var pos in affected)
        {
            var (_, delta) = ClearCell(pos, 1);
            scoreDelta += delta;
        }
        scoreTracker.AddScore(scoreDelta);

        if (!shouldSkipRefillGeneration())
            yield return gravityController.Collapse();
    }

    /// <summary>
    /// Rolls a chance for a random tile to spontaneously trigger a random special effect,
    /// exactly as if it had been matched (same events, same scoring, same clear/collapse).
    /// Called after every gravity settle. Can loop multiple times per settle up to
    /// MaxConsecutiveRandomTriggers; pass forceOnce=true to guarantee exactly one trigger (used
    /// by Board's Inspector test button), bypassing the toggle and chance roll.
    /// </summary>
    public IEnumerator TryRandomSpecialOnGravity(int chainCount, bool forceOnce = false)
    {
        if (EligibleRandomSpecialTypes == null || EligibleRandomSpecialTypes.Length == 0) yield break;
        if (!forceOnce && !EnableRandomSpecialOnGravity) yield break;

        int triggered = 0;
        int cap = forceOnce ? 1 : MaxConsecutiveRandomTriggers;

        while (triggered < cap && (forceOnce || Random.value < RandomSpecialTriggerChance))
        {
            var candidates = new List<Vector2Int>();
            for (int x = 0; x < grid.Width; x++)
                for (int y = 0; y < grid.Height; y++)
                {
                    var occ = grid[x, y].Occupant;
                    if (occ != null && !occ.IsLocked) candidates.Add(new Vector2Int(x, y));
                }

            if (candidates.Count == 0) break;

            gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);

            var origin = candidates[Random.Range(0, candidates.Count)];
            var originSymbol = grid[origin.x, origin.y].Occupant;
            var effectType = EligibleRandomSpecialTypes[Random.Range(0, EligibleRandomSpecialTypes.Length)];

            var affected = new HashSet<Vector2Int>(specialEffectSystem.ComputeAffectedCells(effectType, origin, originSymbol.Type)) { origin };

            Debug.Log($"[MatchResolver] Random gravity bonus: {effectType} at {origin} - clearing {affected.Count} cell(s)");

            foreach (var pos in affected)
            {
                var occ = grid[pos.x, pos.y]?.Occupant;
                if (occ != null) EventBus.Publish(new SymbolMatchedEvent(occ.Type, pos));
            }

            // Same open event a real special match fires - VFX/SFX hooked via SpecialSymbolEventRelay just work.
            EventBus.Publish(new SpecialSymbolMatchedEvent(effectType, origin, affected.ToArray()));
            EventBus.Publish(new ChainMatchedEvent(affected.Count, chainCount, affected.ToArray()));

            int scoreDelta = 0;
            foreach (var pos in affected)
            {
                var (_, delta) = ClearCell(pos, chainCount);
                scoreDelta += delta;
            }
            scoreTracker.AddScore(scoreDelta);

            yield return gravityController.Collapse();
            triggered++;
        }
    }
}
