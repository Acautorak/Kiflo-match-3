using System.Collections;
using UnityEngine;

public class StageManager : MonoBehaviour
{
    [Header("Stage Setup")]
    [SerializeField] private Board board;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private StageDefinition[] stages;
    [SerializeField] private bool autoStartFirstStage = true;

    private int currentStageIndex = -1;
    private StageDefinition currentStage;

    public int CurrentStageIndex => currentStageIndex;
    private bool isTransitioning;
    private bool isStageCleared;
    private bool isStageClearPending;
    private int remainingGraceMoves;

   

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<GameOverEvent>(HandleGameOver);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Unsubscribe<GameOverEvent>(HandleGameOver);
    }

    private void Start()
    {
        if (!autoStartFirstStage) return;

        var saved = SaveSystem.Load();
        if (saved != null && saved.currentStageIndex >= 0 && saved.currentStageIndex < stages.Length)
        {
            currentStageIndex = saved.currentStageIndex;
            currentStage = stages[currentStageIndex];
            isTransitioning = false;
            isStageCleared = false;
            EventBus.Publish(new StageStartedEvent(currentStageIndex, currentStage));
            Debug.Log($"[StageManager] Restored saved stage {currentStageIndex + 1}: {currentStage.name}");
            return;
        }

        StartStage(0);
    }

    public bool HasStagesConfigured() => stages != null && stages.Length > 0;

    public void LoadStageState(int index)
    {
        if (index < 0 || index >= stages.Length)
            return;

        currentStageIndex = index;
        currentStage = stages[index];
        isTransitioning = false;
        isStageCleared = false;
    }

    public void StartStage(int index)
    {
        if (!HasStagesConfigured())
        {
            Debug.LogWarning("[StageManager] No stages configured.");
            return;
        }

        if (index < 0 || index >= stages.Length)
        {
            Debug.LogWarning($"[StageManager] Stage index {index} is out of range.");
            return;
        }

        currentStageIndex = index;
        currentStage = stages[index];
        isTransitioning = false;
        isStageCleared = false;
        isStageClearPending = false;
        remainingGraceMoves = 0;

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.Playing);

        if (board != null)
            board.ResetForStage(currentStage);

        EventBus.Publish(new StageStartedEvent(currentStageIndex, currentStage));
        Debug.Log($"[StageManager] Started stage {currentStageIndex + 1}: {currentStage.name}");
    }

    public void AdvanceToNextStage()
    {
        if (stages == null || stages.Length == 0) return;
        if (currentStageIndex < 0) return;
        if (!isStageCleared) return;

        var nextIndex = currentStageIndex + 1;
        if (nextIndex >= stages.Length)
        {
            Debug.Log("[StageManager] All stages completed.");
            return;
        }

        if (board != null)
            board.ClearBoard();

        StartStage(nextIndex);
    }

    public void StartNewRun()
    {
        if (playerHealth != null)
        {
            playerHealth.ResetForNewRun();
            EventBus.Publish(new HealthChangedEvent(playerHealth.CurrentHealth, playerHealth.MaxHealth));
        }

        if (board != null)
            board.ClearBoard();

        StartStage(0);
    }

    public void FinalizeStageClear()
    {
        if (currentStage == null || !isStageClearPending || isStageCleared) return;

        isStageCleared = true;
        isStageClearPending = false;

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.StageClearing);

        if (board != null)
        {
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} clearing after grace period.");
            board.BeginStageClearCleanup(() =>
            {
                EventBus.Publish(new StageCompletedEvent(currentStageIndex, board.CurrentScore));
            });
        }
        else
        {
            EventBus.Publish(new StageCompletedEvent(currentStageIndex, 0));
        }
    }

    private void HandleScoreChanged(ScoreChangedEvent evt)
    {
        if (currentStage == null) return;
        if (currentStage.goalType != StageGoalType.Score) return;
        if (evt.NewScore >= currentStage.goalValue)
            CompleteStage();
    }

    private void HandlePlayerMove(PlayerMoveEvent evt)
    {
        if (currentStage == null) return;
        if (currentStage.goalType != StageGoalType.MoveCount) return;
        if (evt.MoveCount >= currentStage.goalValue)
            CompleteStage();
    }

    private void CompleteStage()
    {
        if (currentStage == null || isTransitioning || isStageClearPending) return;
        if (currentStage.goalType == StageGoalType.None)
            return;

        isTransitioning = true;
        isStageClearPending = true;
        isStageCleared = false;
        remainingGraceMoves = currentStage != null ? currentStage.gracePeriodMoves : 3;

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.GracePeriod);

        if (board != null)
            board.SetGraceStateActive(true);

        if (board != null)
        {
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} goal reached. Player has {remainingGraceMoves} extra moves before clear.");
            board.BeginStageClearGracePeriod(remainingGraceMoves, currentStage != null ? currentStage.gracePeriodRandomSpecialChance : 0f);
        }
        else
        {
            FinalizeStageClear();
        }
    }

    public void ConsumeStageClearGraceMove()
    {
        if (!isStageClearPending || remainingGraceMoves <= 0) return;

        remainingGraceMoves--;
        if (remainingGraceMoves <= 0)
            FinalizeStageClear();
    }

    private void HandleGameOver(GameOverEvent evt)
    {
        isTransitioning = true;
        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.StageClearing);
        Debug.Log("[StageManager] Player lost. Start a new run from the UI.");
    }
}
