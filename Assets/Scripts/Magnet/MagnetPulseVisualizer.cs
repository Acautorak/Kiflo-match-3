using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placeholder Magnet Pulse visual - no art/shader dependency, same philosophy as
/// ChainLightningVisualizer/TensionSpinVisualizer. The pulled tile's own sprite already carries
/// most of the read (see MatchResolver.TriggerMagnetPulse - it's a real Symbol flying via MoveTo,
/// not an abstract effect), so this only adds a bit of extra juice on top: a fading streak tracing
/// the flight path, synced to the tile's actual flight duration, and a quick "snap" pop where it
/// lands.
///
/// Drop this on any always-loaded object (e.g. next to the other two placeholder visualizers) and
/// it just works - it listens for MagnetPulseFlightEvent over EventBus. Nothing else in the
/// codebase references this class - when real VFX are ready, delete or disable this component and
/// hook the same event from the new system instead.
/// </summary>
public class MagnetPulseVisualizer : MonoBehaviour
{
    [Header("Streak")]
    [SerializeField] private Color streakColor = new Color(0.85f, 0.4f, 1f, 0.8f);
    [Min(0f)] [SerializeField] private float streakWidth = 0.08f;
    [Tooltip("How many extra jittered midpoints the streak gets between its two endpoints. 0 = a straight line.")]
    [Min(0)] [SerializeField] private int jaggedSegments = 2;
    [Tooltip("Max random perpendicular offset applied to each jittered midpoint, in world units.")]
    [Min(0f)] [SerializeField] private float jaggedness = 0.1f;

    [Header("Landing Pop")]
    [SerializeField] private Color popColor = new Color(0.85f, 0.4f, 1f, 0.9f);
    [Min(0f)] [SerializeField] private float popMaxScale = 0.9f;
    [Min(0.01f)] [SerializeField] private float popDuration = 0.2f;

    [Header("Sorting")]
    [Tooltip("Sorting layer both the streak and the landing pop render on. Set this to a layer AT OR ABOVE your symbol sprites, since this is meant to read as a quick highlight on top of the flight, not a background glow.")]
    [SortingLayer] [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 50;

    private Sprite whiteSprite;

    private void Awake()
    {
        var texture = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
    }

    private void OnEnable() => EventBus.Subscribe<MagnetPulseFlightEvent>(OnFlight);
    private void OnDisable() => EventBus.Unsubscribe<MagnetPulseFlightEvent>(OnFlight);

    private void OnFlight(MagnetPulseFlightEvent evt)
    {
        float duration = Mathf.Max(0.05f, evt.Duration);
        StartCoroutine(PlayStreak(evt.FromWorld, evt.ToWorld, duration));
        StartCoroutine(PlayLandingPop(evt.ToWorld, duration));
    }

    /// <summary>A jittered line that fades out over the tile's own flight duration, tracing where
    /// it's headed - same jitter approach as ChainLightningVisualizer's bolt, just fading
    /// continuously instead of popping in/out at fixed lifetimes, since this needs to track a
    /// specific tile's actual travel time rather than an instant zap.</summary>
    private IEnumerator PlayStreak(Vector3 from, Vector3 to, float duration)
    {
        var go = new GameObject("MagnetPulseStreak (placeholder)");
        go.transform.SetParent(transform, worldPositionStays: true);

        var lr = go.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startWidth = lr.endWidth = streakWidth;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.sortingLayerName = sortingLayerName;
        lr.sortingOrder = sortingOrder;

        var points = new List<Vector3> { from };
        var direction = to - from;
        var perpendicular = direction.sqrMagnitude > 0.0001f
            ? Vector3.Cross(direction.normalized, Vector3.forward)
            : Vector3.up;

        for (int i = 1; i <= jaggedSegments; i++)
        {
            float t = i / (float)(jaggedSegments + 1);
            var point = Vector3.Lerp(from, to, t) + perpendicular * Random.Range(-jaggedness, jaggedness);
            points.Add(point);
        }
        points.Add(to);

        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float alpha = streakColor.a * (1f - elapsed / duration);
            var c = streakColor;
            c.a = alpha;
            lr.startColor = lr.endColor = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }

    /// <summary>A quick scale-up-then-fade pop timed to land exactly when the real tile arrives
    /// (see MagnetPulseFlightEvent.Duration), reading as a little "snap into place" beat.</summary>
    private IEnumerator PlayLandingPop(Vector3 position, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        var go = new GameObject("MagnetPulseLandingPop (placeholder)");
        go.transform.SetParent(transform, worldPositionStays: true);
        go.transform.position = position;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = whiteSprite;
        sr.color = popColor;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;

        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            float t = elapsed / popDuration;
            float scale = popMaxScale * Mathf.Sin(t * Mathf.PI); // ramps up then back down to 0, peaking mid-way
            go.transform.localScale = new Vector3(scale, scale, 1f);

            var c = sr.color;
            c.a = popColor.a * (1f - t);
            sr.color = c;

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(go);
    }
}
