using UnityEngine;

/// <summary>What a scratched panel does. Score/Heal/Damage/BonusGraceMoves are one-off amounts;
/// ScoreMultiplier/RandomSpecialChanceBonus/LockChanceReduction/KebabTapDamageBonus fold into
/// PlayerRunStats' flat (non-color) modifiers; the Color* kinds fold into PlayerRunStats'
/// per-color modifiers via AddColorEffect, scoped to TargetColor below. All of these mirror what
/// a PowerupDefinition would do, since a ticket pull is just another run modifier source.</summary>
public enum ScratchRewardKind
{
    Score,
    Heal,
    Damage,
    BonusGraceMoves,
    ScoreMultiplier,
    RandomSpecialChanceBonus,
    LockChanceReduction,
    /// <summary>Permanent run-long bonus to Kebab Karnage tap damage - see PlayerRunStats.AddKebabTapDamageBonus.</summary>
    KebabTapDamageBonus,
    /// <summary>Amount = score multiplier bonus for TargetColor only (e.g. 0.25 = +25% on that color's matches).</summary>
    ColorScoreMultiplierBonus,
    /// <summary>Amount = flat score bonus per cell for TargetColor only (rounded to a whole number).</summary>
    ColorFlatScoreBonus,
    /// <summary>Amount = chance (0-1) to heal on a TargetColor match; SecondaryAmount = HP restored on a hit.</summary>
    ColorHealOnMatch,
    /// <summary>Instantly grants Amount progress toward a Collect goal target of TargetColor - see
    /// LuckyScratchTicketManager's TODO in Apply below; needs the actual Collect-goal tracker wired in.</summary>
    CollectProgress,
}

/// <summary>
/// One possible thing a scratch panel can turn out to be - designers author a pool of these (see
/// LuckyScratchRewardPoolConfig) and LuckyScratchTicketManager rolls one per panel. Deliberately
/// mirrors PowerupDefinition's shape (title/weight/Apply) so scratch tickets read as "the same
/// kind of thing" as powerups to anyone editing the project, just resolved immediately per panel
/// instead of picked once from a menu.
/// </summary>
[CreateAssetMenu(fileName = "New Scratch Reward", menuName = "Match3/Lucky Scratch Ticket/Reward Definition")]
public class ScratchRewardDefinition : ScriptableObject
{
    [Header("Display")]
    public string title = "Reward";
    public Sprite icon;
    [Tooltip("Shown once scratched off, e.g. \"+50\" or \"-1 HP\". Leave blank to auto-build from Kind + Amount.")]
    [SerializeField] private string displayTextOverride;

    [Header("Effect")]
    public ScratchRewardKind kind = ScratchRewardKind.Score;
    [Tooltip("Interpreted per Kind - Score/Heal/Damage/BonusGraceMoves/KebabTapDamageBonus/ColorFlatScoreBonus/" +
             "CollectProgress are whole numbers (rounded); ScoreMultiplier/RandomSpecialChanceBonus/" +
             "LockChanceReduction/ColorScoreMultiplierBonus are fractions (0.1 = 10%); for ColorHealOnMatch " +
             "this is the heal chance (0-1) - see SecondaryAmount for the HP restored on a hit.")]
    public float amount = 0f;
    [Tooltip("Only used by ColorHealOnMatch, as the whole-number HP restored when its chance (Amount) rolls a hit.")]
    public float secondaryAmount = 0f;
    [Tooltip("Only used by the Color*/CollectProgress kinds - which SymbolType this reward targets.")]
    public SymbolType targetColor;

    [Header("Odds")]
    [Tooltip("Relative weight used by the ticket's weighted pool roll - same WeightedPool.Pick pattern " +
             "PowerupManager uses for its offers. Higher = more common. Give Damage entries a lower " +
             "weight than the good outcomes unless you want a genuinely scary ticket.")]
    [Min(0f)]
    public float weight = 1f;

    /// <summary>True if this reward hurts the player - LuckyScratchTicketManager uses this to
    /// decide whether crossing 0 HP counts as this ticket's own loss condition.</summary>
    public bool IsHarmful => kind == ScratchRewardKind.Damage;

    public string BuildDisplayText()
    {
        if (!string.IsNullOrEmpty(displayTextOverride)) return displayTextOverride;

        switch (kind)
        {
            case ScratchRewardKind.Score: return $"+{Mathf.RoundToInt(amount)}";
            case ScratchRewardKind.Heal: return $"+{Mathf.RoundToInt(amount)} HP";
            case ScratchRewardKind.Damage: return $"-{Mathf.RoundToInt(amount)} HP";
            case ScratchRewardKind.BonusGraceMoves: return $"+{Mathf.RoundToInt(amount)} Moves";
            case ScratchRewardKind.ScoreMultiplier: return $"+{amount:P0} Score";
            case ScratchRewardKind.RandomSpecialChanceBonus: return $"+{amount:P0} Special Chance";
            case ScratchRewardKind.LockChanceReduction: return $"-{amount:P0} Lock Chance";
            case ScratchRewardKind.KebabTapDamageBonus: return $"+{Mathf.RoundToInt(amount)} Kebab Damage";
            case ScratchRewardKind.ColorScoreMultiplierBonus: return $"+{amount:P0} {targetColor} Score";
            case ScratchRewardKind.ColorFlatScoreBonus: return $"+{Mathf.RoundToInt(amount)} {targetColor} Score/Cell";
            case ScratchRewardKind.ColorHealOnMatch: return $"{amount:P0} Heal on {targetColor}";
            case ScratchRewardKind.CollectProgress: return $"+{Mathf.RoundToInt(amount)} {targetColor}";
            default: return title;
        }
    }

    /// <summary>
    /// Applies this reward the moment its panel is scratched off (see
    /// LuckyScratchTicketManager.HandlePanelRevealed) - reuses the exact same PlayerHealth/
    /// PlayerRunStats APIs a PowerupDefinition would, plus Board for the Score case since that's
    /// not something either of those own.
    /// </summary>
    public void Apply(Board board, PlayerHealth playerHealth, PlayerRunStats playerRunStats)
    {
        switch (kind)
        {
            case ScratchRewardKind.Score:
                board?.AddBonusScore(Mathf.RoundToInt(amount));
                break;
            case ScratchRewardKind.Heal:
                playerHealth?.Heal(Mathf.RoundToInt(amount));
                break;
            case ScratchRewardKind.Damage:
                playerHealth?.TakeDamage(Mathf.RoundToInt(amount));
                break;
            case ScratchRewardKind.BonusGraceMoves:
                playerRunStats?.AddBonusGraceMoves(Mathf.RoundToInt(amount));
                break;
            case ScratchRewardKind.ScoreMultiplier:
                playerRunStats?.AddScoreMultiplier(amount);
                break;
            case ScratchRewardKind.RandomSpecialChanceBonus:
                playerRunStats?.AddRandomSpecialChanceBonus(amount);
                break;
            case ScratchRewardKind.LockChanceReduction:
                playerRunStats?.AddLockChanceReduction(amount);
                break;
            case ScratchRewardKind.KebabTapDamageBonus:
                playerRunStats?.AddKebabTapDamageBonus(Mathf.RoundToInt(amount));
                break;
            case ScratchRewardKind.ColorScoreMultiplierBonus:
                playerRunStats?.AddColorEffect(targetColor, amount, 0, 0f, 0);
                break;
            case ScratchRewardKind.ColorFlatScoreBonus:
                playerRunStats?.AddColorEffect(targetColor, 0f, Mathf.RoundToInt(amount), 0f, 0);
                break;
            case ScratchRewardKind.ColorHealOnMatch:
                playerRunStats?.AddColorEffect(targetColor, 0f, 0, amount, Mathf.RoundToInt(secondaryAmount));
                break;
            case ScratchRewardKind.CollectProgress:
                // TODO: wire this to whatever actually tracks Collect-goal progress (likely
                // StageManager, which subscribes to SymbolMatchedEvent per StageDefinition's
                // comment) - rolls and displays correctly already, just doesn't grant progress
                // yet. Not calling this out with a LogWarning-per-scratch since a designer could
                // easily leave a CollectProgress entry in a live pool before this is wired.
                Debug.LogWarning($"[ScratchRewardDefinition] '{title}' rolled CollectProgress for " +
                                  $"{targetColor} x{Mathf.RoundToInt(amount)}, but nothing is wired to grant it yet - " +
                                  "see the TODO in ScratchRewardDefinition.Apply.");
                break;
        }
    }
}
