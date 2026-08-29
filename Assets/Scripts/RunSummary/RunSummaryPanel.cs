using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Displays a finished run's stats (see RunSummaryData). Listens for RunSummaryReadyEvent so it
/// shows automatically whenever a run ends - same optional-panel shape as CookieFortunePanel: a
/// root GameObject toggled active/inactive, TMP text fields, one dismiss button. Assumes
/// TextMeshPro - swap the TMP_Text fields for UnityEngine.UI.Text if this project isn't using TMP.
/// </summary>
public class RunSummaryPanel : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text stagesClearedText;
    [SerializeField] private TMP_Text movesText;
    [SerializeField] private TMP_Text damageTakenText;
    [SerializeField] private TMP_Text longestChainText;
    [SerializeField] private TMP_Text symbolsClearedText;
    [SerializeField] private TMP_Text madnessSymbolsClearedText;
    [SerializeField] private TMP_Text durationText;

    [Header("Powerups Collected")]
    [Tooltip("Parent Transform each collected powerup's icon is instantiated under - typically a " +
             "Horizontal/Grid Layout Group. Leave empty to skip the powerup row entirely.")]
    [SerializeField] private Transform powerupIconContainer;
    [Tooltip("Prefab with an Image component - instantiated once per collected powerup, sprite set to that powerup's icon.")]
    [SerializeField] private Image powerupIconPrefab;

    [SerializeField] private Button dismissButton;
    public UnityEngine.Events.UnityEvent OnDismissed;

    private readonly List<GameObject> spawnedIcons = new List<GameObject>();
    private RunSummaryData latestData;

    private void Awake()
    {
        if (dismissButton != null) dismissButton.onClick.AddListener(HandleDismissClicked);
        if (root != null) root.SetActive(false);
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<RunSummaryReadyEvent>(HandleRunSummaryReady);
        EventBus.Subscribe<RunSummaryReadyEvent>(HandleRunSummaryReady);
    }

    private void OnDisable() => EventBus.Unsubscribe<RunSummaryReadyEvent>(HandleRunSummaryReady);

    /// <summary>
    /// Caches the data only - does NOT show the panel. Showing is now driven explicitly by
    /// UIManager once the player clicks Retry on the Game Over screen (see ShowLatest), not
    /// automatically the instant RunSummaryReadyEvent fires (which happens right as the run
    /// ends, potentially mid-cascade - by the time the player has seen Game Over and clicked
    /// Retry, the board has long since settled, so no "wait until Idle" polling is needed here).
    /// </summary>
    private void HandleRunSummaryReady(RunSummaryReadyEvent evt) => latestData = evt.Data;

    /// <summary>Shows the most recently cached summary, if any. Returns false (shows nothing) if
    /// no RunSummaryReadyEvent has fired yet - callers should fall back to their own behavior in
    /// that case rather than assume this always succeeds.</summary>
    public bool ShowLatest()
    {
        if (latestData == null) return false;
        Show(latestData);
        return true;
    }

    public void Show(RunSummaryData data)
    {
        if (scoreText != null) scoreText.text = $"Score: {data.FinalScore:N0}";
        if (stagesClearedText != null) stagesClearedText.text = $"Stages Cleared: {data.StagesCleared}";
        if (movesText != null) movesText.text = $"Moves Made: {data.TotalMoves}";
        if (damageTakenText != null) damageTakenText.text = $"Damage Taken: {data.DamageTaken}";
        if (longestChainText != null) longestChainText.text = $"Longest Chain: {data.LongestChain}";
        if (symbolsClearedText != null) symbolsClearedText.text = $"Symbols Cleared: {data.TotalSymbolsCleared}";
        if (madnessSymbolsClearedText != null) madnessSymbolsClearedText.text = $"Madness Symbols Cleared: {data.MadnessSymbolsCleared}";
        if (durationText != null) durationText.text = $"Time: {FormatDuration(data.RunDurationSeconds)}";

        PopulatePowerupIcons(data.PowerupsCollected);

        if (root != null) root.SetActive(true);
    }

    private void PopulatePowerupIcons(IReadOnlyList<PowerupDefinition> powerups)
    {
        foreach (var icon in spawnedIcons)
            if (icon != null) Destroy(icon);
        spawnedIcons.Clear();

        if (powerupIconContainer == null || powerupIconPrefab == null || powerups == null) return;

        foreach (var p in powerups)
        {
            if (p == null) continue;
            var instance = Instantiate(powerupIconPrefab, powerupIconContainer);
            instance.sprite = p.icon;
            instance.enabled = p.icon != null;
            instance.gameObject.SetActive(true);
            spawnedIcons.Add(instance.gameObject);
        }
    }

    private static string FormatDuration(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.RoundToInt(seconds));
        int minutes = totalSeconds / 60;
        int secs = totalSeconds % 60;
        return $"{minutes:00}:{secs:00}";
    }

    private void HandleDismissClicked()
    {
        if (root != null) root.SetActive(false);
        OnDismissed?.Invoke();
    }
}
