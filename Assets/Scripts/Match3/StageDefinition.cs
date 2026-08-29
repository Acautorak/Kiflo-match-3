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

    [Tooltip("Hand-authored board shape for this stage - which cells within Width x Height are " +
             "playable. Leave the mask empty for a full rectangle. Ignored (overridden) when " +
             "ProceduralStageGenerator hands Board a generated shape instead - see " +
             "ProceduralStageGenerator.GenerateShape and Board.ResetForStage's proceduralShape param.")]
    public BoardShapeData shape;
    [Tooltip("Only matters when this stage has holes (see shape above). True: a hole is a floor/" +
             "ceiling - symbols above it can't fall past it into whatever's below, splitting the " +
             "column into independent pockets. False: gravity passes straight through - a hole is " +
             "just a slot nothing can ever occupy, and symbols fall past it as if it weren't " +
             "there. Untested which plays better - see GravityController.HolesBlockGravity.")]
    public bool holesBlockGravity = true;

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
    TileCollector,
    DiscoDanceDisco,
    CookieSmash
}

public enum StageGoalType
{
    None,
    Score,
    MoveCount,
    /// <summary>Clear count of symbolType for every entry in collectTargets (tracked via SymbolMatchedEvent).</summary>
    Collect,
    /// <summary>Reach a single-move cascade of goalValue chain steps or more (ChainMatchedEvent.ChainCount).
    /// Checked instantly per cascade step - no persistent progress, just a threshold to hit in one go.</summary>
    ChainCombo,
    /// <summary>Clear goalValue total symbols within a SINGLE cascade (all steps triggered by one player
    /// move, summed together) - distinct from ChainCombo, which cares about cascade DEPTH, not total
    /// symbols cleared. Tracked via ChainMatchedEvent.SymbolsCleared, reset whenever ChainCount == 1
    /// (a fresh cascade starting).</summary>
    CascadeCollect,
    /// <summary>Clear goalValue Madness Symbols over the course of the stage (MadnessSymbolClearedEvent).</summary>
    MadnessCleared,
    /// <summary>Go goalValue consecutive real moves (Grace Moves excluded, same as MoveCount) without
    /// taking any damage. Judged once each move's ENTIRE resolution (including any cascade-triggered
    /// damage) has settled - see StageManager's GameStateChangedEvent handler for why this can't just
    /// be judged directly on PlayerMoveEvent (damage from a move's own cascade happens AFTER that
    /// move's PlayerMoveEvent already fired, so judging immediately would misattribute it to the
    /// wrong move).</summary>
    SurviveNoDamage
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
