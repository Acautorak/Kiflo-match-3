/// <summary>
/// EventBus events for "feature mode" mini-games (Kebab Karnage and future ones).
/// Kept generic via FeatureId so multiple feature modes can share one request/started/ended
/// contract instead of each needing its own trio of events.
/// </summary>
public readonly struct FeatureModeRequestedEvent
{
    /// <summary>Which feature mode to start, e.g. KebabKarnageManager.FeatureId. Publish this
    /// from wherever your madness-meter threshold logic lives instead of referencing the
    /// manager directly, if you'd rather keep that system decoupled.</summary>
    public readonly string FeatureId;

    /// <summary>How far the triggering meter overshot its max at the moment it filled (Current -
    /// Max, clamped to >= 0). Optional - defaults to 0. A feature mode that wants to scale itself
    /// off how "full" the fill was (e.g. FreeSpinsManager basing spin count on this) can read it;
    /// anything that doesn't care (e.g. KebabKarnageManager) just ignores it.</summary>
    public readonly float OverflowAmount;

    public FeatureModeRequestedEvent(string featureId, float overflowAmount = 0f)
    {
        FeatureId = featureId;
        OverflowAmount = overflowAmount;
    }
}

public readonly struct FeatureModeStartedEvent
{
    public readonly string FeatureId;
    public FeatureModeStartedEvent(string featureId) => FeatureId = featureId;
}

public readonly struct FeatureModeEndedEvent
{
    public readonly string FeatureId;
    public readonly bool Survived;
    public FeatureModeEndedEvent(string featureId, bool survived)
    {
        FeatureId = featureId;
        Survived = survived;
    }
}

/// <summary>Fired every frame while Kebab Karnage is running, for HUD timer/counter binding.</summary>
public readonly struct KebabKarnageProgressEvent
{
    public readonly float Elapsed;
    public readonly float Duration;
    public readonly int AsteroidsBroken;
    public KebabKarnageProgressEvent(float elapsed, float duration, int asteroidsBroken)
    {
        Elapsed = elapsed;
        Duration = duration;
        AsteroidsBroken = asteroidsBroken;
    }
}
