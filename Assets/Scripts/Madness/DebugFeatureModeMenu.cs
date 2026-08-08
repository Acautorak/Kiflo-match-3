using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// Playtest-only debug menu: one button per feature mode, forcing MadnessFeatureTrigger to fire
/// that feature the next time the board settles (Idle/GracePeriod) - bypassing the Madness meter
/// entirely so you don't have to grind out matches to test Kebab Karnage / Free Spins / Lucky
/// Scratch Ticket. Also has a Reset Run section - deletes the on-disk save and resets every piece
/// of in-memory run state (PlayerHealth, PlayerRunStats, MadnessMeter, board, stage) back to a
/// fresh run in one two-tap-confirmed button, for wiping leftover dev state before cutting a
/// build - and a Complete Stage button that forces the current stage's goal to be met right now
/// via the real CompleteStage()/FinalizeStageClear() path, for testing the stage-clear delay and
/// powerup selection flow without having to actually grind out a stage's goal first.
///
/// Built at runtime as a plain UGUI Canvas + Buttons (no prefab needed) rather than OnGUI/IMGUI -
/// this project's scratch panels already prove UGUI + EventSystem correctly receives clicks
/// through the Device Simulator and the new Input System, whereas raw OnGUI can be unreliable
/// inside the Simulator's view. Piggybacking on the same pipeline that's already known to work
/// avoids that class of problem entirely.
///
/// Compiled out of release builds entirely (requires UNITY_EDITOR or DEVELOPMENT_BUILD).
/// </summary>
public class DebugFeatureModeMenu : MonoBehaviour
{
    [SerializeField] private MadnessFeatureTrigger featureTrigger;
    [Tooltip("Optional - auto-found. Needed for the Reset Run button.")]
    [SerializeField] private StageManager stageManager;
    [Tooltip("Optional - auto-found. Needed for the Reset Run button.")]
    [SerializeField] private PlayerRunStats playerRunStats;
    [Tooltip("Optional - auto-found. Needed for the Reset Run button.")]
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private KeyCode toggleKey = KeyCode.BackQuote;
    [Tooltip("Start with the panel already visible - handy while actively iterating; flip off once it's just sitting in the scene for occasional use.")]
    [SerializeField] private bool startVisible = false;

    private GameObject panelRoot;
    private TextMeshProUGUI resetButtonLabel;
    private bool resetArmed;
    private Coroutine disarmRoutine;
    [Tooltip("How long the 'tap again to confirm' window stays open before silently re-arming to 'Reset Run', in case of a stray first tap.")]
    [SerializeField] private float resetConfirmWindow = 3f;

    private void Awake()
    {
        if (featureTrigger == null) featureTrigger = FindAnyObjectByType<MadnessFeatureTrigger>();
        if (featureTrigger == null)
            Debug.LogWarning("[DebugFeatureModeMenu] No MadnessFeatureTrigger found in scene - buttons will be built but won't do anything.");

        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
        if (playerRunStats == null) playerRunStats = FindAnyObjectByType<PlayerRunStats>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (FindAnyObjectByType<EventSystem>() == null)
            Debug.LogWarning("[DebugFeatureModeMenu] No EventSystem found in scene - buttons won't receive clicks. " +
                              "Your scratch panels rely on one too, so this would likely already be broken if it were truly missing - " +
                              "double check there isn't a second, disabled EventSystem, or that Time.timeScale/a blocking raycaster isn't the real issue instead.");

        BuildUI();
        panelRoot.SetActive(startVisible);
    }

    private void Update()
    {
        if (WasToggleKeyPressedThisFrame())
            panelRoot.SetActive(!panelRoot.activeSelf);
    }

    /// <summary>
    /// This project has Active Input Handling set to Input System only, so legacy
    /// UnityEngine.Input throws InvalidOperationException at runtime - reads the key through
    /// UnityEngine.InputSystem.Keyboard instead. Falls back to legacy UnityEngine.Input only if
    /// the Input System package isn't present at all (ENABLE_INPUT_SYSTEM undefined).
    /// </summary>
    private bool WasToggleKeyPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null) return false;
        return LegacyToInputSystemKey(toggleKey) is { } key && keyboard[key].wasPressedThisFrame;
#else
        return Input.GetKeyDown(toggleKey);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>Maps the handful of KeyCodes this menu offers in the Inspector to their Input
    /// System equivalent - extend this switch if you expose more toggleKey options.</summary>
    private static UnityEngine.InputSystem.Key? LegacyToInputSystemKey(KeyCode legacyKey) => legacyKey switch
    {
        KeyCode.BackQuote => UnityEngine.InputSystem.Key.Backquote,
        KeyCode.Tab => UnityEngine.InputSystem.Key.Tab,
        KeyCode.F1 => UnityEngine.InputSystem.Key.F1,
        KeyCode.F2 => UnityEngine.InputSystem.Key.F2,
        _ => null,
    };
#endif

    private void BuildUI()
    {
        var canvasGO = new GameObject("DebugFeatureModeMenu_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760; // draw above essentially everything else in the scene

        panelRoot = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panelRoot.transform.SetParent(canvasGO.transform, false);

        var panelRect = (RectTransform)panelRoot.transform;
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(10f, -10f);
        panelRect.sizeDelta = new Vector2(240f, 0f);

        panelRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

        var layout = panelRoot.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 6f;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;

        panelRoot.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        AddLabel(panelRoot.transform, "DEBUG: Force Feature Mode", 16, FontStyles.Bold);
        AddLabel(panelRoot.transform, "Fires on the next board settle", 12, FontStyles.Italic);

        AddButton(panelRoot.transform, "Kebab Karnage", () => featureTrigger?.DebugForceFeature(KebabKarnageManager.FeatureId));
        AddButton(panelRoot.transform, "Free Spins", () => featureTrigger?.DebugForceFeature(FreeSpinsManager.FeatureId));
        AddButton(panelRoot.transform, "Lucky Scratch Ticket", () => featureTrigger?.DebugForceFeature(LuckyScratchTicketManager.FeatureId));
        AddButton(panelRoot.transform, "Tile Collector", () => featureTrigger?.DebugForceFeature(TileCollectorManager.FeatureId));

        AddLabel(panelRoot.transform, "DEBUG: Reset Run", 16, FontStyles.Bold);
        AddLabel(panelRoot.transform, "Deletes the save + resets stats/health", 12, FontStyles.Italic);
        resetButtonLabel = AddButton(panelRoot.transform, "Reset Run", HandleResetButtonPressed);

        AddLabel(panelRoot.transform, "DEBUG: Stage", 16, FontStyles.Bold);
        AddLabel(panelRoot.transform, "Completes the current stage right now", 12, FontStyles.Italic);
        AddButton(panelRoot.transform, "Complete Stage", () => stageManager?.DebugForceCompleteStage());

        AddLabel(panelRoot.transform, $"Toggle panel: '{toggleKey}'", 11, FontStyles.Normal);
    }

    private static void AddLabel(Transform parent, string text, int fontSize, FontStyles style)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Left;
    }

    private static TextMeshProUGUI AddButton(Transform parent, string label, System.Action onClick)
    {
        var go = new GameObject($"Button_{label}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.15f);
        go.GetComponent<LayoutElement>().preferredHeight = 32f;
        go.GetComponent<Button>().onClick.AddListener(() => onClick?.Invoke());

        var textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);
        var rect = (RectTransform)textGO.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 14;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        // Buttons shouldn't have their own label eat clicks meant for the Button behind them.
        tmp.raycastTarget = false;
        return tmp;
    }

    /// <summary>Two-tap confirm (rather than a plain single-tap button) since this is destructive -
    /// it deletes the on-disk save file, not just in-memory state. First tap arms it and relabels
    /// the button; a second tap within resetConfirmWindow actually performs the reset. Left alone,
    /// it silently re-arms back to the normal label so a single accidental tap can't nuke a run.</summary>
    private void HandleResetButtonPressed()
    {
        if (!resetArmed)
        {
            resetArmed = true;
            if (resetButtonLabel != null) resetButtonLabel.text = "Tap again to confirm";
            if (disarmRoutine != null) StopCoroutine(disarmRoutine);
            disarmRoutine = StartCoroutine(DisarmAfterDelay());
            return;
        }

        if (disarmRoutine != null) StopCoroutine(disarmRoutine);
        resetArmed = false;
        if (resetButtonLabel != null) resetButtonLabel.text = "Reset Run";

        PerformReset();
    }

    private System.Collections.IEnumerator DisarmAfterDelay()
    {
        yield return new WaitForSeconds(resetConfirmWindow);
        resetArmed = false;
        if (resetButtonLabel != null) resetButtonLabel.text = "Reset Run";
    }

    /// <summary>Deletes the on-disk save and resets every piece of in-memory run state back to a
    /// fresh run, in one call - the thing you actually want before cutting a build, so a leftover
    /// dev save/stat pile-up from testing doesn't ship or get carried into a fresh install's
    /// first run by accident.</summary>
    private void PerformReset()
    {
        SaveSystem.DeleteSave();
        Debug.Log("[DebugFeatureModeMenu] Save file deleted.");

        if (stageManager != null)
        {
            // StartNewRun() already resets PlayerHealth, PlayerRunStats, and MadnessMeter, clears
            // the board, and restarts at stage 0 - one call covers everything, so there's no need
            // to (and no reason to double-fire their change events by) resetting those two
            // individually first.
            stageManager.StartNewRun();
        }
        else
        {
            Debug.LogWarning("[DebugFeatureModeMenu] No StageManager found - falling back to resetting PlayerRunStats/PlayerHealth individually. Board/stage state will NOT be restarted, since that part of the reset only happens inside StageManager.StartNewRun().");
            if (playerRunStats != null) playerRunStats.ResetForNewRun();
            else Debug.LogWarning("[DebugFeatureModeMenu] No PlayerRunStats found either - stats not reset.");

            if (playerHealth != null) playerHealth.ResetForNewRun();
            else Debug.LogWarning("[DebugFeatureModeMenu] No PlayerHealth found either - health not reset.");
        }

        Debug.Log("[DebugFeatureModeMenu] Reset Run complete.");
    }
}
#endif
