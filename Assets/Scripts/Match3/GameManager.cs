using System.Collections.Generic;
using UnityEngine;
using Match3.Events;
using Match3.Grid;
using Match3.Procedural;
using Match3.Save;

namespace Match3.Core
{
    /// <summary>
    /// Orchestrates a single level: builds the board from a procedurally
    /// generated config, drives the swap/match/cascade loop, tracks goals and
    /// moves, and reports everything through the EventBus so UI, audio, and
    /// VFX stay fully decoupled from this class and from BoardModel.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private int levelId = 1;

        private BoardModel _board;
        private LevelConfig _config;
        private int _score;
        private int _movesRemaining;
        private readonly Dictionary<int, int> _colorCollected = new Dictionary<int, int>();

        private void OnEnable() => EventBus.Subscribe<SaveRequestedEvent>(OnSaveRequested);
        private void OnDisable() => EventBus.Unsubscribe<SaveRequestedEvent>(OnSaveRequested);

        private void Start()
        {
            SaveSystem.Load();
            StartLevel(levelId);
        }

        public void StartLevel(int id)
        {
            _config = LevelGenerator.Generate(id);
            _board = new BoardModel(_config.Width, _config.Height, _config.NumColors, _config.Seed);
            _board.FillRandomNoMatches();

            _score = 0;
            _movesRemaining = _config.MoveLimit;
            _colorCollected.Clear();

            EventBus.Publish(new BoardInitializedEvent(_config.Width, _config.Height));
            EventBus.Publish(new MovesChangedEvent(_movesRemaining));
        }

        /// <summary>Call from BoardView after the player drags one tile onto an adjacent one.</summary>
        public void TrySwap(Vector2Int a, Vector2Int b)
        {
            if (_movesRemaining <= 0) return;
            if (!IsAdjacent(a, b)) return;

            _board.Swap(a.x, a.y, b.x, b.y);
            var matches = MatchFinder.FindAllMatches(_board);

            if (matches.Count == 0)
            {
                _board.Swap(a.x, a.y, b.x, b.y); // revert invalid swap
                EventBus.Publish(new TileSwappedEvent(a, b, wasValid: false));
                return;
            }

            EventBus.Publish(new TileSwappedEvent(a, b, wasValid: true));
            _movesRemaining--;
            EventBus.Publish(new MovesChangedEvent(_movesRemaining));

            ResolveCascade(matches, depth: 0);
            CheckEndConditions();
        }

        private void ResolveCascade(List<MatchGroup> matches, int depth)
        {
            EventBus.Publish(new CascadeStepEvent(depth));

            foreach (var group in matches)
            {
                int gained = ScoreForMatch(group.Cells.Count);
                _score += gained;
                EventBus.Publish(new ScoreChangedEvent(_score, gained));
                EventBus.Publish(new MatchFoundEvent(group.ColorId, group.Cells.Count, ToVector(group.Cells[0])));

                _colorCollected.TryGetValue(group.ColorId, out int collectedSoFar);
                collectedSoFar += group.Cells.Count;
                _colorCollected[group.ColorId] = collectedSoFar;
                ReportGoalProgress(group.ColorId, collectedSoFar);

                foreach (var (x, y) in group.Cells)
                    _board.Set(x, y, Tile.Empty);
            }

            _board.Collapse();
            _board.Refill();

            var nextMatches = MatchFinder.FindAllMatches(_board);
            if (nextMatches.Count > 0)
                ResolveCascade(nextMatches, depth + 1);
            else if (!_board.HasPossibleMove())
                _board.Shuffle();
        }

        private void ReportGoalProgress(int colorId, int collected)
        {
            for (int i = 0; i < _config.Goals.Count; i++)
            {
                var goal = _config.Goals[i];
                if (goal.Type == GoalType.CollectColor && goal.ColorId == colorId)
                    EventBus.Publish(new GoalProgressEvent(i, collected, goal.Target));
            }
        }

        private void CheckEndConditions()
        {
            bool allGoalsMet = true;
            foreach (var goal in _config.Goals)
            {
                switch (goal.Type)
                {
                    case GoalType.ScoreTarget:
                        if (_score < goal.Target) allGoalsMet = false;
                        break;
                    case GoalType.CollectColor:
                        _colorCollected.TryGetValue(goal.ColorId, out int c);
                        if (c < goal.Target) allGoalsMet = false;
                        break;
                    case GoalType.ClearObstacles:
                        // Hook up once obstacle tiles are tracked on the board.
                        break;
                }
            }

            if (allGoalsMet)
            {
                int stars = CalculateStars();
                SaveSystem.RecordLevelResult(_config.LevelId, stars, _score);
                EventBus.Publish(new LevelCompletedEvent(_config.LevelId, stars, _score));
            }
            else if (_movesRemaining <= 0)
            {
                EventBus.Publish(new LevelFailedEvent(_config.LevelId));
            }
        }

        private int CalculateStars()
        {
            if (_movesRemaining >= _config.MoveLimit / 2) return 3;
            if (_movesRemaining > 0) return 2;
            return 1;
        }

        private static int ScoreForMatch(int size) => size switch
        {
            3 => 60,
            4 => 120,
            5 => 250,
            _ => 250 + (size - 5) * 100
        };

        private static bool IsAdjacent(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

        private static Vector2Int ToVector((int x, int y) c) => new Vector2Int(c.x, c.y);

        private void OnSaveRequested(SaveRequestedEvent evt) => SaveSystem.Save();
    }
}
