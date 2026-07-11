using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns the grid, symbol spawning/swapping, match resolution, cascades, and special-symbol
/// creation/activation. Publishes everything through EventBus so other systems (UI, audio,
/// GameManager, SaveSystem) stay fully decoupled.
/// </summary>
public class Board : MonoBehaviour
{
    public static Board Instance { get; private set; }

    [Header("Grid Settings")]
    [SerializeField] private int width = 8;
    [SerializeField] private int height = 8;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector2 origin = Vector2.zero;

    [Header("Prefabs")]
    [Tooltip("Single shared symbol prefab, reskinned per SymbolType via its SymbolVisualConfig. " +
             "Only used when symbolPrefabs (below) is empty.")]
    [SerializeField] private Symbol symbolPrefab;
    [Tooltip("Optional: one prefab per SymbolType, indexed by enum order. Leave empty to use symbolPrefab for everything.")]
    [SerializeField] private Symbol[] symbolPrefabs;
    [SerializeField] private Transform symbolParent;

    private Symbol GetPrefab(SymbolType type)
    {
        if (symbolPrefabs != null && symbolPrefabs.Length > 0)
        {
            int index = (int)type;
            if (index < 0 || index >= symbolPrefabs.Length)
            {
                Debug.LogError($"[Board] symbolPrefabs has {symbolPrefabs.Length} entries but SymbolType has more values. " +
                                $"Either fill in all {System.Enum.GetValues(typeof(SymbolType)).Length} slots, or clear the array and assign symbolPrefab instead.");
                return symbolPrefab != null ? symbolPrefab : symbolPrefabs[0];
            }
            return symbolPrefabs[index];
        }

        if (symbolPrefab == null)
            Debug.LogError("[Board] No prefab assigned. Set symbolPrefab (single shared prefab) or fill symbolPrefabs (one per SymbolType) in the Inspector.");

        return symbolPrefab;
    }

    [Header("Timing")]
    [SerializeField] private float swapDuration = 0.2f;
    [SerializeField] private float fallDuration = 0.25f;

    private Cell[,] grid;
    private Symbol selected;
    private bool isBusy;
    private int currentScore;

    private void Awake()
    {
        Instance = this;
        Debug.Log($"[Board] Awake - grid {width}x{height}");
        grid = new Cell[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = new Cell();
    }

    private void Start()
    {
        Debug.Log("[Board] Start");
        var saved = SaveSystem.Load();
        if (saved != null && saved.width == width && saved.height == height)
        {
            Debug.Log("[Board] Loading from save");
            LoadFromSave(saved);
        }
        else
        {
            Debug.Log(saved == null ? "[Board] No save found, populating fresh board" : "[Board] Save size mismatch, populating fresh board");
            PopulateBoard();
        }
    }

    #region Setup

    private void PopulateBoard()
    {
        int spawned = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                SymbolType type;
                do { type = RandomType(); }
                while (CreatesImmediateMatch(x, y, type));

                if (SpawnSymbol(x, y, type, SpecialType.None) != null) spawned++;
            }
        }
        Debug.Log($"[Board] PopulateBoard finished - spawned {spawned}/{width * height} symbols");
    }

    private bool CreatesImmediateMatch(int x, int y, SymbolType type)
    {
        if (x >= 2 && !grid[x - 1, y].IsEmpty && !grid[x - 2, y].IsEmpty
            && grid[x - 1, y].Occupant.Type == type && grid[x - 2, y].Occupant.Type == type)
            return true;

        if (y >= 2 && !grid[x, y - 1].IsEmpty && !grid[x, y - 2].IsEmpty
            && grid[x, y - 1].Occupant.Type == type && grid[x, y - 2].Occupant.Type == type)
            return true;

        return false;
    }

    private SymbolType RandomType()
    {
        var values = System.Enum.GetValues(typeof(SymbolType));
        return (SymbolType)values.GetValue(Random.Range(0, values.Length));
    }

    private Symbol SpawnSymbol(int x, int y, SymbolType type, SpecialType special)
    {
        var prefab = GetPrefab(type);
        if (prefab == null)
        {
            Debug.LogError($"[Board] SpawnSymbol({x},{y},{type}) aborted - GetPrefab returned null");
            return null;
        }
        var instance = Instantiate(prefab, GridToWorld(x, y), Quaternion.identity, symbolParent);
        instance.Initialize(type, special, new Vector2Int(x, y));
        grid[x, y].Occupant = instance;
        return instance;
    }

    private Vector3 GridToWorld(int x, int y) =>
        new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, 0f);

    #endregion

    #region Input / Swapping

    public void SelectSymbol(Symbol symbol)
    {
        if (isBusy) return;

        if (selected == null) { selected = symbol; return; }
        if (selected == symbol) { selected = null; return; }

        if (IsAdjacent(selected.GridPosition, symbol.GridPosition))
            StartCoroutine(TrySwap(selected, symbol));

        selected = null;
    }

    private bool IsAdjacent(Vector2Int a, Vector2Int b) =>
        Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) == 1;

    private IEnumerator TrySwap(Symbol a, Symbol b)
    {
        isBusy = true;
        yield return SwapRoutine(a, b);

        var matches = MatchFinder.FindAllMatches(grid, width, height);
        if (matches.Count == 0)
        {
            yield return SwapRoutine(a, b); // revert - no match, invalid move
            isBusy = false;
            yield break;
        }

        yield return ResolveMatches(matches);
        isBusy = false;
    }

    private IEnumerator SwapRoutine(Symbol a, Symbol b)
    {
        var posA = a.GridPosition;
        var posB = b.GridPosition;

        grid[posA.x, posA.y].Occupant = b;
        grid[posB.x, posB.y].Occupant = a;
        a.GridPosition = posB;
        b.GridPosition = posA;

        a.MoveTo(GridToWorld(posB.x, posB.y), swapDuration);
        b.MoveTo(GridToWorld(posA.x, posA.y), swapDuration);

        yield return new WaitForSeconds(swapDuration);
    }

    #endregion

    #region Matching / Cascades

    private IEnumerator ResolveMatches(List<List<Vector2Int>> initialMatches)
    {
        var currentMatches = initialMatches;
        int chainCount = 0;

        while (currentMatches.Count > 0)
        {
            chainCount++;

            var allPositions = new HashSet<Vector2Int>();
            var specialsToCreate = new List<(Vector2Int pos, SpecialType special, SymbolType type)>();

            foreach (var line in currentMatches)
            {
                foreach (var p in line) allPositions.Add(p);

                // Longer runs upgrade the "seed" cell (middle of the run) into a special symbol.
                if (line.Count >= 5)
                {
                    var center = line[line.Count / 2];
                    specialsToCreate.Add((center, SpecialType.ColorClear, grid[center.x, center.y].Occupant.Type));
                }
                else if (line.Count == 4)
                {
                    var center = line[line.Count / 2];
                    bool horizontal = line[0].y == line[1].y;
                    var special = horizontal ? SpecialType.RowClear : SpecialType.ColumnClear;
                    specialsToCreate.Add((center, special, grid[center.x, center.y].Occupant.Type));
                }
            }

            // Any special symbols caught inside this match activate and pull in extra cells.
            var extraCleared = new HashSet<Vector2Int>();
            foreach (var pos in allPositions)
            {
                var occ = grid[pos.x, pos.y].Occupant;
                if (occ != null && occ.Special != SpecialType.None)
                    foreach (var a in ActivateSpecial(occ)) extraCleared.Add(a);
            }
            foreach (var p in extraCleared) allPositions.Add(p);

            // --- Publish events for this cascade step ---
            foreach (var pos in allPositions)
            {
                var occ = grid[pos.x, pos.y]?.Occupant;
                if (occ != null) EventBus.Publish(new SymbolMatchedEvent(occ.Type, pos));
            }
            EventBus.Publish(new ChainMatchedEvent(currentMatches.Sum(l => l.Count), chainCount, allPositions.ToArray()));

            int scoreDelta = allPositions.Count * 10 * chainCount;
            currentScore += scoreDelta;
            EventBus.Publish(new ScoreChangedEvent(currentScore, scoreDelta));

            // Clear matched cells, but skip the seed cells that are becoming specials.
            var specialPositions = specialsToCreate.Select(s => s.pos).ToHashSet();
            foreach (var pos in allPositions)
            {
                if (specialPositions.Contains(pos)) continue;
                var occ = grid[pos.x, pos.y].Occupant;
                if (occ == null) continue;
                Destroy(occ.gameObject);
                grid[pos.x, pos.y].Occupant = null;
            }

            foreach (var (pos, special, type) in specialsToCreate)
            {
                var existing = grid[pos.x, pos.y].Occupant;
                if (existing != null) Destroy(existing.gameObject);
                SpawnSymbol(pos.x, pos.y, type, special);
                EventBus.Publish(new SpecialSymbolCreatedEvent(special, pos));
            }

            yield return CollapseAndRefill();
            currentMatches = MatchFinder.FindAllMatches(grid, width, height);
        }

        SaveSystem.Save(BuildSaveData());
    }

    /// <summary>Returns every grid position this special symbol clears, and publishes the open event.</summary>
    private Vector2Int[] ActivateSpecial(Symbol special)
    {
        var pos = special.GridPosition;
        var affected = new List<Vector2Int>();

        switch (special.Special)
        {
            case SpecialType.RowClear:
                for (int x = 0; x < width; x++) affected.Add(new Vector2Int(x, pos.y));
                break;
            case SpecialType.ColumnClear:
                for (int y = 0; y < height; y++) affected.Add(new Vector2Int(pos.x, y));
                break;
            case SpecialType.Bomb:
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int nx = pos.x + dx, ny = pos.y + dy;
                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            affected.Add(new Vector2Int(nx, ny));
                    }
                break;
            case SpecialType.ColorClear:
                var targetType = special.Type;
                for (int x = 0; x < width; x++)
                    for (int y = 0; y < height; y++)
                        if (!grid[x, y].IsEmpty && grid[x, y].Occupant.Type == targetType)
                            affected.Add(new Vector2Int(x, y));
                break;
        }

        // This is the "open event" for special matches - hook VFX/SFX/UI to it via SpecialSymbolEventRelay.
        EventBus.Publish(new SpecialSymbolMatchedEvent(special.Special, pos, affected.ToArray()));
        return affected.ToArray();
    }

    #endregion

    #region Collapse / Refill

    private IEnumerator CollapseAndRefill()
    {
        for (int x = 0; x < width; x++)
        {
            int writeY = 0;
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y].IsEmpty) continue;

                if (writeY != y)
                {
                    var occ = grid[x, y].Occupant;
                    grid[x, writeY].Occupant = occ;
                    grid[x, y].Occupant = null;
                    occ.GridPosition = new Vector2Int(x, writeY);
                    occ.MoveTo(GridToWorld(x, writeY), fallDuration);
                }
                writeY++;
            }

            for (int y = writeY; y < height; y++)
            {
                var type = RandomType();
                var spawnHeight = height + (y - writeY); // spawn above the visible board and fall in
                var prefab = GetPrefab(type);
                if (prefab == null) continue;
                var instance = Instantiate(prefab, GridToWorld(x, spawnHeight), Quaternion.identity, symbolParent);
                instance.Initialize(type, SpecialType.None, new Vector2Int(x, y));
                grid[x, y].Occupant = instance;
                instance.MoveTo(GridToWorld(x, y), fallDuration);
            }
        }

        yield return new WaitForSeconds(fallDuration);
    }

    #endregion

    #region Save / Load

    private BoardSaveData BuildSaveData()
    {
        var data = new BoardSaveData
        {
            width = width,
            height = height,
            score = currentScore,
            cells = new CellSaveData[width * height]
        };

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                data.cells[x * height + y] = occ == null
                    ? new CellSaveData { hasSymbol = false }
                    : new CellSaveData { hasSymbol = true, type = occ.Type, special = occ.Special };
            }

        return data;
    }

    private void LoadFromSave(BoardSaveData data)
    {
        currentScore = data.score;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var cellData = data.cells[x * height + y];
                if (cellData.hasSymbol) SpawnSymbol(x, y, cellData.type, cellData.special);
            }

        EventBus.Publish(new ScoreChangedEvent(currentScore, 0));
    }

    /// <summary>Call this from GameManager on pause/quit for a simple, reliable save point.</summary>
    public void SaveNow() => SaveSystem.Save(BuildSaveData());

    [ContextMenu("Delete Save And Log Path")]
    private void DeleteSaveFromEditor()
    {
        Debug.Log($"[Board] Save file path: {Application.persistentDataPath}");
        SaveSystem.DeleteSave();
        Debug.Log("[Board] Save deleted. Press Play again for a fresh board.");
    }

    #endregion
}
