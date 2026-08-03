using UnityEngine;
using TMPro;
using DG.Tweening;

/// <summary>
/// Self-destroying floating text popup: pops in with a scale overshoot, then drifts upward
/// while fading out. Attach to a prefab that has a TMP_Text (world-space TextMeshPro, not
/// TextMeshProUGUI) on it, or let ComboTextRelay create one at runtime via CreateRuntime.
/// </summary>
public class ComboPopupText : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("Must match (or render above, in the Sorting Layers list) whatever Sorting Layer " +
             "your Symbol sprites use - Order in Layer only resolves ties WITHIN the same Sorting " +
             "Layer, so this text can have a huge Order in Layer and still render behind the board " +
             "if it's sitting on a different Sorting Layer entirely.")]
    [SerializeField] private string sortingLayerName = "Symbols";
    [SerializeField] private int sortingOrder = 10;

    /// <summary>Builds a bare-bones runtime popup (world-space TextMeshPro) when no prefab is assigned.</summary>
    public static ComboPopupText CreateRuntime(Vector3 worldPosition)
    {
        var go = new GameObject("ComboPopupText (Runtime)");
        go.transform.position = worldPosition;

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 5f;
        tmp.text = string.Empty;

        var popup = go.AddComponent<ComboPopupText>();
        popup.label = tmp;
        popup.ApplySortingSettings();
        return popup;
    }

    private void Awake() => ApplySortingSettings();

    /// <summary>Explicitly sets BOTH sorting layer and order on the underlying Renderer - setting
    /// sortingOrder alone (as this used to do) has no effect if the layer itself is wrong.</summary>
    private void ApplySortingSettings()
    {
        var renderer = (label as Component)?.GetComponent<Renderer>();
        if (renderer == null) return;

        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;
    }

    /// <summary>
    /// Plays the pop-in / rise / fade animation, then destroys the GameObject on completion.
    /// riseDistance/lifetime/punchScale let the caller scale the effect with combo size.
    /// </summary>
    public void Play(string text, Color color, float fontSize, float riseDistance, float lifetime, float punchScale)
    {
        if (label == null) label = GetComponentInChildren<TMP_Text>();
        if (label == null)
        {
            Debug.LogWarning("[ComboPopupText] No TMP_Text found - destroying without playing.");
            Destroy(gameObject);
            return;
        }

        label.text = text;
        label.fontSize = fontSize;

        var startColor = color;
        startColor.a = 0f;
        label.color = startColor;

        float startY = transform.position.y;
        transform.localScale = Vector3.one * 0.5f;

        Sequence seq = DOTween.Sequence();

        // Pop in: quick overshoot scale, fading in at the same time.
        seq.Append(transform.DOScale(1f + punchScale, lifetime * 0.2f).SetEase(Ease.OutBack));
        seq.Join(label.DOFade(color.a, lifetime * 0.15f));

        // Settle back to normal scale.
        seq.Append(transform.DOScale(1f, lifetime * 0.15f).SetEase(Ease.OutQuad));

        // For the remainder of the lifetime: rise upward and fade out near the end.
        seq.Join(transform.DOMoveY(startY + riseDistance, lifetime * 0.85f).SetEase(Ease.OutCubic));
        seq.Join(label.DOFade(0f, lifetime * 0.6f).SetDelay(lifetime * 0.25f));

        seq.OnComplete(() => Destroy(gameObject));
    }
}
