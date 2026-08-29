using System.Collections;
using DG.Tweening;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>How a Madness Symbol's temporary "can't be destroyed by a match" window counts down.
/// None: no immunity (default - existing Madness Symbols are unaffected). Moves: counts down once
/// per accepted player move regardless of whether this symbol gets matched (see
/// MadnessSystem.TickSurvival / Symbol.TickMadnessImmunityMove). Matches: only counts down each
/// time this symbol is actually caught in a resolved match (see MatchResolver.ClearCell /
/// Symbol.TickMadnessImmunityMatch) - surviving moves untouched doesn't spend it.</summary>
public enum MadnessImmunityMode
{
    None,
    Moves,
    Matches
}

/// <summary>
/// Attach to each symbol prefab (one prefab per SymbolType, or one shared prefab
/// that swaps its sprite via SymbolVisualConfig - either works with Board.cs as written).
/// Requires a Collider2D for click/tap input via OnMouseDown/Drag/Up. Both click-then-click
/// (tap one tile, then tap an adjacent tile) and swipe (press one tile and drag toward a
/// neighbor) work interchangeably, move to move - see OnMouseDrag/OnMouseUp below. OnMouseX
/// callbacks fire for touch as well as mouse on supported platforms, so this covers both without
/// separate touch-specific code.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider2D))]
public class Symbol : MonoBehaviour
{
    public SymbolType Type { get; private set; }
    public SpecialType Special { get; private set; }
    public Vector2Int GridPosition { get; set; }

    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private SymbolVisualConfig visualConfig;

    [Header("Swipe Input (alternative to click-then-click)")]
    [Tooltip("Minimum drag distance, in world units, before a press-and-drag counts as a swipe " +
             "instead of a simple click/tap. Lower = more sensitive; too low can make normal taps " +
             "misfire as swipes.")]
    [SerializeField] private float swipeThreshold = 0.35f;

    [Header("Lock / Freeze (optional)")]
    [Tooltip("Child GameObject holding the frozen/locked look (e.g. an ice overlay sprite). " +
             "Should be INACTIVE by default in the prefab - Symbol just calls SetActive(true/false) " +
             "on it based on lock state, nothing more.")]
    [SerializeField] private GameObject lockOverlay;
    [Tooltip("Optional: SpriteRenderer on the lockOverlay child, if you want a different image " +
             "per remaining layer count / lock behavior. Leave both this and Lock Visual Config " +
             "unassigned if lockOverlay is just a single static ice image - it'll still be enabled " +
             "and disabled correctly with no further setup.")]
    [SerializeField] private SpriteRenderer lockOverlayRenderer;
    [SerializeField] private LockVisualConfig lockVisualConfig;

    [Header("Burning (optional)")]
    [Tooltip("Child GameObject holding the burning look (e.g. a fire/ember overlay). Should be " +
             "INACTIVE by default in the prefab, same as lockOverlay - Symbol just calls " +
             "SetActive(true/false) on it based on burning state, nothing more.")]
    [SerializeField] private GameObject burningOverlay;

    [Header("Madness (optional)")]
    [Tooltip("Child GameObject holding the Madness look (e.g. a swirling overlay). Should be " +
             "INACTIVE by default in the prefab, same as lockOverlay - Symbol just calls " +
             "SetActive(true/false) on it based on Madness state.")]
    [Min(0.01f)]
    [SerializeField] private float convertPulseDuration = 0.35f;
    [Tooltip("Peak scale multiplier reached mid-pulse.")]
    [SerializeField] private float convertPulseScale = 1.25f;
    [Tooltip("Color briefly flashed on the sprite at the pulse's peak, then eased back to normal. Leave white for a subtle flash.")]
    [SerializeField] private Color convertFlashColor = Color.white;

    private Color baseSpriteColor = Color.white;
    private Tween convertTween;

    private void Awake()
    {
        if (spriteRenderer != null) baseSpriteColor = spriteRenderer.color;
    }
    [SerializeField] private GameObject madnessOverlay;
    [Tooltip("Optional: SpriteRenderer on madnessOverlay, swapped to the assigned MadnessSymbolDefinition's icon if both are set.")]
    [SerializeField] private SpriteRenderer madnessOverlayRenderer;
    [Tooltip("Optional child GameObject (e.g. a shield/glow sprite) shown while IsMadnessImmune is " +
             "true, so players can tell this Madness Symbol currently can't be destroyed. Should be " +
             "INACTIVE by default in the prefab, same as the other overlays. Leave unassigned if you " +
             "don't want a distinct immunity visual yet.")]
    [SerializeField] private GameObject madnessImmunityOverlay;

    private Tween activeTween;
    private Tween danceTween;
    private Coroutine danceRoutine;

    public int LockLayers { get; private set; }
    public LockBehavior LockBehaviorMode { get; private set; } = LockBehavior.None;
    public int MovesPerLayer { get; private set; }
    public int MovesUntilNextAutoUnlock { get; private set; }
    public bool IsLocked => LockLayers > 0;

    public MadnessSymbolDefinition MadnessDefinition { get; private set; }
    public int MadnessMovesSurvived { get; private set; }
    public bool IsMadness => MadnessDefinition != null;

    /// <summary>Which countdown (if any) is currently protecting this Madness Symbol from being
    /// destroyed when caught in a match. Set from MadnessSymbolDefinition on spawn - see
    /// InitializeMadness. Deliberately separate from the Lock Layers system (SetLock/IsLocked):
    /// that system's DestroySymbolWhenUnlocked/ScorePerLockHit/gravity-fall/swap-blocking toggles
    /// are board-wide settings meant for ice-block-style obstacles, not per-symbol Madness
    /// behavior, so this tracks its own independent counter instead of reusing LockLayers.</summary>
    public MadnessImmunityMode ImmunityMode { get; private set; } = MadnessImmunityMode.None;
    public int ImmunityRemaining { get; private set; }
    public bool IsMadnessImmune => ImmunityMode != MadnessImmunityMode.None && ImmunityRemaining > 0;

    public void Initialize(SymbolType type, SpecialType special, Vector2Int gridPosition)
    {
        Type = type;
        Special = special;
        GridPosition = gridPosition;
        UpdateVisual();
        UpdateLockVisual();
    }

    public void SetSpecial(SpecialType special)
    {
        Special = special;
        UpdateVisual();
    }

    /// <summary>
    /// Changes this symbol's color/type in place and refreshes its sprite - e.g. a Madness
    /// "convert to color" effect repainting a tile (see MadnessColorConvertEffect / Board.
    /// ConvertRandomSymbols). Purely cosmetic-plus-type: doesn't touch lock state, Madness state,
    /// or Special.
    /// </summary>
    public void SetType(SymbolType newType)
    {
        Type = newType;
        UpdateVisual();
    }
    public Tween PlayConvertHighlight()
    {
        convertTween?.Kill();
        transform.localScale = Vector3.one;

        var half = convertPulseDuration * 0.5f;
        var seq = DOTween.Sequence();
        seq.Append(transform.DOScale(convertPulseScale, half).SetEase(Ease.OutQuad));
        seq.Append(transform.DOScale(1f, half).SetEase(Ease.InQuad));

        if (spriteRenderer != null)
        {
            seq.Insert(0f, spriteRenderer.DOColor(convertFlashColor, half));
            seq.Insert(half, spriteRenderer.DOColor(baseSpriteColor, half));
        }

        convertTween = seq;
        return seq;
    }

    /// <summary>Applies a fresh lock. movesPerLayer only matters for LockBehavior.Temporary.</summary>
    public void SetLock(int layers, LockBehavior behavior, int movesPerLayer = 3)
    {
        LockLayers = Mathf.Max(0, layers);
        LockBehaviorMode = LockLayers > 0 ? behavior : LockBehavior.None;
        MovesPerLayer = Mathf.Max(1, movesPerLayer);
        MovesUntilNextAutoUnlock = MovesPerLayer;
        UpdateLockVisual();
    }

    /// <summary>Restores an exact lock state (used by SaveSystem to preserve the auto-unlock countdown).</summary>
    public void RestoreLockState(int layers, LockBehavior behavior, int movesPerLayer, int movesUntilNextAutoUnlock)
    {
        LockLayers = Mathf.Max(0, layers);
        LockBehaviorMode = LockLayers > 0 ? behavior : LockBehavior.None;
        MovesPerLayer = Mathf.Max(1, movesPerLayer);
        MovesUntilNextAutoUnlock = movesUntilNextAutoUnlock;
        UpdateLockVisual();
    }

    /// <summary>Removes one lock layer. Returns true if this fully unlocked the tile.</summary>
    public bool RemoveLockLayer()
    {
        if (LockLayers <= 0) return true;
        LockLayers--;
        if (LockLayers <= 0)
        {
            LockBehaviorMode = LockBehavior.None;
        }
        else if (LockBehaviorMode == LockBehavior.Temporary)
        {
            MovesUntilNextAutoUnlock = MovesPerLayer;
        }
        UpdateLockVisual();
        return LockLayers <= 0;
    }

    /// <summary>Call once per player move. Returns true if a layer auto-melted off as a result.</summary>
    public bool TickTemporaryLock()
    {
        if (LockBehaviorMode != LockBehavior.Temporary || LockLayers <= 0) return false;

        MovesUntilNextAutoUnlock--;
        if (MovesUntilNextAutoUnlock > 0) return false;

        RemoveLockLayer();
        return true;
    }

    // --- Burning status (see BurningSystem) - modeled directly on the Lock API just above:
    // SetBurning is the SetLock equivalent, TickBurning is the TickTemporaryLock equivalent.
    public bool IsBurning { get; private set; }
    public int MovesUntilBurnOut { get; private set; }

    // Total duration the current burn started with, so TickBurning can compute a 0-1 progress
    // for the shader (_BurnAmount) rather than just an absolute moves-remaining count. Set fresh
    // every time SetBurning ignites (movesUntilBurnOut > 0).
    private int burnTotalMoves;

    private static readonly int BurnAmountID = Shader.PropertyToID("_BurnAmount");
    private MaterialPropertyBlock burnPropertyBlock;

    /// <summary>_BurnAmount value pushed the instant a tile ignites - a small but nonzero floor
    /// rather than 0, so the shader's edge-glow/erosion kicks in immediately (it's gated behind
    /// _BurnAmount > 0.001) instead of the tile looking untouched until the first TickBurning
    /// call on the next move. TickBurning's own progress calculation never drops below this
    /// floor either, so there's no visible jump/reset once real ticking starts.</summary>
    private const float IgniteStartAmount = 0.05f;

    /// <summary>Starts (or refreshes) this symbol's burn countdown - see BurningSystem.
    /// TryIgniteNearby for how a tile gets set alight in the first place. Pass 0 to put out an
    /// existing burn without collecting it (used by SymbolSpawner.Despawn to make sure a pooled
    /// instance doesn't come back out already on fire). Igniting pushes _BurnAmount to
    /// IgniteStartAmount immediately (see that constant's doc) rather than 0, so the SymbolBurning
    /// shader visibly reacts the instant this is called; putting a burn out resets to 0 since
    /// there's nothing left to show.</summary>
    public void SetBurning(int movesUntilBurnOut)
    {
        IsBurning = movesUntilBurnOut > 0;
        MovesUntilBurnOut = Mathf.Max(0, movesUntilBurnOut);
        if (IsBurning) burnTotalMoves = MovesUntilBurnOut;
        if (burningOverlay != null) burningOverlay.SetActive(IsBurning);
        ApplyBurnAmount(IsBurning ? IgniteStartAmount : 0f);
    }

    /// <summary>Call once per accepted player move (see BurningSystem.TickAllBurningTiles).
    /// Returns true the instant the countdown reaches 0 - the caller (BurningSystem) is
    /// responsible for actually collecting the tile; this only manages the counter/visual, same
    /// division of responsibility as TickTemporaryLock/LockingSystem.MeltAllTemporaryLocks. Also
    /// pushes the current burn progress (IgniteStartAmount = just ignited, 1 = about to burn out)
    /// to the SymbolBurning shader every tick, so the tile visibly chars up over its countdown
    /// rather than looking identical until the moment it's collected.</summary>
    public bool TickBurning()
    {
        if (!IsBurning) return false;

        MovesUntilBurnOut--;
        float progress = burnTotalMoves > 0 ? 1f - Mathf.Clamp01((float)MovesUntilBurnOut / burnTotalMoves) : 1f;
        ApplyBurnAmount(Mathf.Max(IgniteStartAmount, progress));

        if (MovesUntilBurnOut > 0) return false;

        IsBurning = false;
        if (burningOverlay != null) burningOverlay.SetActive(false);
        return true;
    }

    /// <summary>Pushes _BurnAmount to spriteRenderer's material via a MaterialPropertyBlock
    /// rather than touching spriteRenderer.material directly - avoids creating a unique material
    /// instance per symbol (which would break SRP batching/GPU instancing across a whole board of
    /// otherwise-identical symbols). No-op if spriteRenderer isn't assigned or isn't using the
    /// SymbolBurning shader - other shaders simply won't have this property, which is harmless.</summary>
    private void ApplyBurnAmount(float amount)
    {
        if (spriteRenderer == null) return;

        burnPropertyBlock ??= new MaterialPropertyBlock();
        spriteRenderer.GetPropertyBlock(burnPropertyBlock);
        burnPropertyBlock.SetFloat(BurnAmountID, amount);
        spriteRenderer.SetPropertyBlock(burnPropertyBlock);
    }

    /// <summary>Marks this symbol as a Madness Symbol of the given definition. MovesSurvived starts at 0.
    /// Also applies the definition's immunity config (if any) - see MadnessSymbolDefinition.immunityMode.</summary>
    public void InitializeMadness(MadnessSymbolDefinition definition)
    {
        MadnessDefinition = definition;
        MadnessMovesSurvived = 0;
        SetMadnessImmunity(definition != null ? definition.immunityMode : MadnessImmunityMode.None,
            definition != null ? definition.immunityAmount : 0);
        UpdateMadnessVisual();
    }

    /// <summary>Strips Madness state back to a plain symbol (e.g. after it's been "defused" by some future effect).</summary>
    public void ClearMadness()
    {
        MadnessDefinition = null;
        MadnessMovesSurvived = 0;
        SetMadnessImmunity(MadnessImmunityMode.None, 0);
        UpdateMadnessVisual();
    }

    /// <summary>Call once per accepted player move this symbol survives unmatched (see Board.TickMadnessSurvival).</summary>
    public void TickMadnessSurvival() => MadnessMovesSurvived++;

    /// <summary>Directly (re)arms immunity - normally driven by InitializeMadness off the definition,
    /// but exposed so a future effect (e.g. a "shield this symbol" power-up) could grant/refresh it
    /// at runtime too. amount &lt;= 0 clears immunity outright regardless of mode.</summary>
    public void SetMadnessImmunity(MadnessImmunityMode mode, int amount)
    {
        ImmunityMode = amount > 0 ? mode : MadnessImmunityMode.None;
        ImmunityRemaining = Mathf.Max(0, amount);
        UpdateMadnessVisual();
    }

    /// <summary>Call once per accepted player move (see MadnessSystem.TickSurvival). No-op unless
    /// ImmunityMode is Moves - Matches-mode immunity only counts down via TickMadnessImmunityMatch.</summary>
    public void TickMadnessImmunityMove()
    {
        if (ImmunityMode != MadnessImmunityMode.Moves || ImmunityRemaining <= 0) return;

        ImmunityRemaining--;
        if (ImmunityRemaining <= 0) ImmunityMode = MadnessImmunityMode.None;
        UpdateMadnessVisual();
    }

    /// <summary>Call once each time this symbol is caught in a resolved match while still immune
    /// (see MatchResolver.ClearCell). No-op unless ImmunityMode is Matches. Returns true if this
    /// hit was the one that used up the last charge, so the caller can fall through to a normal
    /// destroy on the very same hit instead of needing a follow-up match.</summary>
    public bool TickMadnessImmunityMatch()
    {
        if (ImmunityMode != MadnessImmunityMode.Matches || ImmunityRemaining <= 0) return false;

        ImmunityRemaining--;
        bool expired = ImmunityRemaining <= 0;
        if (expired) ImmunityMode = MadnessImmunityMode.None;
        UpdateMadnessVisual();
        return expired;
    }

    /// <summary>Used by detonate-then-reset style effects (e.g. MadnessGrowingDamageEffect) so the threat can build up again.</summary>
    public void ResetMadnessSurvival() => MadnessMovesSurvived = 0;

    private void UpdateMadnessVisual()
    {
        if (madnessImmunityOverlay != null)
            madnessImmunityOverlay.SetActive(IsMadnessImmune);

        if (madnessOverlay == null) return;

        madnessOverlay.SetActive(IsMadness);
        if (IsMadness && madnessOverlayRenderer != null && MadnessDefinition.icon != null)
            madnessOverlayRenderer.sprite = MadnessDefinition.icon;
    }

    private void UpdateVisual()
    {
        if (spriteRenderer == null || visualConfig == null) return;
        var sprite = visualConfig.GetSprite(Type, Special);
        if (sprite != null) spriteRenderer.sprite = sprite;
    }

    private void UpdateLockVisual()
    {
        if (lockOverlay == null) return;

        // Core behavior: just wake it up or put it back to sleep.
        lockOverlay.SetActive(IsLocked);
        if (!IsLocked) return;

        // Optional bonus: if you've wired a renderer + config, pick art per layer/behavior too.
        if (lockOverlayRenderer != null && lockVisualConfig != null)
        {
            var sprite = lockVisualConfig.GetOverlaySprite(LockBehaviorMode, LockLayers);
            if (sprite != null) lockOverlayRenderer.sprite = sprite;
        }
    }

    /// <summary>
    /// Tweens to the given world position and returns the Tween so callers can Join it into
    /// a Sequence (Board does this to wait for every symbol in a swap/fall to finish together).
    /// </summary>
    public Tween MoveTo(Vector3 worldPosition, float duration, Ease ease = Ease.OutQuad)
    {
        hintTween?.Kill(); // a real move means the player acted on (or past) this tile - stop suggesting it
        activeTween?.Kill();
        activeTween = transform.DOMove(worldPosition, duration).SetEase(ease);
        return activeTween;
    }

    [Header("Landing Bounce")]
    [Tooltip("Squash-and-stretch bounce played the instant a gravity fall (see FallTo) arrives - " +
             "NOT played for swaps (see MoveTo), since a horizontal slide isn't a 'landing'.")]
    [Min(0.01f)] [SerializeField] private float landingBounceDuration = 0.18f;
    [Tooltip("How wide/short the squash gets at impact - 0.2 = 20% wider, 20% shorter.")]
    [Range(0f, 0.6f)] [SerializeField] private float landingSquashAmount = 0.22f;

    private Tween landingTween;

    /// <summary>
    /// Same MoveTo contract (returns the move Tween itself for Sequence.Join, exactly like
    /// MoveTo) but chains PlayLandingBounce onto that tween's OnComplete - fires the instant THIS
    /// symbol's own move finishes, independent of whatever outer Sequence it got Join()'d into
    /// (GravityController.Collapse batches a whole column/board's worth of falls into one
    /// Sequence with different individual durations - each tile should bounce the moment it
    /// personally lands, not all at once when the slowest one in the batch finishes). Only
    /// GravityController should call this - a swap should keep using plain MoveTo.
    /// </summary>
    public Tween FallTo(Vector3 worldPosition, float duration, Ease ease = Ease.OutQuad)
    {
        hintTween?.Kill();
        activeTween?.Kill();
        var tween = transform.DOMove(worldPosition, duration).SetEase(ease);
        tween.OnComplete(() => PlayLandingBounce());
        activeTween = tween;
        return tween;
    }

    /// <summary>Quick impact squash (wide+short) easing back out past 1 with a touch of overshoot
    /// (OutElastic) so it reads as a genuine bounce-settle rather than a linear snap-back. Safe to
    /// call standalone too (e.g. a Kebab Karnage asteroid landing) - doesn't depend on FallTo.</summary>
    public Tween PlayLandingBounce()
    {
        landingTween?.Kill();
        transform.localScale = Vector3.one;

        float amt = Mathf.Clamp(landingSquashAmount, 0f, 0.6f);
        var squashed = new Vector3(1f + amt, 1f - amt, 1f);

        var seq = DOTween.Sequence();
        seq.Append(transform.DOScale(squashed, landingBounceDuration * 0.35f).SetEase(Ease.OutQuad));
        seq.Append(transform.DOScale(Vector3.one, landingBounceDuration * 0.65f).SetEase(Ease.OutElastic, 1, 0.6f));

        landingTween = seq;
        return seq;
    }

    [Header("Matched Effect")]
    [Tooltip("Pop-and-fade played by PlayMatchedEffect before the caller destroys this symbol.")]
    [Min(0.01f)] [SerializeField] private float matchedEffectDuration = 0.22f;
    [Tooltip("Peak scale reached mid-pop, before fading out.")]
    [SerializeField] private float matchedPopScale = 1.3f;

    private Tween matchedTween;

    /// <summary>
    /// Plays a quick pop-and-fade, then invokes onComplete once it finishes - the caller
    /// (MatchResolver.ClearCell) uses this to defer Object.Destroy until the visual is actually
    /// done, while the grid cell itself is freed immediately, before this is even called (so
    /// gravity/refill/rescanning never waits on a death animation - this symbol's GameObject just
    /// keeps existing and playing its own exit independently of the logical grid state, exactly
    /// like a fresh replacement tile visually falling in in the same frame is expected to).
    /// Kills any in-flight move/dance tween first - a tile that's mid-swap or mid-fall when it
    /// gets matched shouldn't keep sliding while it's also popping.
    /// </summary>
    public Tween PlayMatchedEffect(System.Action onComplete = null)
    {
        matchedTween?.Kill();
        activeTween?.Kill();
        danceTween?.Kill();
        landingTween?.Kill();

        var seq = DOTween.Sequence();
        seq.Join(transform.DOScale(Vector3.one * Mathf.Max(1f, matchedPopScale), matchedEffectDuration).SetEase(Ease.OutBack));
        if (spriteRenderer != null)
            seq.Join(spriteRenderer.DOFade(0f, matchedEffectDuration).SetEase(Ease.InQuad));
        seq.OnComplete(() => onComplete?.Invoke());

        matchedTween = seq;
        return seq;
    }

    /// <summary>
    /// Restores scale and sprite color to resting values - call before returning this symbol to
    /// SymbolSpawner's pool (see MatchResolver.ClearCell) so a reused instance doesn't come back
    /// out still scaled up and faded from its previous life's PlayMatchedEffect. Symbol.Initialize
    /// doesn't touch either of these itself, so without this a pooled symbol could visibly flash
    /// in invisible-then-oversized until something else happened to touch its transform/color.
    /// Same precedent as SetBurning(0) already being used to make sure a despawned instance
    /// doesn't come back out still on fire - this is the equivalent cleanup for the newer
    /// matched-pop-and-fade effect, which didn't exist when that pattern was established.
    /// </summary>
    public void ResetVisualState()
    {
        transform.localScale = Vector3.one;
        if (spriteRenderer != null) spriteRenderer.color = baseSpriteColor;
    }

    [Header("Hint Pulse")]
    [Tooltip("Gentle continuous scale pulse played by PlayHintPulse (see HintController) while " +
             "this tile is being suggested as one half of a valid move. Loops until StopHintPulse " +
             "is called - HintController does that on any accepted player move, and MoveTo/FallTo " +
             "also kill it defensively the instant this tile actually starts moving for real.")]
    [Min(0.05f)] [SerializeField] private float hintPulseCycleDuration = 0.6f;
    [Range(0f, 0.3f)] [SerializeField] private float hintPulseScaleAmount = 0.12f;

    private Tween hintTween;

    /// <summary>Plays a FINITE burst (loops cycles, then stops and settles back to scale 1) -
    /// not an infinite loop. This matters for HintController's periodic re-hint: PossibleMoveFinder
    /// deterministically finds the same pair again if nothing on the board changed, so restarting
    /// an infinite loop on the same tiles would be visually identical to doing nothing at all.
    /// A finite burst that stops and goes quiet between calls is what actually makes a repeated
    /// hint READ as a repeated event - burst, pause, burst, pause - the classic match-3 pattern.
    /// </summary>
    public Tween PlayHintPulse(int loops = 3)
    {
        hintTween?.Kill();
        transform.localScale = Vector3.one;

        var seq = DOTween.Sequence();
        seq.Append(transform.DOScale(Vector3.one * (1f + Mathf.Clamp(hintPulseScaleAmount, 0f, 0.3f)), hintPulseCycleDuration * 0.5f).SetEase(Ease.InOutSine));
        seq.Append(transform.DOScale(Vector3.one, hintPulseCycleDuration * 0.5f).SetEase(Ease.InOutSine));
        seq.SetLoops(Mathf.Max(1, loops), LoopType.Restart);
        seq.OnComplete(() =>
        {
            transform.localScale = Vector3.one;
            hintTween = null;
        });

        hintTween = seq;
        return seq;
    }

    public void StopHintPulse()
    {
        hintTween?.Kill();
        hintTween = null;
        if (transform != null)
            transform.localScale = Vector3.one;
    }

    private void OnDestroy()
    {
        activeTween?.Kill();
        convertTween?.Kill();
        danceTween?.Kill();
        landingTween?.Kill();
        matchedTween?.Kill();
        hintTween?.Kill();
    }

    [Header("Dance (optional)")]
    [Tooltip("Which transform PlayDanceLoop actually animates. Leave unassigned to dance the " +
             "whole symbol (this GameObject's own transform) - the original behavior. Assign a " +
             "child transform (e.g. a separated 'Icon' child holding just the front sprite) to " +
             "dance ONLY that layer, leaving sibling children like a background sprite completely " +
             "still. Every dance move in this file reads through DanceTransform below, not " +
             "`transform` directly, specifically so this one field controls all of them.")]
    [SerializeField] private Transform danceTarget;
    private Transform DanceTransform => danceTarget != null ? danceTarget : transform;

    /// <summary>
    /// Starts (or restarts) an idle "dance" that keeps swapping between distinct move mechanisms
    /// rather than picking one style and looping it for the whole event - see DanceLoopRoutine/
    /// BuildDanceMove for the full move set: a punch pulse, a wiggle, squash-and-stretch, a fast
    /// shimmy, a smooth breathing pulse, a scale+rotation combo, a pendulum swing, a breakdance-
    /// style spin flourish, a sharp tango accent-and-hold, and a barely-there low-amplitude "just
    /// vibing" sway. Each move plays a handful of times (so it reads as a recognizable little
    /// phrase, not a single twitch) before a fresh move is rolled - magnitude, cycle length,
    /// vibrato, and repeat count are all rejittered every time, so even two symbols that land on
    /// the same move back to back don't look identical, and no two symbols stay in sync with
    /// each other.
    ///
    /// Deliberately stays off DanceTransform.position for every move (see BuildDanceMove) - a
    /// position tween would fight MoveTo's own position tween since they'd both write
    /// DanceTransform.position every frame, whereas scale/rotation are properties MoveTo never
    /// touches, which is the whole reason this uses its own tween slot (danceTween) and coroutine
    /// (danceRoutine) in the first place - so it can run alongside a gravity/swap MoveTo or a
    /// Madness convert highlight without either killing the other. See Board.StartDiscoDance/
    /// StopDiscoDance and SymbolSpawner's dance-for-new-spawns hook (DiscoDanceDiscoManager's
    /// event, including symbols that spawn mid-event).
    /// </summary>
    public void PlayDanceLoop(float cycleDuration, float punchScaleAmount, float punchRotationDegrees)
    {
        StopDance(); // clears any previous loop/tween and resets scale/rotation first
        danceRoutine = StartCoroutine(DanceLoopRoutine(cycleDuration, punchScaleAmount, punchRotationDegrees));
    }

    /// <summary>Stops PlayDanceLoop (kills the current move tween and the rerolling coroutine)
    /// and snaps scale/rotation back to their resting values - a tween or Yoyo loop killed
    /// mid-cycle can otherwise leave the transform slightly off from where it started.</summary>
    public void StopDance()
    {
        if (danceRoutine != null)
        {
            StopCoroutine(danceRoutine);
            danceRoutine = null;
        }
        danceTween?.Kill();
        danceTween = null;
        DanceTransform.localScale = Vector3.one;
        DanceTransform.localRotation = Quaternion.identity;
    }

    private IEnumerator DanceLoopRoutine(float cycleDuration, float punchScaleAmount, float punchRotationDegrees)
    {
        while (true)
        {
            float jitteredCycle = Mathf.Max(0.05f, cycleDuration * Random.Range(0.8f, 1.2f));
            float jitteredScale = punchScaleAmount * Random.Range(0.7f, 1.3f);
            float jitteredRotation = punchRotationDegrees * Random.Range(0.7f, 1.3f) * (Random.value < 0.5f ? -1f : 1f);
            int vibrato = Random.Range(1, 4);
            float elasticity = Random.Range(0.3f, 0.7f);
            int repeats = Random.Range(2, 5); // how many times THIS move plays before rerolling to a different one

            danceTween = BuildDanceMove(Random.Range(0, DanceMoveCount), jitteredCycle, jitteredScale, jitteredRotation, vibrato, elasticity, repeats);
            yield return danceTween.WaitForCompletion();

            // Force back to a clean baseline between moves regardless of which mechanism just ran
            // (a Yoyo-looped DOScale move ending state depends on its loop count parity) so the
            // next move always starts from the same resting scale/rotation rather than drifting.
            DanceTransform.localScale = Vector3.one;
            DanceTransform.localRotation = Quaternion.identity;
        }
    }

    private const int DanceMoveCount = 10;

    /// <summary>Builds one dance move as its own (non-infinitely-looping) tween/sequence that
    /// plays `repeats` times (or, for the swing/vibe moves below, an equivalent even Yoyo loop
    /// count) and then completes, handing control back to DanceLoopRoutine to roll a fresh move.
    /// Each case is a genuinely different mechanism, not just a magnitude/timing variation of the
    /// same one - see PlayDanceLoop's doc for the reasoning on staying off DanceTransform.position.</summary>
    private Tween BuildDanceMove(int moveIndex, float cycleDuration, float punchScale, float punchRotation,
        int vibrato, float elasticity, int repeats)
    {
        switch (moveIndex)
        {
            case 0: // Punch pulse - a quick uniform scale snap, the "classic" punch feel
                return DanceTransform.DOPunchScale(Vector3.one * Mathf.Max(0.01f, punchScale), cycleDuration, vibrato, elasticity)
                    .SetLoops(repeats, LoopType.Restart);

            case 1: // Wiggle - a rotation-only punch
                return DanceTransform.DOPunchRotation(new Vector3(0f, 0f, punchRotation), cycleDuration, vibrato, elasticity)
                    .SetLoops(repeats, LoopType.Restart);

            case 2: // Squash & stretch - a jelly-bounce via non-uniform scale (wide+short, then
                    // tall+thin), smoothly eased rather than a snap - a genuinely different feel
                    // from the punch-based moves above.
            {
                float amt = Mathf.Clamp(Mathf.Abs(punchScale), 0.02f, 0.6f);
                var stretched = new Vector3(1f - amt, 1f + amt, 1f);
                return DanceTransform.DOScale(stretched, cycleDuration * 0.5f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(Mathf.Max(2, repeats * 2), LoopType.Yoyo);
            }

            case 3: // Shimmy - a fast, high-vibrato, low-elasticity rotation shake, distinctly
                    // twitchier than Wiggle above rather than just a magnitude difference.
                return DanceTransform.DOPunchRotation(new Vector3(0f, 0f, punchRotation * 0.6f), cycleDuration * 0.5f,
                        vibrato: Random.Range(6, 10), elasticity: 0.15f)
                    .SetLoops(repeats, LoopType.Restart);

            case 4: // Breathing pulse - a slow, smooth scale in/out with no snap-back at all,
                    // the calmest move in the set.
                return DanceTransform.DOScale(Vector3.one * (1f + Mathf.Max(0.02f, punchScale) * 0.6f), cycleDuration * 0.6f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(Mathf.Max(2, repeats * 2), LoopType.Yoyo);

            case 5: // Combo - scale and rotation punches together, both at full jittered magnitude
            {
                var seq = DOTween.Sequence();
                seq.Join(DanceTransform.DOPunchScale(Vector3.one * Mathf.Max(0.01f, punchScale), cycleDuration, vibrato, elasticity));
                seq.Join(DanceTransform.DOPunchRotation(new Vector3(0f, 0f, punchRotation), cycleDuration, vibrato, elasticity));
                seq.SetLoops(repeats, LoopType.Restart);
                return seq;
            }

            case 6: // Swing - a smooth pendulum sway between two angles, wider and slower than
                    // Wiggle's snap-and-settle punch - reads as a continuous back-and-forth rather
                    // than a twitch. Starts from one extreme so the Yoyo genuinely swings both ways.
            {
                float swingAngle = Mathf.Max(10f, Mathf.Abs(punchRotation) * 1.5f);
                DanceTransform.localRotation = Quaternion.Euler(0f, 0f, -swingAngle);
                return DanceTransform.DORotate(new Vector3(0f, 0f, swingAngle), cycleDuration * 0.7f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(Mathf.Max(2, repeats * 2), LoopType.Yoyo);
            }

            case 7: // Breakdance flourish - a full spin (either direction) with a punchy scale pop
                    // riding alongside it, energetic and a little chaotic. FastBeyond360 lets
                    // DORotate actually complete a full 360 rather than snapping back the "short way".
            {
                float spinDir = Random.value < 0.5f ? 1f : -1f;
                var seq = DOTween.Sequence();
                seq.Join(DanceTransform.DORotate(new Vector3(0f, 0f, 360f * spinDir), cycleDuration * 0.8f, RotateMode.FastBeyond360)
                    .SetEase(Ease.InOutQuad));
                seq.Join(DanceTransform.DOPunchScale(Vector3.one * Mathf.Max(0.05f, punchScale) * 1.4f, cycleDuration * 0.8f,
                    Mathf.Max(vibrato, 2), 0.3f));
                seq.SetLoops(repeats, LoopType.Restart);
                return seq;
            }

            case 8: // Tango - a sharp, dramatic accent: snap to an angle with a matching stretch,
                    // hold the pose a beat, then ease back - distinctly more "posed" than any of
                    // the continuously-looping moves above.
            {
                float tangoAngle = Mathf.Max(10f, Mathf.Abs(punchRotation) * 2f) * (Random.value < 0.5f ? 1f : -1f);
                var seq = DOTween.Sequence();
                seq.Append(DanceTransform.DORotate(new Vector3(0f, 0f, tangoAngle), cycleDuration * 0.25f).SetEase(Ease.OutQuad));
                seq.Join(DanceTransform.DOScale(Vector3.one * (1f + Mathf.Max(0.02f, punchScale) * 0.5f), cycleDuration * 0.25f).SetEase(Ease.OutQuad));
                seq.AppendInterval(cycleDuration * 0.25f); // the dramatic hold
                seq.Append(DanceTransform.DORotate(Vector3.zero, cycleDuration * 0.25f).SetEase(Ease.InOutSine));
                seq.Join(DanceTransform.DOScale(Vector3.one, cycleDuration * 0.25f).SetEase(Ease.InOutSine));
                seq.SetLoops(repeats, LoopType.Restart);
                return seq;
            }

            default: // Just vibing - minimal, low-amplitude, unhurried sway - barely there
                    // compared to everything else in the set, on purpose.
            {
                float vibeAngle = Mathf.Clamp(Mathf.Abs(punchRotation) * 0.3f, 2f, 6f);
                DanceTransform.localRotation = Quaternion.Euler(0f, 0f, -vibeAngle);
                return DanceTransform.DORotate(new Vector3(0f, 0f, vibeAngle), cycleDuration * 1.5f)
                    .SetEase(Ease.InOutSine)
                    .SetLoops(Mathf.Max(2, repeats), LoopType.Yoyo);
            }
        }
    }

    private Vector3 pressWorldPosition;
    private bool isPressed;
    private bool didSwipeThisPress;

    private void OnMouseDown()
    {
        isPressed = true;
        didSwipeThisPress = false;
        pressWorldPosition = GetPointerWorldPosition();
    }

    private void OnMouseDrag()
    {
        if (!isPressed || didSwipeThisPress) return;

        var delta = GetPointerWorldPosition() - pressWorldPosition;
        if (delta.magnitude < swipeThreshold) return;

        // Snap the drag to the dominant cardinal direction - swipes are always 4-directional,
        // same as the grid itself, regardless of the exact drag angle.
        var direction = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
            ? new Vector2Int(delta.x > 0 ? 1 : -1, 0)
            : new Vector2Int(0, delta.y > 0 ? 1 : -1);

        didSwipeThisPress = true;
        Board.Instance?.SwipeSymbol(this, direction);
    }

    private void OnMouseUp()
    {
        isPressed = false;

        // A swipe already fired during this press (see OnMouseDrag) - don't also treat the
        // release as a click, or this move would attempt to run twice.
        if (didSwipeThisPress) return;

        Board.Instance?.SelectSymbol(this);
    }

    /// <summary>
    /// Pointer position in world space, at this symbol's own depth (so it works regardless of
    /// where exactly the board sits on the z-axis relative to the camera). Reads through the new
    /// Input System's Pointer API when Active Input Handling includes it (covers mouse, pen, and
    /// touch through one call), falling back to legacy Input.mousePosition otherwise - reading
    /// UnityEngine.Input directly throws under "Input System Package (New)" only mode.
    /// </summary>
    private Vector3 GetPointerWorldPosition()
    {
        var cam = Camera.main;
        if (cam == null) return transform.position;

        Vector3 screenPos;
#if ENABLE_INPUT_SYSTEM
        screenPos = Pointer.current != null ? (Vector3)Pointer.current.position.ReadValue() : transform.position;
#else
        screenPos = Input.mousePosition;
#endif
        screenPos.z = cam.WorldToScreenPoint(transform.position).z;
        return cam.ScreenToWorldPoint(screenPos);
    }
}

