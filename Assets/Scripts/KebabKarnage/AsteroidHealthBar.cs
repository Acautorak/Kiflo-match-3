using UnityEngine;

/// <summary>
/// World-space health bar, meant to sit as a child of a FallingAsteroid prefab (positioned above
/// its sprite). Scales `fillTransform` on X to show remaining health, and resets rotation every
/// frame so the bar stays readable even while the parent asteroid spins.
///
/// Setup: fillTransform should be a sprite with its pivot on the LEFT edge, so scaling X shrinks
/// it toward that edge instead of shrinking from the center.
/// </summary>
public class AsteroidHealthBar : MonoBehaviour
{
    [Tooltip("The fill sprite/transform that gets scaled on X to represent remaining health. Pivot must be on the left edge.")]
    [SerializeField] private Transform fillTransform;

    [Tooltip("Optional root to hide entirely at full health (only show the bar once damaged). Leave null to always show it.")]
    [SerializeField] private GameObject hideAtFullHealthRoot;

    private Vector3 _fullScale;

    private void Awake()
    {
        if (fillTransform != null) _fullScale = fillTransform.localScale;
    }

    private void LateUpdate()
    {
        // Counteract the parent asteroid's rotation so the bar always reads as horizontal.
        transform.rotation = Quaternion.identity;
    }

    public void SetFraction(float fraction)
    {
        fraction = Mathf.Clamp01(fraction);

        if (fillTransform != null)
        {
            var scale = _fullScale;
            scale.x = _fullScale.x * fraction;
            fillTransform.localScale = scale;
        }

        if (hideAtFullHealthRoot != null)
            hideAtFullHealthRoot.SetActive(fraction < 1f);
    }
}
