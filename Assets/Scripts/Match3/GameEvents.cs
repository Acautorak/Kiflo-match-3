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

/// <summary>Fired when a special symbol is triggered/consumed as part of a match - either a real
/// pre-existing special caught in a match, or a spontaneous "Wonky" proc (MatchResolver.
/// TryRandomSpecialOnGravity/TryRandomSpecialOnGraceMove rolling a random tile into a special
/// effect outside of any real match, gated by RandomSpecialTriggerChance/
/// MaxConsecutiveRandomTriggers/graceRandomSpecialChance) - see IsWonkyProc.</summary>
public readonly struct SpecialSymbolMatchedEvent
{
    public readonly SpecialType Special;
    public readonly Vector2Int Position;
    public readonly Vector2Int[] AffectedCells;
    /// <summary>True if this came from a spontaneous random-gravity/grace-period proc rather than
    /// a real special symbol getting caught in an actual match - lets SpecialSymbolEventRelay (or
    /// any other listener) show a distinct "WONKY!"-style callout instead of the normal special-
    /// match VFX/SFX. Defaults false so every pre-existing publish site (a genuine special match)
    /// is unaffected without needing to be touched.</summary>
    public readonly bool IsWonkyProc;

    public SpecialSymbolMatchedEvent(SpecialType special, Vector2Int position, Vector2Int[] affectedCells, bool isWonkyProc = false)
    {
        Special = special;
        Position = position;
        AffectedCells = affectedCells;
        IsWonkyProc = isWonkyProc;
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

/// <summary>Fired on every accepted player move (Board.RegisterPlayerMove) - MoveCount is the
/// running total for the whole board's lifetime since the last stage start.</summary>
public readonly struct PlayerMoveEvent
{
    public readonly int MoveCount;
    /// <summary>True if this move was consumed from an armed Grace Move (see
    /// GraceMoveController) - StageManager deliberately does NOT count a Grace Move toward a
    /// MoveCount-type stage goal (it keeps its own separate counter for that specifically
    /// because of this flag), even though MoveCount above still increments normally for the
    /// general move tally/HUD/save data. Defaults false so every other publish site is
    /// unaffected.</summary>
    public readonly bool WasGraceMove;

    public PlayerMoveEvent(int moveCount, bool wasGraceMove = false)
    {
        MoveCount = moveCount;
        WasGraceMove = wasGraceMove;
    }
}

/// <summary>
/// Fired the instant a Grace Move Chance roll succeeds (see GraceMoveController.RollForNextMove) -
/// the PLAYER'S NEXT move will be a Grace Move (no damage, doesn't count toward a MoveCount
/// goal). Not the same system as StageManager's stage-clear grace period - this is a separate,
/// powerup-driven per-move mechanic that just happens to share the word "grace". UI hooks this to
/// show a "Grace Move" callout and turn on a sustained highlight.
/// </summary>
public readonly struct GraceMoveArmedEvent { }

/// <summary>Fired the instant an armed Grace Move is actually consumed by the player's next real
/// (validly-registered) move - UI hooks this to fade the highlight back out.</summary>
public readonly struct GraceMoveConsumedEvent { }

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

public readonly struct GameStateChangedEvent
{
    public readonly GameManager.GameplayState Previous;
    public readonly GameManager.GameplayState Current;
    public GameStateChangedEvent(GameManager.GameplayState previous, GameManager.GameplayState current)
    {
        Previous = previous;
        Current = current;
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

/// <summary>
/// Fired when a tile is set alight (see BurningSystem.TryIgniteNearby) - UI/VFX can hook this to
/// play an ignite flourish. Not fired again while it's already burning (re-ignition isn't
/// possible - BurningSystem skips already-burning tiles when picking an ignite target).
/// </summary>
public readonly struct TileIgnitedEvent
{
    public readonly Vector2Int Position;
    public readonly int MovesUntilBurnOut;
    public TileIgnitedEvent(Vector2Int position, int movesUntilBurnOut)
    {
        Position = position;
        MovesUntilBurnOut = movesUntilBurnOut;
    }
}

/// <summary>
/// Fired when a Madness Symbol is created on the board via refill spawn (see
/// GravityController.Collapse). Distinct from MadnessSymbolClearedEvent - this is the "it just
/// appeared" moment, useful for tutorials/VFX that want to react the first time a particular
/// MadnessSymbolDefinition shows up in a run.
/// </summary>
public readonly struct MadnessSymbolSpawnedEvent
{
    public readonly MadnessSymbolDefinition Definition;
    public readonly Vector2Int Position;
 
    public MadnessSymbolSpawnedEvent(MadnessSymbolDefinition definition, Vector2Int position)
    {
        Definition = definition;
        Position = position;
    }
}
