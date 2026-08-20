using UnityEngine;

/// <summary>
/// "The whole board catches fire" - for durationMoves, matching ANY color grants bonus score
/// and/or deals damage to the player, on top of whatever it'd normally do (see
/// MadnessBoardModifiers for how the duration ticks down and how the bonuses fold into scoring -
/// this now applies the same modifier to every SymbolType, not just one). The instant this fires
/// (onClearedEffects), every eligible tile around the symbol's position also ignites and starts
/// burning down (see BurningSystem.IgniteAllNeighbors/Board.IgniteAllNeighbors) - a literal blaze
/// to go with the figurative one.
///
/// Was previously single-color/no-ignite (targetColor + the score/damage modifier only) - reworked
/// into a genuine board-wide "everything's on fire" payoff for clearing this symbol.
/// </summary>
[CreateAssetMenu(fileName = "MadnessIgniteColor", menuName = "Match3/Madness/Effects/Ignite Color")]
public class MadnessIgniteColorEffect : MadnessEffect
{
    [Min(1)] public int durationMoves = 5;
    [Tooltip("Fraction added to score from matching ANY color while ignited (0.5 = +50%). Can be negative to penalize matching instead.")]
    public float scoreMultiplierBonus = 0.5f;
    [Tooltip("Damage dealt to the player each time ANY color is matched while ignited. 0 = purely a score effect.")]
    [Min(0)] public int damagePerMatch = 1;

    public override void Execute(MadnessContext ctx)
    {
        if (ctx.BoardModifiers != null)
        {
            foreach (SymbolType color in System.Enum.GetValues(typeof(SymbolType)))
                ctx.BoardModifiers.ApplyColorModifier(color, durationMoves, scoreMultiplierBonus, damagePerMatch);
        }

        ctx.Board?.IgniteAllNeighbors(ctx.Position);
    }
}
