using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// <summary>
/// Playtest-only debug menu: one button per feature mode, forcing MadnessFeatureTrigger to fire
/// that feature the next time the board settles (Idle/GracePeriod) - bypassing the Madness meter
/// entirely so you don't have to grind out matches to test Kebab Karnage / Free Spins / Lucky
/// Scratch Ticket.
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
    [SerializeField] private KeyCode toggleKey = KeyCode.BackQuote;
    [Tooltip("Start with the panel already visible - handy while actively iterating; flip off once it's just sitting in the scene for occasional use.")]
    [SerializeField] private bool startVisible = false;

    private GameObject panelRoot;

    private void Awake()
    {
        if (featureTrigger == null) featureTrigger = FindAnyObjectByType<MadnessFeatureTrigger>();
        if (featureTrigger == null)
            Debug.LogWarning("[DebugFeatureModeMenu] No MadnessFeatureTrigger found in scene - buttons will be built but won't do anything.");

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

    private static void AddButton(Transform parent, string label, System.Action onClick)
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
    }
}
#endif
