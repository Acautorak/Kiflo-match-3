using UnityEngine;

/// <summary>
/// Run-scoped meter that Madness effects (see MadnessFillMeterEffect) contribute to. What happens
/// when it fills is intentionally NOT implemented yet, per design - this only tracks and publishes
/// the value so UI can show progress now, with IsFull exposed for whenever the "take you somewhere"
/// feature gets built later. Reset via ResetForNewRun() (call from StageManager.StartNewRun()) -
/// unlike MadnessBoardModifiers, this persists across stage transitions within the same run.
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
}
