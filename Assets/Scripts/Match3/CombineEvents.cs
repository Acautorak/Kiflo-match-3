using UnityEngine;

/// <summary>
/// Fired by MatchResolver.TryResolveCombine when a matched group successfully combines into one
/// survivor instead of clearing independently. SurvivorPosition is where the popup/VFX should
/// anchor; CellsCombined is the group's total cell count (survivor + everything merged into it),
/// for scaling the popup's text/emphasis the same way ChainMatchedEvent.ChainCount does for combos.
/// </summary>
public readonly struct CombineTriggeredEvent
{
    public readonly Vector2Int SurvivorPosition;
    public readonly int CellsCombined;

    public CombineTriggeredEvent(Vector2Int survivorPosition, int cellsCombined)
    {
        SurvivorPosition = survivorPosition;
        CellsCombined = cellsCombined;
    }
}
