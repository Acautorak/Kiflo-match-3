using System.Collections;
using UnityEngine;

public class StageManager : MonoBehaviour
{
    [Header("Stage Setup")]
    [SerializeField] private Board board;
    [SerializeField] private StageDefinition[] stages;
    [SerializeField] private float stageClearDelay = 1.5f;
    [SerializeField] private bool autoStartFirstStage = true;

    private int currentStageIndex = -1;
    private StageDefinition currentStage;

    public int CurrentStageIndex => currentStageIndex;
    private bool isTransitioning;

   

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
    }

    private void Start()
    {
        if (autoStartFirstStage)
            StartStage(0);
    }

    public bool HasStagesConfigured() => stages != null && stages.Length > 0;

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

        if (board != null)
            board.ResetForStage(currentStage);

        EventBus.Publish(new StageStartedEvent(currentStageIndex, currentStage));
        Debug.Log($"[StageManager] Started stage {currentStageIndex + 1}: {currentStage.name}");
    }

    public void AdvanceToNextStage()
    {
        if (stages == null || stages.Length == 0) return;
        if (currentStageIndex < 0) return;
        if (isTransitioning) return;

        var nextIndex = currentStageIndex + 1;
        if (nextIndex >= stages.Length)
        {
            Debug.Log("[StageManager] All stages completed.");
            return;
        }

        StartCoroutine(TransitionToStage(nextIndex));
    }

    private IEnumerator TransitionToStage(int nextIndex)
    {
        isTransitioning = true;
        EventBus.Publish(new StageCompletedEvent(currentStageIndex, board != null ? board.CurrentScore : 0));

        if (board != null)
        {
            board.ClearBoard();
            Debug.Log($"[StageManager] Cleared previous stage symbols before transitioning to stage {nextIndex + 1}.");
        }

        yield return new WaitForSeconds(stageClearDelay);
        StartStage(nextIndex);
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
        if (currentStage == null || isTransitioning) return;
        if (currentStage.goalType == StageGoalType.None)
            return;

        if (board != null)
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} complete with score {board.CurrentScore} and {board.MoveCount} moves.");

        AdvanceToNextStage();
    }
}
