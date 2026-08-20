using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Classic match-3 idle hint: after idleThresholdSeconds with no accepted player move, finds a
/// valid swap (see PossibleMoveFinder) and pulses both tiles (see Symbol.PlayHintPulse) until the
/// player acts. Re-picks periodically afterward (rehintIntervalSeconds) in case the player's AFK
/// rather than just thinking. If no valid move exists anywhere, publishes NoValidMovesFoundEvent
/// instead - hook a reshuffle routine to OnNoValidMovesFound if you want the classic
/// "no more moves, shuffling..." behavior; reshuffling itself isn't implemented here since it
/// touches Board's populate/spawn logic directly.
///
/// Idle time only accrues while GameManager.CurrentState is Idle - mid-cascade, a feature mode,
/// grace period, etc. all pause the clock and hide any hint currently showing, so a hint never
/// sits on screen (or silently keeps counting toward a re-hint) while something else has control.
/// </summary>
public class HintController : MonoBehaviour
{
    [SerializeField] private Board board;
    [SerializeField] private GameManager gameManager;

    [Header("Timing")]
    [Tooltip("Seconds of no accepted move before the first hint is shown.")]
    [Min(1f)] [SerializeField] private float idleThresholdSeconds = 8f;
    [Tooltip("Once a hint is showing, it re-picks and restarts after this many ADDITIONAL idle " +
             "seconds - keeps nudging an AFK player rather than showing one hint forever. 0 " +
             "disables re-hinting; the first hint just stays up until the player moves.")]
    [Min(0f)] [SerializeField] private float rehintIntervalSeconds = 6f;
    [Tooltip("How many pulse cycles each hint burst plays before going quiet again - see " +
             "Symbol.PlayHintPulse. A finite burst (not an infinite loop) is what makes a re-hint " +
             "on the same pair actually visible as a repeated event rather than nothing changing.")]
    [Min(1)] [SerializeField] private int hintBurstLoops = 3;

    public UnityEvent OnNoValidMovesFound;

    private float idleTimer;
    private bool hintActive;
    private Symbol hintA;
    private Symbol hintB;

    private void Awake()
    {
        if (board == null) board = FindAnyObjectByType<Board>();
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();

        if (board == null) Debug.LogWarning("[HintController] No Board found in scene - hints disabled.");
        if (gameManager == null) Debug.LogWarning("[HintController] No GameManager found in scene - hints disabled.");
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
        idleTimer = 0f;
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        ClearHint();
    }

    private void HandlePlayerMove(PlayerMoveEvent evt)
    {
        idleTimer = 0f;
        ClearHint();
    }

    private void Update()
    {
        if (board == null || gameManager == null) return;

        if (gameManager.CurrentState != GameManager.GameplayState.Idle)
        {
            // Board's busy (mid-cascade, a feature mode, grace period, etc.) - don't accrue idle
            // time, and don't leave a hint pulsing underneath whatever's actually happening.
            if (hintActive) ClearHint();
            return;
        }

        idleTimer += Time.unscaledDeltaTime;

        if (!hintActive && idleTimer >= idleThresholdSeconds)
        {
            ShowHint();
        }
        else if (hintActive && rehintIntervalSeconds > 0f && idleTimer >= idleThresholdSeconds + rehintIntervalSeconds)
        {
            idleTimer = idleThresholdSeconds; // rebase rather than reset to 0, so the interval stays consistent
            ShowHint();
        }
    }

    private void ShowHint()
    {
        ClearHint();

        if (!PossibleMoveFinder.TryFindHint(board.Grid, board.TreatMadnessSymbolsAsWildcards, out var from, out var to))
        {
            Debug.LogWarning("[HintController] No valid move found anywhere on the board.");
            OnNoValidMovesFound?.Invoke();
            EventBus.Publish(new NoValidMovesFoundEvent());
            return;
        }

        hintA = board.Grid[from].Occupant;
        hintB = board.Grid[to].Occupant;
        hintA?.PlayHintPulse(hintBurstLoops);
        hintB?.PlayHintPulse(hintBurstLoops);
        hintActive = true;
    }

    private void ClearHint()
    {
        hintA?.StopHintPulse();
        hintB?.StopHintPulse();
        hintA = null;
        hintB = null;
        hintActive = false;
    }
}
