using UnityEngine;

/// <summary>Which kind of thing shows this tutorial step.</summary>
public enum TutorialTriggerType
{
    /// <summary>Shows the first time GameManager enters a given state (see triggerState).</summary>
    GameplayState,

    /// <summary>Shows the first time a specific Madness Symbol definition is spawned onto the
    /// board (see triggerMadnessSymbol). Fires off MadnessSymbolSpawnedEvent, published from
    /// GravityController.Collapse.</summary>
    MadnessSymbolSpawned
}

/// <summary>
/// Designer-facing asset: one tutorial popup. Create via Assets > Create > Match3 > Roguelike >
/// Tutorial Step. Add the created asset to a TutorialSequencer's step list in the Inspector -
/// no code required to add, remove, reorder, or retire a tutorial step.
/// </summary>
[CreateAssetMenu(fileName = "TutorialStep", menuName = "Match3/Roguelike/Tutorial Step")]
public class TutorialStepDefinition : ScriptableObject
{
    [Tooltip("Unique key used to remember whether this step has already been shown. Leave blank " +
             "to use the asset's file name - only set this explicitly if you plan to rename the " +
             "asset later and want to keep players' existing 'already seen' state.")]
    [SerializeField] private string id;

    [Header("Trigger")]
    public TutorialTriggerType triggerType = TutorialTriggerType.GameplayState;

    [Tooltip("Used when Trigger Type = GameplayState. Shows the first time GameManager enters this state.")]
    public GameManager.GameplayState triggerState;

    [Tooltip("-1 = trigger on any stage. Set to a specific 0-based stage index to restrict this " +
             "step to one stage, e.g. a swap tutorial that should only appear on stage 0. Only " +
             "used with the GameplayState trigger type.")]
    public int onlyOnStageIndex = -1;

    [Tooltip("Used when Trigger Type = MadnessSymbolSpawned. Shows the first time this specific " +
             "Madness Symbol definition is spawned onto the board (from any spawn source).")]
    public MadnessSymbolDefinition triggerMadnessSymbol;

    [Header("Content")]
    public string title;
    [TextArea(2, 6)] public string body;

    [Header("Behavior")]
    [Tooltip("On: shown once ever per device, then never again. Off: shown every time the " +
             "trigger fires - use for a reminder rather than a one-time introduction.")]
    public bool showOnce = true;

    public string Id => string.IsNullOrEmpty(id) ? name : id;

#if UNITY_EDITOR
    /// <summary>
    /// Catches a specific footgun: a GameplayState trigger targeting Popup itself. Entering
    /// Popup state is always a side effect of SOME popup already opening (this one or any
    /// other), so a step configured this way doesn't fire "when the player is shown a popup" -
    /// it fires as a side effect of any unrelated popup opening, then queues itself right behind
    /// it, which looks like a delayed/misfiring trigger rather than the actual intended one.
    /// </summary>
    private void OnValidate()
    {
        if (triggerType == TutorialTriggerType.GameplayState && triggerState == GameManager.GameplayState.Popup)
            Debug.LogWarning($"[TutorialStepDefinition] '{name}': Trigger Type is GameplayState with " +
                              "Trigger State = Popup. This fires off of ANY popup opening (including " +
                              "this step's own), not a specific moment - almost certainly not what you " +
                              "want. Did you mean to set Trigger Type to MadnessSymbolSpawned instead?", this);
    }
#endif
}
