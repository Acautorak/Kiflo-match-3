using System;
using System.Collections.Generic;

namespace Match3.Save
{
    [Serializable]
    public class LevelResult
    {
        public int LevelId;
        public int Stars;
        public int BestScore;
    }

    [Serializable]
    public class SaveData
    {
        public int SchemaVersion = 1;
        public int HighestUnlockedLevel = 1;
        public int Currency = 0;
        public List<LevelResult> Results = new List<LevelResult>();

        // Lets a player close the app mid-level and resume exactly where they left off.
        public bool HasSuspendedLevel;
        public int SuspendedLevelId;
        public int SuspendedMovesRemaining;
        public int SuspendedScore;
        public List<int> SuspendedBoardColors = new List<int>(); // flattened, row-major
    }
}
