using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Designer-facing asset that fully drives procedural stage generation. Create via
/// Assets > Create > Match3 > Procedural > Stage Generation Config, assign it to StageManager,
/// and tick "Procedural Mode" on to turn a fixed StageDefinition[] list into an endless roguelike
/// run. Every numeric knob below is a min/max range that gets Lerp'd by difficultyCurve, so a
/// designer can reshape the entire run's pacing by editing one curve without touching code.
/// </summary>
[CreateAssetMenu(fileName = "StageGenerationConfig", menuName = "Match3/Procedural/Stage Generation Config")]
public class StageGenerationConfig : ScriptableObject
{
    [Header("Difficulty Curve")]
    [Tooltip("X = stage depth (0-based, 0 is the first stage). Y = difficulty, clamped to 0-1 before " +
             "use. Every range below is Lerp'd min->max using this value, so shape this curve to control " +
             "overall pacing (fast early ramp, plateau, late spike, etc.) without touching any other field. " +
             "The default ramps gently to full difficulty by depth 50 rather than depth 20, so a run has " +
             "a long, gradual climb instead of maxing out difficulty within the first couple dozen stages. " +
             "It's fine for the curve to run past 1.0 on the X axis - depths beyond the last keyframe just " +
             "reuse the final value, giving you a natural difficulty cap for an endless run.")]
    public AnimationCurve difficultyCurve = AnimationCurve.EaseInOut(0f, 0f, 50f, 1f);

    [Header("Goal")]
    [Tooltip("Which goal type a generated stage uses. Weight each entry to control the mix " +
             "(e.g. mostly Score stages with occasional MoveCount/Collect stages for variety).")]
    public List<GoalTypeWeight> goalTypeWeights = new List<GoalTypeWeight>
    {
        new GoalTypeWeight { type = StageGoalType.Score, weight = 3f },
        new GoalTypeWeight { type = StageGoalType.MoveCount, weight = 1f },
        new GoalTypeWeight { type = StageGoalType.Collect, weight = 2f },
    };
    public IntRange scoreGoal = new IntRange { min = 800, max = 6000 };
    public IntRange moveCountGoal = new IntRange { min = 10, max = 28 };
    [Tooltip("How many of the chosen symbol type the player must clear, per target, for a Collect goal.")]
    public IntRange collectGoal = new IntRange { min = 15, max = 60 };
    [Tooltip("How many distinct symbol types a Collect goal requires satisfying simultaneously " +
             "(e.g. 2 = \"collect 20 Red AND 15 Blue\"). Keep min at 1 so early stages stay simple; " +
             "raise max to make later stages juggle more colors at once.")]
    public IntRange collectTargetCount = new IntRange { min = 1, max = 3 };

    [Header("Grace Period")]
    public IntRange gracePeriodMoves = new IntRange { min = 2, max = 5 };
    public FloatRange gracePeriodRandomSpecialChance = new FloatRange { min = 0f, max = 0.3f };

    [Header("Random Special On Gravity")]
    [Tooltip("Stage depth at which the random gravity-bonus effect can first turn on for a stage.")]
    [Min(0)] public int randomSpecialOnGravityUnlockDepth = 6;
    public FloatRange randomSpecialChance = new FloatRange { min = 0.02f, max = 0.15f };
    public IntRange maxConsecutiveRandomTriggers = new IntRange { min = 1, max = 3 };

    [Header("Locks / Freezing - Rules")]
    [Tooltip("These two are treated as fixed rules rather than difficulty-scaled ranges - flip them " +
             "if your project wants locks to behave differently, but most projects won't vary these per stage.")]
    public bool destroySymbolWhenUnlocked = true;
    public bool lockedTilesFallWithGravity = false;

    [Header("Locks / Freezing - Initial Placements")]
    [Tooltip("Stage depth at which designer-placed-style locked tiles start appearing on a fresh stage.")]
    [Min(0)] public int initialLocksUnlockDepth = 5;
    [Tooltip("How many locked tiles to place on the stage, once unlocked by the depth above.")]
    public IntRange initialLockCount = new IntRange { min = 0, max = 6 };

    [Header("Locks / Freezing - Auto Spawn On Refill")]
    [Tooltip("Stage depth at which newly refilled tiles can start spawning already locked.")]
    [Min(0)] public int locksOnRefillUnlockDepth = 10;
    public FloatRange lockSpawnChance = new FloatRange { min = 0.02f, max = 0.12f };

    [Header("Locks / Freezing - Shared Option Pool")]
    [Tooltip("Pool of lock configurations drawn from for initial placements. Each entry's own " +
             "`weight` field controls how often it's picked - reweight or add/remove entries here " +
             "to shift the mix (more Temporary vs Permanent, heavier layer counts, etc).")]
    public LockSpawnOption[] lockSpawnOptions =
    {
        new LockSpawnOption { layers = 1, behavior = LockBehavior.Temporary, movesPerLayer = 3, weight = 3f },
        new LockSpawnOption { layers = 2, behavior = LockBehavior.Temporary, movesPerLayer = 3, weight = 2f },
        new LockSpawnOption { layers = 1, behavior = LockBehavior.Permanent, weight = 1f },
    };

    [Header("Frozen Tiles")]
    [Tooltip("Stage depth at which frozen-tile spawning can first turn on for a stage.")]
    [Min(0)] public int frozenTilesUnlockDepth = 15;
    public List<FrozenModeWeight> frozenTileModeWeights = new List<FrozenModeWeight>
    {
        new FrozenModeWeight { mode = FrozenTileSpawnMode.GenerateNewFrozenTiles, weight = 2f },
        new FrozenModeWeight { mode = FrozenTileSpawnMode.FreezeExistingBottomRows, weight = 1f },
        new FrozenModeWeight { mode = FrozenTileSpawnMode.Both, weight = 1f },
    };
    public IntRange frozenTileBottomRowCount = new IntRange { min = 1, max = 3 };

    [Header("Rules (fixed - not scaled by difficulty)")]
    public bool allowNonMatchingSwaps = true;
}
