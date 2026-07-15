using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turns a stage depth + run seed + StageGenerationConfig into the exact same StageDefinition
/// and InitialLockPlacement[] that a designer would otherwise hand-author. Nothing downstream
/// (Board, StageManager, save/load) needs to know or care whether a StageDefinition came from
/// here or from the Inspector - this is deliberately a pure function of its three inputs so a
/// given (depth, runSeed) always regenerates identically, letting saves store just the seed
/// and current depth instead of a full stage snapshot.
/// </summary>
public static class ProceduralStageGenerator
{
    public static StageDefinition GenerateStage(int depth, int runSeed, StageGenerationConfig config)
    {
        var rng = RunRandom.ForDepth(runSeed, depth, stream: 0);
        float t = Mathf.Clamp01(config.difficultyCurve.Evaluate(depth));

        var stage = new StageDefinition
        {
            name = $"Stage {depth + 1}",
            description = $"Procedurally generated stage (depth {depth + 1}, difficulty {t:0.00}).",
            allowNonMatchingSwaps = config.allowNonMatchingSwaps,
            destroySymbolWhenUnlocked = config.destroySymbolWhenUnlocked,
            lockedTilesFallWithGravity = config.lockedTilesFallWithGravity,
        };

        stage.goalType = WeightedPool.Pick(config.goalTypeWeights, w => w.weight, rng)?.type ?? StageGoalType.Score;
        switch (stage.goalType)
        {
            case StageGoalType.MoveCount:
                stage.goalValue = config.moveCountGoal.Lerp(t);
                break;
            case StageGoalType.Collect:
                stage.collectTargets = GenerateCollectTargets(rng, t, config);
                // goalValue isn't used to decide completion for Collect goals (every target must
                // be met individually - see StageManager), but keep it populated with the combined
                // total so anything reading it generically (logging, analytics) still gets a number.
                stage.goalValue = 0;
                foreach (var target in stage.collectTargets) stage.goalValue += target.count;
                break;
            default:
                stage.goalValue = config.scoreGoal.Lerp(t);
                break;
        }

        stage.gracePeriodMoves = config.gracePeriodMoves.Lerp(t);
        stage.gracePeriodRandomSpecialChance = config.gracePeriodRandomSpecialChance.Lerp(t);

        stage.enableRandomSpecialOnGravity = depth >= config.randomSpecialOnGravityUnlockDepth;
        stage.randomSpecialChance = stage.enableRandomSpecialOnGravity ? config.randomSpecialChance.Lerp(t) : 0f;
        stage.maxConsecutiveRandomTriggers = config.maxConsecutiveRandomTriggers.Lerp(t);

        stage.spawnLocksOnRefill = depth >= config.locksOnRefillUnlockDepth;
        stage.lockSpawnChance = stage.spawnLocksOnRefill ? config.lockSpawnChance.Lerp(t) : 0f;

        bool frozenUnlocked = depth >= config.frozenTilesUnlockDepth;
        stage.frozenTileSpawnMode = frozenUnlocked
            ? (WeightedPool.Pick(config.frozenTileModeWeights, w => w.weight, rng)?.mode ?? FrozenTileSpawnMode.None)
            : FrozenTileSpawnMode.None;
        stage.frozenTileBottomRowCount = frozenUnlocked ? config.frozenTileBottomRowCount.Lerp(t) : 0;

        return stage;
    }

    /// <summary>Picks collectTargetCount.Lerp(t) distinct symbol types, each with its own count.Lerp(t) target.</summary>
    private static CollectGoalTarget[] GenerateCollectTargets(System.Random rng, float t, StageGenerationConfig config)
    {
        var allTypes = (SymbolType[])System.Enum.GetValues(typeof(SymbolType));
        int targetCount = Mathf.Clamp(config.collectTargetCount.Lerp(t), 1, allTypes.Length);

        var pool = new List<SymbolType>(allTypes);
        var targets = new CollectGoalTarget[targetCount];
        for (int i = 0; i < targetCount; i++)
        {
            int pick = rng.Next(pool.Count);
            targets[i] = new CollectGoalTarget
            {
                symbolType = pool[pick],
                count = config.collectGoal.Lerp(t)
            };
            pool.RemoveAt(pick);
        }
        return targets;
    }

    /// <summary>
    /// Designer-placed-style locked tiles for a freshly generated stage. Uses a separate RNG
    /// stream from GenerateStage so tweaking one doesn't reshuffle the other's rolls.
    /// </summary>
    public static InitialLockPlacement[] GenerateInitialLockPlacements(
        int depth, int runSeed, StageGenerationConfig config, int boardWidth, int boardHeight)
    {
        if (depth < config.initialLocksUnlockDepth || boardWidth <= 0 || boardHeight <= 0)
            return System.Array.Empty<InitialLockPlacement>();

        var rng = RunRandom.ForDepth(runSeed, depth, stream: 1);
        float t = Mathf.Clamp01(config.difficultyCurve.Evaluate(depth));
        int count = Mathf.Clamp(config.initialLockCount.Lerp(t), 0, boardWidth * boardHeight);
        if (count <= 0 || config.lockSpawnOptions == null || config.lockSpawnOptions.Length == 0)
            return System.Array.Empty<InitialLockPlacement>();

        var placements = new List<InitialLockPlacement>(count);
        var used = new HashSet<Vector2Int>();
        int attempts = 0;
        int maxAttempts = count * 25;

        while (placements.Count < count && attempts < maxAttempts)
        {
            attempts++;
            var pos = new Vector2Int(rng.Next(0, boardWidth), rng.Next(0, boardHeight));
            if (!used.Add(pos)) continue;

            var option = WeightedPool.Pick(config.lockSpawnOptions, o => o.weight, rng);
            if (option == null) break;

            placements.Add(new InitialLockPlacement
            {
                position = pos,
                layers = option.layers,
                behavior = option.behavior,
                movesPerLayer = option.movesPerLayer
            });
        }

        return placements.ToArray();
    }
}
