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
    [SerializeField] private float scoreMultiplier = 1f;
    [SerializeField] private int bonusGraceMoves = 0;

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
    /// <summary>Subtracted directly from a stage's lockSpawnChance (covers both lock spawn and frozen-tile rolls).</summary>
    public float LockChanceReduction => lockChanceReduction;
    /// <summary>Multiplies every scoreDelta before it's added to the board's score.</summary>
    public float ScoreMultiplier => Mathf.Max(0f, scoreMultiplier);
    /// <summary>Added to a stage's gracePeriodMoves.</summary>
    public int BonusGraceMoves => Mathf.Max(0, bonusGraceMoves);

    public void ResetForNewRun()
    {
        randomSpecialChanceBonus = 0f;
        lockChanceReduction = 0f;
        scoreMultiplier = 1f;
        bonusGraceMoves = 0;
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
}
