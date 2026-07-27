using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Controls the "Kebab Karnage" feature mode: asteroids rain down for a fixed duration, the
/// player taps to break them before they reach a designer-placed AsteroidKillZone. Reaching 0
/// HP (shared PlayerHealth pool) ends the mode as a loss; surviving the full duration ends it as
/// a win and grants a reward meant to help finish the current stage (heal / bonus grace moves /
/// bonus score) rather than a permanent run-wide upgrade.
///
/// Start this by publishing EventBus.Publish(new FeatureModeRequestedEvent(KebabKarnageManager.FeatureId))
/// from wherever your madness-meter threshold logic lives, or just call StartFeatureMode() /
/// Instance.StartFeatureMode() directly if that's simpler for your setup.
/// </summary>
[DisallowMultipleComponent]
public class KebabKarnageManager : MonoBehaviour
{
    public const string FeatureId = "kebab_karnage";

    public static KebabKarnageManager Instance { get; private set; }

    [Header("References (auto-found in Awake if left empty - same pattern as Board.cs)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerRunStats playerRunStats;
    [Tooltip("Optional - if assigned, the survive-reward bonus score is added via Board.AddBonusScore.")]
    [SerializeField] private Board board;
    [Tooltip("Optional - if assigned, shown (and pauses the game) right as the mode starts, before asteroids begin spawning. Leave unassigned to skip straight to spawning.")]
    [SerializeField] private KebabKarnageIntroPopup introPopup;

    [Header("Scene Toggles")]
    [Tooltip("Objects disabled while this mode is active and re-enabled when it ends (e.g. the match-3 board root, its HUD).")]
    [SerializeField] private GameObject[] hideWhileActive;
    [Tooltip("Objects enabled while this mode is active and disabled when it ends (e.g. the asteroid field root, feature HUD).")]
    [SerializeField] private GameObject[] showWhileActive;

    [Header("Spawn Area")]
    [Tooltip("Asteroids spawn at a random X between these two transforms' X positions - drag empty GameObjects to mark the play field edges.")]
    [SerializeField] private Transform spawnAreaLeft;
    [SerializeField] private Transform spawnAreaRight;
    [SerializeField] private float spawnY = 6f;
    [Tooltip("Z position asteroids spawn at. Only matters for correctly converting tap screen " +
             "positions to world space if you're using a Perspective camera - for the more common " +
             "Orthographic 2D setup this has no effect on tap detection (Physics2D queries ignore Z " +
             "entirely). Doesn't need to differ from your board's Z anymore.")]
    [SerializeField] private float spawnZ = -1f;

    [Header("Asteroid Prefabs")]
    [Tooltip("Pool of possible asteroid prefabs, e.g. small/medium/large kebab skewers with different max health.")]
    [SerializeField] private FallingAsteroid[] asteroidPrefabs;

    [Header("Spawn Pacing")]
    [SerializeField] private float initialSpawnInterval = 1.2f;
    [SerializeField] private float minSpawnInterval = 0.4f;
    [Tooltip("How much the spawn interval shrinks per second of mode duration (ramps difficulty up)")]
    [SerializeField] private float spawnIntervalRampPerSecond = 0.01f;

    [Header("Fall Speed")]
    [SerializeField] private float initialFallSpeed = 2f;
    [SerializeField] private float maxFallSpeed = 5f;
    [SerializeField] private float fallSpeedRampPerSecond = 0.05f;

    [Header("Duration")]
    [Tooltip("How long the mode lasts if the player survives, in seconds")]
    [SerializeField] private float modeDuration = 30f;

    [Header("Survive Reward - meant to help finish the CURRENT stage, not a permanent upgrade")]
    [Tooltip("HP restored via PlayerHealth.Heal if the player survives the full duration.")]
    [SerializeField] private int healOnSurvive = 1;
    [Tooltip("Extra grace-period moves granted via PlayerRunStats.AddBonusGraceMoves if the player survives.")]
    [SerializeField] private int bonusGraceMovesOnSurvive = 0;
    [Tooltip("Bonus score added via Board.AddBonusScore (only if a Board reference is assigned above) if the player survives.")]
    [SerializeField] private int bonusScoreOnSurvive = 0;

    [Header("Events")]
    public UnityEvent OnModeStarted;
    public UnityEvent OnModeWon;
    public UnityEvent OnModeLost;
    public UnityEvent<int> OnAsteroidsBrokenChanged;
    public UnityEvent<float, float> OnTimeChanged;

    public bool IsActive { get; private set; }
    public int AsteroidsBroken { get; private set; }

    private readonly List<FallingAsteroid> _activeAsteroids = new List<FallingAsteroid>();
    private Coroutine _spawnRoutine;
    private Coroutine _timerRoutine;
    private float _elapsed;

    private void Awake()
    {
        // Local singleton purely for convenience (e.g. calling Instance.StartFeatureMode()
        // from a debug button); the EventBus path doesn't need this.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Same defensive auto-find pattern Board.cs uses for its own dependencies (see
        // Board.Awake) - if any of these were left unassigned in the inspector, EnterFeatureMode
        // would otherwise silently no-op via the null-conditional below and the board would
        // never actually stop accepting swaps while asteroids are falling.
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (playerRunStats == null) playerRunStats = FindAnyObjectByType<PlayerRunStats>();
        if (board == null) board = FindAnyObjectByType<Board>();

        if (gameManager == null)
            Debug.LogWarning("[KebabKarnageManager] No GameManager found in scene - board input will NOT be disabled while this mode is active.");
        if (playerHealth == null)
            Debug.LogWarning("[KebabKarnageManager] No PlayerHealth found in scene - asteroids reaching the kill zone won't damage the player.");
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
        EventBus.Subscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);
    }

    private void OnDisable() => EventBus.Unsubscribe<FeatureModeRequestedEvent>(HandleFeatureModeRequested);

    /// <summary>
    /// Tap detection lives here centrally rather than in each FallingAsteroid's own OnMouseDown.
    /// Unity's OnMouseDown tracks "which object is under the pointer" per-camera internally, and
    /// destroying that object mid-click (which happens here whenever a tap breaks an asteroid)
    /// can leave that internal tracking in a bad state when colliders overlap - which is exactly
    /// what asteroids do, unlike your board's Symbol tiles which never overlap each other. Using
    /// Physics2D.OverlapPointAll instead sidesteps that machinery entirely and explicitly checks
    /// every collider at the tap point, so overlapping asteroids can't confuse it.
    /// </summary>
    [Header("Debug")]
    [Tooltip("Temporary - logs each stage of tap detection to help track down the 'clicking stops working' issue. Turn off once resolved.")]
    [SerializeField] private bool debugTapLogging = false;

    private void Update()
    {
        if (!IsActive) return;
        if (debugTapLogging && Time.frameCount % 120 == 0) Debug.Log("[KebabKarnageManager] Update alive, IsActive=true"); // periodic heartbeat, not per-frame spam
        if (!TryGetPointerDownThisFrame(out var screenPos)) return;

        if (debugTapLogging) Debug.Log($"[KebabKarnageManager] Pointer-down detected at screen {screenPos}.");

        try
        {
            TryHitAsteroidAtScreenPoint(screenPos);
        }
        catch (System.Exception ex)
        {
            // If this method throws uncaught, Unity silently stops calling this MonoBehaviour's
            // Update() for the rest of the session - which would look exactly like "clicking
            // stopped working" while asteroids keep falling (they're coroutine-driven, not
            // Update-driven). Catching here means a bad frame just gets logged and skipped
            // instead of quietly killing all future tap detection.
            Debug.LogException(ex, this);
        }
    }

    private static bool TryGetPointerDownThisFrame(out Vector2 screenPosition)
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Pointer.current != null && UnityEngine.InputSystem.Pointer.current.press.wasPressedThisFrame)
        {
            screenPosition = UnityEngine.InputSystem.Pointer.current.position.ReadValue();
            return true;
        }
        screenPosition = default;
        return false;
#else
        if (Input.GetMouseButtonDown(0))
        {
            screenPosition = Input.mousePosition;
            return true;
        }

        for (int i = 0; i < Input.touchCount; i++)
        {
            var touch = Input.GetTouch(i);
            if (touch.phase == TouchPhase.Began)
            {
                screenPosition = touch.position;
                return true;
            }
        }

        screenPosition = default;
        return false;
#endif
    }

    private void TryHitAsteroidAtScreenPoint(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null) return;

        float distanceFromCamera = Mathf.Abs(spawnZ - cam.transform.position.z);
        Vector3 worldPos = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, distanceFromCamera));

        // OverlapPointAll (not just OverlapPoint) so an overlapping stack of asteroids can't
        // cause this to find a non-asteroid collider (or nothing) instead - it checks every
        // collider at the point and picks the first one that's actually a FallingAsteroid.
        var hits = Physics2D.OverlapPointAll(worldPos);

        if (debugTapLogging) Debug.Log($"[KebabKarnageManager] World pos {worldPos}, {hits.Length} collider(s) hit.");

        foreach (var col in hits)
        {
            if (col == null) continue; // defensive: skip if its GameObject was destroyed earlier this frame

            var asteroid = col.GetComponent<FallingAsteroid>();
            if (asteroid == null) continue;

            if (debugTapLogging) Debug.Log($"[KebabKarnageManager] Hit asteroid '{asteroid.name}', dealing {GetCurrentTapDamage()} damage.");
            asteroid.TakeHit(GetCurrentTapDamage());
            return; // only damage one asteroid per tap even if several overlap
        }
    }

    private void HandleFeatureModeRequested(FeatureModeRequestedEvent evt)
    {
        if (evt.FeatureId == FeatureId) StartFeatureMode();
    }

    public void StartFeatureMode()
    {
        if (IsActive) return;
        IsActive = true;

        AsteroidsBroken = 0;
        _elapsed = 0f;

        // Lock the board and switch scene objects immediately - don't wait on the popup for
        // this part, so nothing is clickable underneath it even during the announcement.
        SetActiveGroup(hideWhileActive, false);
        SetActiveGroup(showWhileActive, true);

        if (gameManager != null)
        {
            gameManager.EnterFeatureMode();
            Debug.Log($"[KebabKarnageManager] Started - GameManager.AllowsPlayerInput is now {gameManager.AllowsPlayerInput} (should be False).");
        }

        if (introPopup != null)
            introPopup.Show(BeginAsteroidPhase);
        else
            BeginAsteroidPhase();
    }

    /// <summary>
    /// Actually starts asteroids falling. Called immediately by StartFeatureMode if no intro
    /// popup is assigned, or as the popup's dismiss callback otherwise - either way, this is the
    /// point at which OnModeStarted/FeatureModeStartedEvent fire, since "started" should mean
    /// "asteroids are now falling", not "the announcement is on screen."
    /// </summary>
    private void BeginAsteroidPhase()
    {
        if (!IsActive) return; // ForceEnd could have been called while the popup was showing

        OnAsteroidsBrokenChanged?.Invoke(AsteroidsBroken);
        OnTimeChanged?.Invoke(_elapsed, modeDuration);
        OnModeStarted?.Invoke();
        EventBus.Publish(new FeatureModeStartedEvent(FeatureId));

        _spawnRoutine = StartCoroutine(SpawnLoop());
        _timerRoutine = StartCoroutine(TimerLoop());
    }

    private IEnumerator SpawnLoop()
    {
        while (IsActive)
        {
            SpawnAsteroid();

            float interval = Mathf.Max(minSpawnInterval, initialSpawnInterval - _elapsed * spawnIntervalRampPerSecond);
            yield return new WaitForSeconds(interval);
        }
    }

    private IEnumerator TimerLoop()
    {
        while (IsActive && _elapsed < modeDuration)
        {
            _elapsed += Time.deltaTime;
            OnTimeChanged?.Invoke(_elapsed, modeDuration);
            EventBus.Publish(new KebabKarnageProgressEvent(_elapsed, modeDuration, AsteroidsBroken));
            yield return null;
        }

        if (IsActive)
        {
            EndFeatureMode(survived: true);
        }
    }

    private void SpawnAsteroid()
    {
        if (asteroidPrefabs == null || asteroidPrefabs.Length == 0)
        {
            Debug.LogWarning("[KebabKarnageManager] No asteroid prefabs assigned.");
            return;
        }
        if (spawnAreaLeft == null || spawnAreaRight == null)
        {
            Debug.LogWarning("[KebabKarnageManager] Spawn area transforms not assigned.");
            return;
        }

        var prefab = asteroidPrefabs[Random.Range(0, asteroidPrefabs.Length)];
        float x = Random.Range(spawnAreaLeft.position.x, spawnAreaRight.position.x);
        var asteroid = Instantiate(prefab, new Vector3(x, spawnY, spawnZ), Quaternion.identity);

        float fallSpeed = Mathf.Min(maxFallSpeed, initialFallSpeed + _elapsed * fallSpeedRampPerSecond);
        asteroid.Init(fallSpeed);

        asteroid.OnBroken += HandleAsteroidBroken;
        asteroid.OnReachedBottom += HandleAsteroidReachedBottom;

        _activeAsteroids.Add(asteroid);
    }

    /// <summary>Base 1 tap damage + any permanent bonus accumulated on PlayerRunStats (e.g. from a powerup).</summary>
    private int GetCurrentTapDamage() => 1 + (playerRunStats != null ? playerRunStats.KebabTapDamageBonus : 0);

    private void HandleAsteroidBroken(FallingAsteroid asteroid)
    {
        Unregister(asteroid);
        AsteroidsBroken++;
        OnAsteroidsBrokenChanged?.Invoke(AsteroidsBroken);
    }

    private void HandleAsteroidReachedBottom(FallingAsteroid asteroid)
    {
        Unregister(asteroid);

        playerHealth?.TakeDamage(asteroid.Damage);

        if (playerHealth != null && playerHealth.CurrentHealth <= 0)
            EndFeatureMode(survived: false);
    }

    private void Unregister(FallingAsteroid asteroid)
    {
        asteroid.OnBroken -= HandleAsteroidBroken;
        asteroid.OnReachedBottom -= HandleAsteroidReachedBottom;
        _activeAsteroids.Remove(asteroid);
    }

    private void EndFeatureMode(bool survived)
    {
        if (!IsActive) return;
        IsActive = false;

        if (_spawnRoutine != null) StopCoroutine(_spawnRoutine);
        if (_timerRoutine != null) StopCoroutine(_timerRoutine);

        foreach (var asteroid in new List<FallingAsteroid>(_activeAsteroids))
        {
            if (asteroid == null) continue;
            Unregister(asteroid);
            Destroy(asteroid.gameObject);
        }

        SetActiveGroup(showWhileActive, false);
        SetActiveGroup(hideWhileActive, true);

        gameManager?.ExitFeatureMode();

        if (survived) GrantSurviveReward();

        if (survived) OnModeWon?.Invoke();
        else OnModeLost?.Invoke();

        EventBus.Publish(new FeatureModeEndedEvent(FeatureId, survived));
    }

    /// <summary>
    /// Applies whichever reward fields are non-zero. All three are meant to help the player
    /// finish the stage they're currently on (a heal, extra grace moves, or bonus score) - see
    /// the header tooltip. Leave any at 0 to skip it.
    /// </summary>
    private void GrantSurviveReward()
    {
        if (healOnSurvive > 0) playerHealth?.Heal(healOnSurvive);
        if (bonusGraceMovesOnSurvive > 0) playerRunStats?.AddBonusGraceMoves(bonusGraceMovesOnSurvive);
        if (bonusScoreOnSurvive > 0) board?.AddBonusScore(bonusScoreOnSurvive);
    }

    private static void SetActiveGroup(GameObject[] group, bool active)
    {
        if (group == null) return;
        foreach (var go in group)
            if (go != null) go.SetActive(active);
    }

    /// <summary>Call from a pause menu / forfeit button if you need to bail out early.</summary>
    public void ForceEnd(bool countAsSurvived) => EndFeatureMode(countAsSurvived);
}
