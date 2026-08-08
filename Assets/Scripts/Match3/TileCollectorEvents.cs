using UnityEngine;

/// <summary>
/// EventBus events specific to the Tile Collector feature mode - kept in their own file, same
/// convention as LuckyScratchTicketEvents.cs, rather than editing FeatureModeEvents.cs directly.
/// </summary>

/// <summary>Fired the instant a single tile is tapped and collected - VFX/SFX can hook this the
/// same way LuckyScratchPanelRevealedEvent is used.</summary>
public readonly struct TileCollectedEvent
{
    public readonly SymbolType Type;
    public readonly Vector2Int Position;

    public TileCollectedEvent(SymbolType type, Vector2Int position)
    {
        Type = type;
        Position = position;
    }
}

/// <summary>Fired every frame while Tile Collector is running, for a timer/tiles-collected HUD -
/// mirrors KebabKarnageProgressEvent's role for its own timer.</summary>
public readonly struct TileCollectorProgressEvent
{
    public readonly float Elapsed;
    public readonly float Duration;
    public readonly int TilesCollected;

    public TileCollectorProgressEvent(float elapsed, float duration, int tilesCollected)
    {
        Elapsed = elapsed;
        Duration = duration;
        TilesCollected = tilesCollected;
    }
}
