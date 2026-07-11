using System;
using System.Collections.Generic;

namespace Match3.Procedural
{
    /// <summary>
    /// Deterministically generates a LevelConfig from a level number using a
    /// difficulty curve. The same levelId always produces the same level (the
    /// id is hashed into a seed), so levels are reproducible and shareable
    /// (e.g. for a daily-challenge or level-code feature) while still looking
    /// distinct from one another across the sequence.
    /// </summary>
    public static class LevelGenerator
    {
        private const int BaseWidth = 7;
        private const int BaseHeight = 8;
        private const int MaxWidth = 9;
        private const int MaxHeight = 10;
        private const int MinColors = 4;
        private const int MaxColors = 7;

        public static LevelConfig Generate(int levelId, int? overrideSeed = null)
        {
            int seed = overrideSeed ?? HashSeed(levelId);
            var rng = new Random(seed);

            var config = new LevelConfig
            {
                LevelId = levelId,
                Seed = seed,
                Width = Math.Min(BaseWidth + levelId / 10, MaxWidth),
                Height = Math.Min(BaseHeight + levelId / 15, MaxHeight),
                NumColors = Math.Min(MinColors + levelId / 8, MaxColors),
                MoveLimit = Math.Max(15, 30 - levelId / 5),
                ObstacleDensity = Math.Min(0.35f, 0.02f * (levelId / 3))
            };

            config.Goals = GenerateGoals(levelId, rng, config);
            return config;
        }

        private static List<LevelGoal> GenerateGoals(int levelId, Random rng, LevelConfig config)
        {
            var goals = new List<LevelGoal>();

            // Difficulty tiers change goal *shape*, not just raw numbers, so
            // later levels feel structurally different, not just "more".
            int tier = levelId / 10;

            if (tier == 0)
            {
                goals.Add(new LevelGoal
                {
                    Type = GoalType.ScoreTarget,
                    Target = 1000 + levelId * 150
                });
            }
            else if (tier % 2 == 0)
            {
                int color = rng.Next(0, config.NumColors);
                goals.Add(new LevelGoal
                {
                    Type = GoalType.CollectColor,
                    ColorId = color,
                    Target = 15 + levelId
                });
                goals.Add(new LevelGoal
                {
                    Type = GoalType.ScoreTarget,
                    Target = 800 + levelId * 120
                });
            }
            else
            {
                goals.Add(new LevelGoal
                {
                    Type = GoalType.ClearObstacles,
                    Target = (int)(config.Width * config.Height * config.ObstacleDensity)
                });
            }

            return goals;
        }

        /// <summary>
        /// Turns an integer level id into a well-distributed seed using a Knuth
        /// multiplicative hash, so consecutive levels don't produce visually
        /// similar boards even though the id itself increments by 1.
        /// </summary>
        private static int HashSeed(int levelId)
        {
            unchecked
            {
                uint h = (uint)levelId * 2654435761u;
                h ^= h >> 15;
                return (int)h;
            }
        }
    }
}
