using UnityEngine;

/// <summary>
/// Base class for one Madness Symbol effect. Concrete effects (Damage, Score Growth, Ignite
/// Color, Fill Meter, etc.) are separate ScriptableObject assets so a designer composes a
/// MadnessSymbolDefinition from a mix of them entirely in the Inspector - adding a new kind of
/// effect later is a new MadnessEffect subclass, with no changes needed to Board or
/// MadnessSymbolDefinition.
/// </summary>
public abstract class MadnessEffect : ScriptableObject
{
    public abstract void Execute(MadnessContext ctx);
}

/// <summary>Everything a MadnessEffect might need, handed in by Board when a trigger fires.</summary>
public readonly struct MadnessContext
{
    public readonly Board Board;
    public readonly PlayerHealth PlayerHealth;
    public readonly PlayerRunStats PlayerRunStats;
    public readonly MadnessBoardModifiers BoardModifiers;
    public readonly MadnessMeter Meter;
    public readonly Symbol Symbol;
    public readonly Vector2Int Position;
    public readonly SymbolType Color;
    /// <summary>How many prior moves this symbol has survived unmatched. 0 on the move it spawns or is immediately cleared.</summary>
    public readonly int MovesSurvived;
    public readonly int ChainCount;

    public MadnessContext(Board board, PlayerHealth playerHealth, PlayerRunStats playerRunStats,
        MadnessBoardModifiers boardModifiers, MadnessMeter meter, Symbol symbol, Vector2Int position,
        SymbolType color, int movesSurvived, int chainCount)
    {
        Board = board;
        PlayerHealth = playerHealth;
        PlayerRunStats = playerRunStats;
        BoardModifiers = boardModifiers;
        Meter = meter;
        Symbol = symbol;
        Position = position;
        Color = color;
        MovesSurvived = movesSurvived;
        ChainCount = chainCount;
    }
}
