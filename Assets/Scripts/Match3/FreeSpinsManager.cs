using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Controls the "Free Spins" feature mode: board input is disabled, a SPIN button appears, and
/// each press runs one full reel spin on the actual board (see Board.PlayFreeSpin/
/// FreeSpinsController) - not a separate mini-game scene like Kebab Karnage, since Free Spins IS
/// the board. Ends once every spin has been used.
///
/// Start this by publishing EventBus.Publish(new FeatureModeRequestedEvent(FreeSpinsManager.FeatureId))
/// - MadnessFeatureTrigger does this automatically for stages whose StageDefinition.
/// featureModeOnMeterFull is FreeSpins - or call StartFeatureMode() / Instance.StartFeatureMode()
/// directly if that's simpler for your setup.
/// </summary>
[DisallowMultipleComponent]
public class FreeSpinsManager : MonoBehaviour
{
    public const string FeatureId = "free_spins";

    public static FreeSpinsManager Instance { get; private set; }

    [Header("References (auto-found in Awake if left empty - same pattern as KebabKarnageManager)")]
    [SerializeField] private GameManager gameManager;
    [Tooltip("Required - Free Spins runs its reels directly on this board via Board.PlayFreeSpin().")]
    [SerializeField] private Board board;
    [Tooltip("Optional - if assigned, shown (and pauses the game) right as the mode starts, announcing the win, before the SPIN button becomes usable. Leave unassigned to skip straight to spinning.")]
    [SerializeField] private FreeSpinsIntroPopup introPopup;

    [Header("Scene Toggles")]
    [Tooltip("Objects disabled while this mode is active and re-enabled when it ends (e.g. the moves counter, swap-hint UI).")]
    [SerializeField] private GameObject[] hideWhileActive;
    [Tooltip("Objects enabled while this mode is active and disabled when it ends (e.g. the SPIN button, a 'FREE SPINS' banner, remaining-spins counter).")]
    [SerializeField] private GameObject[] showWhileActive;

    [Header("Spin Count")]
    [Tooltip("Spins granted regardless of how much the meter overshot when it filled.")]
    [Min(1)] [SerializeField] private int baseSpinCount = 3;
    [Tooltip("How much MadnessMeter overflow (see FeatureModeRequestedEvent.OverflowAmount) is worth one extra spin, on top of baseSpinCount. 0 disables overflow scaling entirely.")]
    [Min(0f)] [SerializeField] private float overflowPerBonusSpin = 10f;
    [Tooltip("Hard cap on total spins a single trigger can grant, however large the overflow was.")]
    [Min(1)] [SerializeField] private int maxSpinCount = 10;

    [Header("Wonky Chance")]
    [Tooltip("While a spin's cascade chain count is below this, the wonky/random-special trigger " +
             "chance is forced to 100% (see FreeSpinsController.GuaranteedWonkyChainThreshold) " +
             "instead of the normal accumulated rate (stage wonkyChance + PlayerRunStats bonus). " +
             "0 disables the guarantee entirely.")]
    [Min(0)] [SerializeField] private int guaranteedWonkyChainThreshold = 2;

    [Header("Events")]
    public UnityEvent<int> OnModeStarted;      // total spins granted
    public UnityEvent<int> OnSpinsRemainingChanged;
    public UnityEvent OnSpinStarted;
    public UnityEvent OnSpinCompleted;
    public UnityEvent OnModeEnded;

    public bool IsActive { get; private set; }
    public bool IsSpinning { get; private set; }
    public int SpinsRemaining { get; private set; }

    private void Awake()
    {
        // Local singleton purely for convenience (e.g. wiring the SPIN button's OnClick to
        // FreeSpinsManager.Instance.SpinButtonPressed in the Inspector); the EventBus path for
        // starting the mode doesn't need this.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (board == null) board = FindAnyObjectByType<Board>();

        if (gameManager == null)
            Debug.LogWarning("[FreeSpinsManager] No GameManager found in scene - board input will NOT be disabled while this mode is active.");
        if (board == null)
            Debug.LogError("[FreeSpinsManager] No Board found in scene - Free Spins has nothing to spin. Assign it in the Inspector.");
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Subscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
    }

    private void OnDisable() => EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);

    private void HandleFeatureModeRequested(FeatureModeRequestedEvent evt)
    {
        if (evt.FeatureId == FeatureId) StartFeatureMode(ComputeSpinCount(evt.OverflowAmount));
    }

    private int ComputeSpinCount(float overflowAmount)
    {
        int bonusSpins = overflowPerBonusSpin > 0f ? Mathf.FloorToInt(overflowAmount / overflowPerBonusSpin) : 0;
        return Mathf.Clamp(baseSpinCount + bonusSpins, 1, maxSpinCount);
    }

    /// <param name="spinCount">Total spins to grant. Defaults to baseSpinCount (no overflow bonus) if called directly rather than via the meter trigger.</param>
    public void StartFeatureMode(int spinCount = -1)
    {
        if (IsActive) return;
        if (board == null)
        {
            Debug.LogError("[FreeSpinsManager] Cannot start - no Board assigned.");
            return;
        }

        IsActive = true;
        SpinsRemaining = spinCount > 0 ? Mathf.Clamp(spinCount, 1, maxSpinCount) : baseSpinCount;

        // Push our Inspector value into Board's FreeSpinsController now, before any SpinOnce runs -
        // see Board.FreeSpinsGuaranteedWonkyChainThreshold and FreeSpinsController.
        // GuaranteedWonkyChainThreshold. FreeSpinsManager never touches freeSpinsController
        // directly, only this one plain property on Board.
        board.FreeSpinsGuaranteedWonkyChainThreshold = guaranteedWonkyChainThreshold;

        // Lock the board and switch scene objects immediately - don't wait on the popup for
        // this part, so nothing is clickable underneath it even during the announcement (same
        // reasoning as KebabKarnageManager.StartFeatureMode).
        SetActiveGroup(hideWhileActive, false);
        SetActiveGroup(showWhileActive, true);

        if (gameManager != null)
        {
            gameManager.EnterFeatureMode();
            Debug.Log($"[FreeSpinsManager] Started with {SpinsRemaining} spin(s) - GameManager.AllowsPlayerInput is now {gameManager.AllowsPlayerInput} (should be False).");
        }

        if (introPopup != null)
            introPopup.Show(BeginSpinPhase);
        else
            BeginSpinPhase();
    }

    /// <summary>
    /// Actually opens up spinning. Called immediately by StartFeatureMode if no intro popup is
    /// assigned, or as the popup's dismiss callback (Continue button / auto-dismiss timer)
    /// otherwise - either way, this is the point at which OnModeStarted/FeatureModeStartedEvent
    /// fire and the SPIN button becomes interactable (see FreeSpinsUI, which only enables it once
    /// OnModeStarted/OnSpinsRemainingChanged have fired), since "started" should mean "the player
    /// can actually spin now", not "the win announcement is on screen."
    /// </summary>
    private void BeginSpinPhase()
    {
        if (!IsActive) return; // ForceEnd could have been called while the popup was showing

        OnModeStarted?.Invoke(SpinsRemaining);
        OnSpinsRemainingChanged?.Invoke(SpinsRemaining);
        EventBus.Publish(new FeatureModeStartedEvent(FeatureId));
    }

    /// <summary>Wire this to the SPIN button's OnClick. Ignored if the mode isn't active, a spin
    /// is already animating, or no spins remain (shouldn't happen if the button is hidden
    /// correctly via OnSpinsRemainingChanged, but guarded anyway).</summary>
    public void SpinButtonPressed()
    {
        if (!IsActive || IsSpinning || SpinsRemaining <= 0) return;
        StartCoroutine(RunSpin());
    }

    private IEnumerator RunSpin()
    {
        IsSpinning = true;
        OnSpinStarted?.Invoke();

        yield return board.PlayFreeSpin();

        IsSpinning = false;
        SpinsRemaining--;
        OnSpinsRemainingChanged?.Invoke(SpinsRemaining);
        OnSpinCompleted?.Invoke();

        if (SpinsRemaining <= 0)
            EndFeatureMode();
    }

    private void EndFeatureMode()
    {
        if (!IsActive) return;
        IsActive = false;

        SetActiveGroup(showWhileActive, false);
        SetActiveGroup(hideWhileActive, true);

        gameManager?.ExitFeatureMode();

        OnModeEnded?.Invoke();
        // Free Spins always resolves every spin to a guaranteed match (see FreeSpinsController.
        // ForceAMatch) - there's no lose condition, so Survived is always true here. Kept as a
        // real field rather than dropped so FeatureModeEndedEvent's shape stays uniform across
        // every feature mode a listener (e.g. analytics) might subscribe to generically.
        EventBus.Publish(new FeatureModeEndedEvent(FeatureId, survived: true));
    }

    private static void SetActiveGroup(GameObject[] group, bool active)
    {
        if (group == null) return;
        foreach (var go in group)
            if (go != null) go.SetActive(active);
    }

    /// <summary>Call from a pause menu / forfeit button if you need to bail out early, forfeiting any unused spins.</summary>
    public void ForceEnd() => EndFeatureMode();
}
