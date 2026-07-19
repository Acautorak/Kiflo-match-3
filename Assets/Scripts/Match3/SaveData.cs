using System;

[Serializable]
public class BoardSaveData
{
    public int width;
    public int height;
    public int score;
    public int moveCount;
    public int currentHealth;
    public int maxHealth;
    public int currentStageIndex;
    public int runSeed;
    public int[] collectGoalProgress;
    public PlayerRunStatsSaveData runStats;
    public CellSaveData[] cells; // flattened, index = x * height + y
    public bool graceActive;
    public int graceMovesRemaining;
    public float graceRandomSpecialChance;
    public bool isAwaitingPowerupSelection;
}

[Serializable]
public class CellSaveData
{
    public bool hasSymbol;
    public SymbolType type;
    public SpecialType special;

    // Lock/freeze state - lockLayers is 0 for an unlocked tile.
    public int lockLayers;
    public LockBehavior lockBehavior;
    public int movesPerLayer;
    public int movesUntilNextAutoUnlock;
}

/// <summary>Persists PlayerRunStats' accumulated powerup modifiers - see PlayerRunStats.BuildSaveData/RestoreFromSave.</summary>
[Serializable]
public class PlayerRunStatsSaveData
{
    public float randomSpecialChanceBonus;
    public float lockChanceReduction;
    public float scoreMultiplier;
    public int bonusGraceMoves;
    public ColorBonusSaveData[] colorBonuses;
}

[Serializable]
public class ColorBonusSaveData
{
    public SymbolType color;
    public float scoreMultiplierBonus;
    public int flatScoreBonusPerCell;
    public float healChancePerMatch;
    public int healAmountOnMatch;
}
