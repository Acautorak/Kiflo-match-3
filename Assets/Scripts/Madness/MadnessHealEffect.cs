using UnityEngine;

/// <summary>
/// Heals the player for a flat amount - PlayerHealth.Heal already clamps to MaxHealth and no-ops
/// at 0/full HP, so this effect needs no extra guarding of its own. Put this in a
/// MadnessSymbolDefinition's onClearedEffects to make "heals when matched" - the same trigger
/// point MadnessDamageEffect (if you have one) presumably uses for the opposite. Works in
/// onSpawnedEffects or onSurvivedMoveEffects too if you want a symbol that heals just for
/// appearing, or drip-heals every move it survives uncleared - same asset, just wired to a
/// different trigger list on the definition.
/// </summary>
[CreateAssetMenu(fileName = "MadnessHeal", menuName = "Match3/Madness/Effects/Heal")]
public class MadnessHealEffect : MadnessEffect
{
    [Min(0)] public int healAmount = 1;

    public override void Execute(MadnessContext ctx)
    {
        if (healAmount <= 0) return;
        ctx.PlayerHealth?.Heal(healAmount);
    }
}
