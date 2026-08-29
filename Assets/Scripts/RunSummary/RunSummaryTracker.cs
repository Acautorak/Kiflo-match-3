using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Accumulates run-scoped stats purely by listening to events already published elsewhere - no
/// changes needed anywhere else in the codebase to produce this data. Resets whenever
/// StageStartedEvent fires for stage index 0 (the tail end of StageManager.StartNewRun's own
/// StartStage(0) call) - deliberately event-driven rather than requiring StageManager to know
/// this class exists and call into it directly. Finalizes and publishes RunSummaryReadyEvent when
/// GameOverEvent fires - GameOverEvent.FinalScore is used directly rather than tracked separately
/// via ScoreChangedEvent, since it's already exactly the number needed.
/// </summary>
public class RunSummaryTracker : MonoBehaviour
{
    private int deepestStageIndex;
    private int totalMoves;
    private int damageTaken;
    private int lastKnownHealth = -1;
    private int longestChain;
    private int totalSymbolsCleared;
    private int madnessSymbolsCleared;
    private float runStartTime;
    private readonly List<PowerupDefinition> powerupsCollected = new List<PowerupDefinition>();

    private void OnEnable()
    {
        Unsubscribe(); // defensive - same double-subscribe guard every other listener in this codebase uses
        EventBus.Subscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Subscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Subscribe<PowerupSelectedEvent>(HandlePowerupSelected);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<ChainMatchedEvent>(HandleChainMatched);
        EventBus.Subscribe<MadnessSymbolClearedEvent>(HandleMadnessSymbolCleared);
        EventBus.Subscribe<GameOverEvent>(HandleGameOver);
    }

    private void OnDisable() => Unsubscribe();

    private void Unsubscribe()
    {
        EventBus.Unsubscribe<StageStartedEvent>(HandleStageStarted);
        EventBus.Unsubscribe<HealthChangedEvent>(HandleHealthChanged);
        EventBus.Unsubscribe<PowerupSelectedEvent>(HandlePowerupSelected);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Unsubscribe<ChainMatchedEvent>(HandleChainMatched);
        EventBus.Unsubscribe<MadnessSymbolClearedEvent>(HandleMadnessSymbolCleared);
        EventBus.Unsubscribe<GameOverEvent>(HandleGameOver);
    }

    private void HandleStageStarted(StageStartedEvent evt)
    {
        if (evt.StageIndex != 0)
        {
            // Track deepest stage reached regardless of whether this is a fresh run or a resumed
            // save that happens to start mid-run.
            deepestStageIndex = Mathf.Max(deepestStageIndex, evt.StageIndex);
            return;
        }

        // Stage 0 - the tail end of StageManager.StartNewRun() - reset everything for the new run.
        deepestStageIndex = 0;
        totalMoves = 0;
        damageTaken = 0;
        lastKnownHealth = -1;
        longestChain = 0;
        totalSymbolsCleared = 0;
        madnessSymbolsCleared = 0;
        runStartTime = Time.unscaledTime;
        powerupsCollected.Clear();
    }

    private void HandleHealthChanged(HealthChangedEvent evt)
    {
        // A decrease is damage; an increase (a heal, or a bonusMaxHealth powerup raising the
        // ceiling) never counts, so this can't be thrown off by anything that raises health.
        if (lastKnownHealth >= 0 && evt.CurrentHealth < lastKnownHealth)
            damageTaken += lastKnownHealth - evt.CurrentHealth;

        lastKnownHealth = evt.CurrentHealth;
    }

    private void HandlePowerupSelected(PowerupSelectedEvent evt)
    {
        if (evt.Powerup != null) powerupsCollected.Add(evt.Powerup);
    }

    // PlayerMoveEvent.MoveCount resets per stage (see its own doc comment - "the running total
    // for the whole board's lifetime since the last stage start"), so a run-wide total has to
    // come from counting how many times this fires, not from reading the payload directly.
    private void HandlePlayerMove(PlayerMoveEvent evt) => totalMoves++;

    private void HandleChainMatched(ChainMatchedEvent evt)
    {
        longestChain = Mathf.Max(longestChain, evt.ChainCount);
        totalSymbolsCleared += evt.SymbolsCleared;
    }

    private void HandleMadnessSymbolCleared(MadnessSymbolClearedEvent evt) => madnessSymbolsCleared++;

    private void HandleGameOver(GameOverEvent evt)
    {
        float duration = Mathf.Max(0f, Time.unscaledTime - runStartTime);
        var data = new RunSummaryData(
            evt.FinalScore, deepestStageIndex + 1, totalMoves, damageTaken,
            longestChain, totalSymbolsCleared, madnessSymbolsCleared,
            duration, powerupsCollected.ToArray());

        EventBus.Publish(new RunSummaryReadyEvent(data));
    }
}
