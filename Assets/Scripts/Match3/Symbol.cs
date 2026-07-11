using DG.Tweening;
using UnityEngine;

/// <summary>
/// Attach to each symbol prefab (one prefab per SymbolType, or one shared prefab
/// that swaps its sprite via SymbolVisualConfig - either works with Board.cs as written).
/// Requires a Collider2D for click/tap input via OnMouseDown.
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

    private Tween activeTween;

    public void Initialize(SymbolType type, SpecialType special, Vector2Int gridPosition)
    {
        Type = type;
        Special = special;
        GridPosition = gridPosition;
        UpdateVisual();
    }

    public void SetSpecial(SpecialType special)
    {
        Special = special;
        UpdateVisual();
    }

    private void UpdateVisual()
    {
        if (spriteRenderer == null || visualConfig == null) return;
        var sprite = visualConfig.GetSprite(Type, Special);
        if (sprite != null) spriteRenderer.sprite = sprite;
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

    private void OnMouseDown()
    {
        Board.Instance?.SelectSymbol(this);
    }
}

