using UnityEngine;

// All events are readonly structs: cheap, no GC allocation pressure, and immutable once published.

public readonly struct SymbolMatchedEvent
{
    public readonly SymbolType Type;
    public readonly Vector2Int Position;

    public SymbolMatchedEvent(SymbolType type, Vector2Int position)
    {
        Type = type;
        Position = position;
    }
}

public readonly struct ChainMatchedEvent
{
    public readonly int SymbolsCleared;
    public readonly int ChainCount;      // 1 = initial match, 2 = first cascade, etc. Use for combo multipliers/UI.
    public readonly Vector2Int[] Positions;

    public ChainMatchedEvent(int symbolsCleared, int chainCount, Vector2Int[] positions)
    {
        SymbolsCleared = symbolsCleared;
        ChainCount = chainCount;
        Positions = positions;
    }
}

public readonly struct SpecialSymbolCreatedEvent
{
    public readonly SpecialType Special;
    public readonly Vector2Int Position;

    public SpecialSymbolCreatedEvent(SpecialType special, Vector2Int position)
    {
        Special = special;
        Position = position;
    }
}

/// <summary>Fired when a special symbol is triggered/consumed as part of a match.</summary>
public readonly struct SpecialSymbolMatchedEvent
{
    public readonly SpecialType Special;
    public readonly Vector2Int Position;
    public readonly Vector2Int[] AffectedCells;

    public SpecialSymbolMatchedEvent(SpecialType special, Vector2Int position, Vector2Int[] affectedCells)
    {
        Special = special;
        Position = position;
        AffectedCells = affectedCells;
    }
}

public readonly struct ScoreChangedEvent
{
    public readonly int NewScore;
    public readonly int Delta;

    public ScoreChangedEvent(int newScore, int delta)
    {
        NewScore = newScore;
        Delta = delta;
    }
}

public readonly struct GameOverEvent
{
    public readonly int FinalScore;
    public GameOverEvent(int finalScore) => FinalScore = finalScore;
}
