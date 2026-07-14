using System;
using UnityEngine;

[Serializable]
public class StageDefinition
{
    public string name;
    [TextArea] public string description;

    public StageGoalType goalType = StageGoalType.Score;
    public int goalValue = 1000;
    [Tooltip("Only used when goalType = Collect: which symbol type the player needs to clear goalValue of.")]
    public SymbolType goalSymbolType;

    public bool allowNonMatchingSwaps = true;
    public bool enableRandomSpecialOnGravity = false;
    public bool spawnLocksOnRefill = false;
    public bool destroySymbolWhenUnlocked = true;
    public bool lockedTilesFallWithGravity = false;
    public FrozenTileSpawnMode frozenTileSpawnMode = FrozenTileSpawnMode.None;
    [Min(0)] public int frozenTileBottomRowCount = 0;
    [Min(0)] public int gracePeriodMoves = 3;
    [Range(0f, 1f)] public float gracePeriodRandomSpecialChance = 0f;
    [Range(0f, 1f)] public float randomSpecialChance = 0.05f;
    [Min(0)] public int maxConsecutiveRandomTriggers = 3;
    [Range(0f, 1f)] public float lockSpawnChance = 0.05f;
}

public enum StageGoalType
{
    None,
    Score,
    MoveCount,
    /// <summary>Clear goalValue symbols of goalSymbolType (tracked via SymbolMatchedEvent).</summary>
    Collect
}

public enum FrozenTileSpawnMode
{
    None,
    GenerateNewFrozenTiles,
    FreezeExistingBottomRows,
    Both
}
