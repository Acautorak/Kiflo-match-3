using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Controls the "Disco Dance Disco" Madness event: for a fixed duration, disco lights fall
/// cosmetically over the board, every symbol on the board dances in place (see
/// Board.StartDiscoDance/StopDiscoDance -> Symbol.PlayDanceLoop), and two temporary multipliers
/// apply - score (via Board.ScoreEventMultiplier -> ScoreTracker.EventMultiplier) and Collect-goal
/// progress (via CollectMultiplier below, read directly by StageManager.HandleSymbolMatched).
///
/// Deliberately NOT a blocking feature mode like Kebab Karnage/Free Spins/Tile Collector -
/// GameManager.EnterFeatureMode() is never called here, so normal swapping/input keeps working
/// the entire time; this is a buff layered on top of regular play, not its own mini-game. One
/// consequence: because GameManager.CurrentState never leaves Idle/GracePeriod for this event,
/// MadnessFeatureTrigger's "wait for the board to settle" gate doesn't block ANOTHER feature mode
/// from starting while Disco Dance Disco is still running (its own IsActive-based re-entry guard
/// only stops a second Disco Dance Disco from overlapping itself) - so a Free Spins/Kebab Karnage
/// session firing mid-event will simply run underneath it, still benefiting from both multipliers
/// until this event's own duration runs out. If that stacking turns out to feel wrong in
/// playtesting, the fix is a one-line guard here (bail out of StartEvent while
/// gameManager.IsInFeatureMode is true) rather than anything structural.
///
/// Start this the same way as every other feature mode: EventBus.Publish(new
/// FeatureModeRequestedEvent(DiscoDanceDiscoManager.FeatureId)), or call StartEvent() directly.
/// </summary>
[DisallowMultipleComponent]
public class DiscoDanceDiscoManager : MonoBehaviour
{
    public const string FeatureId = "disco_dance_disco";

    public static DiscoDanceDiscoManager Instance { get; private set; }

    [Header("References (auto-found in Awake if left empty)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private Board board;
    [Tooltip("Optional - if assigned, shown (and pauses the game) right as the event triggers, before the buff/visuals actually begin. Leave unassigned to skip straight to the event.")]
    [SerializeField] private DiscoDanceDiscoIntroPopup introPopup;

    [Header("Event Settings")]
    [Tooltip("How long (real seconds, counted while unpaused) the buff and visuals last once the intro popup is dismissed.")]
    [Min(0.1f)] [SerializeField] private float duration = 12f;
    [Tooltip("Every scoreDelta while active is multiplied by this, on top of the run's normal PlayerRunStats.ScoreMultiplier - see Board.ScoreEventMultiplier/ScoreTracker.EventMultiplier.")]
    [Min(1f)] [SerializeField] private float scoreMultiplier = 2f;
    [Tooltip("Every symbol matched toward a Collect-type stage goal counts this many times while active - see CollectMultiplier, read by StageManager.HandleSymbolMatched.")]
    [Min(1)] [SerializeField] private int collectMultiplier = 2;

    [Header("Symbol Dance")]
    [Tooltip("Duration of one punch-scale/punch-rotation cycle - see Symbol.PlayDanceLoop. Loops for the whole event.")]
    [Min(0.05f)] [SerializeField] private float danceCycleDuration = 0.5f;
    [Tooltip("0 disables the scale punch.")]
    [Min(0f)] [SerializeField] private float dancePunchScale = 0.15f;
    [Tooltip("Degrees of punch rotation per cycle. 0 disables the rotation punch.")]
    [SerializeField] private float dancePunchRotationDegrees = 12f;

    [Header("Falling Disco Lights (cosmetic)")]
    [Tooltip("Prefab instantiated repeatedly above the board and tweened straight down, purely cosmetic. Leave unassigned to skip the falling-lights visual entirely.")]
    [SerializeField] private GameObject discoLightPrefab;
    [Tooltip("Parent transform for spawned lights. Falls back to this GameObject's transform if left unassigned.")]
    [SerializeField] private Transform lightsParent;
    [Min(0.02f)] [SerializeField] private float lightSpawnInterval = 0.15f;
    [Tooltip("How far above the top row (world units) a light spawns.")]
    [Min(0f)] [SerializeField] private float spawnHeightAboveBoard = 1.5f;
    [Tooltip("How far below the bottom row (world units) a light travels before being destroyed.")]
    [Min(0f)] [SerializeField] private float despawnDepthBelowBoard = 1.5f;
    [Min(0.05f)] [SerializeField] private float lightFallDuration = 1.5f;

    [Header("Scene Toggles (optional banner/HUD only - input stays normal, board is never hidden)")]
    [SerializeField] private GameObject[] showWhileActive;

    [Header("Events")]
    public UnityEvent OnEventStarted;
    public UnityEvent OnEventEnded;

    public bool IsActive { get; private set; }

    /// <summary>Current multiplier StageManager.HandleSymbolMatched should apply to Collect-goal
    /// progress - 1 (no effect) whenever the event isn't running. Read directly off this
    /// singleton rather than an event, same pattern as Board.Instance/FreeSpinsManager.Instance
    /// elsewhere in the project.</summary>
    public int CollectMultiplier => IsActive ? Mathf.Max(1, collectMultiplier) : 1;

    private Coroutine lightsRoutine;
    private Coroutine timerRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (board == null) board = Board.Instance != null ? Board.Instance : FindAnyObjectByType<Board>();

        if (board == null)
            Debug.LogWarning("[DiscoDanceDiscoManager] No Board found in scene - StartEvent will be a no-op.");
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Subscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Subscribe<StageCompletedEvent>(HandleStageCompleted);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);
    }

    private void HandleFeatureModeRequested(FeatureModeRequestedEvent evt)
    {
        if (evt.FeatureId == FeatureId) StartEvent();
    }

    /// <summary>Cuts the event short the moment the stage actually clears while it's running -
    /// unlike a blocking feature mode, Disco Dance Disco never sets GameManager.IsInFeatureMode,
    /// so StageManager.CompleteStage() isn't deferred behind it and a stage can complete mid-event
    /// (see StageManager's deferral, which only applies to real feature modes). Left unhandled,
    /// the dance/lights/multipliers would otherwise keep running underneath the stage-clear
    /// explosion and the next stage's board, which looks broken and would apply the score/collect
    /// multipliers somewhere they shouldn't.</summary>
    private void HandleStageCompleted(StageCompletedEvent evt)
    {
        if (!IsActive) return;
        Debug.Log("[DiscoDanceDiscoManager] Stage completed while active - ending the event early.");
        EndEvent();
    }

    public void StartEvent()
    {
        if (IsActive || board == null) return;
        IsActive = true;

        SetActiveGroup(showWhileActive, true);
        Debug.Log($"[DiscoDanceDiscoManager] Triggered - {duration}s, x{scoreMultiplier} score, x{collectMultiplier} collect progress.");

        if (introPopup != null)
            introPopup.Show(BeginEventPhase);
        else
            BeginEventPhase();
    }

    /// <summary>Actually starts the buff/visuals/timer. Called immediately by StartEvent if no
    /// intro popup is assigned, or as the popup's dismiss callback otherwise - same shape as
    /// FreeSpinsManager.BeginSpinPhase/TileCollectorManager.BeginSessionPhase.</summary>
    private void BeginEventPhase()
    {
        if (!IsActive) return; // something could have ended the event while the popup was showing

        OnEventStarted?.Invoke();
        EventBus.Publish(new FeatureModeStartedEvent(FeatureId));

        board.ScoreEventMultiplier = Mathf.Max(1f, scoreMultiplier);
        board.StartDiscoDance(danceCycleDuration, dancePunchScale, dancePunchRotationDegrees);

        if (discoLightPrefab != null)
            lightsRoutine = StartCoroutine(SpawnFallingLights());

        timerRoutine = StartCoroutine(RunEventTimer());
    }

    private IEnumerator RunEventTimer()
    {
        yield return new WaitForSeconds(duration);
        EndEvent();
    }

    private void EndEvent()
    {
        if (!IsActive) return;
        IsActive = false;

        if (timerRoutine != null)
        {
            StopCoroutine(timerRoutine);
            timerRoutine = null;
        }

        if (lightsRoutine != null)
        {
            StopCoroutine(lightsRoutine);
            lightsRoutine = null;
        }

        if (board != null)
        {
            board.ScoreEventMultiplier = 1f;
            board.StopDiscoDance();
        }

        SetActiveGroup(showWhileActive, false);

        OnEventEnded?.Invoke();
        // No lose condition (it's a buff, not a mini-game), so it always "survives".
        EventBus.Publish(new FeatureModeEndedEvent(FeatureId, survived: true));
    }

    /// <summary>Playtest-only escape hatch, same idea as other managers' ForceEnd - cuts the
    /// event short, reverting both multipliers and stopping the dance/lights immediately.</summary>
    public void ForceEnd() => EndEvent();

    /// <summary>Repeatedly instantiates discoLightPrefab at a random column above the board,
    /// tweens it straight down past the bottom, then destroys it - purely cosmetic, doesn't touch
    /// grid state or input. Runs for as long as the coroutine lives (killed by EndEvent).</summary>
    private IEnumerator SpawnFallingLights()
    {
        var parent = lightsParent != null ? lightsParent : transform;

        while (true)
        {
            float t = Random.value;
            Vector3 top = board.GridToWorldPosition(0, board.Height - 1);
            Vector3 bottom = board.GridToWorldPosition(0, 0);
            Vector3 rightEdge = board.GridToWorldPosition(board.Width - 1, board.Height - 1);

            float x = Mathf.Lerp(top.x, rightEdge.x, t);
            var spawnPos = new Vector3(x, top.y + spawnHeightAboveBoard, top.z);
            var endPos = new Vector3(x, bottom.y - despawnDepthBelowBoard, top.z);

            var light = Instantiate(discoLightPrefab, spawnPos, Quaternion.identity, parent);
            light.transform.DOMove(endPos, lightFallDuration)
                .SetEase(Ease.Linear)
                .OnComplete(() => Destroy(light));

            yield return new WaitForSeconds(lightSpawnInterval);
        }
    }

    private static void SetActiveGroup(GameObject[] group, bool active)
    {
        if (group == null) return;
        foreach (var go in group)
            if (go != null) go.SetActive(active);
    }
}
