using UnityEngine;

/// <summary>
/// Scrambles a handful of other board symbols, each rolling its own independently random color
/// (as opposed to MadnessColorConvertEffect, which pushes every affected symbol to the same
/// target color). Good for a chaotic "madness spreads unpredictably" moment. Works in
/// onSpawnedEffects, onSurvivedMoveEffects, or onClearedEffects like any other Madness effect.
/// The Madness Symbol's own cell is never a target. See Board.RandomizeSymbolColors for the
/// underlying implementation.
/// </summary>
[CreateAssetMenu(fileName = "MadnessRandomizeColors", menuName = "Match3/Madness/Effects/Randomize Colors")]
public class MadnessRandomizeColorsEffect : MadnessEffect
{
    [Tooltip("How many other symbols to randomize. If fewer eligible cells exist, fewer are randomized.")]
    [Min(0)] public int count = 3;
    [Tooltip("If true, a symbol is guaranteed to end up a different color than it started as (no visible no-op swaps). If false, it's possible - though not guaranteed - for a symbol to randomly land back on its own color.")]
    public bool guaranteeColorChange = true;

    public override void Execute(MadnessContext ctx)
    {
        if (count <= 0) return;
        ctx.Board?.RandomizeSymbolColors(count, ctx.Position, guaranteeColorChange);
    }
}
