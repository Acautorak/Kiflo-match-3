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

public readonly struct HealthChangedEvent
{
    public readonly int CurrentHealth;
    public readonly int MaxHealth;

    public HealthChangedEvent(int currentHealth, int maxHealth)
    {
        CurrentHealth = currentHealth;
        MaxHealth = maxHealth;
    }
}

public readonly struct GameOverEvent
{
    public readonly int FinalScore;
    public GameOverEvent(int finalScore) => FinalScore = finalScore;
}

public readonly struct PlayerMoveEvent
{
    public readonly int MoveCount;
    public PlayerMoveEvent(int moveCount) => MoveCount = moveCount;
}

public readonly struct StageCompletedEvent
{
    public readonly int StageIndex;
    public readonly int Score;
    public StageCompletedEvent(int stageIndex, int score)
    {
        StageIndex = stageIndex;
        Score = score;
    }
}

public readonly struct StageStartedEvent
{
    public readonly int StageIndex;
    public readonly StageDefinition Stage;
    public StageStartedEvent(int stageIndex, StageDefinition stage)
    {
        StageIndex = stageIndex;
        Stage = stage;
    }
}

/// <summary>
/// Fired every time a lock loses a layer - either from being caught in a match/special
/// clear (TriggeredByMatch=true) or from the automatic per-move melt on Temporary locks
/// (TriggeredByMatch=false). FullyUnlocked is true the instant the last layer is removed.
/// </summary>
public readonly struct LockLayerRemovedEvent
{
    public readonly Vector2Int Position;
    public readonly int LayersRemaining;
    public readonly bool TriggeredByMatch;
    public readonly bool FullyUnlocked;

    public LockLayerRemovedEvent(Vector2Int position, int layersRemaining, bool triggeredByMatch, bool fullyUnlocked)
    {
        Position = position;
        LayersRemaining = layersRemaining;
        TriggeredByMatch = triggeredByMatch;
        FullyUnlocked = fullyUnlocked;
    }
}
