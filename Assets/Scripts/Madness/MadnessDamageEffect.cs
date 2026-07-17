using UnityEngine;

/// <summary>
/// Deals damage to the player. Use in onClearedEffects (punish clearing it) or onSpawnedEffects
/// (punish it existing at all) - damagePerMoveSurvived only matters when used in
/// onClearedEffects or onSurvivedMoveEffects, since MovesSurvived is always 0 on spawn.
/// </summary>
[CreateAssetMenu(fileName = "MadnessDamage", menuName = "Match3/Madness/Effects/Damage Player")]
public class MadnessDamageEffect : MadnessEffect
{
    [Min(0)] public int baseDamage = 1;
    [Tooltip("Extra damage per move this symbol survived unmatched before triggering. 0 = flat damage regardless of how long it sat on the board.")]
    [Min(0)] public int damagePerMoveSurvived = 0;

    public override void Execute(MadnessContext ctx)
    {
        int amount = baseDamage + damagePerMoveSurvived * ctx.MovesSurvived;
        if (amount <= 0) return;
        ctx.PlayerHealth?.TakeDamage(amount);
    }
}
