using UnityEngine;

/// <summary>
/// EventBus events for the Chain Lightning powerup (see PlayerRunStats.ChainLightningChance /
/// ChainLightningHitCount and MatchResolver.TryTriggerChainLightning). There's no dedicated
/// art/shader for this yet - ChainLightningVisualizer listens for these and draws a placeholder
/// LineRenderer bolt, so swapping in real VFX later is just replacing that one MonoBehaviour;
/// nothing in MatchResolver needs to change.
/// </summary>
public readonly struct ChainLightningStartedEvent
{
    /// <summary>Grid cell the arc originates from (the triggering match's seed cell).</summary>
    public readonly Vector2Int Origin;
    /// <summary>How many hops this proc will attempt (it may land fewer if the board runs out of
    /// candidate tiles partway through).</summary>
    public readonly int PlannedHitCount;

    public ChainLightningStartedEvent(Vector2Int origin, int plannedHitCount)
    {
        Origin = origin;
        PlannedHitCount = plannedHitCount;
    }
}

/// <summary>
/// Fired once per hop, in order. FromWorld/ToWorld are already-converted world positions (via
/// Board.GridToWorld) so listeners don't need their own grid-to-world math or a Board reference.
/// </summary>
public readonly struct ChainLightningArcEvent
{
    public readonly Vector3 FromWorld;
    public readonly Vector3 ToWorld;
    /// <summary>0-based index of this hop within its proc.</summary>
    public readonly int HopIndex;
    /// <summary>Total hops this proc is attempting - lets a listener fade/intensify the bolt
    /// differently for e.g. the final hop.</summary>
    public readonly int TotalPlannedHits;

    public ChainLightningArcEvent(Vector3 fromWorld, Vector3 toWorld, int hopIndex, int totalPlannedHits)
    {
        FromWorld = fromWorld;
        ToWorld = toWorld;
        HopIndex = hopIndex;
        TotalPlannedHits = totalPlannedHits;
    }
}
