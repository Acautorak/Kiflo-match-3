using UnityEngine;

namespace Match3.Events
{
    public readonly struct BoardInitializedEvent : IEvent
    {
        public readonly int Width;
        public readonly int Height;
        public BoardInitializedEvent(int width, int height) { Width = width; Height = height; }
    }

    public readonly struct TileSwappedEvent : IEvent
    {
        public readonly Vector2Int From;
        public readonly Vector2Int To;
        public readonly bool WasValid;
        public TileSwappedEvent(Vector2Int from, Vector2Int to, bool wasValid)
        {
            From = from; To = to; WasValid = wasValid;
        }
    }

    public readonly struct MatchFoundEvent : IEvent
    {
        public readonly int ColorId;
        public readonly int Count;
        public readonly Vector2Int Origin;
        public MatchFoundEvent(int colorId, int count, Vector2Int origin)
        {
            ColorId = colorId; Count = count; Origin = origin;
        }
    }

    /// <summary>Fired once per wave of the resolve loop — useful for staggering VFX/SFX on big cascades.</summary>
    public readonly struct CascadeStepEvent : IEvent
    {
        public readonly int CascadeDepth;
        public CascadeStepEvent(int depth) { CascadeDepth = depth; }
    }

    public readonly struct ScoreChangedEvent : IEvent
    {
        public readonly int NewScore;
        public readonly int Delta;
        public ScoreChangedEvent(int newScore, int delta) { NewScore = newScore; Delta = delta; }
    }

    public readonly struct MovesChangedEvent : IEvent
    {
        public readonly int RemainingMoves;
        public MovesChangedEvent(int remaining) { RemainingMoves = remaining; }
    }

    public readonly struct GoalProgressEvent : IEvent
    {
        public readonly int GoalIndex;
        public readonly int Current;
        public readonly int Target;
        public GoalProgressEvent(int goalIndex, int current, int target)
        {
            GoalIndex = goalIndex; Current = current; Target = target;
        }
    }

    public readonly struct LevelCompletedEvent : IEvent
    {
        public readonly int LevelId;
        public readonly int Stars;
        public readonly int FinalScore;
        public LevelCompletedEvent(int levelId, int stars, int finalScore)
        {
            LevelId = levelId; Stars = stars; FinalScore = finalScore;
        }
    }

    public readonly struct LevelFailedEvent : IEvent
    {
        public readonly int LevelId;
        public LevelFailedEvent(int levelId) { LevelId = levelId; }
    }

    public readonly struct SaveRequestedEvent : IEvent { }

    public readonly struct SaveCompletedEvent : IEvent
    {
        public readonly bool Success;
        public SaveCompletedEvent(bool success) { Success = success; }
    }
}
