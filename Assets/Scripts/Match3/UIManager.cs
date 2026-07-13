using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Board board;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private StageManager stageManager;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI movesText;
    [SerializeField] private TextMeshProUGUI stageText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Button nextStageButton;
    [SerializeField] private Button newRunButton;
    [SerializeField] private GameObject stageClearPanel;
    [SerializeField] private GameObject gameOverPanel;

    private void Awake()
    {
        if (board == null) board = FindAnyObjectByType<Board>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Subscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Subscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Subscribe<GameOverEvent>(HandleGameOver);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Unsubscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Unsubscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Unsubscribe<GameOverEvent>(HandleGameOver);
    }

    private void Start()
    {
        if (nextStageButton != null)
            nextStageButton.onClick.AddListener(HandleNextStageClicked);

        if (newRunButton != null)
            newRunButton.onClick.AddListener(HandleNewRunClicked);

        SetStageClearVisible(false);
        SetGameOverVisible(false);
        RefreshDisplay();
    }

    private void HandleScoreChanged(ScoreChangedEvent evt)
    {
        RefreshDisplay();
    }

    private void HandlePlayerMove(PlayerMoveEvent evt)
    {
        RefreshDisplay();
    }

    private void HandleHealthChanged(HealthChangedEvent evt)
    {
        RefreshDisplay();
    }

    private void HandleStageStarted(StageStartedEvent evt)
    {
        RefreshDisplay();
        SetStageClearVisible(false);
        SetGameOverVisible(false);
        SetMessage($"Stage {evt.StageIndex + 1}: {evt.Stage.name}");
    }

    private void HandleStageCompleted(StageCompletedEvent evt)
    {
        SetStageClearVisible(true);
        SetMessage("Stage cleared!");
    }

    private void HandleNextStageClicked()
    {
        if (stageManager != null)
            stageManager.AdvanceToNextStage();
    }

    private void HandleNewRunClicked()
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

    private void SetStageClearVisible(bool visible)
    {
        if (stageClearPanel != null)
            stageClearPanel.SetActive(visible);

        if (nextStageButton != null)
            nextStageButton.gameObject.SetActive(visible);
    }

    private void SetGameOverVisible(bool visible)
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(visible);

        if (newRunButton != null)
            newRunButton.gameObject.SetActive(visible);
    }

    private void HandleGameOver(GameOverEvent evt)
    {
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
