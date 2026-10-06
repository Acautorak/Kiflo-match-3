using UnityEngine;

/// <summary>
/// EventBus events for the Magnet Pulse powerup (see PlayerRunStats.MagnetPulseChance and
/// MatchResolver.TriggerMagnetPulse). Unlike Chain Lightning/Tension Spin, Magnet Pulse doesn't
/// need a placeholder visualizer - the real Symbol instances physically fly to their new
/// positions, so the existing sprites already carry the visual. These events exist purely as an
/// OPTIONAL hook for extra flourish later (a trail particle following the flight path, a small
/// pulse/glow on the growing cluster, etc.) - nothing currently subscribes to them, and the
/// feature works correctly with zero listeners.
/// </summary>
public readonly struct MagnetPulseStartedEvent
{
    /// <summary>Color of the cluster this proc is reinforcing.</summary>
    public readonly SymbolType ClusterColor;
    /// <summary>Size of the cluster at the moment this proc started (before any stray tiles were pulled in).</summary>
    public readonly int ClusterSize;

    public MagnetPulseStartedEvent(SymbolType clusterColor, int clusterSize)
    {
        ClusterColor = clusterColor;
        ClusterSize = clusterSize;
    }
}

/// <summary>Fired once per stray tile pulled in, right as its flight tween starts.</summary>
public readonly struct MagnetPulseFlightEvent
{
    public readonly Vector3 FromWorld;
    public readonly Vector3 ToWorld;
    /// <summary>Seconds the real tile's flight tween takes (MatchResolver.MagnetPulseFlightDuration
    /// at the moment this fired), so a listener can time a trail/pop to land exactly when the tile does.</summary>
    public readonly float Duration;

    public MagnetPulseFlightEvent(Vector3 fromWorld, Vector3 toWorld, float duration)
    {
        FromWorld = fromWorld;
        ToWorld = toWorld;
        Duration = duration;
    }
}
