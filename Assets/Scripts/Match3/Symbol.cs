using DG.Tweening;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

    private Tween activeTween;

    public int LockLayers { get; private set; }
    public LockBehavior LockBehaviorMode { get; private set; } = LockBehavior.None;
    public int MovesPerLayer { get; private set; }
    public int MovesUntilNextAutoUnlock { get; private set; }
    public bool IsLocked => LockLayers > 0;

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
        activeTween?.Kill();
        activeTween = transform.DOMove(worldPosition, duration).SetEase(ease);
        return activeTween;
    }

    private void OnDestroy() => activeTween?.Kill();

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

