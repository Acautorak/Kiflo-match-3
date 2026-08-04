using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen flash reacting to health changes: a quick blink to a peak color, then a fade back
/// to transparent - green for a heal, red for damage. Works off HealthChangedEvent's delta from
/// the last known value (the event itself only carries the new CurrentHealth/MaxHealth, not a
/// delta, so this tracks the previous value itself to tell heal from damage - same shape as how
/// ScoreChangedEvent already carries its own Delta, HealthChangedEvent just doesn't).
///
/// Drop a full-screen UI Graphic (an Image with your texture as its Sprite, or a RawImage) on a
/// Canvas that renders above the board/HUD, start it fully transparent, and assign it here as
/// flashGraphic - this script drives its color/alpha, you just place and size it in the scene the
/// way you want (a vignette that only tints the screen edges, a flat full-screen tint, etc., is
/// entirely up to the texture/sprite you bring).
/// </summary>
public class ScreenHealthFlashEffect : MonoBehaviour
{
    [Tooltip("The full-screen overlay this drives - an Image (using your texture as its Sprite) or a RawImage both work, since both derive from Graphic. Should start fully transparent and not block raycasts (raycastTarget off).")]
    [SerializeField] private Graphic flashGraphic;
    [Tooltip("Optional - auto-found in Awake. Only used to read the starting health as a baseline before the first HealthChangedEvent, so a flash doesn't fire on scene load from a baseline of 0.")]
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Heal (health increased)")]
    [Tooltip("RGB is the flash tint, Alpha is the peak opacity reached at the top of each blink.")]
    [SerializeField] private Color healColor = new Color(0.2f, 1f, 0.2f, 0.45f);
    [SerializeField] private float healFadeDuration = 0.5f;

    [Header("Damage (health decreased)")]
    [Tooltip("RGB is the flash tint, Alpha is the peak opacity reached at the top of each blink.")]
    [SerializeField] private Color damageColor = new Color(1f, 0.15f, 0.15f, 0.45f);
    [SerializeField] private float damageFadeDuration = 0.5f;

    [Header("Blink")]
    [Tooltip("How many quick pulses happen before the final fade-out. 1 = a single flash that immediately starts fading (matches 'blinks then fades').")]
    [Min(1)]
    [SerializeField] private int blinkCount = 1;
    [Tooltip("How fast each blink pulse snaps up to peak alpha.")]
    [Min(0.01f)]
    [SerializeField] private float blinkUpDuration = 0.05f;
    [Tooltip("Only relevant if Blink Count > 1 - how long each pulse dips back down before the next one, prior to the final fade.")]
    [Min(0f)]
    [SerializeField] private float blinkDownDuration = 0.1f;

    private int lastKnownHealth;
    private bool hasBaseline;
    private Sequence activeSequence;

    private void Awake()
    {
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (flashGraphic != null)
        {
            flashGraphic.raycastTarget = false; // never let the flash overlay eat board clicks
            SetAlpha(0f);
        }

        // Read the live starting value directly rather than waiting for the first
        // HealthChangedEvent - avoids a race with PlayerHealth's own Awake publishing its
        // initial state before or after this component subscribes.
        if (playerHealth != null)
        {
            lastKnownHealth = playerHealth.CurrentHealth;
            hasBaseline = true;
        }
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Subscribe<HealthChangedEvent>(HandleHealthChanged);
    }

    private void OnDisable() => EventBus.Unsubscribe<HealthChangedEvent>(HandleHealthChanged);

    private void HandleHealthChanged(HealthChangedEvent evt)
    {
        if (!hasBaseline)
        {
            // Fallback only hit if no PlayerHealth reference was found to seed a baseline in
            // Awake - degrades gracefully by treating the first event received as the baseline
            // instead of flashing off of an assumed 0.
            lastKnownHealth = evt.CurrentHealth;
            hasBaseline = true;
            return;
        }

        int delta = evt.CurrentHealth - lastKnownHealth;
        lastKnownHealth = evt.CurrentHealth;

        if (delta == 0 || flashGraphic == null) return; // e.g. a max-health-only change with no actual heal/damage

        if (delta > 0) PlayFlash(healColor, healFadeDuration);
        else PlayFlash(damageColor, damageFadeDuration);
    }

    private void PlayFlash(Color color, float fadeDuration)
    {
        activeSequence?.Kill();

        Color transparent = new Color(color.r, color.g, color.b, 0f);
        Color peak = color;

        SetColor(transparent);

        var sequence = DOTween.Sequence();
        for (int i = 0; i < blinkCount; i++)
        {
            bool isLastBlink = i == blinkCount - 1;
            sequence.Append(flashGraphic.DOColor(peak, blinkUpDuration));
            sequence.Append(flashGraphic.DOColor(transparent, isLastBlink ? fadeDuration : blinkDownDuration));
        }

        activeSequence = sequence;
    }

    private void SetColor(Color color)
    {
        if (flashGraphic != null) flashGraphic.color = color;
    }

    private void SetAlpha(float alpha)
    {
        if (flashGraphic == null) return;
        var c = flashGraphic.color;
        c.a = alpha;
        flashGraphic.color = c;
    }

    private void OnDestroy() => activeSequence?.Kill();
}
