using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Watches for whatever a TutorialStepDefinition is triggered by (a GameManager state, or a
/// specific Madness Symbol spawning) and shows the matching step via PopupManager - which is
/// what actually pauses gameplay (GameManager.EnterPopup) and restores it afterward. This script
/// only ever decides WHICH step to show and WHEN; it knows nothing about how popups pause the
/// game, and PopupManager knows nothing about tutorials.
/// </summary>
public class TutorialSequencer : MonoBehaviour
{
    private const string SeenKeyPrefix = "tutorial_seen_";

    [Tooltip("Every tutorial step this scene should watch for. Add/remove/reorder assets here - " +
             "no code changes needed. Order only matters when multiple steps share a trigger " +
             "(they queue and show back-to-back in list order).")]
    [SerializeField] private List<TutorialStepDefinition> steps = new List<TutorialStepDefinition>();

    [Tooltip("Optional - only needed if any step uses onlyOnStageIndex.")]
    [SerializeField] private StageManager stageManager;

    private void Awake() => ValidateSteps();

    /// <summary>
    /// Catches a common copy-paste mistake: two step assets both pointing at the same
    /// MadnessSymbolDefinition (e.g. a duplicated asset whose Trigger Madness Symbol field never
    /// got repointed at the new symbol). Both steps genuinely match in that case - it's not a
    /// bug in the matching logic, it's two steps legitimately targeting the same spawn - so it
    /// silently shows two popups back-to-back for what looks like one symbol spawning. This just
    /// makes that visible instead of confusing.
    /// </summary>
    private void ValidateSteps()
    {
        var seenTargets = new Dictionary<MadnessSymbolDefinition, TutorialStepDefinition>();
        foreach (var step in steps)
        {
            if (step == null || step.triggerType != TutorialTriggerType.MadnessSymbolSpawned) continue;
            if (step.triggerMadnessSymbol == null) continue;

            if (seenTargets.TryGetValue(step.triggerMadnessSymbol, out var existing))
                Debug.LogWarning($"[TutorialSequencer] '{existing.name}' and '{step.name}' both target the same " +
                                  $"Madness Symbol ('{step.triggerMadnessSymbol.name}') - spawning it will show BOTH " +
                                  "popups back-to-back. If that's not intentional, one of them likely still points " +
                                  "at the wrong symbol (e.g. a leftover reference from duplicating the asset).", step);
            else
                seenTargets[step.triggerMadnessSymbol] = step;
        }
    }

    private void OnEnable()
    {
        EventBus.Subscribe<GameStateChangedEvent>(HandleStateChanged);
        EventBus.Subscribe<MadnessSymbolSpawnedEvent>(HandleMadnessSymbolSpawned);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<GameStateChangedEvent>(HandleStateChanged);
        EventBus.Unsubscribe<MadnessSymbolSpawnedEvent>(HandleMadnessSymbolSpawned);
    }

    private void HandleStateChanged(GameStateChangedEvent evt)
    {
        if (!TutorialSettings.TutorialsEnabled) return;
        if (PopupManager.Instance == null) return;

        // If we just arrived here because a popup closed and restored the prior state (see
        // GameManager.ExitPopup), that's not a fresh entry into evt.Current - it's this same
        // system (or another popup) handing control back. Treating it as a fresh trigger would
        // make a step targeting that state re-show itself the instant its own popup closes,
        // looping forever instead of actually dismissing.
        if (evt.Previous == GameManager.GameplayState.Popup) return;

        int currentStageIndex = stageManager != null ? stageManager.CurrentStageIndex : -1;

        foreach (var step in steps)
        {
            if (step == null || step.triggerType != TutorialTriggerType.GameplayState) continue;
            if (step.triggerState != evt.Current) continue;
            if (step.onlyOnStageIndex >= 0 && step.onlyOnStageIndex != currentStageIndex) continue;

            TryShow(step);
        }
    }

    private void HandleMadnessSymbolSpawned(MadnessSymbolSpawnedEvent evt)
    {
        if (!TutorialSettings.TutorialsEnabled) return;

        Debug.Log($"[TutorialSequencer] Received MadnessSymbolSpawnedEvent for '{evt.Definition?.name}'. " +
                  $"Checking {steps.Count} step(s).");

        if (PopupManager.Instance == null)
        {
            Debug.LogWarning("[TutorialSequencer] No PopupManager in the scene - can't show tutorial popups.");
            return;
        }

        foreach (var step in steps)
        {
            if (step == null || step.triggerType != TutorialTriggerType.MadnessSymbolSpawned) continue;
            // ScriptableObject asset reference equality - the same MadnessSymbolDefinition asset
            // is what's assigned both in a MadnessSpawnOption pool and here, so this is a
            // reliable identity check rather than a name/string comparison.
            if (step.triggerMadnessSymbol != evt.Definition)
            {
                Debug.Log($"[TutorialSequencer] Step '{step.name}' targets '{step.triggerMadnessSymbol?.name}' - doesn't match '{evt.Definition?.name}'.");
                continue;
            }

            Debug.Log($"[TutorialSequencer] Match found: showing step '{step.name}'.");
            TryShow(step);
        }
    }

    private void TryShow(TutorialStepDefinition step)
    {
        if (step.showOnce && HasBeenSeen(step))
        {
            Debug.Log($"[TutorialSequencer] Step '{step.name}' already marked as seen (PlayerPrefs key " +
                      $"'{SeenKeyPrefix}{step.Id}') - skipping. Use Tools > Match3 > Clear Tutorial Seen Flags " +
                      "to reset while testing.");
            return;
        }

        PopupManager.Instance.Show(new PopupRequest
        {
            Title = step.title,
            Body = step.body,
            OnClosed = () =>
            {
                if (step.showOnce) MarkSeen(step);
            }
        });
    }

    private static bool HasBeenSeen(TutorialStepDefinition step) =>
        PlayerPrefs.GetInt(SeenKeyPrefix + step.Id, 0) == 1;

    private static void MarkSeen(TutorialStepDefinition step)
    {
        PlayerPrefs.SetInt(SeenKeyPrefix + step.Id, 1);
        PlayerPrefs.Save();
    }
}
