using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class SpecialSymbolUnityEvent : UnityEvent<Vector2Int, Vector2Int[]> { }

/// <summary>
/// Single scene-wide effects manager: bridges EventBus events into either open, inspector-
/// wireable UnityEvents (for special symbol matches - VFX/SFX/camera shake, no code needed)
/// or directly-handled built-in effects (combo popup text on ChainMatchedEvent). Drop this on
/// one GameObject in the scene (e.g. an "Effects" manager) and wire the UnityEvents in the
/// Inspector as needed.
/// </summary>
public class SpecialSymbolEventRelay : MonoBehaviour
{
    [Header("Special symbol events - wire VFX/SFX/UI here in the Inspector")]
    public SpecialSymbolUnityEvent OnRowClear;
    public SpecialSymbolUnityEvent OnColumnClear;
    public SpecialSymbolUnityEvent OnBomb;
    public SpecialSymbolUnityEvent OnColorClear;
    public SpecialSymbolUnityEvent OnAnySpecialMatch;

    [Header("Combo Popup - Prefab")]
    [Tooltip("Must have a ComboPopupText component. Leave empty to auto-create a basic world-space TextMeshPro popup at runtime.")]
    [SerializeField] private ComboPopupText comboPopupPrefab;

    [Header("Combo Popup - Trigger")]
    [Tooltip("ChainCount is 1 on the very first match of a cascade - not really a 'combo' yet. Popups start showing at this value.")]
    [Min(1)]
    [SerializeField] private int minChainCountToShow = 2;

    [Header("Combo Popup - Text")]
    [SerializeField] private string comboFormat = "COMBO x{0}!";
    [Tooltip("ChainCount at/above which the popup is at max size/color instead of interpolating.")]
    [Min(2)]
    [SerializeField] private int chainCountForMaxEmphasis = 6;
    [SerializeField] private float comboBaseFontSize = 5f;
    [SerializeField] private float comboMaxFontSize = 12f;
    [SerializeField] private Color comboBaseColor = Color.white;
    [SerializeField] private Color comboMaxColor = new Color(1f, 0.55f, 0f); // orange

    [Header("Combo Popup - Motion")]
    [SerializeField] private float comboRiseDistance = 1.5f;
    [SerializeField] private float comboLifetime = 0.9f;
    [SerializeField] private float comboPunchScale = 0.3f;
    [Tooltip("World-space offset applied above the average matched-cell position.")]
    [SerializeField] private Vector3 comboSpawnOffset = new Vector3(0f, 0.25f, 0f);

    [Header("Combo Slow-Mo")]
    [Tooltip("Every Nth chain step gets a brief slow-mo beat (e.g. 3 = triggers on chain x3, x6, x9...). 0 disables this entirely.")]
    [Min(0)]
    [SerializeField] private int comboSlowMoMultiple = 3;
    [Tooltip("Time.timeScale during the slow-mo beat, via TimeController - the usual 'most restrictive wins' rule applies if a popup is also active.")]
    [Range(0f, 1f)]
    [SerializeField] private float comboSlowMoTimeScale = 0.3f;
    [Tooltip("How long the slow-mo beat lasts, in REAL (unscaled) seconds - so it always feels the same length regardless of how slow comboSlowMoTimeScale itself makes everything else run.")]
    [SerializeField] private float comboSlowMoDuration = 0.35f;

    private void OnEnable()
    {
        EventBus.Subscribe<SpecialSymbolMatchedEvent>(HandleSpecialMatched);
        EventBus.Subscribe<ChainMatchedEvent>(HandleChainMatched);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<SpecialSymbolMatchedEvent>(HandleSpecialMatched);
        EventBus.Unsubscribe<ChainMatchedEvent>(HandleChainMatched);
    }

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

    private void HandleChainMatched(ChainMatchedEvent evt)
    {
        TryPlayComboSlowMo(evt);

        if (evt.ChainCount < minChainCountToShow) return;
        if (evt.Positions == null || evt.Positions.Length == 0) return;
        if (Board.Instance == null) return;

        Vector3 sum = Vector3.zero;
        foreach (var p in evt.Positions) sum += Board.Instance.GridToWorldPosition(p.x, p.y);
        Vector3 worldPos = sum / evt.Positions.Length + comboSpawnOffset;

        float t = Mathf.InverseLerp(minChainCountToShow, chainCountForMaxEmphasis, evt.ChainCount);
        float fontSize = Mathf.Lerp(comboBaseFontSize, comboMaxFontSize, t);
        Color color = Color.Lerp(comboBaseColor, comboMaxColor, t);
        string text = string.Format(comboFormat, evt.ChainCount);

        ComboPopupText popup = comboPopupPrefab != null
            ? Instantiate(comboPopupPrefab, worldPos, Quaternion.identity)
            : ComboPopupText.CreateRuntime(worldPos);

        popup.Play(text, color, fontSize, comboRiseDistance, comboLifetime, comboPunchScale);
    }

    /// <summary>Independent of the popup's minChainCountToShow - a slow-mo beat can fire on
    /// chain x3 even if the popup itself only starts showing at x4, since they're unrelated
    /// thresholds tuned separately.</summary>
    private void TryPlayComboSlowMo(ChainMatchedEvent evt)
    {
        if (comboSlowMoMultiple <= 0) return;
        if (evt.ChainCount <= 0 || evt.ChainCount % comboSlowMoMultiple != 0) return;

        StartCoroutine(PlayComboSlowMo());
    }

    private System.Collections.IEnumerator PlayComboSlowMo()
    {
        int handle = TimeController.Push(comboSlowMoTimeScale);
        yield return new WaitForSecondsRealtime(comboSlowMoDuration);
        TimeController.Pop(handle);
    }
}
