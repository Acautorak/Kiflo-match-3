using System;
using UnityEngine;

[Serializable]
public class StageDefinition
{
    public string name;
    [TextArea] public string description;

    public StageGoalType goalType = StageGoalType.Score;
    public int goalValue = 1000;

    public bool allowNonMatchingSwaps = true;
    public bool enableRandomSpecialOnGravity = false;
    public bool spawnLocksOnRefill = false;
    public bool destroySymbolWhenUnlocked = true;
    [Range(0f, 1f)] public float randomSpecialChance = 0.05f;
    [Min(0)] public int maxConsecutiveRandomTriggers = 3;
    [Range(0f, 1f)] public float lockSpawnChance = 0.05f;
}

public enum StageGoalType
{
    None,
    Score,
    MoveCount
}
