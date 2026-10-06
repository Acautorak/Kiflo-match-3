using UnityEngine;

/// <summary>
/// EventBus events for the Meteor Shower powerup (see PlayerRunStats.MeteorShowerChance and
/// MatchResolver.TriggerMeteorShower). No dedicated art/shader exists yet - MeteorShowerVisualizer
/// listens for these and draws a placeholder falling comet + impact burst, so swapping in real VFX
/// later is just replacing that one MonoBehaviour; nothing in MatchResolver needs to change.
/// </summary>
public readonly struct MeteorShowerStartedEvent
{
    /// <summary>World position the meteor starts its visible flight from (high above the board).</summary>
    public readonly Vector3 FromWorld;
    /// <summary>World position of the target 2x2 square's center - where the meteor is headed.</summary>
    public readonly Vector3 ToWorld;
    /// <summary>Seconds the flight takes, so a listener can time its fall to land exactly when
    /// MatchResolver actually resolves the impact.</summary>
    public readonly float FlightDuration;

    public MeteorShowerStartedEvent(Vector3 fromWorld, Vector3 toWorld, float flightDuration)
    {
        FromWorld = fromWorld;
        ToWorld = toWorld;
        FlightDuration = flightDuration;
    }
}

/// <summary>Fired the instant a meteor lands - impact VFX hooks in here. By this point the 4
/// struck cells haven't been cleared yet (that happens afterward through the normal ClearCell
/// pass), so this is purely the "something just slammed into the board" beat.</summary>
public readonly struct MeteorShowerImpactEvent
{
    public readonly Vector3 Position;

    public MeteorShowerImpactEvent(Vector3 position)
    {
        Position = position;
    }
}
