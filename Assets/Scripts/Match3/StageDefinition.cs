using System;
using UnityEngine;
using UnityEngine.Serialization;

[Serializable]
public class StageDefinition
{
    public string name;
    [TextArea] public string description;

    public StageGoalType goalType = StageGoalType.Score;
    public int goalValue = 1000;
    [Tooltip("Only used when goalType = Collect: one or more (symbolType, count) requirements - " +
             "ALL of them must be satisfied to complete the stage.")]
    public CollectGoalTarget[] collectTargets;

    public bool allowNonMatchingSwaps = true;
    public bool enableRandomSpecialOnGravity = false;
    public bool spawnLocksOnRefill = false;
    public bool destroySymbolWhenUnlocked = true;
    public bool lockedTilesFallWithGravity = false;
    public FrozenTileSpawnMode frozenTileSpawnMode = FrozenTileSpawnMode.None;
    [Min(0)] public int frozenTileBottomRowCount = 0;
    [Min(0)] public int gracePeriodMoves = 3;
    [Range(0f, 1f)] public float gracePeriodRandomSpecialChance = 0f;
    [FormerlySerializedAs("randomSpecialChance")]
    [Range(0f, 1f)] public float wonkyChance = 0.05f;
    [Min(0)] public int maxConsecutiveRandomTriggers = 3;
    [Range(0f, 1f)] public float lockSpawnChance = 0.05f;

    [Tooltip("Which feature mode this stage's Madness meter fill should request. Assigned by " +
             "ProceduralStageGenerator from StageGenerationConfig.featureModeWeights - see " +
             "MadnessFeatureTrigger, which reads this off StageManager.CurrentStage instead of " +
             "using a single hardcoded feature id.")]
    public MadnessFeatureModeChoice featureModeOnMeterFull = MadnessFeatureModeChoice.KebabKarnage;
}

/// <summary>Which feature-mode mini-game a stage's Madness meter fill requests. Add new entries here
/// and in MadnessFeatureTrigger.ResolveFeatureId as more feature modes are built.</summary>
public enum MadnessFeatureModeChoice
{
    KebabKarnage,
    FreeSpins,
    LuckyScratchTicket,
    TileCollector
}

public enum StageGoalType
{
    None,
    Score,
    MoveCount,
    /// <summary>Clear count of symbolType for every entry in collectTargets (tracked via SymbolMatchedEvent).</summary>
    Collect
}

/// <summary>One "clear `count` of `symbolType`" requirement within a Collect goal.</summary>
[Serializable]
public struct CollectGoalTarget
{
    public SymbolType symbolType;
    public int count;
}

public enum FrozenTileSpawnMode
{
    None,
    GenerateNewFrozenTiles,
    FreezeExistingBottomRows,
    Both
}
