using UnityEngine;

/// <summary>
/// "Lights a color on fire" - for durationMoves, matching targetColor grants bonus score and/or
/// deals damage to the player, on top of whatever it'd normally do. Expires on its own; see
/// MadnessBoardModifiers for how the duration ticks down and how the bonuses fold into scoring.
/// This is the general pattern for "change the rules for a while" - a new temporary-modifier
/// effect that isn't color-scoped would follow the same shape: add a field to
/// MadnessBoardModifiers, then a new MadnessEffect subclass to set it.
/// </summary>
[CreateAssetMenu(fileName = "MadnessIgniteColor", menuName = "Match3/Madness/Effects/Ignite Color")]
public class MadnessIgniteColorEffect : MadnessEffect
{
    public SymbolType targetColor;
    [Min(1)] public int durationMoves = 5;
    [Tooltip("Fraction added to score from matching targetColor while ignited (0.5 = +50%). Can be negative to penalize matching it instead.")]
    public float scoreMultiplierBonus = 0.5f;
    [Tooltip("Damage dealt to the player each time targetColor is matched while ignited. 0 = purely a score effect.")]
    [Min(0)] public int damagePerMatch = 1;

    public override void Execute(MadnessContext ctx)
    {
        ctx.BoardModifiers?.ApplyColorModifier(targetColor, durationMoves, scoreMultiplierBonus, damagePerMatch);
    }
}
