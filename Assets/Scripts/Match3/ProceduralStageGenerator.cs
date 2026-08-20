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
            holesBlockGravity = config.holesBlockGravity,
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
        stage.wonkyChance = stage.enableRandomSpecialOnGravity ? config.wonkyChance.Lerp(t) : 0f;
        stage.maxConsecutiveRandomTriggers = config.maxConsecutiveRandomTriggers.Lerp(t);

        stage.spawnLocksOnRefill = depth >= config.locksOnRefillUnlockDepth;
        stage.lockSpawnChance = stage.spawnLocksOnRefill ? config.lockSpawnChance.Lerp(t) : 0f;

        bool frozenUnlocked = depth >= config.frozenTilesUnlockDepth;
        stage.frozenTileSpawnMode = frozenUnlocked
            ? (WeightedPool.Pick(config.frozenTileModeWeights, w => w.weight, rng)?.mode ?? FrozenTileSpawnMode.None)
            : FrozenTileSpawnMode.None;
        stage.frozenTileBottomRowCount = frozenUnlocked ? config.frozenTileBottomRowCount.Lerp(t) : 0;

        stage.featureModeOnMeterFull = WeightedPool.Pick(config.featureModeWeights, w => w.weight, rng)?.mode
            ?? MadnessFeatureModeChoice.KebabKarnage;

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
    /// `shapeMask` (usually the output of GenerateShape, converted with .ToMask2D()) is optional
    /// - pass it so locks never land on a hole; null/omitted assumes a full rectangle.
    /// </summary>
    public static InitialLockPlacement[] GenerateInitialLockPlacements(
        int depth, int runSeed, StageGenerationConfig config, int boardWidth, int boardHeight,
        bool[,] shapeMask = null)
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
            if (shapeMask != null && !shapeMask[pos.x, pos.y]) continue; // don't lock a hole
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

    /// <summary>
    /// Picks a weighted ShapeTemplate from config, fits it to (boardWidth, boardHeight), and
    /// validates it before returning - retrying with a different template on failure, then
    /// falling back to null (full rectangle) if nothing validates. Uses its own RNG stream so
    /// tweaking shape odds doesn't reshuffle goal/lock/frozen-tile rolls for the same stage.
    ///
    /// Add to StageGenerationConfig: `public int shapesUnlockDepth;` and
    /// `public ShapeTemplate[] shapeTemplates;` for this to have anything to draw from - depth
    /// below shapesUnlockDepth, or an empty pool, both mean "no shape" (full rectangle), same
    /// as every other gated feature in this generator (frozen tiles, locks-on-refill, etc).
    /// </summary>
    public static BoardShapeData GenerateShape(int depth, int runSeed, StageGenerationConfig config, int boardWidth, int boardHeight)
    {
        if (depth < config.shapesUnlockDepth || config.shapeTemplates == null || config.shapeTemplates.Length == 0)
            return null;

        var rng = RunRandom.ForDepth(runSeed, depth, stream: 2);
        const int maxAttempts = 10;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var template = WeightedPool.Pick(config.shapeTemplates, s => s.weight, rng);
            if (template?.shape == null || template.shape.IsEmpty) continue;

            var fitted = FitMaskToSize(template.shape.ToMask2D(), boardWidth, boardHeight);
            if (IsValidShape(fitted, boardWidth, boardHeight))
                return BoardShapeData.FromMask2D(fitted);
        }

        Debug.LogWarning($"[ProceduralStageGenerator] No shape template produced a valid layout " +
                          $"after {maxAttempts} attempts at depth {depth} - falling back to a full rectangle.");
        return null;
    }

    /// <summary>Centers a template's mask within (targetWidth, targetHeight), cropping if the
    /// template is larger or padding with holes if it's smaller - so one authored template can
    /// be reused across boards of slightly different sizes without a hard dimension match.</summary>
    private static bool[,] FitMaskToSize(bool[,] source, int targetWidth, int targetHeight)
    {
        int srcW = source.GetLength(0);
        int srcH = source.GetLength(1);
        var result = new bool[targetWidth, targetHeight]; // defaults to all-false (hole)

        int offsetX = (targetWidth - srcW) / 2;
        int offsetY = (targetHeight - srcH) / 2;

        for (int x = 0; x < srcW; x++)
        {
            int tx = x + offsetX;
            if (tx < 0 || tx >= targetWidth) continue;
            for (int y = 0; y < srcH; y++)
            {
                int ty = y + offsetY;
                if (ty < 0 || ty >= targetHeight) continue;
                result[tx, ty] = source[x, y];
            }
        }
        return result;
    }

    /// <summary>
    /// Rejects layouts that would be unplayable or degenerate:
    /// - any column with zero active cells (GravityController/PopulateBoard assume a column is
    ///   either fully absent from play or worth iterating - an all-hole column is just noise), or
    /// - any connected pocket smaller than 3 cells (nothing that small can ever host a match, so
    ///   it's dead weight at best and a stuck symbol at worst).
    /// </summary>
    private static bool IsValidShape(bool[,] mask, int width, int height)
    {
        for (int x = 0; x < width; x++)
        {
            bool columnHasActiveCell = false;
            for (int y = 0; y < height; y++)
                if (mask[x, y]) { columnHasActiveCell = true; break; }
            if (!columnHasActiveCell) return false;
        }

        var visited = new bool[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (!mask[x, y] || visited[x, y]) continue;
                if (FloodFillCount(mask, visited, x, y, width, height) < 3) return false;
            }

        return true;
    }

    private static readonly Vector2Int[] OrthogonalDirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    private static int FloodFillCount(bool[,] mask, bool[,] visited, int startX, int startY, int width, int height)
    {
        var stack = new Stack<Vector2Int>();
        stack.Push(new Vector2Int(startX, startY));
        visited[startX, startY] = true;
        int count = 0;

        while (stack.Count > 0)
        {
            var p = stack.Pop();
            count++;

            foreach (var d in OrthogonalDirs)
            {
                var n = p + d;
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (!mask[n.x, n.y] || visited[n.x, n.y]) continue;
                visited[n.x, n.y] = true;
                stack.Push(n);
            }
        }
        return count;
    }
}
