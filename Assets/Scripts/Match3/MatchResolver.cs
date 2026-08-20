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
    private readonly BurningSystem burningSystem;
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

    /// <summary>Optional per-call override for the random-special trigger chance normally read
    /// from RandomSpecialTriggerChance. If set, TryRandomSpecialOnGravity invokes it with the
    /// current chainCount and uses its return value instead of RandomSpecialTriggerChance for
    /// that call - forceOnce still bypasses the roll entirely either way, since it never consults
    /// either chance value. Null (default) preserves normal behavior. This exists so a caller
    /// like FreeSpinsController can guarantee a proc while the cascade is still short (chainCount
    /// below some threshold) and fall back to the real accumulated rate afterward, without
    /// MatchResolver itself knowing anything about Free Spins. Callers are responsible for
    /// restoring this (typically to whatever it was before, often null) once they're done, so the
    /// override doesn't leak into other code sharing the same MatchResolver instance.</summary>
    public System.Func<int, float> TriggerChanceOverride { get; set; }

    /// <summary>
    /// Hard safety cap on how many cascade steps a single Resolve() call can run, regardless of
    /// whether each step is making "real" progress (see anyProgressThisStep below - this is a
    /// different failure mode). A large same-colored patch from a color-convert effect can make
    /// every gravity refill fairly likely to immediately rematch it, clear, refill, and roll
    /// again - each step genuinely destroys something, so nothing else here catches it, and it
    /// can chain for a very long time (technically finite, practically indistinguishable from
    /// stuck) before randomly running dry. This guarantees the player always gets control back.
    /// </summary>
    public int MaxCascadeSteps { get; set; } = 50;

    public MatchResolver(GridModel grid, GravityController gravityController, SpecialEffectSystem specialEffectSystem,
        MadnessSystem madnessSystem, LockingSystem lockingSystem, BurningSystem burningSystem, ScoreTracker scoreTracker, SymbolSpawner symbolSpawner,
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
        this.burningSystem = burningSystem;
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
                var seed = group.GetSeedCell();
                var seedOcc = grid[seed.x, seed.y].Occupant;
                if (seedOcc != null)
                {
                    if (playerRunStats != null)
                    {
                        float healChance = playerRunStats.GetColorHealChance(seedOcc.Type);
                        if (healChance > 0f && Random.value < healChance)
                        {
                            int healAmount = playerRunStats.GetColorHealAmount(seedOcc.Type);
                            if (healAmount > 0) playerHealth?.Heal(healAmount);
                        }
                    }

                    // Madness ignite damage: once per matched GROUP, not once per destroyed cell
                    // (see ClearCell, which used to roll this per-cell - a single 5-run would deal
                    // 5x the intended damage, and an intersection/L-shape even more). Rolled here,
                    // same timing/granularity as the heal-chance check above, using the group's
                    // seed color as the representative color for the whole group.
                    if (madnessBoardModifiers != null)
                    {
                        int igniteDamage = madnessBoardModifiers.GetColorDamagePerMatch(seedOcc.Type);
                        if (igniteDamage > 0) playerHealth?.TakeDamage(igniteDamage);
                    }
                }

                // Same once-per-group granularity as the heal-chance roll above - "Chance on
                // Match to ignite 1 nearby symbol" (see PowerupDefinition.igniteOnMatchChanceBonus/
                // PlayerRunStats.IgniteOnMatchChance). Runs before this group's own cells get
                // cleared below, so the target tile - one of the 8 cells around the seed, not a
                // matched cell itself - is picked while the board still reflects this group's
                // pre-clear state.
                burningSystem?.TryIgniteNearby(group.GetSeedCell());
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
            bool anyProgressThisStep = false;
            foreach (var pos in allPositions)
            {
                bool isSpecialSeed = specialsToCreate.ContainsKey(pos);
                var occBefore = grid[pos.x, pos.y].Occupant;
                // A seed cell that's locked OR an immune Madness Symbol doesn't get replaced by
                // the new special this pass - it goes through ClearCell like anything else (takes
                // a lock hit / absorbs the immunity hit without dying), and only becomes eligible
                // to turn into the special on some future pass once it's actually clear.
                bool seedIsProtected = occBefore != null &&
                    (occBefore.IsLocked || (occBefore.IsMadness && occBefore.IsMadnessImmune));
                if (isSpecialSeed && (occBefore == null || !seedIsProtected)) continue;

                // A locked cell always makes real progress even when not destroyed - RemoveLockLayer()
                // unconditionally removes one layer on every hit, so it's finite and guaranteed to
                // eventually fully unlock. An immune Madness Symbol is NOT guaranteed progress: Moves-
                // mode immunity only ticks down via MadnessSystem.TickSurvival on a real player move
                // (see ClearCell), which never happens mid-cascade - so a group made entirely of
                // Moves-mode-immune cells would otherwise re-match this exact same set of cells every
                // single rescan below, forever. wasLocked captures the one case that's always safe to
                // treat as progress even without a destruction this pass.
                bool wasLocked = occBefore != null && occBefore.IsLocked;

                var (destroyed, delta) = ClearCell(pos, chainCount);
                scoreDelta += delta;
                if (destroyed || wasLocked) anyProgressThisStep = true;
                if (isSpecialSeed && !destroyed) specialsToCreate.Remove(pos);
            }
            scoreTracker.AddScore(scoreDelta);

            if (specialsToCreate.Count > 0) anyProgressThisStep = true; // a newly-spawned special is a real board change too

            foreach (var (pos, info) in specialsToCreate)
            {
                var existing = grid[pos.x, pos.y].Occupant;
                if (existing != null) Object.Destroy(existing.gameObject);
                symbolSpawner.Spawn(pos.x, pos.y, info.type, info.special, gridToWorld(pos.x, pos.y));
                EventBus.Publish(new SpecialSymbolCreatedEvent(info.special, pos));
            }

            if (isStageClearing()) break;

            if (!anyProgressThisStep)
            {
                Debug.LogWarning($"[MatchResolver] Cascade step {chainCount} destroyed nothing and unlocked nothing " +
                                  "(every matched cell was an immune Madness Symbol with no lock to reduce - likely " +
                                  "Moves-mode immunity, which only ticks on a real player move, never mid-cascade). " +
                                  "Stopping the cascade here instead of re-matching the identical cells forever - " +
                                  "the player's next move will tick immunity normally via MadnessSystem.TickSurvival.");
                break;
            }

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

            if (currentGroups.Count > 0 && chainCount >= MaxCascadeSteps)
            {
                Debug.LogWarning($"[MatchResolver] Hit MaxCascadeSteps ({MaxCascadeSteps}) after step {chainCount} " +
                                  "fully resolved - the board still has matches, but stopping here rather than " +
                                  "continuing to cascade. Likely a same-colored patch making refills keep rematching " +
                                  "by chance; worth checking whatever's been repeatedly repainting toward one color. " +
                                  "Whatever's left unresolved will simply get picked up and re-attempted on the " +
                                  "player's next move, same as any other pre-existing board match would be.");
                currentGroups = new List<MatchGroup>();
            }
        }
    }

    private void RegisterSpecialsFromMatchGroup(MatchGroup group,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        if (group.IsIntersection && IntersectionsCreateBombs)
        {
            var seed = group.GetSeedCell();
            var color = GetMatchedColor(group.Cells, seed);
            RegisterSpecialSeed(seed, SpecialType.Bomb, color, specialsToCreate);
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

        var color = GetMatchedColor(line, seed);
        RegisterSpecialSeed(seed, special, color, specialsToCreate);
    }

    /// <summary>
    /// The color a newly-created special should carry: the actual matched color of the run, NOT
    /// necessarily whatever sits at the seed cell. A run's seed is just its middle index (or an
    /// intersection's shared cell) - MatchFinder deliberately lets wildcard cells (an existing
    /// Special symbol, or a Madness Symbol when TreatMadnessSymbolsAsWildcards is on) join ANY
    /// color's run without being that color themselves, so a wildcard can easily land ON the
    /// seed position (e.g. Red,Red,Bomb,Red,Red - the Bomb sits in the middle). Previously the
    /// seed cell's own Type was used unconditionally, which meant a new ColorClear/RowClear/
    /// ColumnClear/Bomb could inherit an unrelated leftover color from whatever wildcard happened
    /// to be sitting there instead of the color actually matched - this is that bug's fix.
    /// Prefers the seed cell if it's a genuine (non-wildcard) match; otherwise scans the rest of
    /// the run for the first non-wildcard cell; only falls back to the seed's own Type (or Red)
    /// if literally every cell in the run is a wildcard.
    /// </summary>
    private SymbolType GetMatchedColor(IEnumerable<Vector2Int> cells, Vector2Int seed)
    {
        bool IsWildcard(Symbol s) => s.Special != SpecialType.None
            || (madnessSystem.TreatMadnessSymbolsAsWildcards && s.IsMadness);

        var seedOcc = grid[seed.x, seed.y].Occupant;
        if (seedOcc != null && !IsWildcard(seedOcc)) return seedOcc.Type;

        foreach (var p in cells)
        {
            var occ = grid[p.x, p.y].Occupant;
            if (occ != null && !IsWildcard(occ)) return occ.Type;
        }

        return seedOcc?.Type ?? SymbolType.Red; // every cell in this run was a wildcard - no genuine color to fall back to
    }

    private void RegisterSpecialSeed(Vector2Int seed, SpecialType special, SymbolType color,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        specialsToCreate[seed] = (special, color);
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

        // Madness immunity: this symbol got swept into the match group like anything else (other
        // cells in the group above/below this call still cleared/scored normally), but it shrugs
        // this hit off instead of being destroyed - no score for a no-op hit, and onClearedEffects
        // below is skipped entirely since it hasn't actually been cleared. Matches-mode immunity
        // spends one charge per hit; Moves-mode immunity is untouched here (it only counts down
        // via MadnessSystem.TickSurvival once per move, regardless of being matched).
        if (occ.IsMadness && occ.IsMadnessImmune)
        {
            Debug.Log($"[MatchResolver] ClearCell({pos}): Madness symbol immune (mode={occ.ImmunityMode}, remaining={occ.ImmunityRemaining}) - hit absorbed, not destroyed.");
            occ.TickMadnessImmunityMatch();
            return (false, 0);
        }

        if (occ.IsMadness)
            Debug.Log($"[MatchResolver] ClearCell({pos}): Madness symbol NOT immune (mode={occ.ImmunityMode}, remaining={occ.ImmunityRemaining}) - destroying.");

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
            // NOTE: ignite damage (GetColorDamagePerMatch) is intentionally NOT applied here -
            // see the per-group roll earlier in Resolve(). Rolling it per destroyed cell here
            // used to mean a single N-cell match dealt Nx the intended damage.
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

        EventBus.Publish(new SpecialSymbolMatchedEvent(effectType, origin, affected.ToArray(), isWonkyProc: true));
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

        // Fixed once per call (chainCount doesn't change across this method's own while loop) -
        // TriggerChanceOverride, when set, replaces the normal accumulated RandomSpecialTriggerChance.
        float triggerChance = TriggerChanceOverride != null ? TriggerChanceOverride(chainCount) : RandomSpecialTriggerChance;

        int triggered = 0;
        int cap = forceOnce ? 1 : MaxConsecutiveRandomTriggers;

        while (triggered < cap && (forceOnce || Random.value < triggerChance))
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

            // Same open event a real special match fires - VFX/SFX hooked via SpecialSymbolEventRelay
            // just work - except isWonkyProc:true here so a distinct "WONKY!" callout can fire too.
            EventBus.Publish(new SpecialSymbolMatchedEvent(effectType, origin, affected.ToArray(), isWonkyProc: true));
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
