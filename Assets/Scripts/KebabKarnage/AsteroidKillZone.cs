using UnityEngine;

/// <summary>
/// Marker component for a designer-placed sprite + Collider2D that represents "the bottom" for
/// Kebab Karnage asteroids. Drop this on any GameObject with a Collider2D (Is Trigger on) - e.g.
/// a ground/basket sprite - and place/resize it in the scene like any other object. FallingAsteroid
/// detects overlap with this via OnTriggerEnter2D, so no bottomY float needs hand-tuning to match
/// the art.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AsteroidKillZone : MonoBehaviour
{
    private void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
            Debug.LogWarning($"[AsteroidKillZone] '{name}': Collider2D needs Is Trigger enabled to register asteroid impacts.", this);
    }
#endif
}
