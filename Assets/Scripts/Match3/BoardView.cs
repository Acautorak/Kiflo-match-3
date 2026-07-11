using UnityEngine;
using Match3.Events;
using Match3.Core;

namespace Match3.Grid
{
    /// <summary>
    /// Thin presentation layer: turns drag input into swap requests to GameManager
    /// and reacts to EventBus events to drive animation/VFX/SFX. Contains no match
    /// or scoring logic itself — that all lives in BoardModel/GameManager, so this
    /// class can be swapped out for a different renderer without touching gameplay.
    /// </summary>
    public class BoardView : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private GameObject tilePrefab;
        [SerializeField] private float cellSize = 1f;

        private Vector2Int? _dragStart;

        private void OnEnable()
        {
            EventBus.Subscribe<BoardInitializedEvent>(OnBoardInitialized);
            EventBus.Subscribe<MatchFoundEvent>(OnMatchFound);
            EventBus.Subscribe<TileSwappedEvent>(OnTileSwapped);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BoardInitializedEvent>(OnBoardInitialized);
            EventBus.Unsubscribe<MatchFoundEvent>(OnMatchFound);
            EventBus.Unsubscribe<TileSwappedEvent>(OnTileSwapped);
        }

        private void OnBoardInitialized(BoardInitializedEvent evt)
        {
            // Spawn/reset tilePrefab instances for a evt.Width x evt.Height grid here.
            // Left intentionally minimal — wire up to your project's sprites/atlas.
        }

        private void OnMatchFound(MatchFoundEvent evt)
        {
            // Trigger a particle burst / sound at CellToWorld(evt.Origin), scaled by evt.Count.
        }

        private void OnTileSwapped(TileSwappedEvent evt)
        {
            // If !evt.WasValid, play a quick "bounce back" animation between From and To.
        }

        public void OnCellPointerDown(Vector2Int cell) => _dragStart = cell;

        public void OnCellPointerUp(Vector2Int cell)
        {
            if (_dragStart.HasValue && _dragStart.Value != cell)
                gameManager.TrySwap(_dragStart.Value, cell);

            _dragStart = null;
        }

        public Vector3 CellToWorld(int x, int y) => new Vector3(x * cellSize, y * cellSize, 0f);
    }
}
