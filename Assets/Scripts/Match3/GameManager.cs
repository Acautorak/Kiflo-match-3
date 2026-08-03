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
    /// Popup: a modal popup (tutorial step, powerup choice, confirmation dialog, etc. - see
    /// PopupManager) is on screen. Blocks input the same way FeatureMode does, but is expected
    /// to be short-lived and always resumes whatever state was active before it opened (see
    /// EnterPopup/ExitPopup) rather than driving its own gameplay.
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
        StageClearing,
        Popup
    }

    [SerializeField] private Board board;
    [SerializeField] private StageManager stageManager;

    [Header("Popup Time Scale")]
    [Tooltip("Time.timeScale applied for the whole duration a popup is showing - everything " +
             "using default (scaled-time) tweens/coroutines, e.g. cascade drops and highlight " +
             "sequences, will visibly slow down or freeze along with it. 0 = fully frozen, a " +
             "small value like 0.05 = dramatic slow-mo instead of a hard freeze. UI button clicks " +
             "still work regardless, since uGUI input isn't timeScale-dependent.")]
    [Range(0f, 1f)]
    [SerializeField] private float popupTimeScale = 0.05f;

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
    private GameplayState stateBeforePopup = GameplayState.Idle;
    private int popupTimeScaleHandle = -1;

    /// <summary>
    /// Tracks whether FeatureMode is active independent of currentState, specifically so
    /// ExitFeatureMode still works correctly if a popup is currently covering FeatureMode (i.e.
    /// currentState == Popup while the mini-game underneath has actually ended). Checking
    /// currentState == FeatureMode alone would make ExitFeatureMode silently no-op in that case,
    /// leaving stateBeforePopup pointing at FeatureMode and wrongly resuming it once the popup
    /// closes, even though the mini-game already finished. See ExitFeatureMode.
    /// </summary>
    private bool featureModeActive;

    public GameplayState CurrentState => currentState;
    public bool AllowsBoardRefill => currentState == GameplayState.Playing
        || currentState == GameplayState.ResolvingMatches
        || currentState == GameplayState.ResolvingSpecialMadness
        || currentState == GameplayState.ResolvingMadnessColorChange
        || currentState == GameplayState.ResolvingFreeSpin;
    public bool AllowsPlayerInput => currentState == GameplayState.Idle || currentState == GameplayState.GracePeriod;
    /// <summary>True for the whole Free Spins span, including while a popup is momentarily
    /// covering FeatureMode - see featureModeActive.</summary>
    public bool IsInFeatureMode => featureModeActive;
    public bool IsPopupActive => currentState == GameplayState.Popup;

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
    ///
    /// Popup is guarded the same way: while a popup is showing, nothing can change state out
    /// from under it (see PopupManager) - only ExitPopup can leave. A popup can still interrupt
    /// an active FeatureMode (e.g. an error dialog) - featureModeActive is tracked independently
    /// of currentState specifically so ExitFeatureMode keeps working correctly even while a
    /// popup is covering it (see ExitFeatureMode).
    /// </summary>
    public void SetState(GameplayState newState)
    {
        if (currentState == newState) return;

        if (currentState == GameplayState.Popup)
        {
            Debug.LogWarning($"[GameManager] Ignored SetState({newState}) while a popup is active - " +
                              "only ExitPopup can leave Popup.");
            return;
        }

        bool isFreeSpinInternalTransition =
            (currentState == GameplayState.FeatureMode && newState == GameplayState.ResolvingFreeSpin)
            || (currentState == GameplayState.ResolvingFreeSpin && newState == GameplayState.FeatureMode);

        bool wouldEscapeFeatureMode = featureModeActive
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

    /// <summary>Idle/GracePeriod re-enable input directly; the mid-cascade states are what a stray
    /// leftover coroutine would try to resume into on its way back to Idle.</summary>
    private static bool IsInputRelatedState(GameplayState state) =>
        state == GameplayState.Idle
        || state == GameplayState.GracePeriod
        || state == GameplayState.Playing
        || state == GameplayState.ResolvingMatches
        || state == GameplayState.ResolvingSpecialMadness
        || state == GameplayState.ResolvingMadnessColorChange;

    /// <summary>Bypasses the FeatureMode/Popup guards above - only Enter/Exit methods for those
    /// states, and this class's own event-driven transitions, should call this directly.</summary>
    private void SetStateInternal(GameplayState newState)
    {
        var previous = currentState;
        currentState = newState;
        Debug.Log($"[GameManager] State -> {currentState}");
        EventBus.Publish(new GameStateChangedEvent(previous, currentState));
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
        if (featureModeActive) return;

        // Only Idle/GracePeriod are safe to restore into - both allow player input. If the
        // madness meter happens to fill mid-cascade (e.g. currentState is ResolvingMatches when
        // this is called), remembering THAT state would leave the board permanently unable to
        // accept input once the feature mode ends, since nothing else is left to transition it
        // back to Idle afterward. Default to Idle for any other captured state instead.
        stateBeforeFeatureMode = (currentState == GameplayState.Idle || currentState == GameplayState.GracePeriod)
            ? currentState
            : GameplayState.Idle;

        featureModeActive = true;
        SetStateInternal(GameplayState.FeatureMode);
    }

    /// <summary>
    /// Called by a feature-mode manager when its mini-game ends. Restores whatever state was
    /// active before EnterFeatureMode was called, unless an explicit returnState override is
    /// passed (e.g. force back to Idle regardless).
    ///
    /// If a popup is currently covering FeatureMode (currentState == Popup), we can't change
    /// currentState out from under it - but we still mark FeatureMode as no-longer-active and
    /// fix up what ExitPopup will restore into, so the popup closing doesn't wrongly resume a
    /// mini-game that has actually already ended.
    /// </summary>
    public void ExitFeatureMode(GameplayState? returnState = null)
    {
        if (!featureModeActive) return;
        featureModeActive = false;

        if (currentState == GameplayState.Popup)
        {
            stateBeforePopup = returnState ?? stateBeforeFeatureMode;
            return;
        }

        SetStateInternal(returnState ?? stateBeforeFeatureMode);
    }

    /// <summary>
    /// Called by PopupManager when a modal popup (tutorial step, powerup choice, confirmation
    /// dialog, etc.) opens. Remembers the state we were in beforehand so ExitPopup can restore
    /// it - same single-slot save/restore pattern as EnterFeatureMode/ExitFeatureMode. Also pushes
    /// a slow-mo/pause request via TimeController, so anything still animating (a cascade
    /// mid-drop, a highlight sweep) visibly slows/freezes along with gameplay rather than
    /// silently continuing off-screen while the popup has focus.
    /// </summary>
    public void EnterPopup()
    {
        if (currentState == GameplayState.Popup) return;
        stateBeforePopup = currentState;
        popupTimeScaleHandle = TimeController.Push(popupTimeScale);
        SetStateInternal(GameplayState.Popup);
    }

    /// <summary>
    /// Called by PopupManager once the popup queue drains. Restores whatever was active before
    /// EnterPopup, unless an explicit returnState override is passed, and releases this popup's
    /// TimeController request (Time.timeScale returns to normal, or to whatever any OTHER still-
    /// active request wants, if something else also has one pushed).
    /// </summary>
    public void ExitPopup(GameplayState? returnState = null)
    {
        if (currentState != GameplayState.Popup) return;

        if (popupTimeScaleHandle != -1)
        {
            TimeController.Pop(popupTimeScaleHandle);
            popupTimeScaleHandle = -1;
        }

        SetStateInternal(returnState ?? stateBeforePopup);
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
