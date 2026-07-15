using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
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

    [Header("Rules")]
    [Tooltip("If true, any adjacent swap completes and stays, even if it doesn't create a match. " +
             "If false, swaps that don't create a match are reverted (classic match-3 behavior).")]
    [SerializeField] private bool allowNonMatchingSwaps = true;
    [Tooltip("If true, an L/T-shaped intersection (two runs crossing) creates one Bomb special " +
             "and the individual run lengths are ignored. If false, each run in the group creates " +
             "its own special independently (e.g. two crossing 4-runs each become a line-clear), " +
             "while all their cells still clear together as one chain.")]
    [SerializeField] private bool intersectionsCreateBombs = true;

    [Header("Random Special Effect (Gravity Bonus)")]
    [Tooltip("Whenever the board settles after gravity (any clear + refill), roll a chance for " +
             "one random tile to trigger a random special effect on its own, exactly as if it had " +
             "been matched - clears cells, scores, and fires the same events as a real special match.")]
    [SerializeField] private bool enableRandomSpecialOnGravity = false;
    [Tooltip("Chance (0-1) that a bonus effect fires each time gravity settles the board.")]
    [Range(0f, 1f)]
    [SerializeField] private float randomSpecialTriggerChance = 0.05f;
    [Tooltip("Which special effects can be randomly picked. Leave empty to disable even if the toggle above is on.")]
    [SerializeField] private SpecialType[] eligibleRandomSpecialTypes =
    {
        SpecialType.RowClear, SpecialType.ColumnClear, SpecialType.Bomb, SpecialType.ColorClear
    };
    [Tooltip("Safety cap on how many bonus effects can chain back-to-back in a single settle, " +
             "in case the chance above is set high enough to otherwise cascade indefinitely.")]
    [Min(0)]
    [SerializeField] private int maxConsecutiveRandomTriggers = 3;

    [Header("Locking / Freezing")]
    [Tooltip("If true, breaking a lock's final layer also clears/destroys the tile immediately " +
             "(classic 'destroy to unlock' obstacle behavior). If false, the lock just falls away " +
             "and the tile becomes a normal free tile, staying on the board.")]
    [SerializeField] private bool destroySymbolWhenUnlocked = true;
    [Tooltip("If true, locked/frozen tiles obey gravity and can fall during collapse/refill. If false, they stay fixed and act as a floor.")]
    [SerializeField] private bool lockedTilesFallWithGravity = false;
    [Tooltip("Controls whether frozen tiles can be introduced during refill.")]
    [SerializeField] private FrozenTileSpawnMode frozenTileSpawnMode = FrozenTileSpawnMode.None;
    [Tooltip("If greater than 0, any newly spawned frozen tiles are only allowed in the bottom N rows of the board.")]
    [SerializeField] private int frozenTileBottomRowCount = 0;
    [Tooltip("Multiplier applied to Lock Spawn Chance for rows outside the bottom-row priority band " +
             "(y >= Frozen Tile Bottom Row Count). 0 = never spawn/freeze outside the band, 1 = no " +
             "priority (uniform chance everywhere). Only relevant when Frozen Tile Bottom Row Count > 0.")]
    [Range(0f, 1f)]
    [SerializeField] private float frozenTileOutsideBottomRowsChanceMultiplier = 0.25f;
    [Tooltip("Score bonus awarded each time a lock takes a hit from a match/special effect, whether " +
             "or not it fully breaks this hit. Auto-melt hits (from moves) don't award this.")]
    [SerializeField] private int scorePerLockHit = 5;
    [Tooltip("If true, locked/frozen tiles can still be selected and swapped like normal. If false " +
             "(default), they block selection until fully unlocked.")]
    [SerializeField] private bool allowSwappingLockedTiles = false;
    [Tooltip("Designer-authored locked/frozen tiles for a fresh level layout, applied right after " +
             "the board populates. Ignored when loading from an existing save (lock state persists there).")]
    [SerializeField] private InitialLockPlacement[] initialLockPlacements;

    [Header("Locking / Freezing - Auto Spawn On Refill (optional)")]
    [Tooltip("If true, newly refilled tiles have a chance to spawn already locked, picked from Lock Spawn Options below.")]
    [SerializeField] private bool spawnLocksOnRefill = false;
    [Range(0f, 1f)]
    [SerializeField] private float lockSpawnChance = 0.05f;
    [SerializeField] private LockSpawnOption[] lockSpawnOptions =
    {
        new LockSpawnOption { layers = 1, behavior = LockBehavior.Temporary, movesPerLayer = 3, weight = 3f },
        new LockSpawnOption { layers = 2, behavior = LockBehavior.Temporary, movesPerLayer = 3, weight = 2f },
        new LockSpawnOption { layers = 1, behavior = LockBehavior.Permanent, weight = 1f },
    };

    [Header("System References (auto-found in Awake if left empty)")]
    [Tooltip("Optional explicit link. If left empty, Board finds it in the scene at Awake. " +
             "Assign this if you want Board to wait for StageManager to drive initialization " +
             "(see Start()) instead of populating/loading itself.")]
    [SerializeField] private StageManager stageManager;
    [SerializeField] private PlayerHealth playerHealth;

    private Cell[,] grid;
    private Symbol selected;
    private bool isBusy;
    private bool isStageClearing;
    private int currentScore;
    private GameManager gameManager;
    private int moveCount;
    private bool stageClearGraceActive;
    private int stageClearGraceMovesRemaining;
    private float stageClearGraceRandomSpecialChance;
    private bool stageClearPendingAfterResolution;
    private bool hasLoadedSavedState;

    public int MoveCount => moveCount;
    public int CurrentScore => currentScore;
    public bool IsGameBusy => isBusy || isStageClearing;
    public bool HasLoadedSavedState => hasLoadedSavedState;
    public int Width => width;
    public int Height => height;

    private void Awake()
    {
        Instance = this;
        gameManager = FindAnyObjectByType<GameManager>();
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        Debug.Log($"[Board] Awake - grid {width}x{height}");
        grid = new Cell[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y] = new Cell();
    }

    private void Start()
    {
        // Unity doesn't guarantee Start() ordering between Board and StageManager. When a
        // StageManager is present, it owns "what happens at game start" (fresh stage vs.
        // restored stage) and is responsible for calling InitializeBoard() itself, at the
        // right point in its own Start(). Only self-initialize here when Board is running
        // standalone (no StageManager in the scene), to avoid both scripts populating/loading
        // the board independently and racing each other.
        if (stageManager != null)
        {
            Debug.Log("[Board] Start - StageManager present, waiting for it to drive initialization.");
            return;
        }

        Debug.Log("[Board] Start - no StageManager found, initializing independently.");
        InitializeBoard();
    }

    /// <summary>
    /// Loads the saved board state if one matches the current grid size, otherwise populates
    /// a fresh board, then resolves any pre-existing matches. Called automatically from Start()
    /// when Board is running standalone; called explicitly by StageManager (when present) once
    /// it has decided whether this is a fresh run or a restored one.
    /// </summary>
    public void InitializeBoard()
    {
        var saved = SaveSystem.Load();
        if (saved != null && saved.width == width && saved.height == height)
        {
            Debug.Log("[Board] Loading from save");
            LoadFromSave(saved);
            hasLoadedSavedState = true;
        }
        else
        {
            Debug.Log(saved == null ? "[Board] No save found, populating fresh board" : "[Board] Save size mismatch, populating fresh board");
            PopulateBoard();
            hasLoadedSavedState = false;
        }

        StartCoroutine(ResolveAnyExistingMatches());
    }

    /// <summary>
    /// Safety net: verifies the board right after populate/load and clears anything that's
    /// already matched. Guards against stale/corrupt saves (e.g. captured mid-cascade, or
    /// from an older build) silently persisting an unresolved match forever.
    /// </summary>
    private IEnumerator ResolveAnyExistingMatches()
    {
        var groups = MatchFinder.FindMatchGroups(grid, width, height);
        if (groups.Count == 0) yield break;

        Debug.Log($"[Board] Found {groups.Count} pre-existing match group(s) on start - resolving.");
        isBusy = true;
        yield return ResolveMatches(groups);
        isBusy = false;
        RestoreGameManagerRestingState();
    }

    /// <summary>
    /// Puts GameManager back into whichever "not actively busy" state currently applies -
    /// Idle (waiting for player input) normally, or GracePeriod if a stage-clear grace period
    /// is in progress. Skipped while a stage-clear cleanup is underway/pending, since Board
    /// or StageManager own that transition explicitly. Call this any time isBusy is cleared.
    /// </summary>
    private void RestoreGameManagerRestingState()
    {
        if (gameManager == null) return;
        if (isStageClearing || stageClearPendingAfterResolution) return;
        gameManager.SetState(stageClearGraceActive
            ? GameManager.GameplayState.GracePeriod
            : GameManager.GameplayState.Idle);
    }

    #region Setup

    private void PopulateBoard(InitialLockPlacement[] overrideLockPlacements = null)
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

        ApplyInitialLockPlacements(overrideLockPlacements);
    }

    public void ClearBoard()
    {
        ClearExistingSymbols();
        currentScore = 0;
        moveCount = 0;
        selected = null;
        isBusy = false;
        isStageClearing = false;
        ResetStageClearGraceState();
        EventBus.Publish(new ScoreChangedEvent(currentScore, 0));
    }

    public void ResetForStage(StageDefinition stage, InitialLockPlacement[] proceduralLockPlacements = null)
    {
        ClearBoard();
        ApplyStageRules(stage);
        PopulateBoard(proceduralLockPlacements);
        hasLoadedSavedState = false;
        SaveNow();
        StartCoroutine(ResolveAnyExistingMatches());
    }

    public void BeginStageClearGracePeriod(int extraMoves = 3, float randomSpecialChance = 0f)
    {
        stageClearGraceActive = true;
        stageClearGraceMovesRemaining = Mathf.Max(0, extraMoves);
        stageClearGraceRandomSpecialChance = randomSpecialChance;
        stageClearPendingAfterResolution = false;
    }

    public void SetGraceStateActive(bool active)
    {
        stageClearGraceActive = active;
        if (!active)
        {
            stageClearGraceMovesRemaining = 0;
            stageClearGraceRandomSpecialChance = 0f;
            stageClearPendingAfterResolution = false;
        }
    }

    public void ResetStageClearGraceState()
    {
        stageClearGraceActive = false;
        stageClearGraceMovesRemaining = 0;
        stageClearGraceRandomSpecialChance = 0f;
        stageClearPendingAfterResolution = false;
    }

    private bool ShouldSkipRefillGeneration()
    {
        if (stageClearGraceActive && stageClearGraceMovesRemaining > 0) return true;
        if (stageClearPendingAfterResolution) return true;
        if (gameManager != null && gameManager.CurrentState == GameManager.GameplayState.StageClearing)
            return true;
        return false;
    }

    private void ConsumeStageClearGraceMove()
    {
        if (!stageClearGraceActive || stageClearGraceMovesRemaining <= 0) return;

        stageClearGraceMovesRemaining--;
        Debug.Log($"[Board] Stage clear grace moves remaining: {stageClearGraceMovesRemaining}");

        if (stageClearGraceMovesRemaining <= 0)
        {
            stageClearGraceActive = false;
            stageClearPendingAfterResolution = true;
            if (gameManager != null)
                gameManager.SetState(GameManager.GameplayState.StageClearing);
            Debug.Log("[Board] Final grace move consumed; stage clear will begin after the current resolution completes.");
        }
    }

    public void BeginStageClearCleanup(System.Action onComplete)
    {
        if (isStageClearing) return;

        isStageClearing = true;
        isBusy = true;
        StartCoroutine(ExplodeRemainingSymbols(onComplete));
    }

    private void ApplyStageRules(StageDefinition stage)
    {
        if (stage == null) return;

        allowNonMatchingSwaps = stage.allowNonMatchingSwaps;
        enableRandomSpecialOnGravity = stage.enableRandomSpecialOnGravity;
        spawnLocksOnRefill = stage.spawnLocksOnRefill;
        destroySymbolWhenUnlocked = stage.destroySymbolWhenUnlocked;
        lockedTilesFallWithGravity = stage.lockedTilesFallWithGravity;
        frozenTileSpawnMode = stage.frozenTileSpawnMode;
        frozenTileBottomRowCount = stage.frozenTileBottomRowCount;
        randomSpecialTriggerChance = stage.randomSpecialChance;
        maxConsecutiveRandomTriggers = stage.maxConsecutiveRandomTriggers;
        lockSpawnChance = stage.lockSpawnChance;
    }

    private void ClearExistingSymbols()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ != null)
                {
                    Destroy(occ.gameObject);
                    grid[x, y].Occupant = null;
                }
            }
    }

    private void ApplyInitialLockPlacements(InitialLockPlacement[] overridePlacements = null)
    {
        var placements = (overridePlacements != null && overridePlacements.Length > 0)
            ? overridePlacements
            : initialLockPlacements;

        if (placements == null || placements.Length == 0) return;

        int applied = 0;
        foreach (var placement in placements)
        {
            var p = placement.position;
            if (p.x < 0 || p.x >= width || p.y < 0 || p.y >= height)
            {
                Debug.LogWarning($"[Board] Initial lock placement {p} is out of bounds ({width}x{height}) - skipped.");
                continue;
            }

            var occ = grid[p.x, p.y].Occupant;
            if (occ == null)
            {
                Debug.LogWarning($"[Board] Initial lock placement {p} has no symbol to lock - skipped.");
                continue;
            }

            occ.SetLock(placement.layers, placement.behavior, placement.movesPerLayer);
            applied++;
        }
        Debug.Log($"[Board] Applied {applied}/{placements.Length} initial lock placement(s).");
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

    private Symbol SpawnSymbol(int x, int y, SymbolType type, SpecialType special,
        int lockLayers = 0, LockBehavior lockBehavior = LockBehavior.None, int movesPerLayer = 3)
    {
        var prefab = GetPrefab(type);
        if (prefab == null)
        {
            Debug.LogError($"[Board] SpawnSymbol({x},{y},{type}) aborted - GetPrefab returned null");
            return null;
        }
        var instance = Instantiate(prefab, GridToWorld(x, y), Quaternion.identity, symbolParent);
        instance.Initialize(type, special, new Vector2Int(x, y));
        if (lockLayers > 0 && lockBehavior != LockBehavior.None)
            instance.SetLock(lockLayers, lockBehavior, movesPerLayer);
        grid[x, y].Occupant = instance;
        return instance;
    }

    private Vector3 GridToWorld(int x, int y) =>
        new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, 0f);

    #endregion

    #region Input / Swapping

    public void SelectSymbol(Symbol symbol)
    {
        if (IsGameBusy) return;
        if (gameManager != null && !gameManager.AllowsPlayerInput) return;

        if (symbol.IsLocked && !allowSwappingLockedTiles)
        {
            Debug.Log($"[Board] {symbol.name} at {symbol.GridPosition} is locked " +
                       $"({symbol.LockBehaviorMode}, {symbol.LockLayers} layer(s) left) - selection blocked.");
            selected = null;
            return;
        }

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
        gameManager?.SetState(GameManager.GameplayState.Playing);
        yield return SwapRoutine(a, b);

        var matchGroups = MatchFinder.FindMatchGroups(grid, width, height);
        Debug.Log($"[Board] Post-swap scan: {matchGroups.Count} group(s) - " +
                   string.Join(" | ", matchGroups.Select(g =>
                       $"cells={g.Cells.Count} lines={g.Lines.Count} intersection={g.IsIntersection} longestRun={g.LongestRun}")));

        if (matchGroups.Count == 0)
            ApplyHealthPenalty();

        bool swapIsValid = matchGroups.Count > 0 || allowNonMatchingSwaps;
        if (!swapIsValid)
        {
            yield return SwapRoutine(a, b); // revert - no match, invalid move
            isBusy = false;
            RestoreGameManagerRestingState();
            yield break;
        }

        RegisterPlayerMove();
        ConsumeStageClearGraceMove();
        yield return TryRandomSpecialOnGraceMove();

        // This swap counts as an accepted player move - every Temporary lock on the board
        // melts one layer, regardless of whether it was actually matched.
        if (MeltAllTemporaryLocks())
        {
            if (ShouldSkipRefillGeneration() || stageClearPendingAfterResolution)
            {
                matchGroups = MatchFinder.FindMatchGroups(grid, width, height); // melting can reveal new matches
            }
            else
            {
                yield return CollapseAndRefill();
                matchGroups = MatchFinder.FindMatchGroups(grid, width, height); // melting can reveal new matches
            }
        }

        if (matchGroups.Count > 0)
            yield return ResolveMatches(matchGroups);

        if (stageClearPendingAfterResolution && !isStageClearing)
        {
            stageClearPendingAfterResolution = false;
            stageManager?.FinalizeStageClear();
        }

        isBusy = false;
        RestoreGameManagerRestingState();
    }

    private void RegisterPlayerMove()
    {
        moveCount++;
        EventBus.Publish(new PlayerMoveEvent(moveCount));
    }

    private void ApplyHealthPenalty(int amount = 1)
    {
        if (playerHealth != null)
            playerHealth.TakeDamage(amount);
    }

    private IEnumerator SwapRoutine(Symbol a, Symbol b)
    {
        var posA = a.GridPosition;
        var posB = b.GridPosition;

        grid[posA.x, posA.y].Occupant = b;
        grid[posB.x, posB.y].Occupant = a;
        a.GridPosition = posB;
        b.GridPosition = posA;

        var sequence = DOTween.Sequence();
        sequence.Join(a.MoveTo(GridToWorld(posB.x, posB.y), swapDuration));
        sequence.Join(b.MoveTo(GridToWorld(posA.x, posA.y), swapDuration));

        yield return sequence.WaitForCompletion();
    }

    /// <summary>
    /// Ticks every Temporary lock on the board once (called after each accepted player move).
    /// Returns true if any tile was fully destroyed as a result, so the caller knows to
    /// re-run gravity/refill before continuing.
    /// </summary>
    private bool MeltAllTemporaryLocks()
    {
        bool anyDestroyed = false;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || occ.LockBehaviorMode != LockBehavior.Temporary) continue;

                bool melted = occ.TickTemporaryLock();
                if (!melted) continue;

                var pos = new Vector2Int(x, y);
                bool fullyUnlocked = !occ.IsLocked;
                EventBus.Publish(new LockLayerRemovedEvent(pos, occ.LockLayers, triggeredByMatch: false, fullyUnlocked));

                if (fullyUnlocked && destroySymbolWhenUnlocked)
                {
                    Destroy(occ.gameObject);
                    grid[x, y].Occupant = null;
                    anyDestroyed = true;
                }
            }

        return anyDestroyed;
    }

    #endregion

    #region Matching / Cascades

    private IEnumerator ResolveMatches(List<MatchGroup> initialGroups)
    {
        var currentGroups = initialGroups;
        int chainCount = 0;
        gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);

        while (currentGroups.Count > 0)
        {
            if (isStageClearing) break;

            chainCount++;
            Debug.Log($"[Board] Cascade step {chainCount}: {currentGroups.Count} group(s) - " +
                       string.Join(" | ", currentGroups.Select(g =>
                           $"cells={g.Cells.Count} intersection={g.IsIntersection} longestRun={g.LongestRun} seed={g.GetSeedCell()}")));

            var allPositions = new HashSet<Vector2Int>();
            var specialsToCreate = new Dictionary<Vector2Int, (SpecialType special, SymbolType type)>();

            foreach (var group in currentGroups)
            {
                foreach (var p in group.Cells) allPositions.Add(p);
                RegisterSpecialsFromMatchGroup(group, specialsToCreate);
            }

            // Any special symbols caught inside this match activate and pull in extra cells.
            var extraCleared = new HashSet<Vector2Int>();
            bool anySpecialActivated = false;
            foreach (var pos in allPositions)
            {
                var occ = grid[pos.x, pos.y].Occupant;
                if (occ != null && occ.Special != SpecialType.None)
                {
                    if (!anySpecialActivated)
                    {
                        anySpecialActivated = true;
                        gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);
                    }
                    foreach (var a in ActivateSpecial(occ)) extraCleared.Add(a);
                }
            }
            foreach (var p in extraCleared) allPositions.Add(p);
            if (anySpecialActivated) gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);

            // --- Publish events for this cascade step ---
            foreach (var pos in allPositions)
            {
                var occ = grid[pos.x, pos.y]?.Occupant;
                if (occ != null) EventBus.Publish(new SymbolMatchedEvent(occ.Type, pos));
            }
            EventBus.Publish(new ChainMatchedEvent(currentGroups.Sum(g => g.Cells.Count), chainCount, allPositions.ToArray()));

            // Clear matched cells (locked tiles take a hit instead of clearing until their lock
            // breaks). Cells reserved for becoming a special are skipped here UNLESS they're
            // still locked - in that case they take a hit too, and special creation there is
            // deferred (removed from specialsToCreate) until a future pass finds it unlocked.
            int scoreDelta = 0;
            foreach (var pos in allPositions)
            {
                bool isSpecialSeed = specialsToCreate.ContainsKey(pos);
                var occBefore = grid[pos.x, pos.y].Occupant;
                if (isSpecialSeed && (occBefore == null || !occBefore.IsLocked)) continue;

                var (destroyed, delta) = ClearCell(pos, chainCount);
                scoreDelta += delta;
                if (isSpecialSeed && !destroyed) specialsToCreate.Remove(pos);
            }
            currentScore += scoreDelta;
            EventBus.Publish(new ScoreChangedEvent(currentScore, scoreDelta));

            foreach (var (pos, info) in specialsToCreate)
            {
                var existing = grid[pos.x, pos.y].Occupant;
                if (existing != null) Destroy(existing.gameObject);
                SpawnSymbol(pos.x, pos.y, info.type, info.special);
                EventBus.Publish(new SpecialSymbolCreatedEvent(info.special, pos));
            }

            if (isStageClearing) break;

            if (ShouldSkipRefillGeneration())
            {
                Debug.Log("[Board] Stage clear grace active - skipping refill generation.");
                currentGroups = new List<MatchGroup>();
                break;
            }

            yield return CollapseAndRefill();
            yield return TryRandomSpecialOnGravity(chainCount);
            gameManager?.SetState(GameManager.GameplayState.ResolvingMatches);
            currentGroups = MatchFinder.FindMatchGroups(grid, width, height);
        }

        SaveSystem.Save(BuildSaveData());
    }

    private void RegisterSpecialsFromMatchGroup(MatchGroup group,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        if (group.IsIntersection && intersectionsCreateBombs)
        {
            var seed = group.GetSeedCell();
            RegisterSpecialSeed(seed, SpecialType.Bomb, specialsToCreate);
            return;
        }

        // Not treating this as a bomb (either a straight run, or intersections-as-bomb
        // is disabled) - let each constituent run create its own special independently.
        foreach (var line in group.Lines)
        {
            if (line.Count < 4) continue;
            RegisterSpecialFromLine(line, specialsToCreate);
        }
    }

    private void RegisterSpecialFromLine(List<Vector2Int> line,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        var seed = line[line.Count / 2];
        var special = line.Count >= 5
            ? SpecialType.ColorClear
            : (line[0].y == line[1].y ? SpecialType.RowClear : SpecialType.ColumnClear);

        RegisterSpecialSeed(seed, special, specialsToCreate);
    }

    private void RegisterSpecialSeed(Vector2Int seed, SpecialType special,
        Dictionary<Vector2Int, (SpecialType special, SymbolType type)> specialsToCreate)
    {
        var seedType = grid[seed.x, seed.y].Occupant?.Type ?? SymbolType.Red;
        specialsToCreate[seed] = (special, seedType);
    }

    private IEnumerator ExplodeRemainingSymbols(System.Action onComplete)
    {
        var remaining = new List<Symbol>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ != null)
                    remaining.Add(occ);
            }

        for (int i = 0; i < remaining.Count; i++)
        {
            var symbol = remaining[i];
            if (symbol == null) continue;

            var pos = symbol.GridPosition;
            if (grid[pos.x, pos.y].Occupant == symbol)
                grid[pos.x, pos.y].Occupant = null;

            symbol.transform.DOScale(0.15f, 0.15f).SetEase(Ease.InBack);
            symbol.transform.DOScale(0f, 0.15f).SetEase(Ease.InBack);
            Destroy(symbol.gameObject, 0.3f);
            yield return new WaitForSeconds(0.25f);
        }

        isBusy = false;
        isStageClearing = false;
        onComplete?.Invoke();
    }

    /// <summary>Every grid position a given special effect would hit, from a given origin cell.</summary>
    private List<Vector2Int> ComputeAffectedCells(SpecialType type, Vector2Int origin, SymbolType colorForColorClear)
    {
        return type switch
        {
            SpecialType.RowClear => ComputeRowClearAffectedCells(origin),
            SpecialType.ColumnClear => ComputeColumnClearAffectedCells(origin),
            SpecialType.Bomb => ComputeBombAffectedCells(origin),
            SpecialType.ColorClear => ComputeColorClearAffectedCells(origin, colorForColorClear),
            _ => new List<Vector2Int>()
        };
    }

    private List<Vector2Int> ComputeRowClearAffectedCells(Vector2Int origin)
    {
        var affected = new List<Vector2Int>();
        for (int x = 0; x < width; x++) affected.Add(new Vector2Int(x, origin.y));
        return affected;
    }

    private List<Vector2Int> ComputeColumnClearAffectedCells(Vector2Int origin)
    {
        var affected = new List<Vector2Int>();
        for (int y = 0; y < height; y++) affected.Add(new Vector2Int(origin.x, y));
        return affected;
    }

    private List<Vector2Int> ComputeBombAffectedCells(Vector2Int origin)
    {
        var affected = new List<Vector2Int>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                int nx = origin.x + dx, ny = origin.y + dy;
                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                    affected.Add(new Vector2Int(nx, ny));
            }

        return affected;
    }

    private List<Vector2Int> ComputeColorClearAffectedCells(Vector2Int origin, SymbolType colorForColorClear)
    {
        var affected = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                if (!grid[x, y].IsEmpty && grid[x, y].Occupant.Type == colorForColorClear)
                    affected.Add(new Vector2Int(x, y));

        return affected;
    }

    /// <summary>
    /// Attempts to clear a single matched/affected cell. If it's locked, this reduces the lock
    /// by one layer instead of destroying it - unless that exact hit breaks the last layer and
    /// destroySymbolWhenUnlocked is true, in which case it clears immediately on the same hit.
    /// Returns whether the cell actually emptied, and the score this hit is worth.
    /// </summary>
    private (bool destroyed, int scoreDelta) ClearCell(Vector2Int pos, int chainCount)
    {
        var occ = grid[pos.x, pos.y].Occupant;
        if (occ == null) return (false, 0);

        if (occ.IsLocked)
        {
            bool fullyUnlocked = occ.RemoveLockLayer();
            EventBus.Publish(new LockLayerRemovedEvent(pos, occ.LockLayers, triggeredByMatch: true, fullyUnlocked));

            if (!fullyUnlocked) return (false, scorePerLockHit);
            if (!destroySymbolWhenUnlocked) return (false, scorePerLockHit);
            // else: the same hit that broke the lock also clears the tile - fall through
        }

        Destroy(occ.gameObject);
        grid[pos.x, pos.y].Occupant = null;
        return (true, 10 * chainCount);
    }

    /// <summary>Returns every grid position this special symbol clears, and publishes the open event.</summary>
    private Vector2Int[] ActivateSpecial(Symbol special)
    {
        var pos = special.GridPosition;
        var affected = special.Special switch
        {
            SpecialType.RowClear => ActivateRowClear(pos),
            SpecialType.ColumnClear => ActivateColumnClear(pos),
            SpecialType.Bomb => ActivateBomb(pos),
            SpecialType.ColorClear => ActivateColorClear(pos, special.Type),
            _ => new Vector2Int[0]
        };

        // This is the "open event" for special matches - hook VFX/SFX/UI to it via SpecialSymbolEventRelay.
        EventBus.Publish(new SpecialSymbolMatchedEvent(special.Special, pos, affected));
        return affected;
    }

    private Vector2Int[] ActivateRowClear(Vector2Int origin)
    {
        return ComputeRowClearAffectedCells(origin).ToArray();
    }

    private Vector2Int[] ActivateColumnClear(Vector2Int origin)
    {
        return ComputeColumnClearAffectedCells(origin).ToArray();
    }

    private Vector2Int[] ActivateBomb(Vector2Int origin)
    {
        return ComputeBombAffectedCells(origin).ToArray();
    }

    private Vector2Int[] ActivateColorClear(Vector2Int origin, SymbolType colorForColorClear)
    {
        return ComputeColorClearAffectedCells(origin, colorForColorClear).ToArray();
    }

    /// <summary>
    /// Rolls a chance for a random tile to spontaneously trigger a random special effect,
    /// exactly as if it had been matched (same events, same scoring, same clear/collapse).
    /// Called after every gravity settle. Can loop multiple times per settle up to
    /// maxConsecutiveRandomTriggers; pass forceOnce=true to guarantee exactly one trigger
    /// (used by the Inspector test button), bypassing the toggle and chance roll.
    /// </summary>
    private IEnumerator TryRandomSpecialOnGraceMove()
    {
        if (!stageClearGraceActive || stageClearGraceMovesRemaining <= 0) yield break;
        if (eligibleRandomSpecialTypes == null || eligibleRandomSpecialTypes.Length == 0) yield break;
        if (stageClearGraceRandomSpecialChance <= 0f || Random.value >= stageClearGraceRandomSpecialChance) yield break;

        var candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ != null && !occ.IsLocked) candidates.Add(new Vector2Int(x, y));
            }

        if (candidates.Count == 0) yield break;

        gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);

        var origin = candidates[Random.Range(0, candidates.Count)];
        var originSymbol = grid[origin.x, origin.y].Occupant;
        var effectType = eligibleRandomSpecialTypes[Random.Range(0, eligibleRandomSpecialTypes.Length)];
        var affected = new HashSet<Vector2Int>(ComputeAffectedCells(effectType, origin, originSymbol.Type)) { origin };

        Debug.Log($"[Board] Grace-period bonus: {effectType} at {origin} - clearing {affected.Count} cell(s)");

        foreach (var pos in affected)
        {
            var occ = grid[pos.x, pos.y]?.Occupant;
            if (occ != null) EventBus.Publish(new SymbolMatchedEvent(occ.Type, pos));
        }

        EventBus.Publish(new SpecialSymbolMatchedEvent(effectType, origin, affected.ToArray()));
        EventBus.Publish(new ChainMatchedEvent(affected.Count, 1, affected.ToArray()));

        int scoreDelta = 0;
        foreach (var pos in affected)
        {
            var (_, delta) = ClearCell(pos, 1);
            scoreDelta += delta;
        }
        currentScore += scoreDelta;
        EventBus.Publish(new ScoreChangedEvent(currentScore, scoreDelta));

        if (!ShouldSkipRefillGeneration())
            yield return CollapseAndRefill();
    }

    private IEnumerator TryRandomSpecialOnGravity(int chainCount, bool forceOnce = false)
    {
        if (eligibleRandomSpecialTypes == null || eligibleRandomSpecialTypes.Length == 0) yield break;
        if (!forceOnce && !enableRandomSpecialOnGravity) yield break;

        int triggered = 0;
        int cap = forceOnce ? 1 : maxConsecutiveRandomTriggers;

        while (triggered < cap && (forceOnce || Random.value < randomSpecialTriggerChance))
        {
            var candidates = new List<Vector2Int>();
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                {
                    var occ = grid[x, y].Occupant;
                    if (occ != null && !occ.IsLocked) candidates.Add(new Vector2Int(x, y));
                }

            if (candidates.Count == 0) break;

            gameManager?.SetState(GameManager.GameplayState.ResolvingSpecialMadness);

            var origin = candidates[Random.Range(0, candidates.Count)];
            var originSymbol = grid[origin.x, origin.y].Occupant;
            var effectType = eligibleRandomSpecialTypes[Random.Range(0, eligibleRandomSpecialTypes.Length)];

            var affected = new HashSet<Vector2Int>(ComputeAffectedCells(effectType, origin, originSymbol.Type)) { origin };

            Debug.Log($"[Board] Random gravity bonus: {effectType} at {origin} - clearing {affected.Count} cell(s)");

            foreach (var pos in affected)
            {
                var occ = grid[pos.x, pos.y]?.Occupant;
                if (occ != null) EventBus.Publish(new SymbolMatchedEvent(occ.Type, pos));
            }

            // Same open event a real special match fires - VFX/SFX hooked via SpecialSymbolEventRelay just work.
            EventBus.Publish(new SpecialSymbolMatchedEvent(effectType, origin, affected.ToArray()));
            EventBus.Publish(new ChainMatchedEvent(affected.Count, chainCount, affected.ToArray()));

            int scoreDelta = 0;
            foreach (var pos in affected)
            {
                var (_, delta) = ClearCell(pos, chainCount);
                scoreDelta += delta;
            }
            currentScore += scoreDelta;
            EventBus.Publish(new ScoreChangedEvent(currentScore, scoreDelta));

            yield return CollapseAndRefill();
            triggered++;
        }
    }

    [ContextMenu("Test: Trigger Random Gravity Bonus Now")]
    private void TestTriggerRandomGravityBonus()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Board] This only works in Play mode.");
            return;
        }
        if (eligibleRandomSpecialTypes == null || eligibleRandomSpecialTypes.Length == 0)
        {
            Debug.LogWarning("[Board] Assign at least one entry in Eligible Random Special Types first.");
            return;
        }
        StartCoroutine(ForceRandomGravityBonus());
    }

    private IEnumerator ForceRandomGravityBonus()
    {
        isBusy = true;
        yield return TryRandomSpecialOnGravity(chainCount: 1, forceOnce: true);

        // A forced bonus can itself create a fresh match (e.g. a color-clear leaving 3 in a
        // row after refill) - resolve that too, same as it would during normal play.
        var groups = MatchFinder.FindMatchGroups(grid, width, height);
        if (groups.Count > 0) yield return ResolveMatches(groups);

        isBusy = false;
        RestoreGameManagerRestingState();
    }

    private bool ShouldSpawnFrozenTileOnRefill(int x, int y)
    {
        if (frozenTileSpawnMode != FrozenTileSpawnMode.GenerateNewFrozenTiles &&
            frozenTileSpawnMode != FrozenTileSpawnMode.Both)
            return false;

        return RollFrozenTileChance(y);
    }

    /// <summary>
    /// Chance check shared by both frozen-tile paths (spawning new locked tiles on refill, and
    /// occasionally freezing tiles already sitting on the board). Row 0 is the bottom of the
    /// board (gravity pulls tiles down to y = 0 - see CollapseAndRefill), so rows within the
    /// bottom frozenTileBottomRowCount rows roll at the full configured lockSpawnChance. Rows
    /// above that band roll at a reduced chance instead of being excluded outright, so "bottom
    /// N rows" reads as a priority region rather than a hard cutoff. Set frozenTileBottomRowCount
    /// to 0 to disable the priority entirely (uniform chance across the whole board).
    /// </summary>
    private bool RollFrozenTileChance(int y)
    {
        float chance = lockSpawnChance;
        if (frozenTileBottomRowCount > 0 && y >= frozenTileBottomRowCount)
            chance *= frozenTileOutsideBottomRowsChanceMultiplier;

        return Random.value < chance;
    }

    /// <summary>
    /// FreezeExistingBottomRows / Both: after the board settles, give already-placed unlocked
    /// tiles a chance to freeze in place (as opposed to GenerateNewFrozenTiles, which only rolls
    /// for tiles being newly spawned during refill). Reuses the same bottom-row priority and
    /// weighted option pool as the refill path, and SetLock() already drives the lock overlay's
    /// SpriteRenderer, so the visual updates immediately with no further wiring needed.
    /// </summary>
    private void TryFreezeExistingSymbols()
    {
        if (frozenTileSpawnMode != FrozenTileSpawnMode.FreezeExistingBottomRows &&
            frozenTileSpawnMode != FrozenTileSpawnMode.Both)
            return;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || occ.IsLocked) continue;
                if (!RollFrozenTileChance(y)) continue;

                var option = PickWeightedLockOption();
                if (option == null) continue;

                occ.SetLock(option.layers, option.behavior, option.movesPerLayer);
            }
        }
    }

    private LockSpawnOption PickWeightedLockOption()
    {
        if (lockSpawnOptions == null || lockSpawnOptions.Length == 0) return null;

        float total = lockSpawnOptions.Sum(o => Mathf.Max(0f, o.weight));
        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        float cumulative = 0f;
        foreach (var o in lockSpawnOptions)
        {
            cumulative += Mathf.Max(0f, o.weight);
            if (roll <= cumulative) return o;
        }
        return lockSpawnOptions[lockSpawnOptions.Length - 1];
    }

    #endregion

    #region Collapse / Refill

    private IEnumerator CollapseAndRefill()
    {
        var sequence = DOTween.Sequence();
        bool anyMovement = false;

        for (int x = 0; x < width; x++)
        {
            int writeY = 0;
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y].IsEmpty) continue;

                var occ = grid[x, y].Occupant;

                if (occ.IsLocked && !lockedTilesFallWithGravity)
                {
                    // Locked/frozen tiles don't react to gravity - they stay exactly where
                    // they are and act as a floor for the segment above them. Nothing below
                    // this row will ever be reached by tiles above it while it holds.
                    writeY = y + 1;
                    continue;
                }

                if (writeY != y)
                {
                    grid[x, writeY].Occupant = occ;
                    grid[x, y].Occupant = null;
                    occ.GridPosition = new Vector2Int(x, writeY);
                    sequence.Join(occ.MoveTo(GridToWorld(x, writeY), fallDuration));
                    anyMovement = true;
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

                if (ShouldSpawnFrozenTileOnRefill(x, y))
                {
                    var option = PickWeightedLockOption();
                    if (option != null) instance.SetLock(option.layers, option.behavior, option.movesPerLayer);
                }

                grid[x, y].Occupant = instance;
                sequence.Join(instance.MoveTo(GridToWorld(x, y), fallDuration));
                anyMovement = true;
            }
        }

        if (anyMovement) yield return sequence.WaitForCompletion();

        TryFreezeExistingSymbols();
    }

    #endregion

    #region Locking / Freezing - Public API

    /// <summary>Explicitly locks/freezes a cell - for level-design or gameplay scripts (e.g. an ability that freezes a tile).</summary>
    public void LockCell(int x, int y, int layers, LockBehavior behavior, int movesPerLayer = 3)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
        {
            Debug.LogWarning($"[Board] LockCell({x},{y}) out of range.");
            return;
        }
        var occ = grid[x, y].Occupant;
        if (occ == null)
        {
            Debug.LogWarning($"[Board] LockCell({x},{y}) - no symbol occupies that cell.");
            return;
        }
        occ.SetLock(layers, behavior, movesPerLayer);
    }

    /// <summary>Immediately and fully removes any lock on a cell, regardless of remaining layers - for power-ups, currency-unlock, etc.</summary>
    public void UnlockCell(int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height) return;
        var occ = grid[x, y].Occupant;
        if (occ == null || !occ.IsLocked) return;

        occ.SetLock(0, LockBehavior.None);
        EventBus.Publish(new LockLayerRemovedEvent(new Vector2Int(x, y), 0, triggeredByMatch: false, fullyUnlocked: true));
    }

    public bool IsCellLocked(int x, int y) =>
        x >= 0 && x < width && y >= 0 && y < height &&
        grid[x, y].Occupant != null && grid[x, y].Occupant.IsLocked;

    [ContextMenu("Test: Lock A Random Tile (Temporary, 2 layers)")]
    private void TestLockRandomTileTemporary() => TestLockRandomTile(LockBehavior.Temporary);

    [ContextMenu("Test: Lock A Random Tile (Permanent)")]
    private void TestLockRandomTilePermanent() => TestLockRandomTile(LockBehavior.Permanent);

    private void TestLockRandomTile(LockBehavior behavior)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Board] This only works in Play mode.");
            return;
        }

        var candidates = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ != null && !occ.IsLocked) candidates.Add(new Vector2Int(x, y));
            }

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[Board] No unlocked tile available to lock.");
            return;
        }

        var pos = candidates[Random.Range(0, candidates.Count)];
        LockCell(pos.x, pos.y, 2, behavior, movesPerLayer: 3);
        Debug.Log($"[Board] Locked tile at {pos} ({behavior}, 2 layers) for testing.");
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
            moveCount = moveCount,
            currentHealth = playerHealth != null ? playerHealth.CurrentHealth : 0,
            maxHealth = playerHealth != null ? playerHealth.MaxHealth : 0,
            currentStageIndex = stageManager != null ? stageManager.CurrentStageIndex : -1,
            runSeed = stageManager != null ? stageManager.RunSeed : 0,
            collectGoalProgress = stageManager != null ? stageManager.GetCollectProgressSnapshot() : System.Array.Empty<int>(),
            cells = new CellSaveData[width * height]
        };

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var occ = grid[x, y].Occupant;
                data.cells[x * height + y] = occ == null
                    ? new CellSaveData { hasSymbol = false }
                    : new CellSaveData
                    {
                        hasSymbol = true,
                        type = occ.Type,
                        special = occ.Special,
                        lockLayers = occ.LockLayers,
                        lockBehavior = occ.LockBehaviorMode,
                        movesPerLayer = occ.MovesPerLayer,
                        movesUntilNextAutoUnlock = occ.MovesUntilNextAutoUnlock
                    };
            }

        return data;
    }

    private void LoadFromSave(BoardSaveData data)
    {
        ClearExistingSymbols();
        currentScore = data.score;
        moveCount = data.moveCount;
        selected = null;
        isBusy = false;
        isStageClearing = false;

        if (playerHealth != null)
        {
            playerHealth.ResetForNewRun();
            if (data.maxHealth > 0)
                playerHealth.SetHealth(data.currentHealth, data.maxHealth);
        }

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                var cellData = data.cells[x * height + y];
                if (!cellData.hasSymbol) continue;

                var symbol = SpawnSymbol(x, y, cellData.type, cellData.special);
                if (symbol != null && cellData.lockLayers > 0)
                    symbol.RestoreLockState(cellData.lockLayers, cellData.lockBehavior, cellData.movesPerLayer, cellData.movesUntilNextAutoUnlock);
            }

        if (stageManager != null && data.currentStageIndex >= 0)
            stageManager.LoadStageState(data.currentStageIndex, data.runSeed, data.collectGoalProgress);

        EventBus.Publish(new ScoreChangedEvent(currentScore, 0));
        EventBus.Publish(new PlayerMoveEvent(moveCount));
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
