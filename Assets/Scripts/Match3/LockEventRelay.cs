using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class LockLayerRemovedUnityEvent : UnityEvent<Vector2Int, int, bool> { } // position, layersRemaining, triggeredByMatch

[System.Serializable]
public class LockPositionUnityEvent : UnityEvent<Vector2Int> { }

/// <summary>
/// Bridges EventBus's LockLayerRemovedEvent into open, inspector-wireable UnityEvents,
/// so designers can hook ice-crack/shatter/melt VFX and SFX without writing code.
/// Drop this on any GameObject in the scene (e.g. an "Effects" manager) and wire the
/// events in the Inspector.
/// </summary>
public class LockEventRelay : MonoBehaviour
{
    [Header("Open events - wire VFX/SFX/UI here in the Inspector")]
    [Tooltip("Fired every time a lock loses a layer, whether from a match hit or an automatic melt.")]
    public LockLayerRemovedUnityEvent OnLockLayerRemoved;
    [Tooltip("Fired only when a lock hit came from being caught in a match/special clear.")]
    public LockLayerRemovedUnityEvent OnLockHitByMatch;
    [Tooltip("Fired only when a Temporary lock melted a layer on its own from the move countdown.")]
    public LockLayerRemovedUnityEvent OnLockAutoMelted;
    [Tooltip("Fired when a lock's last layer is removed and the tile becomes free to play.")]
    public LockPositionUnityEvent OnFullyUnlocked;

    private void OnEnable() => EventBus.Subscribe<LockLayerRemovedEvent>(HandleLockLayerRemoved);
    private void OnDisable() => EventBus.Unsubscribe<LockLayerRemovedEvent>(HandleLockLayerRemoved);

    private void HandleLockLayerRemoved(LockLayerRemovedEvent evt)
    {
        OnLockLayerRemoved?.Invoke(evt.Position, evt.LayersRemaining, evt.TriggeredByMatch);

        if (evt.TriggeredByMatch) OnLockHitByMatch?.Invoke(evt.Position, evt.LayersRemaining, evt.TriggeredByMatch);
        else OnLockAutoMelted?.Invoke(evt.Position, evt.LayersRemaining, evt.TriggeredByMatch);

        if (evt.FullyUnlocked) OnFullyUnlocked?.Invoke(evt.Position);
    }
}
