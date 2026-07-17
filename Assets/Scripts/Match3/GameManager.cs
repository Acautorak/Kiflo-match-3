using UnityEngine;

/// <summary>
/// Minimal glue script: listens on the bus for score/chain updates and triggers saves
/// at safe points (pause/quit). Replace the Debug.Log calls with real UI hookups.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>
    /// Idle: waiting for player input (nothing busy on the board).
    /// Playing: an accepted swap is currently animating.
    /// ResolvingMatches: matches/cascades are being cleared, scored, and refilled.
    /// ResolvingSpecialMadness: a special-symbol activation or random "gravity bonus" effect
    /// is animating/calculating as part of the current cascade step.
    /// ResolvingMadnessColorChange: a Madness effect is repainting other board symbols (Board.
    /// ConvertRandomSymbols / RandomizeSymbolColors) and playing their staggered highlight
    /// sequence. Same "something's animating mid-cascade" role as ResolvingSpecialMadness, just
    /// its own state so UI can tell the two apart if it wants to (e.g. a different banner/SFX).
    /// MadnessBonusGameplay: reserved for a future dedicated bonus round - not implemented
    /// yet, nothing in the project currently transitions into or out of this state.
    /// GracePeriod: stage-clear goal was reached; player gets a few extra moves.
    /// StageClearing: stage-clear cleanup animation, or game-over lockout.
    /// </summary>
    public enum GameplayState
    {
        Idle,
        Playing,
        ResolvingMatches,
        ResolvingSpecialMadness,
        ResolvingMadnessColorChange,
        MadnessBonusGameplay,
        GracePeriod,
        StageClearing
    }

    [SerializeField] private Board board;
    [SerializeField] private StageManager stageManager;

    private GameplayState currentState = GameplayState.Idle;

    public GameplayState CurrentState => currentState;
    public bool AllowsBoardRefill => currentState == GameplayState.Playing
        || currentState == GameplayState.ResolvingMatches
        || currentState == GameplayState.ResolvingSpecialMadness
        || currentState == GameplayState.ResolvingMadnessColorChange;
    public bool AllowsPlayerInput => currentState == GameplayState.Idle || currentState == GameplayState.GracePeriod;

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

    /// <summary>
    /// Reserved for a future dedicated bonus round. Nothing calls this yet, and no gameplay
    /// is implemented for MadnessBonusGameplay - it only flips the state so UI/other systems
    /// can start reacting to it (e.g. showing a placeholder banner) ahead of the real feature.
    /// </summary>
    public void EnterMadnessBonusGameplay()
    {
        Debug.Log("[GameManager] MadnessBonusGameplay requested - not implemented yet, no-op beyond the state change.");
        SetState(GameplayState.MadnessBonusGameplay);
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
        SetState(GameplayState.Idle);
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
