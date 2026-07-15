using UnityEngine;

/// <summary>
/// Designer-facing asset: one powerup a player can pick between stages. Create via
/// Assets > Create > Match3 > Roguelike > Powerup. Leave any effect field at 0 to skip it -
/// a powerup can combine several small bonuses or just do one thing well.
/// </summary>
[CreateAssetMenu(fileName = "Powerup", menuName = "Match3/Roguelike/Powerup")]
public class PowerupDefinition : ScriptableObject
{
    [Header("Presentation")]
    public string title;
    [TextArea] public string description;
    public Sprite icon;
    [Tooltip("Weight in the random offer pool - higher shows up more often. Doesn't affect strength.")]
    [Min(0f)] public float weight = 1f;

    [Header("Effect - leave any at 0/default to skip it")]
    [Tooltip("Added to PlayerRunStats.RandomSpecialChanceBonus (boosts the gravity-bonus and grace-period special chance).")]
    public float randomSpecialChanceBonus;
    [Tooltip("Added to PlayerRunStats.LockChanceReduction (reduces lock/freeze spawn chance).")]
    public float lockChanceReduction;
    [Tooltip("Added to PlayerRunStats.ScoreMultiplier (e.g. 0.1 = +10% score for the rest of the run).")]
    public float scoreMultiplierBonus;
    [Tooltip("Added to PlayerRunStats.BonusGraceMoves.")]
    public int bonusGraceMoves;
    [Tooltip("Raises PlayerHealth's max HP for the rest of the run.")]
    public int bonusMaxHealth;
    [Tooltip("Immediate heal on pick, independent of bonusMaxHealth.")]
    public int healAmount;

    [Header("Per-Color Effect - leave empty to skip. Add one entry per color you want to buff.")]
    [Tooltip("Each entry targets one SymbolType. All three fields on an entry stack additively " +
             "with any other powerup that targets the same color (including copies of this one).")]
    public ColorEffect[] colorEffects;

    [System.Serializable]
    public class ColorEffect
    {
        public SymbolType color;
        [Tooltip("Added to this color's own score multiplier bonus (e.g. 0.5 = +50% score for " +
                 "matches of this color only). Applied per cleared cell, on top of the flat " +
                 "10-per-chain-step base score, before the global Score Multiplier Bonus above.")]
        public float scoreMultiplierBonus;
        [Tooltip("Flat bonus points added per cell of this color cleared, in addition to the multiplier above.")]
        public int flatScoreBonusPerCell;
        [Tooltip("Added to this color's chance (0-1) to heal on match - baseline is 0, so this " +
                 "powerup is what gives you any chance at all, and stacks with copies/other " +
                 "powerups targeting the same color. Rolled once per matched group, not per cell.")]
        [Range(0f, 1f)] public float healChanceBonus;
        [Tooltip("HP restored if the chance above rolls a hit. Only matters if healChanceBonus is > 0 for this color (from this or another powerup).")]
        public int healAmountOnMatch;
    }

    /// <summary>Applies every non-zero effect field to the given run stats / health components.</summary>
    public void Apply(PlayerRunStats stats, PlayerHealth health)
    {
        if (stats != null)
        {
            stats.AddRandomSpecialChanceBonus(randomSpecialChanceBonus);
            stats.AddLockChanceReduction(lockChanceReduction);
            stats.AddScoreMultiplier(scoreMultiplierBonus);
            stats.AddBonusGraceMoves(bonusGraceMoves);

            if (colorEffects != null)
                foreach (var ce in colorEffects)
                    stats.AddColorEffect(ce.color, ce.scoreMultiplierBonus, ce.flatScoreBonusPerCell, ce.healChanceBonus, ce.healAmountOnMatch);
        }

        if (health != null)
        {
            if (bonusMaxHealth != 0) health.IncreaseMaxHealth(bonusMaxHealth);
            if (healAmount != 0) health.Heal(healAmount);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// Catches the "entered 9 meaning 9%" mistake at edit time - scoreMultiplierBonus (and its
    /// per-color equivalent) are raw fractions (0.1 = +10%), not whole percentages. Anything with
    /// magnitude > 1 would more than double the multiplier from a single powerup, which is almost
    /// always a typo rather than an intentional design choice.
    /// </summary>
    private void OnValidate()
    {
        if (Mathf.Abs(scoreMultiplierBonus) > 1f)
            Debug.LogWarning($"[PowerupDefinition] '{name}': scoreMultiplierBonus is {scoreMultiplierBonus}. " +
                              $"This field is a fraction, not a percentage - 0.1 means +10%, so {scoreMultiplierBonus} " +
                              $"means {(scoreMultiplierBonus >= 0 ? "+" : "")}{scoreMultiplierBonus * 100f:0}%. " +
                              $"Did you mean {scoreMultiplierBonus / 100f}?", this);

        if (colorEffects == null) return;
        foreach (var ce in colorEffects)
        {
            if (ce != null && Mathf.Abs(ce.scoreMultiplierBonus) > 1f)
                Debug.LogWarning($"[PowerupDefinition] '{name}': colorEffects entry for {ce.color} has " +
                                  $"scoreMultiplierBonus {ce.scoreMultiplierBonus} - likely meant {ce.scoreMultiplierBonus / 100f} " +
                                  "(this field is a fraction, e.g. 0.1 = +10%, not a whole percentage).", this);
        }
    }
#endif
}
