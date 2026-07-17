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

/// <summary>
/// Fired whenever a PlayerRunStats modifier changes (a powerup was picked, or the stats reset
/// for a new run). UI can subscribe to this to show a live stats panel without polling.
/// </summary>
public readonly struct PlayerStatsChangedEvent
{
    public readonly PlayerRunStats Stats;
    public PlayerStatsChangedEvent(PlayerRunStats stats) => Stats = stats;
}

/// <summary>
/// Fired once PowerupManager has rolled which powerups to offer after a stage clears. UI shows
/// Choices and calls PowerupManager.SelectPowerup() with the pick; the run doesn't advance to
/// the next stage until that happens (see PowerupManager for the no-pool-configured fallback).
/// </summary>
public readonly struct PowerupChoicesOfferedEvent
{
    public readonly PowerupDefinition[] Choices;
    public PowerupChoicesOfferedEvent(PowerupDefinition[] choices) => Choices = choices;
}

/// <summary>Fired right after a powerup's effect has been applied, before the next stage starts.</summary>
public readonly struct PowerupSelectedEvent
{
    public readonly PowerupDefinition Powerup;
    public PowerupSelectedEvent(PowerupDefinition powerup) => Powerup = powerup;
}

/// <summary>Fired when a Madness Symbol is cleared (matched or caught in a special), after its onClearedEffects have run.</summary>
public readonly struct MadnessSymbolClearedEvent
{
    public readonly MadnessSymbolDefinition Definition;
    public readonly Vector2Int Position;
    public readonly int MovesSurvived;
    public MadnessSymbolClearedEvent(MadnessSymbolDefinition definition, Vector2Int position, int movesSurvived)
    {
        Definition = definition;
        Position = position;
        MovesSurvived = movesSurvived;
    }
}

/// <summary>Fired whenever MadnessMeter's value changes.</summary>
public readonly struct MadnessMeterChangedEvent
{
    public readonly float Current;
    public readonly float Max;
    public MadnessMeterChangedEvent(float current, float max)
    {
        Current = current;
        Max = max;
    }
}

/// <summary>Fired when a Madness effect applies (or extends) a temporary per-color board modifier - e.g. MadnessIgniteColorEffect.</summary>
public readonly struct ColorModifierAppliedEvent
{
    public readonly SymbolType Color;
    public readonly int DurationMoves;
    public ColorModifierAppliedEvent(SymbolType color, int durationMoves)
    {
        Color = color;
        DurationMoves = durationMoves;
    }
}

/// <summary>Fired when a temporary per-color board modifier's duration runs out.</summary>
public readonly struct ColorModifierExpiredEvent
{
    public readonly SymbolType Color;
    public ColorModifierExpiredEvent(SymbolType color) => Color = color;
}

/// <summary>
/// Fired when existing board symbols are repainted to a new color in place (see Board.
/// ConvertRandomSymbols / MadnessColorConvertEffect) - not a match or clear, just a color change.
/// VFX can hook this for a "corruption spreading" flourish without touching gameplay code.
/// </summary>
public readonly struct SymbolsConvertedEvent
{
    public readonly SymbolType NewColor;
    public readonly Vector2Int[] Positions;
    public SymbolsConvertedEvent(SymbolType newColor, Vector2Int[] positions)
    {
        NewColor = newColor;
        Positions = positions;
    }
}

/// <summary>
/// Fired when existing board symbols are each repainted to their own independently random color
/// (see Board.RandomizeSymbolColors / MadnessRandomizeColorsEffect) - unlike
/// SymbolsConvertedEvent, there's no single shared color here, so NewColors is parallel to
/// Positions (NewColors[i] is the new color of Positions[i]).
/// </summary>
public readonly struct SymbolsRandomizedEvent
{
    public readonly Vector2Int[] Positions;
    public readonly SymbolType[] NewColors;
    public SymbolsRandomizedEvent(Vector2Int[] positions, SymbolType[] newColors)
    {
        Positions = positions;
        NewColors = newColors;
    }
}
