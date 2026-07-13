using UnityEngine;

/// <summary>
/// Minimal glue script: listens on the bus for score/chain updates and triggers saves
/// at safe points (pause/quit). Replace the Debug.Log calls with real UI hookups.
/// </summary>
public class GameManager : MonoBehaviour
{
    public enum GameplayState
    {
        Idle,
        Playing,
        GracePeriod,
        StageClearing
    }

    [SerializeField] private Board board;
    [SerializeField] private StageManager stageManager;

    private GameplayState currentState = GameplayState.Playing;

    public GameplayState CurrentState => currentState;
    public bool AllowsBoardRefill => currentState == GameplayState.Playing;
    public bool AllowsPlayerInput => currentState == GameplayState.Playing || currentState == GameplayState.GracePeriod;

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<ChainMatchedEvent>(HandleChainMatched);
        EventBus.Subscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Subscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Subscribe<GameOverEvent>(HandleGameOver);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<ChainMatchedEvent>(HandleChainMatched);
        EventBus.Unsubscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Unsubscribe<GameOverEvent>(HandleGameOver);
    }

    public void SetState(GameplayState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
        Debug.Log($"[GameManager] State -> {currentState}");
    }

    private void HandleScoreChanged(ScoreChangedEvent evt)
    {
        Debug.Log($"Score: {evt.NewScore} (+{evt.Delta})");
    }

    private void HandleChainMatched(ChainMatchedEvent evt)
    {
        if (evt.ChainCount > 1)
            Debug.Log($"Combo x{evt.ChainCount}!");
    }

    private void HandleStageStarted(StageStartedEvent evt)
    {
        SetState(GameplayState.Playing);
    }

    private void HandleStageCompleted(StageCompletedEvent evt)
    {
        SetState(GameplayState.Idle);
    }

    private void HandleGameOver(GameOverEvent evt)
    {
        SetState(GameplayState.StageClearing);
    }

    private void OnApplicationQuit() => board.SaveNow();

    private void OnApplicationPause(bool paused)
    {
        if (paused) board.SaveNow();
    }
}
