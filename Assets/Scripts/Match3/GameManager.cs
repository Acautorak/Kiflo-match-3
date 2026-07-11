using UnityEngine;

/// <summary>
/// Minimal glue script: listens on the bus for score/chain updates and triggers saves
/// at safe points (pause/quit). Replace the Debug.Log calls with real UI hookups.
/// </summary>
public class GameManager : MonoBehaviour
{
    [SerializeField] private Board board;

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<ChainMatchedEvent>(HandleChainMatched);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<ChainMatchedEvent>(HandleChainMatched);
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

    private void OnApplicationQuit() => board.SaveNow();

    private void OnApplicationPause(bool paused)
    {
        if (paused) board.SaveNow();
    }
}
