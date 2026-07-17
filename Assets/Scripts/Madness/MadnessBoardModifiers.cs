using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Temporary, move-duration-based rule modifiers that Madness Symbol effects apply to the board
/// (e.g. MadnessIgniteColorEffect "lighting a color on fire" for N moves). Distinct from
/// PlayerRunStats: PlayerRunStats holds PERMANENT-for-the-run modifiers the player chose via
/// powerups; this holds TEMPORARY modifiers a Madness Symbol imposed, which tick down and expire
/// on their own. Board.ClearCell folds the color-scoped bonuses in here on top of PlayerRunStats',
/// and Board.TickMadnessSurvival() calls TickMove() once per accepted player move to count them
/// down. Currently only color-scoped bonuses exist, but new non-color-scoped modifiers (e.g. a
/// board-wide effect) can be added the same way - a new field/list here plus a new MadnessEffect
/// subclass to set it, without touching the color-scoped API.
/// </summary>
public class MadnessBoardModifiers : MonoBehaviour
{
    private class ActiveColorModifier
    {
        public SymbolType color;
        public int movesRemaining;
        public float scoreMultiplierBonus;
        public int damagePerMatch;
    }

    private readonly List<ActiveColorModifier> activeColorModifiers = new List<ActiveColorModifier>();

    /// <summary>
    /// Applies a color modifier. If this color already has an active modifier, durations don't
    /// stack (the longer one wins) but the score/damage bonuses add on top of the existing ones.
    /// </summary>
    public void ApplyColorModifier(SymbolType color, int durationMoves, float scoreMultiplierBonus, int damagePerMatch)
    {
        if (durationMoves <= 0) return;

        foreach (var m in activeColorModifiers)
        {
            if (m.color != color) continue;
            m.movesRemaining = Mathf.Max(m.movesRemaining, durationMoves);
            m.scoreMultiplierBonus += scoreMultiplierBonus;
            m.damagePerMatch += damagePerMatch;
            EventBus.Publish(new ColorModifierAppliedEvent(color, m.movesRemaining));
            return;
        }

        activeColorModifiers.Add(new ActiveColorModifier
        {
            color = color,
            movesRemaining = durationMoves,
            scoreMultiplierBonus = scoreMultiplierBonus,
            damagePerMatch = damagePerMatch
        });
        EventBus.Publish(new ColorModifierAppliedEvent(color, durationMoves));
    }

    public float GetColorScoreMultiplierBonus(SymbolType color)
    {
        float total = 0f;
        foreach (var m in activeColorModifiers)
            if (m.color == color) total += m.scoreMultiplierBonus;
        return total;
    }

    public int GetColorDamagePerMatch(SymbolType color)
    {
        int total = 0;
        foreach (var m in activeColorModifiers)
            if (m.color == color) total += m.damagePerMatch;
        return total;
    }

    /// <summary>Call once per accepted player move. Ticks every active modifier down and expires any that hit 0.</summary>
    public void TickMove()
    {
        for (int i = activeColorModifiers.Count - 1; i >= 0; i--)
        {
            activeColorModifiers[i].movesRemaining--;
            if (activeColorModifiers[i].movesRemaining > 0) continue;

            EventBus.Publish(new ColorModifierExpiredEvent(activeColorModifiers[i].color));
            activeColorModifiers.RemoveAt(i);
        }
    }

    /// <summary>Clears every active modifier. Stage-scoped (not run-scoped like PlayerRunStats) - call on stage reset.</summary>
    public void Clear() => activeColorModifiers.Clear();
}
