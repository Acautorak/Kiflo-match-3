using UnityEngine;

/// <summary>
/// EventBus events for the Tension Spin powerup (see PlayerRunStats.TensionSpinChance and
/// MatchResolver.TriggerTensionSpin). There's no dedicated particle art for this yet -
/// TensionSpinVisualizer listens for these and draws a placeholder highlight along the spinning
/// row/column, so swapping in real VFX later is just replacing that one MonoBehaviour; nothing in
/// MatchResolver needs to change.
/// </summary>
public readonly struct TensionSpinStartedEvent
{
    /// <summary>True if this proc is spinning a column (fixed x), false if it's spinning a row (fixed y).</summary>
    public readonly bool IsColumn;
    /// <summary>The column's x, or the row's y - whichever IsColumn selects.</summary>
    public readonly int Index;
    /// <summary>World position of the line's index-0 cell (where symbols exit).</summary>
    public readonly Vector3 FromWorld;
    /// <summary>World position of the line's far-end cell (where fresh symbols enter).</summary>
    public readonly Vector3 ToWorld;
    /// <summary>Total estimated seconds this proc will spin for, so a placeholder/real effect can
    /// time itself (e.g. fade in, hold, fade out) without needing its own duration guess.</summary>
    public readonly float Duration;

    public TensionSpinStartedEvent(bool isColumn, int index, Vector3 fromWorld, Vector3 toWorld, float duration)
    {
        IsColumn = isColumn;
        Index = index;
        FromWorld = fromWorld;
        ToWorld = toWorld;
        Duration = duration;
    }
}

/// <summary>Fired once this proc's line has fully landed. Carries the same IsColumn/Index as its
/// matching TensionSpinStartedEvent so a listener tracking multiple concurrent highlights (two
/// procs from the same cascade step) can tell which one just finished.</summary>
public readonly struct TensionSpinLandedEvent
{
    public readonly bool IsColumn;
    public readonly int Index;

    public TensionSpinLandedEvent(bool isColumn, int index)
    {
        IsColumn = isColumn;
        Index = index;
    }
}
