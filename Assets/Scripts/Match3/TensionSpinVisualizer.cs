using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placeholder Tension Spin visual - no particle/shader art dependency, same philosophy as
/// ChainLightningVisualizer. Draws a translucent, gently pulsing strip along the spinning
/// row/column using a 1x1 runtime-generated Texture2D, so it works with zero asset setup.
///
/// Drop this on any always-loaded object (e.g. next to Board, alongside ChainLightningVisualizer)
/// and it just works: it listens for TensionSpinStartedEvent/TensionSpinLandedEvent over EventBus.
/// Nothing else in the codebase references this class - when real particle VFX are ready, delete
/// or disable this component and hook the same events from the new system instead.
/// </summary>
public class TensionSpinVisualizer : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private Color stripColor = new Color(1f, 0.85f, 0.3f, 0.35f);
    [Tooltip("How wide (world units) the strip is across the row/column - should roughly match one cell's size.")]
    [Min(0.01f)] [SerializeField] private float stripThickness = 1f;
    [Tooltip("How much the strip's alpha pulses up and down while active, as a fraction of stripColor's own alpha.")]
    [Range(0f, 1f)] [SerializeField] private float pulseAmount = 0.5f;
    [Tooltip("How many pulses per second while active.")]
    [Min(0.01f)] [SerializeField] private float pulseSpeed = 4f;
    [Tooltip("Fallback: if a matching TensionSpinLandedEvent never arrives for some reason, the " +
             "strip is force-destroyed this long after it started, so a stuck highlight can't " +
             "linger forever.")]
    [Min(0.1f)] [SerializeField] private float maxLifetimeSafety = 5f;

    [Header("Sorting")]
    [Tooltip("Sorting layer the strip renders on. Set this to a layer BELOW your symbol sprites " +
             "so the strip reads as a background glow behind the spinning symbols, not on top of them.")]
    [SortingLayer] [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = -1;

    private Sprite whiteSprite;
    private readonly Dictionary<(bool isColumn, int index), GameObject> activeStrips = new Dictionary<(bool, int), GameObject>();

    private void Awake()
    {
        var texture = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<TensionSpinStartedEvent>(OnStarted);
        EventBus.Subscribe<TensionSpinLandedEvent>(OnLanded);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<TensionSpinStartedEvent>(OnStarted);
        EventBus.Unsubscribe<TensionSpinLandedEvent>(OnLanded);
    }

    private void OnStarted(TensionSpinStartedEvent evt)
    {
        var key = (evt.IsColumn, evt.Index);

        // Guard against a second proc landing on the exact same row/column while the first is
        // still spinning (two groups in one cascade step both picking this line) - just restart
        // the strip's lifetime rather than stacking a second overlapping one.
        if (activeStrips.TryGetValue(key, out var existing) && existing != null)
            Destroy(existing);

        var go = new GameObject($"TensionSpinStrip (placeholder) {(evt.IsColumn ? "col" : "row")} {evt.Index}");
        go.transform.SetParent(transform, worldPositionStays: true);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = whiteSprite;
        sr.color = stripColor;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;
        sr.drawMode = SpriteDrawMode.Simple;

        var midpoint = Vector3.Lerp(evt.FromWorld, evt.ToWorld, 0.5f);
        go.transform.position = midpoint;

        float length = Vector3.Distance(evt.FromWorld, evt.ToWorld) + stripThickness; // pad by one cell so the strip covers the end cells fully, not just their centers
        go.transform.localScale = evt.IsColumn
            ? new Vector3(stripThickness, length, 1f)
            : new Vector3(length, stripThickness, 1f);

        activeStrips[key] = go;
        StartCoroutine(PulseAndSafetyDestroy(go, key, Mathf.Max(evt.Duration, maxLifetimeSafety)));
    }

    private void OnLanded(TensionSpinLandedEvent evt)
    {
        var key = (evt.IsColumn, evt.Index);
        if (activeStrips.TryGetValue(key, out var go) && go != null) Destroy(go);
        activeStrips.Remove(key);
    }

    private IEnumerator PulseAndSafetyDestroy(GameObject go, (bool, int) key, float safetyLifetime)
    {
        var sr = go.GetComponent<SpriteRenderer>();
        float baseAlpha = stripColor.a;
        float elapsed = 0f;

        while (go != null && elapsed < safetyLifetime)
        {
            float pulse = 1f + pulseAmount * Mathf.Sin(elapsed * pulseSpeed * Mathf.PI * 2f);
            var c = sr.color;
            c.a = Mathf.Clamp01(baseAlpha * pulse);
            sr.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Safety net only - OnLanded is the normal removal path and will already have destroyed
        // this and removed the dictionary entry if it fired in time.
        if (go != null) Destroy(go);
        if (activeStrips.TryGetValue(key, out var current) && current == go) activeStrips.Remove(key);
    }
}
