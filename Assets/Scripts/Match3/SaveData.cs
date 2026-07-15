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
    public CellSaveData[] cells; // flattened, index = x * height + y
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
