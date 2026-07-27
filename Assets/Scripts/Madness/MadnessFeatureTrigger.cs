using UnityEngine;

/// <summary>
/// Bridges the generic MadnessMeter to feature-mode mini-games. Listens for the meter filling
/// up and, when it does, consumes it immediately (so it starts refilling from 0 right away) but
/// DEFERS the actual feature-mode request until GameManager reports it's back to a settled state
/// (Idle or GracePeriod) - i.e. any in-flight cascade has fully finished resolving.
///
/// This matters because the meter can fill mid-cascade (a match's Madness effects run as part of
/// match resolution, before Board's own coroutine has finished clearing/refilling/scoring
/// everything). Starting a feature mode transition in the middle of that used to race against
/// Board's own resolve-to-Idle callback landing afterward and re-enabling input out from under
/// the feature mode - see GameManager.SetState's FeatureMode guard for the safety net that
/// catches this if it ever happens anyway, but waiting here avoids the race in the first place.
///
/// This is the implementation of the "what happens when it fills" behavior MadnessMeter's own
/// header deliberately leaves open, kept as a separate component so MadnessMeter itself stays
/// generic. Which feature mode gets requested is a per-stage designer choice - see
/// StageDefinition.featureModeOnMeterFull (assigned by ProceduralStageGenerator from
/// StageGenerationConfig.featureModeWeights) - read here off StageManager.CurrentStage.
/// featureIdOnFill is kept only as the fallback used when no StageManager/CurrentStage is
/// available (e.g. Board running standalone without a StageManager in the scene).
/// </summary>
public class MadnessFeatureTrigger : MonoBehaviour
{
    [SerializeField] private MadnessMeter meter;
    [SerializeField] private GameManager gameManager;
    [Tooltip("Optional explicit link. If left empty, auto-found in Awake - used to read the current " +
             "stage's featureModeOnMeterFull choice.")]
    [SerializeField] private StageManager stageManager;

    [Tooltip("Fallback feature mode id used only when no StageManager/CurrentStage is available. Defaults to Kebab Karnage.")]
    [SerializeField] private string featureIdOnFill = KebabKarnageManager.FeatureId;

    private bool _pendingRequest;
    private float _pendingOverflow;

    private void Awake()
    {
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<MadnessMeterChangedEvent>(HandleMeterChanged);
        EventBus.Subscribe<MadnessMeterChangedEvent>(HandleMeterChanged);
    }

    private void OnDisable() => EventBus.Unsubscribe<MadnessMeterChangedEvent>(HandleMeterChanged);

    private void HandleMeterChanged(MadnessMeterChangedEvent evt)
    {
        if (_pendingRequest) return; // already consumed and waiting to fire - don't consume twice
        if (meter == null || !meter.IsFull) return;

        // Capture how far the meter overshot before Consume() zeroes it out, so whichever feature
        // mode gets requested below can scale itself off it if it wants to (see
        // FeatureModeRequestedEvent.OverflowAmount - FreeSpinsManager uses this for spin count).
        _pendingOverflow = Mathf.Max(0f, evt.Current - evt.Max);

        meter.Consume();
        _pendingRequest = true;
        Debug.Log("[MadnessFeatureTrigger] Meter filled - waiting for the board to settle (Idle/GracePeriod) before starting the feature mode.");

        // In case the board already happens to be settled right now, don't wait an extra frame.
        TryFireIfSettled();
    }

    private void Update()
    {
        if (_pendingRequest) TryFireIfSettled();
    }

    private void TryFireIfSettled()
    {
        if (!_pendingRequest) return;

        bool settled = gameManager == null
            || gameManager.CurrentState == GameManager.GameplayState.Idle
            || gameManager.CurrentState == GameManager.GameplayState.GracePeriod;

        if (!settled) return;

        _pendingRequest = false;
        string featureId = ResolveFeatureId();
        Debug.Log($"[MadnessFeatureTrigger] Board settled - requesting feature mode '{featureId}' (overflow {_pendingOverflow:0.##}).");
        EventBus.Publish(new FeatureModeRequestedEvent(featureId, _pendingOverflow));
    }

    /// <summary>Maps the current stage's designer-chosen MadnessFeatureModeChoice to the concrete
    /// feature id string each manager listens for. Falls back to featureIdOnFill if there's no
    /// StageManager/CurrentStage (e.g. Board running standalone).</summary>
    private string ResolveFeatureId()
    {
        var stage = stageManager != null ? stageManager.CurrentStage : null;
        if (stage == null) return featureIdOnFill;

        return stage.featureModeOnMeterFull switch
        {
            MadnessFeatureModeChoice.FreeSpins => FreeSpinsManager.FeatureId,
            _ => KebabKarnageManager.FeatureId,
        };
    }
}
