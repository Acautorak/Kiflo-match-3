using UnityEngine;

/// <summary>
/// Bridges the generic MadnessMeter to feature-mode mini-games. Listens for the meter filling
/// up and, when it does, consumes it immediately (so it starts refilling from 0 right away) but
/// DEFERS the actual feature-mode request until GameManager reports it's back to a settled state
/// (Idle or GracePeriod) - i.e. any in-flight cascade has fully finished resolving.
///
/// This matters because the meter can fill mid-cascade (a match's Madness effects run as part of
/// match resolution, before Board's own coroutine has finished clearing/refilling/scoring
/// everything). Starting a feature mode transition in the middle of that used to race against
/// Board's own resolve-to-Idle callback landing afterward and re-enabling input out from under
/// the feature mode - see GameManager.SetState's FeatureMode guard for the safety net that
/// catches this if it ever happens anyway, but waiting here avoids the race in the first place.
///
/// This is the implementation of the "what happens when it fills" behavior MadnessMeter's own
/// header deliberately leaves open, kept as a separate component so MadnessMeter itself stays
/// generic. Which feature mode gets requested is a per-stage designer choice - see
/// StageDefinition.featureModeOnMeterFull (assigned by ProceduralStageGenerator from
/// StageGenerationConfig.featureModeWeights) - read here off StageManager.CurrentStage.
/// featureIdOnFill is kept only as the fallback used when no StageManager/CurrentStage is
/// available (e.g. Board running standalone without a StageManager in the scene).
///
/// Two additional pacing controls live here too:
/// - featuresBlockedDuringGracePeriod: specific features can be locked out of firing while a
///   stage's clear-out grace period is active (StageManager.IsStageClearPending) - they simply
///   wait for the board to be back at a genuine mid-stage Idle instead.
/// - guaranteedFallbackMoves: if the Madness meter hasn't organically filled within this many
///   player moves, a feature fires anyway, so pacing doesn't depend entirely on how much scoring/
///   combo activity a given stretch of play happens to produce. This counter deliberately pauses
///   during a stage's grace period so it can't force a feature right as a stage is wrapping up.
///
/// One more block applies unconditionally to every feature, not configurable: while
/// StageManager.IsAwaitingPowerupSelection is true (the post-clear powerup-choice screen), no
/// feature mode can fire - it stays pending until the player has picked and the next stage has
/// actually started.
/// </summary>
public class MadnessFeatureTrigger : MonoBehaviour
{
    [SerializeField] private MadnessMeter meter;
    [SerializeField] private GameManager gameManager;
    [Tooltip("Optional explicit link. If left empty, auto-found in Awake - used to read the current " +
             "stage's featureModeOnMeterFull choice.")]
    [SerializeField] private StageManager stageManager;

    [Tooltip("Fallback feature mode used only when no StageManager/CurrentStage is available (e.g. " +
             "Board running standalone without a StageManager in the scene). Defaults to Kebab Karnage.")]
    [SerializeField] private MadnessFeatureModeChoice featureModeOnFillFallback = MadnessFeatureModeChoice.KebabKarnage;

    [Header("Grace Period Locking")]
    [Tooltip("Feature modes that must NOT fire while a stage-clear grace period is active (goal " +
             "already reached, player is spending their bonus grace moves before the stage fully " +
             "clears - see StageManager.IsStageClearPending). A blocked feature just keeps waiting " +
             "instead of firing - it goes off as soon as the board is back to a real mid-stage Idle " +
             "on the next stage. Leave empty to allow every feature during grace period, same as " +
             "before. Pick straight from this dropdown - no manual id-string typing needed.")]
    [SerializeField] private System.Collections.Generic.List<MadnessFeatureModeChoice> featuresBlockedDuringGracePeriod
        = new System.Collections.Generic.List<MadnessFeatureModeChoice>();

    [Header("Guaranteed Fallback")]
    [Tooltip("If > 0, and no feature has organically triggered (via the Madness meter filling) " +
             "within this many player moves, one gets forced anyway - keeps pacing consistent even " +
             "through an unlucky/quiet stretch instead of leaving it purely up to the meter. Move " +
             "counting pauses while a stage-clear grace period is active (see IsStageClearPending) " +
             "so the fallback can't fire right as a stage ends - only during genuine mid-stage play. " +
             "0 = disabled, purely meter-driven like before.")]
    [Min(0)]
    [SerializeField] private int guaranteedFallbackMoves = 40;

    private int _movesSinceLastFeature;

    private bool _pendingRequest;
    private float _pendingOverflow;
    /// <summary>Set by DebugForceFeature - if non-null, PeekFeatureId returns this instead of the
    /// current stage's featureModeOnMeterFull, consumed (cleared) only once a request actually
    /// fires. Playtest-only escape hatch, see DebugFeatureModeMenu.</summary>
    private string _debugForcedFeatureId;

    private void Awake()
    {
        if (gameManager == null) gameManager = FindAnyObjectByType<GameManager>();
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
    }

    private void OnEnable()
    {
        EventBus.Unsubscribe<MadnessMeterChangedEvent>(HandleMeterChanged);
        EventBus.Subscribe<MadnessMeterChangedEvent>(HandleMeterChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<MadnessMeterChangedEvent>(HandleMeterChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
    }

    /// <summary>Counts real mid-stage moves toward the guaranteed fallback (see
    /// guaranteedFallbackMoves) - deliberately does NOT count (or arm) while a stage-clear grace
    /// period or a powerup-selection screen is active, so a quiet stretch that happens to land
    /// right at either of those can't force a feature mode in. The counter simply resumes once
    /// real play picks back up.</summary>
    private void HandlePlayerMove(PlayerMoveEvent evt)
    {
        if (guaranteedFallbackMoves <= 0) return;
        if (_pendingRequest) return; // a request (organic or fallback) is already in flight
        if (stageManager != null && (stageManager.IsStageClearPending || stageManager.IsAwaitingPowerupSelection)) return;

        _movesSinceLastFeature++;
        if (_movesSinceLastFeature < guaranteedFallbackMoves) return;

        Debug.Log($"[MadnessFeatureTrigger] Guaranteed fallback hit ({_movesSinceLastFeature} moves since the last feature) - arming a trigger.");
        _pendingOverflow = 0f;
        _pendingRequest = true;
        TryFireIfSettled();
    }

    private void HandleMeterChanged(MadnessMeterChangedEvent evt)
    {
        if (_pendingRequest) return; // already consumed and waiting to fire - don't consume twice
        if (meter == null || !meter.IsFull) return;

        // Capture how far the meter overshot before Consume() zeroes it out, so whichever feature
        // mode gets requested below can scale itself off it if it wants to (see
        // FeatureModeRequestedEvent.OverflowAmount - FreeSpinsManager uses this for spin count).
        _pendingOverflow = Mathf.Max(0f, evt.Current - evt.Max);

        meter.Consume();
        _pendingRequest = true;
        Debug.Log("[MadnessFeatureTrigger] Meter filled - waiting for the board to settle (Idle/GracePeriod) before starting the feature mode.");

        // In case the board already happens to be settled right now, don't wait an extra frame.
        TryFireIfSettled();
    }

    private void Update()
    {
        if (_pendingRequest) TryFireIfSettled();
    }

    /// <summary>
    /// Playtest-only: forces the next feature-mode request to be featureId instead of whatever
    /// the current stage's featureModeOnMeterFull would normally pick, bypassing the Madness
    /// meter entirely so you don't have to grind matches to test a specific mode. Still goes
    /// through the exact same "wait for the board to settle" path a real meter-fill uses (see
    /// TryFireIfSettled), so it won't yank a feature mode in from under an in-flight cascade -
    /// if you're mid-match when you press the debug button, it'll fire the instant that match
    /// finishes resolving rather than immediately. Called by DebugFeatureModeMenu.
    /// </summary>
    public void DebugForceFeature(string featureId, float overflowAmount = 0f)
    {
        if (string.IsNullOrEmpty(featureId)) return;

        _debugForcedFeatureId = featureId;
        _pendingOverflow = Mathf.Max(0f, overflowAmount);
        _pendingRequest = true;
        Debug.Log($"[MadnessFeatureTrigger] DEBUG override armed - forcing '{featureId}' to fire once the board next settles.");

        TryFireIfSettled();
    }

    private void TryFireIfSettled()
    {
        if (!_pendingRequest) return;

        bool settled = gameManager == null
            || gameManager.CurrentState == GameManager.GameplayState.Idle
            || gameManager.CurrentState == GameManager.GameplayState.GracePeriod;

        // Awaiting a powerup pick is its own hard block, regardless of feature - starting a
        // mini-game while that choice UI is up would either fight it for input or get hidden
        // behind it, and unlike the grace-period list this isn't something any single feature
        // should be allowed to opt into.
        if (stageManager != null && stageManager.IsAwaitingPowerupSelection)
            settled = false;

        if (!settled) return;

        MadnessFeatureModeChoice? choice = PeekFeatureChoice();

        bool inGracePeriod = gameManager != null && gameManager.CurrentState == GameManager.GameplayState.GracePeriod;
        if (inGracePeriod && choice.HasValue && featuresBlockedDuringGracePeriod.Contains(choice.Value))
        {
            // Stay pending - Update() keeps polling every frame, so this fires the instant the
            // state moves on from GracePeriod (either the next real Idle mid-stage, or after the
            // upcoming stage starts), without needing any extra bookkeeping here.
            return;
        }

        string featureId = PeekFeatureId();
        _pendingRequest = false;
        _debugForcedFeatureId = null; // consumed now that a request is actually firing
        _movesSinceLastFeature = 0;
        Debug.Log($"[MadnessFeatureTrigger] Board settled - requesting feature mode '{featureId}' (overflow {_pendingOverflow:0.##}).");
        EventBus.Publish(new FeatureModeRequestedEvent(featureId, _pendingOverflow));
    }

    /// <summary>Same resolution as PeekFeatureId, but returns the MadnessFeatureModeChoice enum
    /// value instead of a concrete feature id string - what featuresBlockedDuringGracePeriod
    /// actually compares against. A debug-forced string that doesn't match any known manager's
    /// FeatureId (e.g. a typo, or a custom feature not in the enum) returns null, which simply
    /// can't match anything in the block list - a debug override always still fires, it just
    /// can't be grace-period-blocked unless it maps back to a real choice.</summary>
    private MadnessFeatureModeChoice? PeekFeatureChoice()
    {
        if (_debugForcedFeatureId != null) return FeatureIdToChoice(_debugForcedFeatureId);

        var stage = stageManager != null ? stageManager.CurrentStage : null;
        return stage?.featureModeOnMeterFull;
    }

    private static MadnessFeatureModeChoice? FeatureIdToChoice(string featureId)
    {
        if (featureId == FreeSpinsManager.FeatureId) return MadnessFeatureModeChoice.FreeSpins;
        if (featureId == LuckyScratchTicketManager.FeatureId) return MadnessFeatureModeChoice.LuckyScratchTicket;
        if (featureId == KebabKarnageManager.FeatureId) return MadnessFeatureModeChoice.KebabKarnage;
        return null;
    }

    /// <summary>The single source of truth mapping a MadnessFeatureModeChoice to the concrete
    /// feature id string each manager listens for - used both to resolve the current stage's
    /// choice and the standalone-mode fallback, so there's only one place to update if a new
    /// feature mode is ever added.</summary>
    private static string FeatureIdFor(MadnessFeatureModeChoice choice) => choice switch
    {
        MadnessFeatureModeChoice.FreeSpins => FreeSpinsManager.FeatureId,
        MadnessFeatureModeChoice.LuckyScratchTicket => LuckyScratchTicketManager.FeatureId,
        _ => KebabKarnageManager.FeatureId,
    };

    /// <summary>Maps the current stage's designer-chosen MadnessFeatureModeChoice to the concrete
    /// feature id string each manager listens for. A debug override (see DebugForceFeature) wins
    /// if one is armed. Side-effect-free (doesn't consume the debug override) so TryFireIfSettled
    /// can safely peek at it while deciding whether a grace-period block applies, without
    /// accidentally discarding the override on a check that ends up NOT firing. Falls back to
    /// featureModeOnFillFallback if there's no StageManager/CurrentStage (e.g. Board running
    /// standalone).</summary>
    private string PeekFeatureId()
    {
        if (_debugForcedFeatureId != null) return _debugForcedFeatureId;

        var stage = stageManager != null ? stageManager.CurrentStage : null;
        var choice = stage?.featureModeOnMeterFull ?? featureModeOnFillFallback;
        return FeatureIdFor(choice);
    }
}
