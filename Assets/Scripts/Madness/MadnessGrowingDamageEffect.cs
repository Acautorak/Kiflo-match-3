using UnityEngine;

/// <summary>
/// Threat that grows the longer this symbol survives unmatched. Intended for
/// onSurvivedMoveEffects. Supports two independent patterns you can mix: a small per-move drip,
/// and/or a one-time detonation once it's survived long enough - after which its survival counter
/// resets, so it can detonate again if left alone even longer.
/// </summary>
[CreateAssetMenu(fileName = "MadnessGrowingDamage", menuName = "Match3/Madness/Effects/Growing Damage")]
public class MadnessGrowingDamageEffect : MadnessEffect
{
    [Tooltip("Damage dealt every move this symbol survives unmatched. 0 = no drip, detonation only.")]
    [Min(0)] public int damagePerMoveSurvived = 0;
    [Tooltip("If > 0, once MovesSurvived reaches this the detonation below fires once and the counter resets. 0 = no detonation, drip only.")]
    [Min(0)] public int detonateAfterMoves = 0;
    [Min(0)] public int detonateDamage = 0;

    public override void Execute(MadnessContext ctx)
    {
        if (damagePerMoveSurvived > 0)
            ctx.PlayerHealth?.TakeDamage(damagePerMoveSurvived);

        if (detonateAfterMoves > 0 && ctx.MovesSurvived >= detonateAfterMoves)
        {
            if (detonateDamage > 0) ctx.PlayerHealth?.TakeDamage(detonateDamage);
            ctx.Symbol?.ResetMadnessSurvival();
        }
    }
}
