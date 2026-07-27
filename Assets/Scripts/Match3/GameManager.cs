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
    /// FeatureMode: a "feature" mini-game triggered by a madness meter threshold is running
    /// (e.g. Kebab Karnage's falling-asteroid mode, see KebabKarnageManager). Normal board input
    /// is disabled the same way it already is outside Idle/GracePeriod - the mini-game reads its
    /// own input directly (see KebabKarnageManager's centralized tap detection) independent of
    /// AllowsPlayerInput. See SetState for how this state resists being overwritten by anything
    /// other than ExitFeatureMode while active.
    /// ResolvingFreeSpin: a Free Spins reel result is being resolved (cascades/scoring/refill via
    /// the normal MatchResolver pipeline - see Board.PlayFreeSpin/FreeSpinsController). This is
    /// deliberately its own state rather than reusing ResolvingMatches/ResolvingSpecialMadness/
    /// ResolvingMadnessColorChange, because those are blocked outright while FeatureMode is
    /// active (see SetState) - a stray leftover coroutine resuming into one of them mid-feature-
    /// mode would wrongly look like normal play resuming. ResolvingFreeSpin is treated as part of
    /// the same continuous span as FeatureMode (see SetState/IsInFeatureMode) since it's Free
    /// Spins' own mini-game legitimately doing work, not something trying to escape it.
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
        FeatureMode,
        ResolvingFreeSpin,
        GracePeriod,
        StageClearing
    }

    [SerializeField] private Board board;
    [SerializeField] private StageManager stageManager;

    private void Awake()
    {
        // Doesn't auto-destroy duplicates - Board's own gameManager reference was likely wired
        // by hand in the inspector to a specific instance, and destroying the "wrong" one at
        // runtime could break that reference instead of fixing anything. This just makes the
        // problem loudly visible so it can be fixed in the scene directly.
        var all = FindObjectsByType<GameManager>(FindObjectsSortMode.None);
        if (all.Length > 1)
        {
            var names = string.Join(", ", System.Array.ConvertAll(all, g => g.gameObject.name));
            Debug.LogWarning($"[GameManager] {all.Length} GameManager components found in the scene ({names}) - " +
                              "different systems (Board, KebabKarnageManager, etc.) may end up reading state from " +
                              "different instances if their inspector references don't all point at the same one. " +
                              "Remove the extra(s) so only one GameManager exists.");
        }
    }

    private GameplayState currentState = GameplayState.Idle;
    private GameplayState stateBeforeFeatureMode = GameplayState.Idle;

    public GameplayState CurrentState => currentState;
    public bool AllowsBoardRefill => currentState == GameplayState.Playing
        || currentState == GameplayState.ResolvingMatches
        || currentState == GameplayState.ResolvingSpecialMadness
        || currentState == GameplayState.ResolvingMadnessColorChange
        || currentState == GameplayState.ResolvingFreeSpin;
    public bool AllowsPlayerInput => currentState == GameplayState.Idle || currentState == GameplayState.GracePeriod;
    /// <summary>True for the whole Free Spins span, not just the FeatureMode "waiting for the next
    /// spin" moments - ResolvingFreeSpin (a spin's own cascade resolving) counts too.</summary>
    public bool IsInFeatureMode => currentState == GameplayState.FeatureMode || currentState == GameplayState.ResolvingFreeSpin;

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

    /// <summary>
    /// Requests a state change. While FeatureMode (or ResolvingFreeSpin - see below) is active,
    /// this refuses to move to any state that would re-enable player input (Idle, GracePeriod) or
    /// resume mid-cascade animation states (Playing, ResolvingMatches, etc.) - only ExitFeatureMode
    /// can properly leave FeatureMode. Without this, something that was already resolving when a
    /// feature mode started (e.g. Board's own cascade-complete callback finishing AFTER
    /// EnterFeatureMode was called, since that coroutine keeps running independently) can call
    /// SetState(Idle) on its way out and silently hand board input back to the player while
    /// asteroids are still falling. StageClearing/MadnessBonusGameplay are still allowed through
    /// since neither re-enables input anyway.
    ///
    /// One explicit exception: FeatureMode <-> ResolvingFreeSpin is allowed both ways.
    /// ResolvingFreeSpin is Free Spins' own "a reel result is cascading" state (see
    /// FreeSpinsController.SpinOnce) - it's part of the same continuous feature-mode span, not
    /// something trying to escape it, so it's treated as active-FeatureMode for this guard's
    /// purposes too (see IsFeatureModeGuardActive) rather than being blocked like the plain
    /// resolving states are.
    /// </summary>
    public void SetState(GameplayState newState)
    {
        if (currentState == newState) return;

        bool isFreeSpinInternalTransition =
            (currentState == GameplayState.FeatureMode && newState == GameplayState.ResolvingFreeSpin)
            || (currentState == GameplayState.ResolvingFreeSpin && newState == GameplayState.FeatureMode);

        bool wouldEscapeFeatureMode = IsFeatureModeGuardActive(currentState)
            && !isFreeSpinInternalTransition
            && IsInputRelatedState(newState);

        if (wouldEscapeFeatureMode)
        {
            Debug.LogWarning($"[GameManager] Ignored SetState({newState}) while FeatureMode is active - " +
                              "something tried to change state out from under the feature mode. Only " +
                              "ExitFeatureMode can properly leave FeatureMode.");
            return;
        }

        SetStateInternal(newState);
    }

    /// <summary>True for FeatureMode itself and for ResolvingFreeSpin - both count as "the guard
    /// above is active" since ResolvingFreeSpin is just Free Spins' own in-progress work, not a
    /// state anything else should be able to interrupt out of either.</summary>
    private static bool IsFeatureModeGuardActive(GameplayState state) =>
        state == GameplayState.FeatureMode || state == GameplayState.ResolvingFreeSpin;

    /// <summary>Idle/GracePeriod re-enable input directly; the mid-cascade states are what a stray
    /// leftover coroutine would try to resume into on its way back to Idle.</summary>
    private static bool IsInputRelatedState(GameplayState state) =>
        state == GameplayState.Idle
        || state == GameplayState.GracePeriod
        || state == GameplayState.Playing
        || state == GameplayState.ResolvingMatches
        || state == GameplayState.ResolvingSpecialMadness
        || state == GameplayState.ResolvingMadnessColorChange;

    /// <summary>Bypasses the FeatureMode guard above - only Enter/ExitFeatureMode should call this directly.</summary>
    private void SetStateInternal(GameplayState newState)
    {
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

    /// <summary>
    /// Called by a feature-mode manager (e.g. KebabKarnageManager) when its mini-game starts.
    /// Remembers the state we were in beforehand so ExitFeatureMode can restore it, in case the
    /// feature was triggered mid-GracePeriod rather than from plain Idle.
    /// </summary>
    public void EnterFeatureMode()
    {
        if (currentState == GameplayState.FeatureMode) return;

        // Only Idle/GracePeriod are safe to restore into - both allow player input. If the
        // madness meter happens to fill mid-cascade (e.g. currentState is ResolvingMatches when
        // this is called), remembering THAT state would leave the board permanently unable to
        // accept input once the feature mode ends, since nothing else is left to transition it
        // back to Idle afterward. Default to Idle for any other captured state instead.
        stateBeforeFeatureMode = (currentState == GameplayState.Idle || currentState == GameplayState.GracePeriod)
            ? currentState
            : GameplayState.Idle;

        SetStateInternal(GameplayState.FeatureMode);
    }

    /// <summary>
    /// Called by a feature-mode manager when its mini-game ends. Restores whatever state was
    /// active before EnterFeatureMode was called, unless an explicit returnState override is
    /// passed (e.g. force back to Idle regardless).
    /// </summary>
    public void ExitFeatureMode(GameplayState? returnState = null)
    {
        if (!IsFeatureModeGuardActive(currentState)) return;
        SetStateInternal(returnState ?? stateBeforeFeatureMode);
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

    private void OnApplicationQuit()
    {
        if (board == null)
        {
            Debug.LogWarning("[GameManager] OnApplicationQuit called without a Board reference; skipping save.");
            return;
        }

        board.SaveNow();
    }

    private void OnApplicationPause(bool paused)
    {
        if (!paused) return;
        if (board == null)
        {
            Debug.LogWarning("[GameManager] OnApplicationPause called without a Board reference; skipping save.");
            return;
        }

        board.SaveNow();
    }
}
