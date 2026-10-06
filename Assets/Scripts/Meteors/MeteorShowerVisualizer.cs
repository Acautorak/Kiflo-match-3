using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Placeholder Meteor Shower visual - no art/shader dependency, same philosophy as the other
/// placeholder visualizers in this project. Builds a simple colored "rock" with a built-in
/// TrailRenderer for the comet streak during the fall (TrailRenderer needs nothing but a material,
/// so this works with zero asset setup), then a quick flash + expanding ring burst on impact.
///
/// Drop this on any always-loaded object (e.g. next to the other placeholder visualizers) and it
/// just works - it listens for MeteorShowerStartedEvent/MeteorShowerImpactEvent over EventBus.
/// Nothing else in the codebase references this class - when real VFX are ready, delete or disable
/// this component and hook the same events from the new system instead.
/// </summary>
public class MeteorShowerVisualizer : MonoBehaviour
{
    [Header("Meteor Body")]
    [SerializeField] private Color meteorColor = new Color(1f, 0.45f, 0.15f, 1f);
    [Min(0.01f)] [SerializeField] private float meteorSize = 0.5f;
    [Tooltip("Degrees per second the meteor spins while falling, purely for flair.")]
    [SerializeField] private float spinSpeed = 540f;

    [Header("Trail")]
    [SerializeField] private Color trailStartColor = new Color(1f, 0.8f, 0.3f, 0.9f);
    [SerializeField] private Color trailEndColor = new Color(1f, 0.3f, 0.1f, 0f);
    [Min(0.01f)] [SerializeField] private float trailWidth = 0.25f;
    [Min(0.01f)] [SerializeField] private float trailTime = 0.25f;

    [Header("Impact")]
    [SerializeField] private Color flashColor = new Color(1f, 0.9f, 0.6f, 1f);
    [Min(0f)] [SerializeField] private float flashMaxScale = 1.4f;
    [SerializeField] private Color ringColor = new Color(1f, 0.6f, 0.2f, 0.8f);
    [Min(0f)] [SerializeField] private float ringMaxRadius = 1.5f;
    [Min(1)] [SerializeField] private int ringSegments = 24;
    [Min(0.01f)] [SerializeField] private float impactDuration = 0.35f;

    [Header("Sorting")]
    [Tooltip("Sorting layer everything here renders on. Set this to a layer AT OR ABOVE your symbol sprites, since a meteor falling in front of the board reads better than one falling behind it.")]
    [SortingLayer] [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int sortingOrder = 100;

    private Sprite whiteSprite;

    private void Awake()
    {
        var texture = Texture2D.whiteTexture;
        whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
    }

    private void OnEnable()
    {
        EventBus.Subscribe<MeteorShowerStartedEvent>(OnStarted);
        EventBus.Subscribe<MeteorShowerImpactEvent>(OnImpact);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<MeteorShowerStartedEvent>(OnStarted);
        EventBus.Unsubscribe<MeteorShowerImpactEvent>(OnImpact);
    }

    private void OnStarted(MeteorShowerStartedEvent evt) => StartCoroutine(PlayFall(evt.FromWorld, evt.ToWorld, evt.FlightDuration));

    private void OnImpact(MeteorShowerImpactEvent evt) => StartCoroutine(PlayImpact(evt.Position));

    /// <summary>Falls the meteor body from FromWorld to ToWorld over `duration`, spinning for
    /// flair, with a TrailRenderer drawing the comet streak behind it for free. Destroys itself on
    /// arrival - the separate impact burst below is triggered by MatchResolver's own
    /// MeteorShowerImpactEvent, not by this coroutine finishing, so the two stay in sync even if
    /// something external ever changes the flight's actual timing.</summary>
    private IEnumerator PlayFall(Vector3 from, Vector3 to, float duration)
    {
        var go = new GameObject("MeteorShowerBody (placeholder)");
        go.transform.SetParent(transform, worldPositionStays: true);
        go.transform.position = from;
        go.transform.localScale = Vector3.one * meteorSize;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = whiteSprite;
        sr.color = meteorColor;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;

        var trail = go.AddComponent<TrailRenderer>();
        trail.material = new Material(Shader.Find("Sprites/Default"));
        trail.time = trailTime;
        trail.startWidth = trailWidth;
        trail.endWidth = 0f;
        trail.startColor = trailStartColor;
        trail.endColor = trailEndColor;
        trail.sortingLayerName = sortingLayerName;
        trail.sortingOrder = sortingOrder - 1; // just behind the body itself

        go.transform.DOMove(to, duration).SetEase(Ease.InQuad);
        go.transform.DORotate(new Vector3(0f, 0f, spinSpeed * duration), duration, RotateMode.FastBeyond360).SetEase(Ease.Linear);

        yield return new WaitForSeconds(duration);

        Destroy(go);
    }

    /// <summary>A quick flash pop plus an expanding fading ring at the impact point - same
    /// "scale-and-fade" placeholder trick as the other visualizers' landing pops, just bigger and
    /// paired with a ring for a proper "something hit the ground" read.</summary>
    private IEnumerator PlayImpact(Vector3 position)
    {
        var flashGo = new GameObject("MeteorShowerFlash (placeholder)");
        flashGo.transform.SetParent(transform, worldPositionStays: true);
        flashGo.transform.position = position;

        var flashSr = flashGo.AddComponent<SpriteRenderer>();
        flashSr.sprite = whiteSprite;
        flashSr.color = flashColor;
        flashSr.sortingLayerName = sortingLayerName;
        flashSr.sortingOrder = sortingOrder + 1; // above the ring/body

        var ringGo = new GameObject("MeteorShowerRing (placeholder)");
        ringGo.transform.SetParent(transform, worldPositionStays: true);

        var lr = ringGo.AddComponent<LineRenderer>();
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.loop = true;
        lr.useWorldSpace = true;
        lr.positionCount = ringSegments;
        lr.startWidth = lr.endWidth = trailWidth;
        lr.sortingLayerName = sortingLayerName;
        lr.sortingOrder = sortingOrder;

        float elapsed = 0f;
        while (elapsed < impactDuration)
        {
            float t = elapsed / impactDuration;

            float flashScale = flashMaxScale * (1f - t);
            flashGo.transform.localScale = new Vector3(flashScale, flashScale, 1f);
            var fc = flashColor;
            fc.a = flashColor.a * (1f - t);
            flashSr.color = fc;

            float radius = Mathf.Lerp(0f, ringMaxRadius, t);
            for (int i = 0; i < ringSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / ringSegments;
                lr.SetPosition(i, position + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
            }
            var rc = ringColor;
            rc.a = ringColor.a * (1f - t);
            lr.startColor = lr.endColor = rc;

            elapsed += Time.deltaTime;
            yield return null;
        }

        Destroy(flashGo);
        Destroy(ringGo);
    }
}
