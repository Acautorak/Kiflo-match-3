using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Board board;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private StageManager stageManager;
    [Tooltip("Used to defer showing the Game Over panel until the board is settled (Idle or " +
             "GracePeriod) - see HandleGameOver. Auto-found in Awake if left empty.")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI movesText;
    [SerializeField] private TextMeshProUGUI stageText;
    [SerializeField] private TextMeshProUGUI goalText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Button newRunButton;
    [SerializeField] private GameObject stageClearPanel;
    [SerializeField] private GameObject gameOverPanel;
    [Tooltip("Shown after New Run is clicked, before the run actually restarts - see " +
             "HandleNewRunClicked. Optional: if left unassigned, New Run restarts immediately " +
             "the same way it always did.")]
    [SerializeField] private RunSummaryPanel runSummaryPanel;

    private void Awake()
    {
        if (board == null) board = FindAnyObjectByType<Board>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Subscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Subscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Subscribe<GameOverEvent>(HandleGameOver);
        EventBus.Subscribe<GameStateChangedEvent>(HandleGameStateChanged);
        EventBus.Subscribe<MadnessSymbolClearedEvent>(HandleMadnessSymbolClearedForGoalDisplay);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Unsubscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Unsubscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Unsubscribe<GameOverEvent>(HandleGameOver);
        EventBus.Unsubscribe<GameStateChangedEvent>(HandleGameStateChanged);
        EventBus.Unsubscribe<MadnessSymbolClearedEvent>(HandleMadnessSymbolClearedForGoalDisplay);
    }

    /// <summary>Purely a HUD refresh trigger for MadnessCleared - StageManager owns the actual
    /// progress counting, this just re-reads it once it's changed.</summary>
    private void HandleMadnessSymbolClearedForGoalDisplay(MadnessSymbolClearedEvent evt) => RefreshGoalDisplay();

    private void Start()
    {
        if (newRunButton != null)
            newRunButton.onClick.AddListener(HandleNewRunClicked);

        if (runSummaryPanel != null)
            runSummaryPanel.OnDismissed.AddListener(HandleRunSummaryDismissed);

        SetStageClearVisible(false);
        SetGameOverVisible(false);
        RefreshDisplay();
        RefreshGoalDisplay();
    }

    private void HandleScoreChanged(ScoreChangedEvent evt)
    {
        RefreshDisplay();
        RefreshGoalDisplay();
    }

    private void HandlePlayerMove(PlayerMoveEvent evt)
    {
        RefreshDisplay();
        RefreshGoalDisplay();
    }

    private void HandleHealthChanged(HealthChangedEvent evt)
    {
        RefreshDisplay();
    }

    private void HandleStageStarted(StageStartedEvent evt)
    {
        _pendingGameOverReveal = false;
        RefreshDisplay();
        RefreshGoalDisplay();
        SetStageClearVisible(false);
        SetGameOverVisible(false);
        SetMessage($"Stage {evt.StageIndex + 1}: {evt.Stage.name}");
    }

    private void HandleStageCompleted(StageCompletedEvent evt)
    {
        SetStageClearVisible(true);
        SetMessage("Stage cleared!");
    }

    /// <summary>
    /// Clicking New Run no longer restarts immediately - it shows the run summary first (see
    /// RunSummaryPanel.ShowLatest), and the actual reset+restart now happens in
    /// HandleRunSummaryDismissed once the player has seen it. Falls back to the old immediate-
    /// restart behavior if no RunSummaryPanel is assigned (or it has nothing cached yet), so New
    /// Run always does SOMETHING rather than silently doing nothing.
    /// </summary>
    private void HandleNewRunClicked()
    {
        SetGameOverVisible(false);

        if (runSummaryPanel != null && runSummaryPanel.ShowLatest())
            return; // RestartRun continues from HandleRunSummaryDismissed once the player dismisses it

        RestartRun();
    }

    private void HandleRunSummaryDismissed() => RestartRun();

    private void RestartRun()
    {
        if (playerHealth != null)
            playerHealth.ResetToFullHealth();

        if (stageManager != null)
            stageManager.StartNewRun();

        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (board != null)
        {
            if (scoreText != null)
                scoreText.text = $"Score: {board.CurrentScore}";

            if (movesText != null)
                movesText.text = $"Moves: {board.MoveCount}";

            if (stageText != null)
                stageText.text = $"Stage: {(stageManager != null ? stageManager.CurrentStageIndex + 1 : 1)}";
        }

        if (healthSlider != null && playerHealth != null)
        {
            var max = Mathf.Max(1, playerHealth.MaxHealth);
            healthSlider.maxValue = max;
            healthSlider.value = Mathf.Clamp(playerHealth.CurrentHealth, 0, max);
        }
    }

    private void RefreshGoalDisplay()
    {
        if (goalText == null || stageManager == null) return;

        switch (stageManager.CurrentGoalType)
        {
            case StageGoalType.Score:
                goalText.text = $"Score Goal: {(board != null ? board.CurrentScore : 0)}/{stageManager.CurrentGoalValue}";
                break;
            case StageGoalType.MoveCount:
                goalText.text = $"Moves Goal: {(board != null ? board.MoveCount : 0)}/{stageManager.CurrentGoalValue}";
                break;
            case StageGoalType.Collect:
                goalText.text = FormatCollectGoalText();
                break;
            case StageGoalType.ChainCombo:
                // Momentary (a single-move threshold, not an accumulating total) - nothing
                // meaningful to show as "progress", just the target itself.
                goalText.text = $"Chain Goal: reach a {stageManager.CurrentGoalValue}+ combo in one move";
                break;
            case StageGoalType.CascadeCollect:
                goalText.text = $"Cascade Goal: clear {stageManager.CurrentGoalValue}+ in one cascade";
                break;
            case StageGoalType.MadnessCleared:
                goalText.text = $"Madness Goal: {stageManager.CurrentMadnessClearedCount}/{stageManager.CurrentGoalValue} cleared";
                break;
            case StageGoalType.SurviveNoDamage:
                goalText.text = $"No-Damage Streak: {stageManager.CurrentNoDamageStreak}/{stageManager.CurrentGoalValue}";
                break;
            default:
                goalText.text = string.Empty;
                break;
        }
    }

    private string FormatCollectGoalText()
    {
        var entries = stageManager.CurrentCollectProgress;
        if (entries == null || entries.Count == 0) return string.Empty;

        var parts = new List<string>(entries.Count);
        foreach (var entry in entries)
            parts.Add($"{entry.SymbolType} {entry.Current}/{entry.Target}");

        return "Collect: " + string.Join("   ", parts);
    }

    private void SetStageClearVisible(bool visible)
    {
        if (stageClearPanel != null)
            stageClearPanel.SetActive(visible);
        // No button here anymore - PowerupManager/PowerupSelectionUI take over from here and
        // call StageManager.AdvanceToNextStage() once a powerup is picked (or automatically if
        // no Powerup Pool Config is assigned - see PowerupManager).
    }

    private void SetGameOverVisible(bool visible)
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(visible);

        if (newRunButton != null)
            newRunButton.gameObject.SetActive(visible);
    }

    /// <summary>True from the moment GameOverEvent fires until the reveal actually happens (see
    /// TryRevealGameOverIfSettled) - GameOverEvent can fire mid-cascade (e.g. a color-damage roll
    /// during match resolution), so showing the panel immediately could pop up on top of tiles
    /// still popping/falling. Same "wait for Idle/GracePeriod" definition of settled that
    /// MadnessFeatureTrigger already uses for feature-mode starts.</summary>
    private bool _pendingGameOverReveal;

    private void HandleGameOver(GameOverEvent evt)
    {
        _pendingGameOverReveal = true;
        TryRevealGameOverIfSettled(); // in case the board already happens to be settled right now
    }

    private void HandleGameStateChanged(GameStateChangedEvent evt)
    {
        if (_pendingGameOverReveal) TryRevealGameOverIfSettled();

        // SurviveNoDamage's streak is judged/updated by StageManager at exactly this moment (a
        // move's resolution settling) - refresh so the HUD isn't showing a stale count until some
        // unrelated event happens to trigger a refresh next.
        if (stageManager != null && stageManager.CurrentGoalType == StageGoalType.SurviveNoDamage)
            RefreshGoalDisplay();
    }

    private void TryRevealGameOverIfSettled()
    {
        if (!_pendingGameOverReveal) return;

        bool settled = gameManager == null
            || gameManager.CurrentState == GameManager.GameplayState.Idle
            || gameManager.CurrentState == GameManager.GameplayState.GracePeriod;
        if (!settled) return;

        _pendingGameOverReveal = false;
        SetStageClearVisible(false);
        SetGameOverVisible(true);
        SetMessage("Game over. Start a new run.");
    }

    private void SetMessage(string text)
    {
        if (messageText != null)
            messageText.text = text;
    }
}
