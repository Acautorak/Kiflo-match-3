using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class SpecialSymbolUnityEvent : UnityEvent<Vector2Int, Vector2Int[]> { }

/// <summary>
/// Bridges EventBus's SpecialSymbolMatchedEvent into open, inspector-wireable UnityEvents,
/// so designers can hook VFX, SFX, camera shake, UI popups, etc. without writing code.
/// Drop this on any GameObject in the scene (e.g. an "Effects" manager) and wire the
/// events in the Inspector.
/// </summary>
public class SpecialSymbolEventRelay : MonoBehaviour
{
    [Header("Open events - wire VFX/SFX/UI here in the Inspector")]
    public SpecialSymbolUnityEvent OnRowClear;
    public SpecialSymbolUnityEvent OnColumnClear;
    public SpecialSymbolUnityEvent OnBomb;
    public SpecialSymbolUnityEvent OnColorClear;
    public SpecialSymbolUnityEvent OnAnySpecialMatch;

    private void OnEnable() => EventBus.Subscribe<SpecialSymbolMatchedEvent>(HandleSpecialMatched);
    private void OnDisable() => EventBus.Unsubscribe<SpecialSymbolMatchedEvent>(HandleSpecialMatched);

    private void HandleSpecialMatched(SpecialSymbolMatchedEvent evt)
    {
        OnAnySpecialMatch?.Invoke(evt.Position, evt.AffectedCells);

        switch (evt.Special)
        {
            case SpecialType.RowClear: OnRowClear?.Invoke(evt.Position, evt.AffectedCells); break;
            case SpecialType.ColumnClear: OnColumnClear?.Invoke(evt.Position, evt.AffectedCells); break;
            case SpecialType.Bomb: OnBomb?.Invoke(evt.Position, evt.AffectedCells); break;
            case SpecialType.ColorClear: OnColorClear?.Invoke(evt.Position, evt.AffectedCells); break;
        }
    }
}
