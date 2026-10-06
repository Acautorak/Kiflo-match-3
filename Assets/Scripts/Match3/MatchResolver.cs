using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
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

    /// <summary>Time.timeScale multiplier (via TimeController) for the duration of a Chain
    /// Lightning arc, e.g. 0.35 = 35% speed. Independent, overlapping procs (two groups matching
    /// in the same cascade step, or a step overlapping something else's slow-mo) each push their
    /// own request - TimeController always resolves to the most restrictive (lowest) active one,
    /// so they naturally combine instead of fighting each other.</summary>
    public float ChainLightningTimeScale { get; set; } = 0.35f;

    /// <summary>Delay between each successive lightning hop, so the chain reads as discrete jumps
    /// rather than every target popping simultaneously. Runs under the slowed timescale above
    /// (WaitForSeconds respects Time.timeScale), so it naturally feels punchier/slower too.</summary>
    public float ChainLightningHopDelay { get; set; } = 0.12f;

    /// <summary>Extra pause held after the last hop before restoring normal time scale, so the
    /// final hit reads clearly instead of snapping back to full speed mid-impact.</summary>
    public float ChainLightningSettleDelay { get; set; } = 0.15f;

    /// <summary>How long (seconds) a Tension Spin proc's row/column visibly conveyor-scrolls
    /// before its final landing tick - same idea as FreeSpinsController.ReelSpinDuration, just a
    /// separate knob since a single-line Tension Spin can reasonably want different pacing than a
    /// whole-board Free Spins reel.</summary>
    public float TensionSpinReelDuration { get; set; } = 0.5f;

    /// <summary>How long each individual conveyor tick takes during a Tension Spin - lower =
    /// faster-scrolling reel. Also sets the floor on how many ticks a spin runs: at least
    /// grid.Height (column) or grid.Width (row) ticks always happen, so every original symbol in
    /// the line is guaranteed to have scrolled off and been replaced - see TriggerTensionSpin.</summary>
    public float TensionSpinTumbleStepDuration { get; set; } = 0.08f;

    /// <summary>How long the final landing tick takes, once the tumble ticks above finish -
    /// separate from TensionSpinTumbleStepDuration so the landing beat can ease in slower/softer
    /// than the tumble itself, same idea as FreeSpinsController's fallDuration landing tick.</summary>
    public float TensionSpinLandingDuration { get; set; } = 0.3f;

    /// <summary>How long (seconds) each pulled-in stray tile's flight tween takes.</summary>
    public float MagnetPulseFlightDuration { get; set; } = 0.35f;

    /// <summary>Pause between successive stray-tile pulls within the same proc, so a multi-hit
    /// proc reads as a sequence of pulls rather than everything snapping at once.</summary>
    public float MagnetPulseHopDelay { get; set; } = 0.1f;

    private static readonly Vector2Int[] FourDirections =
        { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    /// <summary>How many cells above the top of the board a meteor's visible flight starts from -
    /// same "spawn from above the visible grid" trick GravityController already uses for refills
    /// (grid.Height + rank), just a much bigger offset so it clearly reads as "falling from the sky"
    /// rather than a normal refill drop.</summary>
    public float MeteorShowerSkyHeight { get; set; } = 8f;

    /// <summary>How long (seconds) a meteor's fall from the sky to its target takes, at normal
    /// time scale - the slow-mo below only kicks in AFTER it lands, so the incoming streak itself
    /// always reads as fast and dramatic regardless of impact pacing.</summary>
    public float MeteorShowerFlightDuration { get; set; } = 0.45f;

    /// <summary>Time.timeScale multiplier (via TimeController) for a brief hit-pause right as a
    /// meteor lands - unlike Chain Lightning, this does NOT cover the flight itself, only the
    /// impact beat, so the fall stays snappy and only the "thud" gets the weighty slow-mo treatment.</summary>
    public float MeteorShowerImpactTimeScale { get; set; } = 0.25f;

    /// <summary>How long the impact hit-pause holds (in scaled seconds - WaitForSeconds respects
    /// Time.timeScale, so this plays out longer in real time while MeteorShowerImpactTimeScale is active).</summary>
    public float MeteorShowerImpactPauseDuration { get; set; } = 0.15f;

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
            var combineSurvivors = new HashSet<Vector2Int>();
            var combineFlyTarget = new Dictionary<Vector2Int, Vector2Int>();
            var lightningProcOrigins = new List<Vector2Int>();
            var tensionSpinProcs = new List<(bool isColumn, int index)>();
            int magnetPulseProcCount = 0;
            int meteorShowerProcCount = 0;

            foreach (var group in currentGroups)
            {
                foreach (var p in group.Cells) allPositions.Add(p);

                bool combined = TryResolveCombine(group, combineSurvivors, combineFlyTarget);
                if (!combined)
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

                // Chain Lightning: same once-per-group roll granularity as the heal/ignite checks
                // above. Baseline chance is 0 - see PlayerRunStats.ChainLightningChance /
                // PowerupDefinition.chainLightningChanceBonus. Deferred to a list rather than fired
                // inline because the actual arc needs to yield (hop delays + TimeController
                // slow-mo), and this foreach isn't itself a coroutine.
                if (playerRunStats != null)
                {
                    float lightningChance = playerRunStats.ChainLightningChance;
                    if (lightningChance > 0f && Random.value < lightningChance)
                        lightningProcOrigins.Add(seed);
                }

                // Tension Spin: same once-per-group roll granularity as Chain Lightning above.
                // Picks the axis of one of THIS group's own match lines (a random one of the two
                // crossing lines for an L/T intersection) rather than a random line elsewhere on
                // the board - the whole point is "the row/column this match happened in becomes a
                // slot reel". Baseline chance is 0 - see PlayerRunStats.TensionSpinChance /
                // PowerupDefinition.tensionSpinChanceBonus. Deferred to a list for the same
                // yield-mid-foreach reason as Chain Lightning.
                if (playerRunStats != null)
                {
                    float tensionChance = playerRunStats.TensionSpinChance;
                    if (tensionChance > 0f && Random.value < tensionChance)
                    {
                        var line = group.Lines[Random.Range(0, group.Lines.Count)];
                        tensionSpinProcs.Add(GetLineAxis(line));
                    }
                }

                // Magnet Pulse: same once-per-group roll granularity as the others above, but
                // unlike Chain Lightning/Tension Spin it doesn't care which group/color triggered
                // it - TriggerMagnetPulse always retargets whatever the single biggest same-color
                // cluster on the board happens to be at the time it actually runs. A plain counter
                // is enough here (no per-group data to carry forward). Baseline chance is 0 - see
                // PlayerRunStats.MagnetPulseChance / PowerupDefinition.magnetPulseChanceBonus.
                if (playerRunStats != null)
                {
                    float magnetChance = playerRunStats.MagnetPulseChance;
                    if (magnetChance > 0f && Random.value < magnetChance)
                        magnetPulseProcCount++;
                }

                // Meteor Shower: same once-per-group roll granularity as the others. Like Magnet
                // Pulse, it doesn't care which group/color triggered it - TriggerMeteorShower picks
                // a fresh random 2x2 square each time it actually runs. Baseline chance is 0 - see
                // PlayerRunStats.MeteorShowerChance / PowerupDefinition.meteorShowerChanceBonus.
                if (playerRunStats != null)
                {
                    float meteorChance = playerRunStats.MeteorShowerChance;
                    if (meteorChance > 0f && Random.value < meteorChance)
                        meteorShowerProcCount++;
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

            // Chain Lightning: each proc jumps to up to ChainLightningHitCount fully random
            // still-occupied cells anywhere on the board (skipping anything already being cleared
            // this step) and folds them straight into allPositions, so they clear/score/take lock
            // hits through the exact same path below as a real match - no separate handling needed.
            if (lightningProcOrigins.Count > 0)
            {
                gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);
                foreach (var origin in lightningProcOrigins)
                    yield return TryTriggerChainLightning(origin, allPositions, chainCount);
                gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);
            }

            // Meteor Shower: same "fold into allPositions before the ClearCell pass" approach as
            // Chain Lightning - the 4 struck cells need to clear/score/take lock hits through the
            // exact same pipeline as the original match, in this same step, AFTER the player has
            // actually seen the meteor fall and land (the flight+impact plays out here, blocking,
            // before anything visually disappears).
            if (meteorShowerProcCount > 0)
            {
                gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);
                for (int i = 0; i < meteorShowerProcCount; i++)
                    yield return TriggerMeteorShower(allPositions);
                gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);
            }

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
                // Combine survivor: scores exactly like a normal clear, but is never destroyed -
                // it just plays a happy little reaction bounce and stays exactly where it is.
                // TryResolveCombine already guaranteed this cell isn't locked/special/Madness, so
                // ComputeMatchScore's plain color-based formula applies with no further checks.
                if (combineSurvivors.Contains(pos))
                {
                    var survivorOcc = grid[pos.x, pos.y].Occupant;
                    if (survivorOcc != null)
                    {
                        scoreDelta += ComputeMatchScore(survivorOcc.Type, chainCount);
                        survivorOcc.PlayMergeReceiveBounce();
                        anyProgressThisStep = true; // the other cells in its group are being removed
                    }
                    continue;
                }

                // Combine doomed cell: scores identically to a normal clear, but flies into and
                // shrinks toward its survivor (PlayMergeInto) instead of popping in place, then
                // despawns exactly the same way ClearCell's normal path does.
                if (combineFlyTarget.TryGetValue(pos, out var survivorPos))
                {
                    var dyingOcc = grid[pos.x, pos.y].Occupant;
                    if (dyingOcc != null)
                    {
                        scoreDelta += ComputeMatchScore(dyingOcc.Type, chainCount);
                        grid[pos.x, pos.y].Occupant = null;
                        var dying = dyingOcc;
                        dying.PlayMergeInto(gridToWorld(survivorPos.x, survivorPos.y), () =>
                        {
                            dying.ResetVisualState();
                            symbolSpawner.Despawn(dying);
                        });
                        anyProgressThisStep = true;
                    }
                    continue;
                }

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

            // Tension Spin: runs after this step's own clear+refill has fully settled, spinning
            // the ENTIRE row/column the triggering match's line ran along (including whatever just
            // landed there from the refill above) - unlike Chain Lightning, this doesn't fold into
            // allPositions/ClearCell, since it doesn't clear cells, it rerolls them via the exact
            // same conveyor-scroll tick FreeSpinsController.ShiftColumnDown uses for a whole-board
            // spin, just generalized to one row or column (see TriggerTensionSpin). Whatever new
            // matches that creates are picked up for free by the MatchFinder rescan right below -
            // no separate cascade-continuation logic needed.
            bool anyBoardShapingProc = tensionSpinProcs.Count > 0 || magnetPulseProcCount > 0;
            if (anyBoardShapingProc) gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);

            foreach (var (isColumn, index) in tensionSpinProcs)
                yield return TriggerTensionSpin(isColumn, index);

            // Magnet Pulse runs after Tension Spin so it's reacting to the most settled version of
            // the board this step can produce (including whatever colors Tension Spin just
            // rerolled), rather than a cluster snapshot that's about to be invalidated.
            for (int i = 0; i < magnetPulseProcCount; i++)
                yield return TriggerMagnetPulse();

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
    /// One Chain Lightning proc: hops to up to PlayerRunStats.ChainLightningHitCount fully random
    /// still-occupied cells anywhere on the board, publishing a ChainLightningArcEvent per hop for
    /// VFX to render (see ChainLightningVisualizer - a placeholder LineRenderer bolt, since there's
    /// no dedicated art/shader yet; swap in real VFX later without touching this method at all).
    /// Every hit cell is added into `allPositions` before this returns, so the caller's normal
    /// ClearCell pass handles scoring and lock damage identically to a real match - deliberately
    /// no separate scoring/lock logic here. Targets are picked without regard to color (fully
    /// random), excluding cells already in `allPositions` so lightning never "double-hits" a cell
    /// that's already part of this step's match. Time.timeScale dips via TimeController for the
    /// duration; TimeController's stack means an overlapping second proc (or any other slow-mo
    /// request) combines correctly instead of one fight overwriting the other.
    /// </summary>
    private IEnumerator TryTriggerChainLightning(Vector2Int origin, HashSet<Vector2Int> allPositions, int chainCount)
    {
        int hitCount = playerRunStats?.ChainLightningHitCount ?? 0;
        if (hitCount <= 0) yield break;

        var candidates = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var pos = new Vector2Int(x, y);
                if (allPositions.Contains(pos)) continue; // already being cleared this step
                if (grid[x, y].Occupant != null) candidates.Add(pos);
            }

        if (candidates.Count == 0) yield break;

        int timeScaleHandle = TimeController.Push(ChainLightningTimeScale);
        Debug.Log($"[MatchResolver] Chain Lightning proc from {origin} - up to {hitCount} hit(s), " +
                  $"{candidates.Count} candidate tile(s) available.");
        EventBus.Publish(new ChainLightningStartedEvent(origin, hitCount));

        var from = origin;
        for (int i = 0; i < hitCount && candidates.Count > 0; i++)
        {
            int idx = Random.Range(0, candidates.Count);
            var target = candidates[idx];
            candidates.RemoveAt(idx);

            EventBus.Publish(new ChainLightningArcEvent(gridToWorld(from.x, from.y), gridToWorld(target.x, target.y), i, hitCount));
            allPositions.Add(target);
            from = target;

            if (ChainLightningHopDelay > 0f) yield return new WaitForSeconds(ChainLightningHopDelay);
        }

        if (ChainLightningSettleDelay > 0f) yield return new WaitForSeconds(ChainLightningSettleDelay);
        TimeController.Pop(timeScaleHandle);
    }

    /// <summary>
    /// One Meteor Shower proc: picks a fresh random 2x2 square of currently-occupied,
    /// not-already-claimed-this-step cells anywhere on the board, plays a telegraphed fall from
    /// high above the board down onto it (MeteorShowerFlightDuration, normal time scale) followed
    /// by a brief hit-pause right on impact (MeteorShowerImpactTimeScale/
    /// MeteorShowerImpactPauseDuration - unlike Chain Lightning, the slow-mo here covers only the
    /// landing beat, not the fall itself), then adds all 4 struck cells into `allPositions`. The
    /// caller's normal ClearCell pass handles everything from there - scoring, lock damage,
    /// collecting them exactly like a real match - and whatever lands in their place from the
    /// refill afterward cascades through the ordinary rescan, same as every other proc in this
    /// file. No-ops quietly if no valid 2x2 square exists anywhere (board too full of holes/
    /// already-claimed cells, or too small).
    /// </summary>
    private IEnumerator TriggerMeteorShower(HashSet<Vector2Int> allPositions)
    {
        var cells = FindRandomValid2x2Square(allPositions);
        if (cells == null) yield break;

        Vector3 toWorld = Vector3.zero;
        foreach (var c in cells) toWorld += gridToWorld(c.x, c.y);
        toWorld /= cells.Length;

        Vector3 fromWorld = gridToWorld(cells[0].x, grid.Height + Mathf.RoundToInt(MeteorShowerSkyHeight));
        float flightDuration = Mathf.Max(0.05f, MeteorShowerFlightDuration);

        Debug.Log($"[MatchResolver] Meteor Shower proc - targeting 2x2 square anchored at {cells[0]}.");
        EventBus.Publish(new MeteorShowerStartedEvent(fromWorld, toWorld, flightDuration));

        yield return new WaitForSeconds(flightDuration); // the fall itself plays at normal speed - only the impact below gets slow-mo

        int timeScaleHandle = TimeController.Push(MeteorShowerImpactTimeScale);
        EventBus.Publish(new MeteorShowerImpactEvent(toWorld));

        if (MeteorShowerImpactPauseDuration > 0f) yield return new WaitForSeconds(MeteorShowerImpactPauseDuration);
        TimeController.Pop(timeScaleHandle);

        foreach (var c in cells) allPositions.Add(c);
    }

    /// <summary>
    /// Every 2x2 anchor (x,y)-(x+1,y+1) on the board where all 4 cells are currently occupied
    /// (holes/empty cells always fail this automatically, same as everywhere else in this file)
    /// and none are already in `allPositions` (so a meteor never re-claims a cell this step's
    /// original match - or an earlier meteor this same step - already owns), picks one at random.
    /// Returns null if no valid square exists anywhere.
    /// </summary>
    private Vector2Int[] FindRandomValid2x2Square(HashSet<Vector2Int> allPositions)
    {
        var validAnchors = new List<Vector2Int>();
        for (int x = 0; x < grid.Width - 1; x++)
            for (int y = 0; y < grid.Height - 1; y++)
            {
                var bl = new Vector2Int(x, y);
                var br = new Vector2Int(x + 1, y);
                var tl = new Vector2Int(x, y + 1);
                var tr = new Vector2Int(x + 1, y + 1);

                if (allPositions.Contains(bl) || allPositions.Contains(br) || allPositions.Contains(tl) || allPositions.Contains(tr))
                    continue;
                if (grid[bl.x, bl.y].Occupant == null || grid[br.x, br.y].Occupant == null ||
                    grid[tl.x, tl.y].Occupant == null || grid[tr.x, tr.y].Occupant == null)
                    continue;

                validAnchors.Add(bl);
            }

        if (validAnchors.Count == 0) return null;

        var anchor = validAnchors[Random.Range(0, validAnchors.Count)];
        return new[]
        {
            anchor,
            new Vector2Int(anchor.x + 1, anchor.y),
            new Vector2Int(anchor.x, anchor.y + 1),
            new Vector2Int(anchor.x + 1, anchor.y + 1)
        };
    }

    /// <summary>Given one of a MatchGroup's own Lines (always axis-aligned and 3+ cells long - see
    /// MatchFinder.ScanLine), returns whether it runs along a column (isColumn=true, index=x) or a
    /// row (isColumn=false, index=y). Used by Tension Spin to spin the same row/column the
    /// triggering match's line actually ran along.</summary>
    private static (bool isColumn, int index) GetLineAxis(List<Vector2Int> line)
    {
        bool isColumn = line[0].x == line[1].x;
        return (isColumn, isColumn ? line[0].x : line[0].y);
    }

    /// <summary>
    /// One Tension Spin proc: spins the entire row (isColumn=false) or column (isColumn=true) at
    /// `index` like a single Free Spins reel - see FreeSpinsController.SpinColumn/ShiftColumnDown,
    /// which this deliberately mirrors tick-for-tick, just generalized to either axis (via
    /// TensionSpinPosAt) since Free Spins only ever needed columns. Every occupant riding the line
    /// - locked or not, Free Spins doesn't exempt locked tiles either, so neither does this - shifts
    /// one step per tick until it cycles off the low-index end (despawned) and a fresh random
    /// symbol enters from the high-index end. Publishes TensionSpinStartedEvent/
    /// TensionSpinLandedEvent for VFX (see TensionSpinVisualizer - a placeholder, no dedicated
    /// particle art yet). Doesn't rescan for matches itself - the caller (MatchResolver.Resolve)
    /// already runs a MatchFinder pass right after every proc finishes, so anything this lands
    /// cascades through the normal pipeline for free.
    /// </summary>
    private IEnumerator TriggerTensionSpin(bool isColumn, int index)
    {
        int length = isColumn ? grid.Height : grid.Width;
        if (length <= 0) yield break;

        float stepDuration = Mathf.Max(0.01f, TensionSpinTumbleStepDuration);
        int requestedTicks = Mathf.RoundToInt(TensionSpinReelDuration / stepDuration);
        int tumbleTicks = Mathf.Max(length, requestedTicks);
        float landingDuration = Mathf.Max(0.01f, TensionSpinLandingDuration);

        var fromWorld = gridToWorld(TensionSpinPosAt(isColumn, index, 0).x, TensionSpinPosAt(isColumn, index, 0).y);
        var toWorld = gridToWorld(TensionSpinPosAt(isColumn, index, length - 1).x, TensionSpinPosAt(isColumn, index, length - 1).y);

        Debug.Log($"[MatchResolver] Tension Spin proc - spinning {(isColumn ? "column" : "row")} {index} ({tumbleTicks} tick(s)).");
        EventBus.Publish(new TensionSpinStartedEvent(isColumn, index, fromWorld, toWorld, tumbleTicks * stepDuration + landingDuration));

        for (int i = 0; i < tumbleTicks; i++)
            yield return ShiftTensionSpinLine(isColumn, index, length, stepDuration, Ease.Linear).WaitForCompletion();

        yield return ShiftTensionSpinLine(isColumn, index, length, landingDuration, Ease.OutQuad).WaitForCompletion();

        EventBus.Publish(new TensionSpinLandedEvent(isColumn, index));
    }

    /// <summary>Maps a 0-based position `i` along the spinning line back to a grid cell: (index, i)
    /// for a column, (i, index) for a row.</summary>
    private static Vector2Int TensionSpinPosAt(bool isColumn, int index, int i) =>
        isColumn ? new Vector2Int(index, i) : new Vector2Int(i, index);

    /// <summary>
    /// One conveyor tick for the line at `index` - identical shape to FreeSpinsController.
    /// ShiftColumnDown (the low-index occupant exits past the board and is despawned, everything
    /// else shifts one step toward index 0, a fresh random symbol enters from beyond the
    /// high-index end), generalized to either axis via TensionSpinPosAt so the exact same tick
    /// works for a row as for a column.
    /// </summary>
    private Sequence ShiftTensionSpinLine(bool isColumn, int index, int length, float duration, Ease ease)
    {
        var sequence = DOTween.Sequence();

        var exitPos = TensionSpinPosAt(isColumn, index, 0);
        var exiting = grid[exitPos.x, exitPos.y].Occupant;
        if (exiting != null)
        {
            var exitWorldPos = isColumn ? gridToWorld(exitPos.x, -1) : gridToWorld(-1, exitPos.y);
            var exitTween = exiting.MoveTo(exitWorldPos, duration, ease);
            exitTween.OnComplete(() => symbolSpawner.Despawn(exiting));
            sequence.Join(exitTween);
        }

        for (int i = 1; i < length; i++)
        {
            var fromPos = TensionSpinPosAt(isColumn, index, i);
            var toPos = TensionSpinPosAt(isColumn, index, i - 1);
            var occ = grid[fromPos.x, fromPos.y].Occupant;
            grid[fromPos.x, fromPos.y].Occupant = null;
            grid[toPos.x, toPos.y].Occupant = occ;
            if (occ == null) continue;

            occ.GridPosition = toPos;
            sequence.Join(occ.MoveTo(gridToWorld(toPos.x, toPos.y), duration, ease));
        }

        var newType = symbolSpawner.RandomType();
        var landingPos = TensionSpinPosAt(isColumn, index, length - 1);
        var spawnWorldPos = isColumn ? gridToWorld(landingPos.x, length) : gridToWorld(length, landingPos.y);
        var instance = symbolSpawner.Spawn(landingPos.x, landingPos.y, newType, SpecialType.None, spawnWorldPos);
        if (instance != null)
            sequence.Join(instance.MoveTo(gridToWorld(landingPos.x, landingPos.y), duration, ease));

        return sequence;
    }

    /// <summary>
    /// One Magnet Pulse proc: finds the single largest connected same-color blob anywhere on the
    /// board (see FindLargestColorCluster), then pulls PlayerRunStats.MagnetPulseHitCount stray
    /// tiles of that color in from elsewhere, one at a time - each pull position-swaps a random
    /// stray with a random occupied cell bordering the current blob (see PullOneStrayIntoCluster),
    /// so nothing is destroyed or spawned, two tiles just trade places and the blob visibly grows.
    /// Deliberately a LOOSE nudge, not a guaranteed setup: it doesn't check whether the result is
    /// exactly one legal swap from completing a match, it just grows the cluster and trusts that a
    /// bigger same-color blob is, on average, much easier for the player to finish off. No-ops
    /// quietly if the board's biggest cluster is only a single tile (nothing worth reinforcing) or
    /// if a pull ever finds nowhere left to pull from/into (blob boxed in by locked tiles/edges, or
    /// no stray tiles of that color left anywhere else).
    /// </summary>
    private IEnumerator TriggerMagnetPulse()
    {
        int hitCount = playerRunStats?.MagnetPulseHitCount ?? 0;
        if (hitCount <= 0) yield break;

        var (clusterCells, clusterColor) = FindLargestColorCluster();
        if (clusterCells == null || clusterCells.Count < 2) yield break; // nothing worth reinforcing

        Debug.Log($"[MatchResolver] Magnet Pulse proc - targeting {clusterColor} cluster of size {clusterCells.Count}, up to {hitCount} pull(s).");
        EventBus.Publish(new MagnetPulseStartedEvent(clusterColor, clusterCells.Count));

        for (int i = 0; i < hitCount; i++)
        {
            var (landingPos, sequence) = PullOneStrayIntoCluster(clusterCells, clusterColor);
            if (!landingPos.HasValue) break; // nowhere left to pull from/into this proc

            if (sequence != null) yield return sequence.WaitForCompletion();
            clusterCells.Add(landingPos.Value); // grows for the next pull this same proc

            if (MagnetPulseHopDelay > 0f) yield return new WaitForSeconds(MagnetPulseHopDelay);
        }
    }

    /// <summary>
    /// Finds every stray tile of `clusterColor` outside `clusterCells`, every occupied cell
    /// bordering `clusterCells` that ISN'T already clusterColor, picks one of each at random,
    /// position-swaps them (grid registration + GridPosition updated immediately; the visual
    /// catch-up is the returned Sequence), and returns the landing position so the caller can grow
    /// `clusterCells` for its next pull - or (null, null) if no valid (stray, border) pair exists
    /// right now. Locked tiles are excluded on both sides, same default every other board-wide
    /// effect in this file uses for "don't disturb a locked tile". Mirrors the split GravityController
    /// uses between building a DOTween Sequence here and the caller awaiting it with
    /// WaitForCompletion() - this method itself never yields.
    /// </summary>
    private (Vector2Int? landingPos, Sequence sequence) PullOneStrayIntoCluster(HashSet<Vector2Int> clusterCells, SymbolType clusterColor)
    {
        var strays = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var pos = new Vector2Int(x, y);
                if (clusterCells.Contains(pos)) continue;
                var occ = grid[x, y].Occupant;
                if (occ != null && !occ.IsLocked && occ.Type == clusterColor) strays.Add(pos);
            }
        if (strays.Count == 0) return (null, null);

        var border = new List<Vector2Int>();
        foreach (var cell in clusterCells)
            foreach (var dir in FourDirections)
            {
                var n = cell + dir;
                if (!grid.InBounds(n) || clusterCells.Contains(n) || border.Contains(n)) continue;
                var occ = grid[n.x, n.y].Occupant;
                if (occ == null || occ.IsLocked || occ.Type == clusterColor) continue;
                border.Add(n);
            }
        if (border.Count == 0) return (null, null);

        var strayPos = strays[Random.Range(0, strays.Count)];
        var landingPos = border[Random.Range(0, border.Count)];

        var strayOcc = grid[strayPos.x, strayPos.y].Occupant;
        var landingOcc = grid[landingPos.x, landingPos.y].Occupant;

        grid[strayPos.x, strayPos.y].Occupant = landingOcc;
        grid[landingPos.x, landingPos.y].Occupant = strayOcc;
        strayOcc.GridPosition = landingPos;
        landingOcc.GridPosition = strayPos;

        EventBus.Publish(new MagnetPulseFlightEvent(gridToWorld(strayPos.x, strayPos.y), gridToWorld(landingPos.x, landingPos.y), MagnetPulseFlightDuration));

        var sequence = DOTween.Sequence();
        sequence.Join(strayOcc.MoveTo(gridToWorld(landingPos.x, landingPos.y), MagnetPulseFlightDuration, Ease.InOutSine));
        sequence.Join(landingOcc.MoveTo(gridToWorld(strayPos.x, strayPos.y), MagnetPulseFlightDuration, Ease.InOutSine));

        return (landingPos, sequence);
    }

    /// <summary>
    /// Flood-fills the whole board into same-color connected components (4-directional, locked
    /// tiles excluded from participating at all - same default as every other board-wide effect
    /// here) and returns the single largest one, with its color. Returns (null, default) if the
    /// board is entirely empty/locked. Ties are resolved by whichever component the scan reaches
    /// first (bottom-left to top-right) - not randomized, since which specific tile cluster "wins"
    /// a tie barely matters for what's ultimately a loose nudge rather than a precise effect.
    /// </summary>
    private (HashSet<Vector2Int> cells, SymbolType color) FindLargestColorCluster()
    {
        var visited = new HashSet<Vector2Int>();
        HashSet<Vector2Int> best = null;
        SymbolType bestColor = default;

        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var start = new Vector2Int(x, y);
                if (visited.Contains(start)) continue;

                var occ = grid[x, y].Occupant;
                if (occ == null || occ.IsLocked)
                {
                    visited.Add(start);
                    continue;
                }

                var component = FloodFillSameColor(start, occ.Type, visited);
                if (best == null || component.Count > best.Count)
                {
                    best = component;
                    bestColor = occ.Type;
                }
            }

        return (best, bestColor);
    }

    /// <summary>4-directional flood fill from `start`, collecting every reachable cell that's
    /// occupied, unlocked, and matches `color` - marks every visited cell (including dead ends and
    /// off-color/locked/empty neighbors it had to check) into the shared `visited` set so
    /// FindLargestColorCluster's outer scan never reprocesses the same ground twice.</summary>
    private HashSet<Vector2Int> FloodFillSameColor(Vector2Int start, SymbolType color, HashSet<Vector2Int> visited)
    {
        var component = new HashSet<Vector2Int>();
        var stack = new Stack<Vector2Int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (visited.Contains(cur)) continue;
            visited.Add(cur);

            if (!grid.InBounds(cur)) continue;
            var occ = grid[cur.x, cur.y].Occupant;
            if (occ == null || occ.IsLocked || occ.Type != color) continue;

            component.Add(cur);
            foreach (var dir in FourDirections)
                stack.Push(cur + dir);
        }

        return component;
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

        // Grid state is freed immediately - gravity/refill/rescanning must never wait on a death
        // animation. The GameObject itself keeps existing and plays its own pop-and-fade
        // independently (PlayMatchedEffect), only actually returned to the pool once that
        // finishes - ResetVisualState first so it doesn't come back out of the pool still scaled
        // up and faded from this life's matched effect.
        grid[pos.x, pos.y].Occupant = null;
        var dying = occ;
        dying.PlayMatchedEffect(() =>
        {
            dying.ResetVisualState();
            symbolSpawner.Despawn(dying);
        });

        return (true, ComputeMatchScore(color, chainCount));
    }

    /// <summary>
    /// Shared per-cell scoring formula - used by ClearCell for a normal clear, and by
    /// TryResolveCombine for a combined group's cells (including the survivor, which still earns
    /// its own score even though it isn't actually destroyed). Keeping this in one place means a
    /// combined match scores IDENTICALLY to a normal one, exactly as intended ("normal matched 3
    /// values as usual") rather than needing to duplicate this formula a second time.
    /// </summary>
    private int ComputeMatchScore(SymbolType color, int chainCount)
    {
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

        return baseScore;
    }

    /// <summary>
    /// Roguelike "Combine on Match": once per group, a chance (PlayerRunStats.CombineOnMatchChance)
    /// that instead of every matched cell clearing independently, all but one collapse into a
    /// single surviving symbol at a random one of their positions. Scoring is completely
    /// unaffected - every cell, including the survivor, still awards score via ComputeMatchScore
    /// exactly as a normal clear would, and SymbolMatchedEvent/ChainMatchedEvent/Collect-goal
    /// progress (published earlier in Resolve, before this runs) already counted every cell as
    /// matched regardless - only what's physically LEFT on the board afterward changes.
    ///
    /// Only eligible for a "pure" group: every cell must be a genuine, non-special, non-Madness,
    /// unlocked symbol of the same color. Excluding Special/Madness/locked cells sidesteps a pile
    /// of edge cases a v1 doesn't need to solve (a merged-away Madness Symbol would need its own
    /// immunity/onClearedEffects/MadnessSymbolClearedEvent handling; a merged-away lock would need
    /// RemoveLockLayer's hit-absorption logic) - deliberately scoped this way for now. A group
    /// that would otherwise create a special (a 4+/intersection match) simply doesn't when it
    /// combines instead - upgrading the survivor into a special of its own is an intentional
    /// future step (see PlayerRunStats.CombineOnMatchChance's doc comment), not implemented yet.
    ///
    /// Returns true if this group combined - adds the survivor's position to `survivorPositions`
    /// and every other cell to `flyTarget` (mapping doomed cell -> survivor position) so the main
    /// clearing loop in Resolve() knows to treat this group's cells specially instead of running
    /// them through ordinary ClearCell.
    /// </summary>
    private bool TryResolveCombine(MatchGroup group, HashSet<Vector2Int> survivorPositions,
        Dictionary<Vector2Int, Vector2Int> flyTarget)
    {
        if (playerRunStats == null) return false;

        float chance = playerRunStats.CombineOnMatchChance;
        if (chance <= 0f || Random.value >= chance) return false;

        var cells = group.Cells.ToList();
        foreach (var p in cells)
        {
            var occ = grid[p.x, p.y].Occupant;
            if (occ == null || occ.Special != SpecialType.None || occ.IsMadness || occ.IsLocked)
                return false; // not a "pure" group - leave it to resolve normally
        }

        // Pick a random cell for variety (preserves "random position among the matched cells"),
        // then correct it to the bottom-most cell in the SAME COLUMN among this group's cells.
        // Gravity only ever moves things vertically within their own column, and a matched run's
        // cells are always contiguous (no gaps - that's what makes it a run), so that bottom-most
        // cell is exactly where the surviving tile will end up once gravity collapses the column
        // underneath it. Without this correction, a mid-run survivor (e.g. the middle of a
        // vertical 3-match) would settle from the merge, then immediately get dragged down again
        // by gravity closing the gap the removed cells below it left behind - two separate
        // motions where there should be one. Since every cell in the group is the same type
        // anyway, whichever occupant is already sitting at that final spot just IS the survivor -
        // no grid-data relocation needed, only the OTHER cells need to fly anywhere.
        var candidate = cells[Random.Range(0, cells.Count)];
        var landingPos = candidate;
        foreach (var p in cells)
            if (p.x == candidate.x && p.y < landingPos.y)
                landingPos = p;

        survivorPositions.Add(landingPos);
        foreach (var p in cells)
            if (p != landingPos)
                flyTarget[p] = landingPos;

        Debug.Log($"[MatchResolver] Combine rolled for a {cells.Count}-cell group - survivor lands at {landingPos}.");
        EventBus.Publish(new CombineTriggeredEvent(landingPos, cells.Count));
        return true;
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
