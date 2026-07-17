using UnityEngine;

/// <summary>
/// "Infects" a handful of other board symbols, repainting them to toColor in place (no clear, no
/// score, no damage - just a color change; combine with other effects on the same
/// MadnessSymbolDefinition for those). Works well in onSpawnedEffects (an immediate board
/// shakeup the moment it appears) or onClearedEffects (a parting shot when it's finally matched).
/// The Madness Symbol's own cell is never a conversion target. See Board.ConvertRandomSymbols for
/// the underlying implementation.
/// </summary>
[CreateAssetMenu(fileName = "MadnessColorConvert", menuName = "Match3/Madness/Effects/Color Convert")]
public class MadnessColorConvertEffect : MadnessEffect
{
    [Tooltip("If true, only symbols currently of fromColor are eligible for conversion. If false, symbols of any color can be picked.")]
    public bool restrictSourceColor = false;
    [Tooltip("Only used when restrictSourceColor is true.")]
    public SymbolType fromColor;
    public SymbolType toColor;
    [Tooltip("How many other symbols to convert. If fewer eligible cells exist, fewer are converted.")]
    [Min(0)] public int count = 3;

    public override void Execute(MadnessContext ctx)
    {
        if (count <= 0) return;
        SymbolType? source = restrictSourceColor ? fromColor : (SymbolType?)null;
        ctx.Board?.ConvertRandomSymbols(source, toColor, count, ctx.Position);
    }
}
