using System;
using System.Collections.Generic;

namespace Match3.Procedural
{
    public enum GoalType { ScoreTarget, CollectColor, ClearObstacles }

    [Serializable]
    public struct LevelGoal
    {
        public GoalType Type;
        public int ColorId;   // used when Type == CollectColor
        public int Target;
    }

    [Serializable]
    public class LevelConfig
    {
        public int LevelId;
        public int Seed;
        public int Width;
        public int Height;
        public int NumColors;
        public int MoveLimit;
        public float ObstacleDensity;
        public List<LevelGoal> Goals = new List<LevelGoal>();
    }
}
