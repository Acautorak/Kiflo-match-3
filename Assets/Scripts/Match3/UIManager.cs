using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Board board;
    [SerializeField] private StageManager stageManager;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI movesText;
    [SerializeField] private TextMeshProUGUI stageText;
    [SerializeField] private TextMeshProUGUI messageText;
    [SerializeField] private Slider healthSlider;
    [SerializeField] private Button nextStageButton;
    [SerializeField] private GameObject stageClearPanel;

    private void Awake()
    {
        if (board == null) board = FindObjectOfType<Board>();
        if (stageManager == null) stageManager = FindObjectOfType<StageManager>();
    }

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Subscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Subscribe<StageCompletedEvent>(HandleStageCompleted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Unsubscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Unsubscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);
    }

    private void Start()
    {
        if (nextStageButton != null)
            nextStageButton.onClick.AddListener(HandleNextStageClicked);

        SetStageClearVisible(false);
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

    private void RefreshDisplay()
    {
        if (board == null) return;

        if (scoreText != null)
            scoreText.text = $"Score: {board.CurrentScore}";

        if (movesText != null)
            movesText.text = $"Moves: {board.MoveCount}";

        if (stageText != null)
            stageText.text = $"Stage: {(stageManager != null ? stageManager.CurrentStageIndex + 1 : 1)}";

        if (healthSlider != null)
        {
            healthSlider.maxValue = board.MaxHealth;
            healthSlider.value = board.CurrentHealth;
        }
    }

    private void SetStageClearVisible(bool visible)
    {
        if (stageClearPanel != null)
            stageClearPanel.SetActive(visible);

        if (nextStageButton != null)
            nextStageButton.gameObject.SetActive(visible);
    }

    private void SetMessage(string text)
    {
        if (messageText != null)
            messageText.text = text;
    }
}
