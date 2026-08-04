using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Controls the "Lucky Scratch Ticket" feature mode: a giant ticket appears with a fixed set of
/// designer-placed scratch panels (unlike Kebab Karnage's procedurally-spawned asteroids, a
/// scratch ticket is a hand-authored layout, so panels are wired up in the Inspector rather than
/// instantiated at runtime). The player scratches each panel with their finger (see
/// ScratchSurface); the instant a panel finishes, its reward applies immediately - score, a heal,
/// a stat buff, or damage to the player - rather than waiting to batch everything at the end, so
/// a bad pull can genuinely end the ticket early with a loss, same as Kebab Karnage's HP-to-zero
/// loss condition.
///
/// The mode ends (survived: true) once every panel has been scratched, or (survived: false) if a
/// Damage reward drops the player's HP to 0 mid-ticket - see HandlePanelRevealed. An optional
/// timeout auto-reveals whatever's left so nobody gets stuck staring at an unfinished ticket.
///
/// The player can also cash out early once earlyExitUnlockFraction of panels are scratched (see
/// OnEarlyExitUnlocked / ExitEarly) - this locks in only what's already been revealed and skips
/// every remaining panel entirely, dodging both further upgrades and further damage.
///
/// Start this by publishing EventBus.Publish(new FeatureModeRequestedEvent(LuckyScratchTicketManager.FeatureId))
/// from wherever your madness-meter threshold logic lives, or just call StartFeatureMode() /
/// Instance.StartFeatureMode() directly - identical trigger contract to KebabKarnageManager.
/// </summary>
[DisallowMultipleComponent]
public class LuckyScratchTicketManager : MonoBehaviour
{
    public const string FeatureId = "lucky_scratch_ticket";

    public static LuckyScratchTicketManager Instance { get; private set; }

    [Header("References (auto-found in Awake if left empty - same pattern as KebabKarnageManager)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerRunStats playerRunStats;
    [Tooltip("Optional - if assigned, Score rewards are added via Board.AddBonusScore.")]
    [SerializeField] private Board board;
    [Tooltip("Optional - only needed if Deterministic Rng Stream >= 0 (reproducible-per-run rolls).")]
    [SerializeField] private StageManager stageManager;
    [Tooltip("Optional - shown (and pauses the game) right as the mode starts, before the ticket becomes scratchable. Leave unassigned to skip straight to the ticket.")]
    [SerializeField] private LuckyScratchIntroPopup introPopup;

    [Header("Scene Toggles")]
    [Tooltip("Objects disabled while this mode is active and re-enabled when it ends (e.g. the match-3 board root, its HUD).")]
    [SerializeField] private GameObject[] hideWhileActive;
    [Tooltip("Objects enabled while this mode is active and disabled when it ends (e.g. the ticket root, feature HUD).")]
    [SerializeField] private GameObject[] showWhileActive;

    [Header("Ticket Layout")]
    [Tooltip("Every scratch panel on the designed ticket, in whatever order you want. A giant " +
             "ticket is a fixed layout, so these are wired by hand rather than spawned.")]
    [SerializeField] private LuckyScratchPanel[] panels;
    [Tooltip("Reward pool this ticket draws from - each panel gets an independent weighted roll " +
             "(rewards can repeat across panels).")]
    [SerializeField] private LuckyScratchRewardPoolConfig rewardPool;
    [Tooltip("If >= 0, rolls use the same deterministic RunRandom.ForDepth/WeightedPool.Pick pattern " +
             "PowerupManager uses (reproducible from the run seed) on this dedicated stream index - " +
             "pick a number not already used by another system (PowerupManager uses stream 2). " +
             "Leave -1 to just use UnityEngine.Random instead.")]
    [SerializeField] private int deterministicRngStream = -1;

    [Header("Skip / Timeout")]
    [Tooltip("If > 0, any panels still unscratched after this many seconds auto-reveal, so a player can't get stuck on the ticket forever. 0 = no timeout.")]
    [Min(0f)]
    [SerializeField] private float autoRevealTimeout = 25f;

    [Header("Early Exit")]
    [Tooltip("Once at least this fraction of panels have been scratched, the player can choose to " +
             "bail out and move on to the next stage instead of finishing the ticket - locking in " +
             "only what they've already revealed and skipping every remaining panel's reward " +
             "(dodging both further upgrades AND further damage). 0 = available immediately; " +
             "1 = never available (effectively disables early exit).")]
    [Range(0f, 1f)]
    [SerializeField] private float earlyExitUnlockFraction = 0.5f;
    [Tooltip("Invoked once, the moment enough panels have been scratched to unlock early exit - " +
             "show your 'Continue to Next Stage' button here. See OnEarlyExitUsed to hide it again.")]
    public UnityEvent OnEarlyExitUnlocked;
    /// <summary>Invoked right as an early exit is taken (before the wind-down delay) - hide the
    /// exit button here, same as OnEarlyExitUnlocked shows it.</summary>
    public UnityEvent OnEarlyExitUsed;

    [Header("Wind-Down")]
    [Tooltip("Extra delay after the outcome is decided (last panel scratched, or a Damage panel " +
             "drops HP to 0) before the mode actually exits - gives the final panel's own pop " +
             "animation, plus anything hooked to OnTicketWindDown below, time to play instead of " +
             "the board/HUD snapping back the instant the last reveal lands.")]
    [Min(0f)]
    [SerializeField] private float windDownDelay = 1.2f;
    [Tooltip("Invoked once the outcome is decided but before windDownDelay starts counting - hook " +
             "a ticket-wide animation here (the whole card sliding/fading, a win banner, confetti, " +
             "a 'shattered' effect on a loss, etc.). OnModeWon/OnModeLost still fire afterward, " +
             "once windDownDelay has fully elapsed and the mode is actually tearing down.")]
    public UnityEvent OnTicketWindDown;

    [Header("Events")]
    public UnityEvent OnModeStarted;
    public UnityEvent OnModeWon;
    public UnityEvent OnModeLost;
    /// <summary>(panelsRevealed, totalPanels) - bind a progress bar/label to this.</summary>
    public UnityEvent<int, int> OnPanelsRevealedChanged;

    public bool IsActive { get; private set; }
    public int PanelsRevealed { get; private set; }
    /// <summary>True once enough panels have been scratched (see earlyExitUnlockFraction) and the
    /// mode hasn't already started ending some other way.</summary>
    public bool CanExitEarly => IsActive && !isEnding && earlyExitUnlocked;

    private Coroutine timeoutRoutine;
    private Coroutine endingRoutine;
    private bool isEnding;
    private bool earlyExitUnlocked;
    private int ticketPlayCount;

    private void Awake()
    {
        // Local singleton purely for convenience (e.g. a "Reveal All" button calling
        // Instance.RevealAllRemaining()); the EventBus path doesn't need this.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (playerRunStats == null) playerRunStats = FindAnyObjectByType<PlayerRunStats>();
        if (board == null) board = FindAnyObjectByType<Board>();
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();

        if (gameManager == null)
            Debug.LogWarning("[LuckyScratchTicketManager] No GameManager found in scene - board input will NOT be disabled while the ticket is active.");
        if (playerHealth == null)
            Debug.LogWarning("[LuckyScratchTicketManager] No PlayerHealth found in scene - Damage rewards won't do anything.");
        if (panels == null || panels.Length == 0)
            Debug.LogWarning("[LuckyScratchTicketManager] No panels assigned - StartFeatureMode will immediately end with nothing to scratch.");
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Subscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
    }

    private void OnDisable() => EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);

    private void HandleFeatureModeRequested(FeatureModeRequestedEvent evt)
    {
        if (evt.FeatureId == FeatureId) StartFeatureMode();
    }

    public void StartFeatureMode()
    {
        if (IsActive) return;
        IsActive = true;
        isEnding = false;
        earlyExitUnlocked = false;
        endingRoutine = null;
        PanelsRevealed = 0;
        ticketPlayCount++;

        // Lock the board and switch scene objects immediately - don't wait on the popup for
        // this part, so nothing is clickable underneath it even during the announcement (same
        // reasoning as KebabKarnageManager.StartFeatureMode).
        SetActiveGroup(hideWhileActive, false);
        SetActiveGroup(showWhileActive, true);

        if (gameManager != null)
        {
            gameManager.EnterFeatureMode();
            Debug.Log($"[LuckyScratchTicketManager] Started - GameManager.AllowsPlayerInput is now {gameManager.AllowsPlayerInput} (should be False).");
        }

        if (introPopup != null)
            introPopup.Show(BeginScratchPhase);
        else
            BeginScratchPhase();
    }

    /// <summary>
    /// Rolls rewards onto every panel and unlocks scratching. Called immediately by
    /// StartFeatureMode if no intro popup is assigned, or as the popup's dismiss callback
    /// otherwise - mirrors KebabKarnageManager.BeginAsteroidPhase.
    /// </summary>
    private void BeginScratchPhase()
    {
        if (!IsActive) return; // ForceEnd could have been called while the popup was showing

        RollAndAssignRewards();

        if (panels != null)
        {
            foreach (var panel in panels)
            {
                if (panel == null) continue;
                panel.gameObject.SetActive(true);
                panel.OnRevealed += HandlePanelRevealed;
            }
        }

        OnPanelsRevealedChanged?.Invoke(PanelsRevealed, panels?.Length ?? 0);
        OnModeStarted?.Invoke();
        EventBus.Publish(new FeatureModeStartedEvent(FeatureId));

        if (autoRevealTimeout > 0f)
            timeoutRoutine = StartCoroutine(TimeoutRoutine());

        // Edge case: no panels assigned - don't leave the player staring at a ticket that can
        // never finish.
        if (panels == null || panels.Length == 0)
            EndFeatureMode(survived: true);
    }

    private void RollAndAssignRewards()
    {
        if (panels == null) return;

        if (rewardPool == null || rewardPool.rewards == null || rewardPool.rewards.Length == 0)
        {
            Debug.LogWarning("[LuckyScratchTicketManager] No reward pool assigned - every panel will scratch off to nothing.");
            foreach (var panel in panels) panel?.Setup(null);
            return;
        }

        // Same deterministic-per-run-seed pattern PowerupManager uses (RunRandom.ForDepth on a
        // dedicated stream, then repeated WeightedPool.Pick calls against the same rng instance
        // so each panel still gets an independently-advancing roll) - only if a StageManager and
        // a stream index were actually assigned; otherwise falls back to plain UnityEngine.Random
        // so this still works without RunRandom wired up in your project.
        bool useDeterministic = deterministicRngStream >= 0 && stageManager != null;
        var rng = useDeterministic
            ? RunRandom.ForDepth(stageManager.RunSeed, ticketPlayCount, stream: deterministicRngStream)
            : null;

        var poolList = useDeterministic ? new List<ScratchRewardDefinition>(rewardPool.rewards) : null;

        foreach (var panel in panels)
        {
            if (panel == null) continue;

            ScratchRewardDefinition pick = useDeterministic
                ? WeightedPool.Pick(poolList, r => r.weight, rng)
                : WeightedPickFallback(rewardPool.rewards);

            panel.Setup(pick);
        }
    }

    /// <summary>Plain-Random weighted pick, used only when no deterministic RNG stream is configured.</summary>
    private static ScratchRewardDefinition WeightedPickFallback(ScratchRewardDefinition[] pool)
    {
        float total = 0f;
        foreach (var r in pool)
            if (r != null) total += Mathf.Max(0f, r.weight);
        if (total <= 0f) return pool.Length > 0 ? pool[0] : null;

        float roll = Random.Range(0f, total);
        float cursor = 0f;
        foreach (var r in pool)
        {
            if (r == null) continue;
            cursor += Mathf.Max(0f, r.weight);
            if (roll <= cursor) return r;
        }
        return pool[pool.Length - 1];
    }

    /// <summary>Applies the panel's reward the moment it's scratched off, then checks both loss
    /// (Damage dropped HP to 0) and win (every panel now revealed) conditions.</summary>
    private void HandlePanelRevealed(LuckyScratchPanel panel)
    {
        if (isEnding) return; // outcome already decided (e.g. RevealAllRemaining still mid-loop after a death) - ignore

        panel.OnRevealed -= HandlePanelRevealed;

        var reward = panel.AssignedReward;
        if (reward != null)
        {
            reward.Apply(board, playerHealth, playerRunStats);
            EventBus.Publish(new LuckyScratchPanelRevealedEvent(PanelsRevealed, panels.Length, reward));
        }

        PanelsRevealed++;
        OnPanelsRevealedChanged?.Invoke(PanelsRevealed, panels.Length);
        EventBus.Publish(new LuckyScratchProgressEvent(PanelsRevealed, panels.Length));

        if (reward != null && reward.IsHarmful && playerHealth != null && playerHealth.CurrentHealth <= 0)
        {
            BeginEnding(survived: false);
            return;
        }

        if (PanelsRevealed >= panels.Length)
        {
            BeginEnding(survived: true);
            return;
        }

        if (!earlyExitUnlocked && panels.Length > 0 && PanelsRevealed / (float)panels.Length >= earlyExitUnlockFraction)
        {
            earlyExitUnlocked = true;
            Debug.Log($"[LuckyScratchTicketManager] Early exit unlocked at {PanelsRevealed}/{panels.Length} panels scratched.");
            OnEarlyExitUnlocked?.Invoke();
        }
    }

    /// <summary>Wire a "Continue to Next Stage" button's onClick to this once CanExitEarly is
    /// true (see OnEarlyExitUnlocked). Locks in the outcome as a win using only what's already
    /// been revealed - every remaining panel is simply never scratched, so neither its reward NOR
    /// its potential Damage ever applies. Reuses the same wind-down path as a normal finish, so
    /// OnTicketWindDown/OnModeWon still fire as usual.</summary>
    public void ExitEarly()
    {
        if (!CanExitEarly) return;

        Debug.Log($"[LuckyScratchTicketManager] Player exited early after {PanelsRevealed}/{panels.Length} panels - skipping the rest.");
        BeginEnding(survived: true);
    }

    /// <summary>Locks in the outcome immediately (no more reveals get processed - see the
    /// isEnding guard in HandlePanelRevealed/RevealAllRemaining), fires OnTicketWindDown so a
    /// ticket-wide animation can start right away, then waits windDownDelay before the mode
    /// actually tears down via EndFeatureMode. This is what gives the final panel's pop tween
    /// (and anything else hooked to the wind-down) room to play instead of the board snapping
    /// back the instant the last reveal lands. Also fires OnEarlyExitUsed defensively if the exit
    /// button happened to still be visible (e.g. a death or the timeout landed the same frame it
    /// unlocked) so the UI always ends up hiding it, not just on the ExitEarly() path.</summary>
    private void BeginEnding(bool survived)
    {
        if (isEnding) return;
        isEnding = true;

        if (earlyExitUnlocked) OnEarlyExitUsed?.Invoke();

        if (timeoutRoutine != null) StopCoroutine(timeoutRoutine);

        OnTicketWindDown?.Invoke();
        endingRoutine = StartCoroutine(EndAfterWindDown(survived));
    }

    private IEnumerator EndAfterWindDown(bool survived)
    {
        if (windDownDelay > 0f) yield return new WaitForSeconds(windDownDelay);
        EndFeatureMode(survived);
    }

    private IEnumerator TimeoutRoutine()
    {
        yield return new WaitForSeconds(autoRevealTimeout);
        if (!IsActive || isEnding) yield break;

        Debug.Log("[LuckyScratchTicketManager] Auto-reveal timeout hit - forcing remaining panels open.");
        RevealAllRemaining();
    }

    /// <summary>Instantly scratches off every panel that isn't already revealed - wire a "Reveal
    /// All" button's onClick to this, or let the timeout above call it automatically. Panels
    /// reveal one at a time through the normal HandlePanelRevealed path, so a Damage panel late
    /// in the batch can still end the ticket as a loss exactly like a manual scratch would.</summary>
    public void RevealAllRemaining()
    {
        if (!IsActive || isEnding || panels == null) return;
        foreach (var panel in panels)
        {
            if (isEnding) break; // a Damage reveal earlier in this loop may have already decided the outcome
            if (panel != null && !panel.IsRevealed)
                panel.ForceReveal();
        }
    }

    private void EndFeatureMode(bool survived)
    {
        if (!IsActive) return;
        IsActive = false;
        isEnding = false;

        if (timeoutRoutine != null) StopCoroutine(timeoutRoutine);
        if (endingRoutine != null) StopCoroutine(endingRoutine);
        endingRoutine = null;

        if (panels != null)
        {
            foreach (var panel in panels)
            {
                if (panel == null) continue;
                panel.OnRevealed -= HandlePanelRevealed; // safe even if already unsubscribed above
            }
        }

        SetActiveGroup(showWhileActive, false);
        SetActiveGroup(hideWhileActive, true);

        gameManager?.ExitFeatureMode();

        if (survived) OnModeWon?.Invoke();
        else OnModeLost?.Invoke();

        EventBus.Publish(new FeatureModeEndedEvent(FeatureId, survived));
    }

    private static void SetActiveGroup(GameObject[] group, bool active)
    {
        if (group == null) return;
        foreach (var go in group)
            if (go != null) go.SetActive(active);
    }

    /// <summary>Call from a pause menu / forfeit button if you need to bail out early - this
    /// skips the wind-down delay entirely and tears down immediately, since an explicit forfeit
    /// shouldn't have to wait out an animation the player is actively trying to leave.</summary>
    public void ForceEnd(bool countAsSurvived)
    {
        isEnding = true; // block any in-flight reveal from also trying to BeginEnding
        EndFeatureMode(countAsSurvived);
    }
}
