using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-run modifiers that powerups accumulate onto. Values here get folded into Board's chance
/// calculations (see Board.ApplyStageRules and its score-delta multiplier) and StageManager's
/// grace period - stage difficulty (StageGenerationConfig) sets each chance's baseline, these
/// stats shift it up or down from there. They are NOT permanent meta-progression: call
/// ResetForNewRun() whenever a fresh run starts (StageManager.StartNewRun() already does this)
/// so powerups never carry over between runs.
/// </summary>
public class PlayerRunStats : MonoBehaviour
{
    [Header("Current run modifiers - do not hand-tune these, powerups accumulate onto them at runtime")]
    [SerializeField] private float randomSpecialChanceBonus = 0f;
    [SerializeField] private float lockChanceReduction = 0f;
    [Tooltip("Chance (0-1), rolled once per matched group, that 1 random unburning/unlocked tile " +
             "adjacent to the match ignites and starts burning down (see BurningSystem). Baseline " +
             "0, so this is purely a powerup-granted chance, same shape as randomSpecialChanceBonus.")]
    [SerializeField] private float igniteOnMatchChanceBonus = 0f;
    [Tooltip("Chance (0-1), rolled once per matched group, that its cells combine into ONE " +
             "surviving symbol of the matched color (at a random one of their positions) instead " +
             "of all clearing independently. Baseline 0 - purely a powerup-granted chance, same " +
             "shape as igniteOnMatchChanceBonus. Scoring/events are unaffected either way - see " +
             "MatchResolver.Resolve for the actual mechanic.")]
    [SerializeField] private float combineOnMatchChanceBonus = 0f;
    [Tooltip("Chance (0-1), rolled once per matched group, that a bolt of chain lightning arcs " +
             "out and additionally clears chainLightningHitCountBonus random tiles elsewhere on " +
             "the board (locked tiles take a normal lock hit, same as being caught in a real " +
             "match). Baseline 0 - purely a powerup-granted chance, same shape as " +
             "igniteOnMatchChanceBonus. See MatchResolver.TryTriggerChainLightning.")]
    [SerializeField] private float chainLightningChanceBonus = 0f;
    [Tooltip("Added ON TOP OF a baseline of 5 (see PlayerRunStats.BaseChainLightningHitCount) - " +
             "how many EXTRA random tiles a single Chain Lightning proc hits, beyond the 5 it " +
             "already hits with zero powerups picked. Only matters if chainLightningChanceBonus " +
             "is > 0 (from this or another powerup) - a chance of 0 never procs regardless of hit count.")]
    [SerializeField] private int chainLightningHitCountBonus = 0;
    [Tooltip("Chance (0-1), rolled once per matched group, that the entire row/column the " +
             "triggering match's line ran along turns into a slot-machine reel: every symbol in " +
             "it spins (same conveyor mechanic as Free Spins) and re-lands with fresh random " +
             "types, possibly creating new matches that cascade normally. Baseline 0 - purely a " +
             "powerup-granted chance, same shape as chainLightningChanceBonus. See " +
             "MatchResolver.TriggerTensionSpin.")]
    [SerializeField] private float tensionSpinChanceBonus = 0f;
    [Tooltip("Chance (0-1), rolled once per matched group, that Magnet Pulse procs: finds the " +
             "single largest connected same-color blob anywhere on the board and pulls " +
             "magnetPulseHitCountBonus stray tiles of that color in from elsewhere, swapping each " +
             "onto the blob's border so it visibly grows - nudging the player toward an easy match " +
             "there without guaranteeing one. Baseline 0 - purely a powerup-granted chance, same " +
             "shape as chainLightningChanceBonus. See MatchResolver.TriggerMagnetPulse.")]
    [SerializeField] private float magnetPulseChanceBonus = 0f;
    [Tooltip("Added ON TOP OF a baseline of 1 (see PlayerRunStats.BaseMagnetPulseHitCount) - how " +
             "many EXTRA stray tiles get pulled into the cluster per proc, beyond the 1 pulled in " +
             "with zero powerups picked. Only matters if magnetPulseChanceBonus is > 0.")]
    [SerializeField] private int magnetPulseHitCountBonus = 0;
    [Tooltip("Chance (0-1), rolled once per matched group, that Meteor Shower procs: a 2x2 square " +
             "of tiles elsewhere on the board gets struck by a falling meteor (telegraphed flight " +
             "+ impact, see MatchResolver.TriggerMeteorShower), then those 4 tiles are collected " +
             "exactly like a real match - scored, locks damaged - and refilled, possibly cascading. " +
             "Baseline 0 - purely a powerup-granted chance, same shape as chainLightningChanceBonus.")]
    [SerializeField] private float meteorShowerChanceBonus = 0f;
    [Tooltip("Added ON TOP OF a baseline of 1 (see PlayerRunStats.BaseMeteorShowerCount) - how " +
             "many EXTRA separate 2x2 meteors fall per proc, beyond the 1 that falls with zero " +
             "powerups picked. Only matters if meteorShowerChanceBonus is > 0.")]
    [SerializeField] private int meteorShowerCountBonus = 0;
    [SerializeField] private float scoreMultiplier = 1f;
    [SerializeField] private int bonusGraceMoves = 0;
    [SerializeField] private int kebabTapDamageBonus = 0;
    [Tooltip("Chance (0-1), rolled after every accepted move, that the FOLLOWING move becomes a " +
             "Grace Move (no damage taken, doesn't count toward a MoveCount stage goal) - see " +
             "GraceMoveController. Baseline 0, so this is purely a powerup-granted chance, same " +
             "shape as randomSpecialChanceBonus above.")]
    [SerializeField] private float graceMoveChanceBonus = 0f;

    /// <summary>
    /// Per-color score modifiers accumulated from powerups (see PowerupDefinition.colorEffects).
    /// Not hand-tuned in the Inspector for the same reason as the fields above - runtime-only state.
    /// </summary>
    [System.Serializable]
    public struct ColorBonus
    {
        public SymbolType color;
        public float scoreMultiplierBonus;
        public int flatScoreBonusPerCell;
        /// <summary>Chance (0-1) to heal when this color matches. Baseline 0 - powerups add to it.</summary>
        public float healChancePerMatch;
        /// <summary>HP restored if the chance above rolls a hit.</summary>
        public int healAmountOnMatch;
    }

    [SerializeField] private List<ColorBonus> colorBonuses = new List<ColorBonus>();

    /// <summary>How many times AddScoreMultiplier has been called this run - diagnostic only, logged alongside it.</summary>
    private int scoreMultiplierApplyCount = 0;

    /// <summary>Added directly to a stage's randomSpecialChance / gracePeriodRandomSpecialChance.</summary>
    public float RandomSpecialChanceBonus => randomSpecialChanceBonus;
    /// <summary>Chance (0-1, clamped) rolled once per matched group by BurningSystem.TryIgniteNearby.</summary>
    public float IgniteOnMatchChance => Mathf.Clamp01(igniteOnMatchChanceBonus);
    /// <summary>Chance (0-1, clamped) rolled once per matched group by MatchResolver.Resolve.</summary>
    public float CombineOnMatchChance => Mathf.Clamp01(combineOnMatchChanceBonus);
    /// <summary>Chance (0-1, clamped) rolled once per matched group by MatchResolver.TryTriggerChainLightning.</summary>
    public float ChainLightningChance => Mathf.Clamp01(chainLightningChanceBonus);
    /// <summary>How many tiles a Chain Lightning proc hits with zero powerups picked. Powerups
    /// only need to add ON TOP of this via chainLightningHitCountBonus - a Chain Lightning
    /// powerup that only raises chainLightningChanceBonus (the more common case, since chance is
    /// the interesting knob to buff repeatedly) still does something meaningful once it procs,
    /// rather than silently hitting 0 tiles.</summary>
    private const int BaseChainLightningHitCount = 5;

    /// <summary>Total tiles a Chain Lightning proc hits: the baseline above plus every powerup's chainLightningHitCountBonus.</summary>
    public int ChainLightningHitCount => Mathf.Max(0, BaseChainLightningHitCount + chainLightningHitCountBonus);
    /// <summary>Chance (0-1, clamped) rolled once per matched group by MatchResolver.TriggerTensionSpin.</summary>
    public float TensionSpinChance => Mathf.Clamp01(tensionSpinChanceBonus);
    /// <summary>Chance (0-1, clamped) rolled once per matched group by MatchResolver.TriggerMagnetPulse.</summary>
    public float MagnetPulseChance => Mathf.Clamp01(magnetPulseChanceBonus);

    /// <summary>How many stray tiles a Magnet Pulse proc pulls in with zero powerups picked - same
    /// "non-zero baseline" reasoning as BaseChainLightningHitCount, so a powerup that only raises
    /// magnetPulseChanceBonus still does something the moment it procs.</summary>
    private const int BaseMagnetPulseHitCount = 1;

    /// <summary>Total stray tiles a Magnet Pulse proc pulls in: the baseline above plus every powerup's magnetPulseHitCountBonus.</summary>
    public int MagnetPulseHitCount => Mathf.Max(0, BaseMagnetPulseHitCount + magnetPulseHitCountBonus);
    /// <summary>Chance (0-1, clamped) rolled once per matched group by MatchResolver.TriggerMeteorShower.</summary>
    public float MeteorShowerChance => Mathf.Clamp01(meteorShowerChanceBonus);

    /// <summary>How many 2x2 meteors fall per proc with zero powerups picked - same non-zero
    /// baseline reasoning as BaseChainLightningHitCount/BaseMagnetPulseHitCount.</summary>
    private const int BaseMeteorShowerCount = 1;

    /// <summary>Total meteors a proc drops: the baseline above plus every powerup's meteorShowerCountBonus.</summary>
    public int MeteorShowerCount => Mathf.Max(0, BaseMeteorShowerCount + meteorShowerCountBonus);
    /// <summary>Subtracted directly from a stage's lockSpawnChance (covers both lock spawn and frozen-tile rolls).</summary>
    public float LockChanceReduction => lockChanceReduction;
    /// <summary>Multiplies every scoreDelta before it's added to the board's score.</summary>
    public float ScoreMultiplier => Mathf.Max(0f, scoreMultiplier);
    /// <summary>Added to a stage's gracePeriodMoves.</summary>
    public int BonusGraceMoves => Mathf.Max(0, bonusGraceMoves);
    /// <summary>Added to the base 1 tap damage dealt to Kebab Karnage asteroids per tap (see KebabKarnageManager).</summary>
    public int KebabTapDamageBonus => Mathf.Max(0, kebabTapDamageBonus);
    /// <summary>Chance (0-1, clamped) that the next move after any given move becomes a Grace Move - see GraceMoveController.</summary>
    public float GraceMoveChance => Mathf.Clamp01(graceMoveChanceBonus);

    public void ResetForNewRun()
    {
        randomSpecialChanceBonus = 0f;
        lockChanceReduction = 0f;
        igniteOnMatchChanceBonus = 0f;
        combineOnMatchChanceBonus = 0f;
        chainLightningChanceBonus = 0f;
        chainLightningHitCountBonus = 0;
        tensionSpinChanceBonus = 0f;
        magnetPulseChanceBonus = 0f;
        magnetPulseHitCountBonus = 0;
        meteorShowerChanceBonus = 0f;
        meteorShowerCountBonus = 0;
        scoreMultiplier = 1f;
        bonusGraceMoves = 0;
        kebabTapDamageBonus = 0;
        graceMoveChanceBonus = 0f;
        scoreMultiplierApplyCount = 0;
        colorBonuses.Clear();
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddRandomSpecialChanceBonus(float amount)
    {
        if (amount == 0f) return;
        randomSpecialChanceBonus += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddIgniteChanceBonus(float amount)
    {
        if (amount == 0f) return;
        igniteOnMatchChanceBonus += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddCombineOnMatchChanceBonus(float amount)
    {
        if (amount == 0f) return;
        combineOnMatchChanceBonus += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddChainLightningChanceBonus(float amount)
    {
        if (amount == 0f) return;
        chainLightningChanceBonus += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddChainLightningHitCountBonus(int amount)
    {
        if (amount == 0) return;
        chainLightningHitCountBonus = Mathf.Max(0, chainLightningHitCountBonus + amount);
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddTensionSpinChanceBonus(float amount)
    {
        if (amount == 0f) return;
        tensionSpinChanceBonus += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddMagnetPulseChanceBonus(float amount)
    {
        if (amount == 0f) return;
        magnetPulseChanceBonus += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddMagnetPulseHitCountBonus(int amount)
    {
        if (amount == 0) return;
        magnetPulseHitCountBonus = Mathf.Max(0, magnetPulseHitCountBonus + amount);
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddMeteorShowerChanceBonus(float amount)
    {
        if (amount == 0f) return;
        meteorShowerChanceBonus += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddMeteorShowerCountBonus(int amount)
    {
        if (amount == 0) return;
        meteorShowerCountBonus = Mathf.Max(0, meteorShowerCountBonus + amount);
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddLockChanceReduction(float amount)
    {
        if (amount == 0f) return;
        lockChanceReduction += amount;
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddScoreMultiplier(float amount)
    {
        if (amount == 0f) return;
        scoreMultiplierApplyCount++;
        float before = scoreMultiplier;
        scoreMultiplier = Mathf.Max(0f, scoreMultiplier + amount);
        // Diagnostic: if this count climbs far faster than "once per stage you actually picked
        // it", something's calling this more than once per real selection (duplicate PowerupManager/
        // UI instance in the scene, or the same PowerupDefinition listed twice in the pool asset).
        Debug.Log($"[PlayerRunStats] AddScoreMultiplier: {before} -> {scoreMultiplier} (+{amount}), call #{scoreMultiplierApplyCount} this run");
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddBonusGraceMoves(int amount)
    {
        if (amount == 0) return;
        bonusGraceMoves = Mathf.Max(0, bonusGraceMoves + amount);
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    /// <summary>Permanent (run-long) increase to Kebab Karnage tap damage, e.g. from a PowerupDefinition.</summary>
    public void AddKebabTapDamageBonus(int amount)
    {
        if (amount == 0) return;
        kebabTapDamageBonus = Mathf.Max(0, kebabTapDamageBonus + amount);
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public void AddGraceMoveChanceBonus(float amount)
    {
        if (amount == 0f) return;
        graceMoveChanceBonus = Mathf.Clamp01(graceMoveChanceBonus + amount);
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    public float GetColorScoreMultiplierBonus(SymbolType color)
    {
        for (int i = 0; i < colorBonuses.Count; i++)
            if (colorBonuses[i].color == color) return colorBonuses[i].scoreMultiplierBonus;
        return 0f;
    }

    public int GetColorFlatScoreBonus(SymbolType color)
    {
        for (int i = 0; i < colorBonuses.Count; i++)
            if (colorBonuses[i].color == color) return colorBonuses[i].flatScoreBonusPerCell;
        return 0;
    }

    /// <summary>Chance (0-1, clamped) to heal when this color matches. Baseline 0.</summary>
    public float GetColorHealChance(SymbolType color)
    {
        for (int i = 0; i < colorBonuses.Count; i++)
            if (colorBonuses[i].color == color) return Mathf.Clamp01(colorBonuses[i].healChancePerMatch);
        return 0f;
    }

    /// <summary>HP restored if GetColorHealChance's roll succeeds.</summary>
    public int GetColorHealAmount(SymbolType color)
    {
        for (int i = 0; i < colorBonuses.Count; i++)
            if (colorBonuses[i].color == color) return colorBonuses[i].healAmountOnMatch;
        return 0;
    }

    /// <summary>Accumulates a color-targeted powerup's effect onto that color's running totals.</summary>
    public void AddColorEffect(SymbolType color, float scoreMultiplierBonus, int flatScoreBonusPerCell,
        float healChancePerMatch, int healAmountOnMatch)
    {
        if (scoreMultiplierBonus == 0f && flatScoreBonusPerCell == 0 && healChancePerMatch == 0f && healAmountOnMatch == 0)
            return;

        for (int i = 0; i < colorBonuses.Count; i++)
        {
            if (colorBonuses[i].color != color) continue;
            var b = colorBonuses[i];
            b.scoreMultiplierBonus += scoreMultiplierBonus;
            b.flatScoreBonusPerCell += flatScoreBonusPerCell;
            b.healChancePerMatch = Mathf.Clamp01(b.healChancePerMatch + healChancePerMatch);
            b.healAmountOnMatch += healAmountOnMatch;
            colorBonuses[i] = b;
            EventBus.Publish(new PlayerStatsChangedEvent(this));
            return;
        }

        colorBonuses.Add(new ColorBonus
        {
            color = color,
            scoreMultiplierBonus = scoreMultiplierBonus,
            flatScoreBonusPerCell = flatScoreBonusPerCell,
            healChancePerMatch = Mathf.Clamp01(healChancePerMatch),
            healAmountOnMatch = healAmountOnMatch
        });
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }

    /// <summary>Snapshots every accumulated modifier for SaveSystem. Called by Board.BuildSaveData.</summary>
    public PlayerRunStatsSaveData BuildSaveData()
    {
        var data = new PlayerRunStatsSaveData
        {
            randomSpecialChanceBonus = randomSpecialChanceBonus,
            lockChanceReduction = lockChanceReduction,
            igniteOnMatchChanceBonus = igniteOnMatchChanceBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            combineOnMatchChanceBonus = combineOnMatchChanceBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            chainLightningChanceBonus = chainLightningChanceBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            chainLightningHitCountBonus = chainLightningHitCountBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            tensionSpinChanceBonus = tensionSpinChanceBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            magnetPulseChanceBonus = magnetPulseChanceBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            magnetPulseHitCountBonus = magnetPulseHitCountBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            meteorShowerChanceBonus = meteorShowerChanceBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            meteorShowerCountBonus = meteorShowerCountBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            scoreMultiplier = scoreMultiplier,
            bonusGraceMoves = bonusGraceMoves,
            kebabTapDamageBonus = kebabTapDamageBonus,
            graceMoveChanceBonus = graceMoveChanceBonus, // REQUIRES a matching field added to PlayerRunStatsSaveData - see note in RestoreFromSave
            colorBonuses = new ColorBonusSaveData[colorBonuses.Count]
        };

        for (int i = 0; i < colorBonuses.Count; i++)
        {
            var b = colorBonuses[i];
            data.colorBonuses[i] = new ColorBonusSaveData
            {
                color = b.color,
                scoreMultiplierBonus = b.scoreMultiplierBonus,
                flatScoreBonusPerCell = b.flatScoreBonusPerCell,
                healChancePerMatch = b.healChancePerMatch,
                healAmountOnMatch = b.healAmountOnMatch
            };
        }

        return data;
    }

    /// <summary>
    /// Restores every accumulated modifier from a save file, fully replacing current state
    /// (not additive). Called by Board.LoadFromSave. Passing null resets to a fresh run's
    /// baseline, same as ResetForNewRun().
    /// </summary>
    public void RestoreFromSave(PlayerRunStatsSaveData data)
    {
        if (data == null)
        {
            ResetForNewRun();
            return;
        }

        randomSpecialChanceBonus = data.randomSpecialChanceBonus;
        lockChanceReduction = data.lockChanceReduction;
        // NOTE: same caveat as graceMoveChanceBonus below - assumes PlayerRunStatsSaveData has a
        // `public float igniteOnMatchChanceBonus;` field; add it there if it isn't already present.
        igniteOnMatchChanceBonus = data.igniteOnMatchChanceBonus;
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public float combineOnMatchChanceBonus;` field; add it there if it isn't already present.
        combineOnMatchChanceBonus = data.combineOnMatchChanceBonus;
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public float chainLightningChanceBonus;` field; add it there if it isn't already present.
        chainLightningChanceBonus = data.chainLightningChanceBonus;
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public int chainLightningHitCountBonus;` field; add it there if it isn't already present.
        chainLightningHitCountBonus = Mathf.Max(0, data.chainLightningHitCountBonus);
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public float tensionSpinChanceBonus;` field; add it there if it isn't already present.
        tensionSpinChanceBonus = data.tensionSpinChanceBonus;
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public float magnetPulseChanceBonus;` field; add it there if it isn't already present.
        magnetPulseChanceBonus = data.magnetPulseChanceBonus;
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public int magnetPulseHitCountBonus;` field; add it there if it isn't already present.
        magnetPulseHitCountBonus = Mathf.Max(0, data.magnetPulseHitCountBonus);
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public float meteorShowerChanceBonus;` field; add it there if it isn't already present.
        meteorShowerChanceBonus = data.meteorShowerChanceBonus;
        // NOTE: same caveat - assumes PlayerRunStatsSaveData has a
        // `public int meteorShowerCountBonus;` field; add it there if it isn't already present.
        meteorShowerCountBonus = Mathf.Max(0, data.meteorShowerCountBonus);
        scoreMultiplier = Mathf.Max(0f, data.scoreMultiplier);
        bonusGraceMoves = Mathf.Max(0, data.bonusGraceMoves);
        kebabTapDamageBonus = Mathf.Max(0, data.kebabTapDamageBonus);
        // NOTE: this line (and its BuildSaveData counterpart above) assume PlayerRunStatsSaveData
        // has a `public float graceMoveChanceBonus;` field - I don't have that class's source, so
        // add the field there yourself if it isn't already present, or this won't compile.
        graceMoveChanceBonus = Mathf.Clamp01(data.graceMoveChanceBonus);
        scoreMultiplierApplyCount = 0; // fresh diagnostic count for this session, not part of the save

        colorBonuses.Clear();
        if (data.colorBonuses != null)
        {
            foreach (var cb in data.colorBonuses)
            {
                colorBonuses.Add(new ColorBonus
                {
                    color = cb.color,
                    scoreMultiplierBonus = cb.scoreMultiplierBonus,
                    flatScoreBonusPerCell = cb.flatScoreBonusPerCell,
                    healChancePerMatch = cb.healChancePerMatch,
                    healAmountOnMatch = cb.healAmountOnMatch
                });
            }
        }

        Debug.Log($"[PlayerRunStats] Restored from save: scoreMultiplier={scoreMultiplier}, " +
                  $"randomSpecialChanceBonus={randomSpecialChanceBonus}, lockChanceReduction={lockChanceReduction}, " +
                  $"igniteOnMatchChanceBonus={igniteOnMatchChanceBonus}, combineOnMatchChanceBonus={combineOnMatchChanceBonus}, " +
                  $"chainLightningChanceBonus={chainLightningChanceBonus}, chainLightningHitCountBonus={chainLightningHitCountBonus}, " +
                  $"tensionSpinChanceBonus={tensionSpinChanceBonus}, " +
                  $"magnetPulseChanceBonus={magnetPulseChanceBonus}, magnetPulseHitCountBonus={magnetPulseHitCountBonus}, " +
                  $"meteorShowerChanceBonus={meteorShowerChanceBonus}, meteorShowerCountBonus={meteorShowerCountBonus}, " +
                  $"bonusGraceMoves={bonusGraceMoves}, kebabTapDamageBonus={kebabTapDamageBonus}, colorBonuses={colorBonuses.Count}");
        EventBus.Publish(new PlayerStatsChangedEvent(this));
    }
}
