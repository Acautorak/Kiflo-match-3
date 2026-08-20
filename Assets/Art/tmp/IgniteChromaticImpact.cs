using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Plays a brief Chromatic Aberration + Vignette + Bloom burst through the scene's post-process
/// Volume every time a tile ignites (see BurningSystem.TryIgniteNearby / TileIgnitedEvent) - a
/// quick punch up then back down, distinct from the burning tile's own steady shader glow, so
/// ignition itself reads as a discrete "hit" moment rather than blending into the ongoing fire
/// visual. The Bloom kick in particular makes it read as a brief whole-screen flash-glow, not just
/// the burning tile's own glow getting brighter.
///
/// Requires a URP Volume (the same Global Volume set up for Bloom already works fine). Chromatic
/// Aberration, Vignette, and Bloom overrides on its profile are each independently optional - add
/// any combination; whichever exist get pulsed, whichever don't are simply skipped. If none exist,
/// this logs a warning once and otherwise does nothing - safe to drop into the scene before any
/// override is actually added.
/// </summary>
public class IgniteChromaticImpact : MonoBehaviour
{
    [Tooltip("Auto-found via FindAnyObjectByType if left empty. The Volume whose profile has the Chromatic Aberration override to pulse.")]
    [SerializeField] private Volume volume;

    [Header("Burst Shape")]
    [Tooltip("Peak Chromatic Aberration intensity during the burst (0-1, same range as the override's own Intensity slider).")]
    [Range(0f, 1f)] [SerializeField] private float peakIntensity = 0.5f;
    [Tooltip("Time to punch up to peakIntensity.")]
    [Min(0.01f)] [SerializeField] private float attackDuration = 0.06f;
    [Tooltip("Time to ease back down to the baseline (whatever the override's intensity was when this component started).")]
    [Min(0.01f)] [SerializeField] private float releaseDuration = 0.18f;

    [Header("Vignette (optional - layers on top if a Vignette override exists on the same profile)")]
    [Tooltip("Peak Vignette intensity during the burst, on top of whatever its baseline was. Uses the same attack/release timing as Chromatic Aberration above.")]
    [Range(0f, 1f)] [SerializeField] private float peakVignetteIntensity = 0.35f;

    [Header("Bloom (optional - layers on top if a Bloom override exists on the same profile)")]
    [Tooltip("Peak Bloom intensity during the burst, on top of whatever its baseline was - a brief whole-screen flash-glow rather than just the burning tile's own glow. Uses the same attack/release timing as Chromatic Aberration above.")]
    [Min(0f)] [SerializeField] private float peakBloomIntensity = 2.5f;

    private ChromaticAberration chromaticAberration;
    private float baselineIntensity;
    private Vignette vignette;
    private float baselineVignetteIntensity;
    private Bloom bloom;
    private float baselineBloomIntensity;
    private Tween activeBurst;
    private bool warnedMissingOverride;

    private void Awake()
    {
        if (volume == null) volume = FindAnyObjectByType<Volume>();

        if (volume == null)
        {
            Debug.LogWarning("[IgniteChromaticImpact] No Volume found in scene - ignite bursts will be a no-op.");
            return;
        }

        if (volume.profile == null)
        {
            Debug.LogWarning("[IgniteChromaticImpact] Volume has no profile assigned - ignite bursts will be a no-op.");
            return;
        }

        // Chromatic Aberration, Vignette, and Bloom are each independently optional - any
        // combination can be present on the profile. Missing one just means that part of the
        // burst does nothing; it doesn't block the others from still working.
        if (volume.profile.TryGet(out chromaticAberration))
            baselineIntensity = chromaticAberration.intensity.value;

        if (volume.profile.TryGet(out vignette))
            baselineVignetteIntensity = vignette.intensity.value;

        if (volume.profile.TryGet(out bloom))
            baselineBloomIntensity = bloom.intensity.value;

        if (chromaticAberration == null && vignette == null && bloom == null)
            Debug.LogWarning("[IgniteChromaticImpact] Volume profile has no Chromatic Aberration, Vignette, or Bloom override - add at least one for ignite bursts to do anything.");
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<TileIgnitedEvent>(HandleTileIgnited);
        EventBus.Subscribe<TileIgnitedEvent>(HandleTileIgnited);
    }

    private void OnDisable() => EventBus.Unsubscribe<TileIgnitedEvent>(HandleTileIgnited);

    private void HandleTileIgnited(TileIgnitedEvent evt)
    {
        if (chromaticAberration == null && vignette == null && bloom == null)
        {
            if (!warnedMissingOverride)
            {
                warnedMissingOverride = true;
                Debug.LogWarning("[IgniteChromaticImpact] Ignite happened but there's no Chromatic Aberration, Vignette, or Bloom override to pulse - see Awake's warning above.");
            }
            return;
        }

        PlayBurst();
    }

    /// <summary>Punches Chromatic Aberration, Vignette, and/or Bloom (whichever exist on the
    /// profile) up to their peak values then eases back down to their respective baselines,
    /// captured once in Awake.</summary>
    private void PlayBurst()
    {
        // Kill any in-flight burst rather than letting two overlap and fight over the value -
        // several tiles can ignite in quick succession (a cascade with multiple matched groups
        // each rolling their own ignite chance), so this just restarts from wherever the previous
        // burst currently was rather than queuing up.
        activeBurst?.Kill();

        var sequence = DOTween.Sequence();

        if (chromaticAberration != null)
        {
            sequence.Join(DOTween.To(() => chromaticAberration.intensity.value,
                    v => chromaticAberration.intensity.value = v,
                    peakIntensity, attackDuration).SetEase(Ease.OutQuad));
            sequence.Insert(attackDuration, DOTween.To(() => chromaticAberration.intensity.value,
                    v => chromaticAberration.intensity.value = v,
                    baselineIntensity, releaseDuration).SetEase(Ease.InQuad));
        }

        if (vignette != null)
        {
            sequence.Join(DOTween.To(() => vignette.intensity.value,
                    v => vignette.intensity.value = v,
                    peakVignetteIntensity, attackDuration).SetEase(Ease.OutQuad));
            sequence.Insert(attackDuration, DOTween.To(() => vignette.intensity.value,
                    v => vignette.intensity.value = v,
                    baselineVignetteIntensity, releaseDuration).SetEase(Ease.InQuad));
        }

        if (bloom != null)
        {
            sequence.Join(DOTween.To(() => bloom.intensity.value,
                    v => bloom.intensity.value = v,
                    peakBloomIntensity, attackDuration).SetEase(Ease.OutQuad));
            sequence.Insert(attackDuration, DOTween.To(() => bloom.intensity.value,
                    v => bloom.intensity.value = v,
                    baselineBloomIntensity, releaseDuration).SetEase(Ease.InQuad));
        }

        activeBurst = sequence;
    }
}
