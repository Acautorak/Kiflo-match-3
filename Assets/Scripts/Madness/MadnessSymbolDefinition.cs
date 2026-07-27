using UnityEngine;

/// <summary>
/// Designer-facing asset: one kind of Madness Symbol. Create via Assets > Create > Match3 >
/// Madness > Madness Symbol. Compose its behavior entirely from MadnessEffect assets - leave any
/// list empty to skip that trigger. A single definition can combine several effects at once (e.g.
/// deal damage AND ignite a color, both firing when it's cleared).
/// </summary>
[CreateAssetMenu(fileName = "MadnessSymbol", menuName = "Match3/Madness/Madness Symbol")]
public class MadnessSymbolDefinition : ScriptableObject
{
    [Header("Presentation")]
    public string title;
    [TextArea] public string description;
    [Tooltip("Shown on the symbol's Madness overlay, if Symbol.madnessOverlayRenderer is wired up in the prefab.")]
    public Sprite icon;

    [Header("Immunity (optional)")]
    [Tooltip("If not None, this symbol can't be destroyed by a match for a while after spawning - " +
             "it still gets swept into match groups normally (other symbols in the group still " +
             "clear/score), it just shrugs off the hit itself instead of being destroyed, and its " +
             "onClearedEffects don't fire until immunity runs out. Moves: counts down every " +
             "accepted player move, matched or not. Matches: only counts down each time it's " +
             "actually caught in a match - surviving untouched doesn't spend it.")]
    public MadnessImmunityMode immunityMode = MadnessImmunityMode.None;
    [Tooltip("How many moves (Moves mode) or match-hits (Matches mode) of immunity this symbol spawns with.")]
    [Min(0)] public int immunityAmount = 0;

    [Header("Triggers - leave any empty to skip it")]
    [Tooltip("Fires once, immediately, the moment this symbol spawns onto the board.")]
    public MadnessEffect[] onSpawnedEffects;
    [Tooltip("Fires once per accepted player move this symbol survives WITHOUT being cleared - " +
             "this is what growing threat/value effects (Score Growth, Growing Damage) hook into.")]
    public MadnessEffect[] onSurvivedMoveEffects;
    [Tooltip("Fires the moment this symbol is cleared - matched normally, or caught in a special/chain.")]
    public MadnessEffect[] onClearedEffects;
}
