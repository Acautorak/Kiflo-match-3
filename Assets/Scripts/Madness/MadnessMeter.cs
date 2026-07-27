using UnityEngine;

/// <summary>
/// Run-scoped meter that Madness effects (see MadnessFillMeterEffect) contribute to. What happens
/// when it fills is intentionally NOT implemented here, per design - this only tracks and publishes
/// the value so UI can show progress, with IsFull exposed for whatever listens for it (see
/// MadnessFeatureTrigger for the first such listener, which starts Kebab Karnage). Reset via
/// ResetForNewRun() (call from StageManager.StartNewRun()) - unlike MadnessBoardModifiers, this
/// persists across stage transitions within the same run.
/// </summary>
public class MadnessMeter : MonoBehaviour
{
    [SerializeField] private float maxValue = 100f;
    private float current;

    public float Current => current;
    public float Max => maxValue;
    public float Normalized => maxValue > 0f ? Mathf.Clamp01(current / maxValue) : 0f;
    public bool IsFull => maxValue > 0f && current >= maxValue;

    public void Add(float amount)
    {
        if (amount == 0f) return;
        current = Mathf.Clamp(current + amount, 0f, maxValue);
        EventBus.Publish(new MadnessMeterChangedEvent(current, maxValue));
    }

    public void ResetForNewRun()
    {
        current = 0f;
        EventBus.Publish(new MadnessMeterChangedEvent(current, maxValue));
    }

    /// <summary>
    /// Drains the meter back to 0 because something acted on IsFull (e.g. a feature mode was
    /// triggered) - distinct from ResetForNewRun so a fresh run vs. "the meter was spent" can be
    /// told apart if anything ever needs to react differently to the two.
    /// </summary>
    public void Consume()
    {
        current = 0f;
        EventBus.Publish(new MadnessMeterChangedEvent(current, maxValue));
    }
}
