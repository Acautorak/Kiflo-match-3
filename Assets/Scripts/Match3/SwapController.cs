using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Owns symbol selection input (click-then-click and swipe) and the swap animation itself.
/// Extracted from Board's "Input / Swapping" region.
///
/// Deliberately doesn't own what happens AFTER a swap is requested (match detection, scoring,
/// cascades, grace-period consumption, etc.) - that orchestration still lives in Board.TrySwap,
/// since it's tightly coupled to systems (ResolveMatches, StageManager) that haven't been
/// extracted yet. This class's job ends at "here are the two symbols to swap" - it hands off via
/// the onSwapRequested delegate, which Board wires to start its own TrySwap coroutine.
/// </summary>
public class SwapController
{
    private readonly GridModel grid;
    private readonly System.Func<int, int, Vector3> gridToWorld;
    private readonly float swapDuration;
    private readonly System.Func<bool> isGameBusy;
    private readonly System.Func<bool> allowsPlayerInput;
    private readonly System.Func<bool> allowSwappingLockedTiles;
    private readonly System.Action<Symbol, Symbol> onSwapRequested;

    private Symbol selected;

    public SwapController(GridModel grid, System.Func<int, int, Vector3> gridToWorld, float swapDuration,
        System.Func<bool> isGameBusy, System.Func<bool> allowsPlayerInput,
        System.Func<bool> allowSwappingLockedTiles, System.Action<Symbol, Symbol> onSwapRequested)
    {
        this.grid = grid;
        this.gridToWorld = gridToWorld;
        this.swapDuration = swapDuration;
        this.isGameBusy = isGameBusy;
        this.allowsPlayerInput = allowsPlayerInput;
        this.allowSwappingLockedTiles = allowSwappingLockedTiles;
        this.onSwapRequested = onSwapRequested;
    }

    public void SelectSymbol(Symbol symbol)
    {
        if (isGameBusy()) return;
        if (!allowsPlayerInput()) return;

        if (symbol.IsLocked && !allowSwappingLockedTiles())
        {
            Debug.Log($"[SwapController] {symbol.name} at {symbol.GridPosition} is locked " +
                       $"({symbol.LockBehaviorMode}, {symbol.LockLayers} layer(s) left) - selection blocked.");
            selected = null;
            return;
        }

        if (selected == null) { selected = symbol; return; }
        if (selected == symbol) { selected = null; return; }

        if (IsAdjacent(selected.GridPosition, symbol.GridPosition))
            onSwapRequested(selected, symbol);

        selected = null;
    }

    /// <summary>
    /// Alternative to the click-then-click flow above: a single press-and-drag gesture on
    /// `symbol` toward `direction` immediately attempts a swap with whichever tile sits in that
    /// direction, without needing a second tap. Both input modes work interchangeably move to
    /// move.
    /// </summary>
    public void SwipeSymbol(Symbol symbol, Vector2Int direction)
    {
        // A swipe is a complete gesture on its own - don't let a pending click-selection
        // (from an earlier single tap that never got a second tap) interfere with it.
        selected = null;

        if (isGameBusy()) return;
        if (!allowsPlayerInput()) return;

        if (symbol.IsLocked && !allowSwappingLockedTiles())
        {
            Debug.Log($"[SwapController] {symbol.name} at {symbol.GridPosition} is locked " +
                       $"({symbol.LockBehaviorMode}, {symbol.LockLayers} layer(s) left) - swipe blocked.");
            return;
        }

        var targetPos = symbol.GridPosition + direction;
        if (!grid.InBounds(targetPos)) return;

        var target = grid[targetPos].Occupant;
        if (target == null) return;

        onSwapRequested(symbol, target);
    }

    /// <summary>Clears any pending click-selection - call when the board resets/reloads so a
    /// stale selection from before a stage change can't carry over.</summary>
    public void ClearSelection() => selected = null;

    private bool IsAdjacent(Vector2Int a, Vector2Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

    /// <summary>
    /// Swaps two symbols' grid positions and animates them into place. Board calls this twice
    /// from inside TrySwap when a swap turns out invalid - once to swap, once more with the same
    /// pair to revert, since running it again just swaps them back.
    /// </summary>
    public IEnumerator SwapRoutine(Symbol a, Symbol b)
    {
        var posA = a.GridPosition;
        var posB = b.GridPosition;

        grid[posA.x, posA.y].Occupant = b;
        grid[posB.x, posB.y].Occupant = a;
        a.GridPosition = posB;
        b.GridPosition = posA;

        var sequence = DOTween.Sequence();
        sequence.Join(a.MoveTo(gridToWorld(posB.x, posB.y), swapDuration));
        sequence.Join(b.MoveTo(gridToWorld(posA.x, posA.y), swapDuration));

        yield return sequence.WaitForCompletion();
    }
}
