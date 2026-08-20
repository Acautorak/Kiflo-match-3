using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Controls the "Cookie Smash" feature mode: a big cookie fills the screen, the player taps it a
/// fixed number of times to break it (see requiredTaps), each tap visibly cracking it further via
/// CookieCrack.shader's _CrackAmount property. Once fully broken, a random powerup is rolled from
/// rewardPool and applied immediately (no player choice, unlike the stage-clear powerup screen -
/// see PowerupManager for that flow), then shown alongside a random fortune line in
/// fortunePanel. Dismissing the panel ends the mode.
///
/// Same overall shape as KebabKarnageManager (singleton, scene-toggle groups, EnterFeatureMode/
/// ExitFeatureMode, FeatureModeRequestedEvent subscription) - see that class for the pattern this
/// follows. The two real differences: tap detection is uGUI-driven (CookieTapTarget forwards a
/// single UI object's clicks here) rather than Physics2D-driven across many falling objects, and
/// there's no lose condition - every session ends in a reward, there's just a pace to it.
///
/// Start this by publishing EventBus.Publish(new FeatureModeRequestedEvent(CookieSmashManager.FeatureId))
/// from wherever your madness-meter threshold logic lives, or call StartFeatureMode() /
/// Instance.StartFeatureMode() directly.
/// </summary>
[DisallowMultipleComponent]
public class CookieSmashManager : MonoBehaviour
{
    public const string FeatureId = "cookie_smash";

    public static CookieSmashManager Instance { get; private set; }

    [Header("References (auto-found in Awake if left empty - same pattern as Board.cs)")]
    [SerializeField] private GameManager gameManager;
    [SerializeField] private PlayerRunStats playerRunStats;
    [SerializeField] private PlayerHealth playerHealth;
    [Tooltip("Panel shown once the cookie fully breaks - fortune text + the granted powerup. " +
             "Required for a meaningful reward reveal; if left unassigned the mode just applies " +
             "the powerup silently and ends immediately with no panel.")]
    [SerializeField] private CookieFortunePanel fortunePanel;

    [Header("Scene Toggles")]
    [Tooltip("Objects disabled while this mode is active and re-enabled when it ends (e.g. the match-3 board root, its HUD).")]
    [SerializeField] private GameObject[] hideWhileActive;
    [Tooltip("Objects enabled while this mode is active and disabled when it ends (e.g. the cookie canvas root).")]
    [SerializeField] private GameObject[] showWhileActive;

    [Header("Cookie Visual")]
    [Tooltip("The cookie's own Image - CookieTapTarget should be on the same GameObject (or a " +
             "child covering the same rect) so taps route back to RegisterTap(). Its material is " +
             "instanced at runtime (see Awake) so _CrackAmount changes don't affect other users of " +
             "the same shared material asset.")]
    [SerializeField] private Image cookieImage;
    [Tooltip("Base material using CookieCrack.shader. Instanced into cookieImage.material at " +
             "runtime - assign the ASSET here, not a runtime instance.")]
    [SerializeField] private Material crackMaterialTemplate;
    private static readonly int CrackAmountId = Shader.PropertyToID("_CrackAmount");

    [Tooltip("Punch-scale + fade played on the cookie the instant the final tap lands, before the " +
             "fortune panel shows. Purely cosmetic - set duration to 0 to skip it.")]
    [SerializeField] private float breakAnimDuration = 0.35f;

    [Header("Taps")]
    [Tooltip("Exactly how many taps break the cookie - fixed, not scaled by Madness meter overflow.")]
    [Min(1)] [SerializeField] private int requiredTaps = 8;
    [Tooltip("Ignores taps landing faster than this, in seconds - guards against a double-tap or " +
             "an over-eager multi-touch device counting as 2+ taps at once.")]
    [Min(0f)] [SerializeField] private float minTapInterval = 0.05f;

    [Header("Reward")]
    [Tooltip("Pool the random powerup is rolled from when the cookie breaks. Reuses " +
             "PowerupPoolConfig (same asset type the stage-clear powerup screen uses) rather than " +
             "inventing a parallel pool type - point this at the same asset, a dedicated smaller " +
             "one, or leave unassigned to grant nothing (fortune-only).")]
    [SerializeField] private PowerupPoolConfig rewardPool;
    [SerializeField] private FortuneTextPool fortuneTextPool;

    [Header("Events")]
    public UnityEvent OnModeStarted;
    public UnityEvent OnCookieBroken;
    public UnityEvent OnModeEnded;
    public UnityEvent<int, int> OnTapsChanged;

    public bool IsActive { get; private set; }
    public int TapsLanded { get; private set; }

    private bool _cookieBroken;
    private float _lastTapTime = -999f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (playerRunStats == null) playerRunStats = FindAnyObjectByType<PlayerRunStats>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (gameManager == null)
            Debug.LogWarning("[CookieSmashManager] No GameManager found in scene - board input will NOT be disabled while this mode is active.");

        // Instance the material once up front so runtime _CrackAmount writes never touch the
        // shared asset - same reasoning as any other "don't mutate a shared Material" rule.
        if (cookieImage != null && crackMaterialTemplate != null)
            cookieImage.material = Instantiate(crackMaterialTemplate);
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
        _cookieBroken = false;
        TapsLanded = 0;
        _lastTapTime = -999f;

        SetActiveGroup(hideWhileActive, false);
        SetActiveGroup(showWhileActive, true);

        if (cookieImage != null)
        {
            cookieImage.gameObject.SetActive(true);
            cookieImage.material?.SetFloat(CrackAmountId, 0f);
            cookieImage.transform.localScale = Vector3.one;
            var color = cookieImage.color;
            color.a = 1f;
            cookieImage.color = color;
        }

        if (gameManager != null)
        {
            gameManager.EnterFeatureMode();
            Debug.Log($"[CookieSmashManager] Started - GameManager.AllowsPlayerInput is now {gameManager.AllowsPlayerInput} (should be False).");
        }

        OnTapsChanged?.Invoke(TapsLanded, requiredTaps);
        OnModeStarted?.Invoke();
        EventBus.Publish(new FeatureModeStartedEvent(FeatureId));
        EventBus.Publish(new CookieSmashProgressEvent(TapsLanded, requiredTaps));
    }

    /// <summary>Called by CookieTapTarget.OnPointerDown. Public so a debug button or an
    /// alternate input method (keyboard, controller) can drive it too.</summary>
    public void RegisterTap()
    {
        if (!IsActive || _cookieBroken) return;
        if (Time.unscaledTime - _lastTapTime < minTapInterval) return;
        _lastTapTime = Time.unscaledTime;

        TapsLanded = Mathf.Min(requiredTaps, TapsLanded + 1);
        float progress = requiredTaps > 0 ? (float)TapsLanded / requiredTaps : 1f;
        cookieImage?.material?.SetFloat(CrackAmountId, progress);

        OnTapsChanged?.Invoke(TapsLanded, requiredTaps);
        EventBus.Publish(new CookieSmashProgressEvent(TapsLanded, requiredTaps));

        if (TapsLanded >= requiredTaps) BreakCookie();
    }

    private void BreakCookie()
    {
        if (_cookieBroken) return;
        _cookieBroken = true;

        OnCookieBroken?.Invoke();
        StartCoroutine(BreakSequence());
    }

    private System.Collections.IEnumerator BreakSequence()
    {
        // Simple punch-scale + fade rather than pulling in a shatter-particle dependency - swap
        // this coroutine's body for a shatter VFX / DOTween sequence if you want something fancier,
        // the rest of the flow (reward roll -> fortune panel -> end) doesn't care how this looks.
        if (cookieImage != null && breakAnimDuration > 0f)
        {
            float t = 0f;
            Vector3 startScale = cookieImage.transform.localScale;
            Color startColor = cookieImage.color;

            while (t < breakAnimDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / breakAnimDuration);
                cookieImage.transform.localScale = Vector3.Lerp(startScale, startScale * 1.15f, p);
                var c = startColor;
                c.a = Mathf.Lerp(1f, 0f, p);
                cookieImage.color = c;
                yield return null;
            }

            cookieImage.gameObject.SetActive(false);
        }

        var grantedPowerup = RollAndApplyReward();
        string fortune = fortuneTextPool != null ? fortuneTextPool.PickRandom() : "...";

        if (fortunePanel != null)
            fortunePanel.Show(fortune, grantedPowerup, EndFeatureMode);
        else
            EndFeatureMode(); // no panel assigned - reward's already applied, just close out
    }

    /// <summary>Rolls one weighted-random powerup from rewardPool and applies it immediately -
    /// same weighted-pick shape as MadnessSystem.PickWeightedOption (cumulative-weight roll over
    /// Random.Range(0, total)), reimplemented locally rather than depending on WeightedPool's
    /// exact generic signature. Deliberately NOT run-seeded (uses UnityEngine.Random, not
    /// RunRandom) - this is a real-time bonus moment, not part of deterministic stage generation,
    /// same as KebabKarnageManager's survive-reward. Returns null (grants nothing) if rewardPool
    /// is unassigned or empty.</summary>
    private PowerupDefinition RollAndApplyReward()
    {
        if (rewardPool == null || rewardPool.powerups == null || rewardPool.powerups.Length == 0)
            return null;

        float total = 0f;
        foreach (var p in rewardPool.powerups)
            if (p != null) total += Mathf.Max(0f, p.weight);
        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        float cumulative = 0f;
        PowerupDefinition picked = null;
        foreach (var p in rewardPool.powerups)
        {
            if (p == null) continue;
            cumulative += Mathf.Max(0f, p.weight);
            if (roll <= cumulative) { picked = p; break; }
        }
        picked ??= rewardPool.powerups[rewardPool.powerups.Length - 1];
        if (picked == null) return null;

        Debug.Log($"[CookieSmashManager] Rolled and applied reward powerup: '{picked.title}' ({picked.name})");
        picked.Apply(playerRunStats, playerHealth);
        // Reuses the same event the stage-clear powerup screen publishes on a real pick, so any
        // global listener (toast UI, analytics) reacts to a Cookie Smash grant identically.
        EventBus.Publish(new PowerupSelectedEvent(picked));
        return picked;
    }

    private void EndFeatureMode()
    {
        if (!IsActive) return;
        IsActive = false;

        SetActiveGroup(showWhileActive, false);
        SetActiveGroup(hideWhileActive, true);

        gameManager?.ExitFeatureMode();

        OnModeEnded?.Invoke();
        EventBus.Publish(new FeatureModeEndedEvent(FeatureId, true)); // no lose condition - every session "survives"
    }

    private static void SetActiveGroup(GameObject[] group, bool active)
    {
        if (group == null) return;
        foreach (var go in group)
            if (go != null) go.SetActive(active);
    }

    /// <summary>Call from a pause menu / forfeit button if you need to bail out early. Skips the
    /// reward roll and fortune panel entirely - an aborted session grants nothing, same spirit as
    /// KebabKarnageManager.ForceEnd(countAsSurvived: false).</summary>
    public void ForceEnd()
    {
        if (!IsActive) return;
        StopAllCoroutines();
        _cookieBroken = true;
        if (cookieImage != null) cookieImage.gameObject.SetActive(false);
        EndFeatureMode();
    }
}
