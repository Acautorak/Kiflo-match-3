using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Controls the "Tile Collector" feature mode: for a fixed duration, every tap on a real board
/// tile immediately collects it (score, despawn, gravity refill) - no swapping, no matching.
/// Unlike Kebab Karnage/Free Spins/Lucky Scratch Ticket, the board itself is NOT hidden - the
/// actual tiles ARE the minigame, so hideWhileActive/showWhileActive here are for surrounding HUD
/// only (swap your normal move/score HUD for a timer/tiles-collected one, say), not the board.
///
/// Start this the same way as every other feature mode: EventBus.Publish(new
/// FeatureModeRequestedEvent(TileCollectorManager.FeatureId)), or call StartFeatureMode() directly.
///
/// If introPopup is assigned, StartFeatureMode locks the board/HUD and enters FeatureMode
/// immediately (same as before) but holds off on OnModeStarted/FeatureModeStartedEvent and the
/// actual timed session until the player dismisses it - see BeginSessionPhase, same shape as
/// FreeSpinsManager's introPopup/BeginSpinPhase.</summary>
[DisallowMultipleComponent]
public class TileCollectorManager : MonoBehaviour
{
    public const string FeatureId = "tile_collector";

    public static TileCollectorManager Instance { get; private set; }

    [Header("References (auto-found in Awake if left empty)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private Board board;
    [Tooltip("Optional - if assigned, shown (and pauses the game) right as the mode starts, announcing the win, before the timer/tap-to-collect window actually begins. Leave unassigned to skip straight to collecting.")]
    [SerializeField] private TileCollectorIntroPopup introPopup;

    [Header("Session")]
    [Min(1f)]
    [SerializeField] private float duration = 15f;
    [Min(1)]
    [SerializeField] private int scorePerTile = 25;

    [Header("Scene Toggles")]
    [Tooltip("The board stays visible/interactive for this whole mode - these are for surrounding HUD only.")]
    [SerializeField] private GameObject[] hideWhileActive;
    [SerializeField] private GameObject[] showWhileActive;

    [Header("Events")]
    public UnityEvent OnModeStarted;
    public UnityEvent OnModeEnded;
    /// <summary>(elapsed, duration, tilesCollected) - bind a timer/counter HUD to this.</summary>
    public UnityEvent<float, float, int> OnProgressChanged;

    public bool IsActive { get; private set; }

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
            Debug.LogWarning("[TileCollectorManager] No Board found in scene - StartFeatureMode will be a no-op.");
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Subscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Unsubscribe<TileCollectorProgressEvent>(HandleProgress);
        EventBus.Subscribe<TileCollectorProgressEvent>(HandleProgress);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Unsubscribe<TileCollectorProgressEvent>(HandleProgress);
    }

    private void HandleFeatureModeRequested(FeatureModeRequestedEvent evt)
    {
        if (evt.FeatureId == FeatureId) StartFeatureMode();
    }

    public void StartFeatureMode()
    {
        if (IsActive || board == null) return;
        IsActive = true;

        SetActiveGroup(hideWhileActive, false);
        SetActiveGroup(showWhileActive, true);

        gameManager?.EnterFeatureMode();
        Debug.Log($"[TileCollectorManager] Started - {duration}s, {scorePerTile} points/tile.");

        if (introPopup != null)
            introPopup.Show(BeginSessionPhase);
        else
            BeginSessionPhase();
    }

    /// <summary>Actually starts the timed collecting session. Called immediately by
    /// StartFeatureMode if no intro popup is assigned, or as the popup's dismiss callback
    /// (Continue button / auto-dismiss timer) otherwise - either way, this is the point at which
    /// OnModeStarted/FeatureModeStartedEvent fire and RunSession's timer actually starts counting
    /// down, since "started" should mean "the player can actually start tapping now", not "the
    /// win announcement is on screen."</summary>
    private void BeginSessionPhase()
    {
        if (!IsActive) return; // something could have ended the mode while the popup was showing

        OnModeStarted?.Invoke();
        EventBus.Publish(new FeatureModeStartedEvent(FeatureId));

        StartCoroutine(RunSession());
    }

    private IEnumerator RunSession()
    {
        yield return board.PlayTileCollectorSession(duration, scorePerTile);
        EndFeatureMode();
    }

    private void HandleProgress(TileCollectorProgressEvent evt) =>
        OnProgressChanged?.Invoke(evt.Elapsed, evt.Duration, evt.TilesCollected);

    private void EndFeatureMode()
    {
        if (!IsActive) return;
        IsActive = false;

        SetActiveGroup(showWhileActive, false);
        SetActiveGroup(hideWhileActive, true);

        gameManager?.ExitFeatureMode();

        OnModeEnded?.Invoke();
        // No lose condition in this mode (no damage source), so it always "survives".
        EventBus.Publish(new FeatureModeEndedEvent(FeatureId, survived: true));
    }

    private static void SetActiveGroup(GameObject[] group, bool active)
    {
        if (group == null) return;
        foreach (var go in group)
            if (go != null) go.SetActive(active);
    }
}
