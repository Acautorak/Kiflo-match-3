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

    [Header("Timing")]
    [SerializeField] private float swapDuration = 0.2f;
    [SerializeField] private float fallDuration = 0.25f;

    [Header("Free Spins")]
    [Tooltip("Delay before each successive reel/column starts spinning - 0 = every column starts simultaneously.")]
    [Min(0f)] [SerializeField] private float freeSpinsColumnStartStagger = 0.08f;
    [Tooltip("How long each reel visibly tumbles through quick random rerolls before its final landing roll. 0 = skip tumbling and land immediately (the original single-fall behavior).")]
    [Min(0f)] [SerializeField] private float freeSpinsReelSpinDuration = 0.6f;
    [Tooltip("How fast each tumble step cycles while a reel is spinning - lower = faster flicker. Only matters while Free Spins Reel Spin Duration > 0.")]
    [Min(0.01f)] [SerializeField] private float freeSpinsReelTumbleStepDuration = 0.08f;
    [Tooltip("How many forced explosions to chain together (via the Random Special Effect / Gravity Bonus mechanism) when a spin lands with no natural match. 1 = a single blast; higher values build a bigger forced combo before the normal cascade check.")]
    [Min(1)] [SerializeField] private int freeSpinsMinimumForcedChain = 1;
    [Tooltip("How many leftover symbols explode together per wave when a stage clears. Higher = faster overall cleanup.")]
    [Min(1)]
    [SerializeField] private int stageClearExplosionsPerWave = 4;
    [Tooltip("Delay between each wave of leftover-symbol explosions during stage clear cleanup (not per-symbol - per wave).")]
    [Min(0f)]
    [SerializeField] private float stageClearExplosionWaveDelay = 0.05f;

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
    [Tooltip("Hard cap on how many cascade steps a SINGLE move's cascade can run, regardless of " +
             "whether matches are still being found - guarantees the player always gets control " +
             "back even if something (e.g. a large same-colored patch from repeated color-convert " +
             "effects) keeps making refills likely to immediately rematch. See MatchResolver.")]
    [Min(1)]
    [SerializeField] private int maxCascadeSteps = 50;

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

    [Header("Burning Status Effect")]
    [Tooltip("How many accepted player moves an ignited tile burns for before it's auto-collected. See BurningSystem/PlayerRunStats.IgniteOnMatchChance.")]
    [Min(1)] [SerializeField] private int burnDurationMoves = 3;
    [Tooltip("Score awarded when a burnt-out tile is auto-collected - same idea as Tile Collector's Score Per Tile.")]
    [Min(0)] [SerializeField] private int scorePerBurntTile = 15;
    [Tooltip("Optional - a prefab that visibly flies from the matched tile to the tile it's about to ignite, catching fire only on arrival. Leave unassigned to ignite instantly with no travel visual.")]
    [SerializeField] private GameObject burningSparkPrefab;
    [Tooltip("Parent transform for spawned sparks. Falls back to this Board's own transform if left unassigned.")]
    [SerializeField] private Transform burningSparkParent;
    [Tooltip("How long (seconds) a spark takes to travel from the matched tile to its ignite target. Only matters if Burning Spark Prefab is assigned.")]
    [Min(0.05f)] [SerializeField] private float burningSparkFlightDuration = 0.35f;
    [Tooltip("How far (world units) the spark's flight path bows away from a straight line - 0 flies straight, higher arcs more. Bows to a random side each flight. Only matters if Burning Spark Prefab is assigned.")]
    [Min(0f)] [SerializeField] private float burningSparkArcHeight = 0.75f;

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

    [Header("Madness Symbols - Auto Spawn On Refill (optional)")]
    [Tooltip("If true, newly refilled tiles have a chance to spawn as a Madness Symbol, picked from Madness Spawn Options below.")]
    [SerializeField] private bool spawnMadnessOnRefill = false;
    [Range(0f, 1f)]
    [SerializeField] private float madnessSpawnChance = 0.03f;
    [Tooltip("Weighted pool of Madness Symbol definitions to draw from. Each entry's own `weight` field controls how often it's picked.")]
    [SerializeField] private MadnessSpawnOption[] madnessSpawnOptions;
    [Tooltip("If true, Madness Symbols act as wildcards during match detection - same as a Special " +
             "symbol - joining whatever color run they're touching instead of needing their own Type " +
             "to match. Purely a matching-rule toggle; spawning, effects, and visuals are unaffected.")]
    [SerializeField] private bool treatMadnessSymbolsAsWildcards = false;
    [Tooltip("Delay between each symbol's highlight pulse when a Madness effect converts/randomizes " +
             "several symbols at once (ConvertRandomSymbols / RandomizeSymbolColors) - lets the change " +
             "read as a one-by-one sequence instead of every symbol flashing simultaneously. 0 = simultaneous.")]
    [Min(0f)]
    [SerializeField] private float madnessConvertHighlightStagger = 0.06f;

    [Header("Madness Symbols - Systems (optional)")]
    [Tooltip("Tracks temporary board-wide/per-color rule modifiers Madness effects apply (e.g. an ignited color). Auto-found in Awake if left empty.")]
    [SerializeField] private MadnessBoardModifiers madnessBoardModifiers;
    [Tooltip("Run-scoped meter Madness effects can contribute to. Auto-found in Awake if left empty.")]
    [SerializeField] private MadnessMeter madnessMeter;

    [Header("System References (auto-found in Awake if left empty)")]
    [Tooltip("Optional explicit link. If left empty, Board finds it in the scene at Awake. " +
             "Assign this if you want Board to wait for StageManager to drive initialization " +
             "(see Start()) instead of populating/loading itself.")]
    [SerializeField] private StageManager stageManager;
    [SerializeField] private PlayerHealth playerHealth;
    [Tooltip("Optional. When assigned, its RandomSpecialChanceBonus/LockChanceReduction/ScoreMultiplier " +
             "modify every stage's baseline chances and score gains (see ApplyStageRules and the " +
             "score-delta application sites) - this is how powerups picked between stages take effect.")]
    [SerializeField] private PlayerRunStats playerRunStats;

    private GridModel grid;
    private SymbolSpawner symbolSpawner;
    private ScoreTracker scoreTracker;
    private LockingSystem lockingSystem;
    private BurningSystem burningSystem;
    private MadnessSystem madnessSystem;
    private GravityController gravityController;
    private SpecialEffectSystem specialEffectSystem;
    private BoardSaveIO saveIO;
    private SwapController swapController;
    private MatchResolver matchResolver;
    private FreeSpinsController freeSpinsController;
    private TileCollectorController tileCollectorController;
    private bool isBusy;
    private bool isStageClearing;
    private GameManager gameManager;
    private int moveCount;
    private bool stageClearGraceActive;
    private int stageClearGraceMovesRemaining;
    private float stageClearGraceRandomSpecialChance;
    private bool stageClearPendingAfterResolution;
    private bool hasLoadedSavedState;

    public int MoveCount => moveCount;
    public int CurrentScore => scoreTracker.CurrentScore;
    public bool IsGameBusy => isBusy || isStageClearing;
    public bool HasLoadedSavedState => hasLoadedSavedState;
    public int Width => width;
    public int Height => height;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[Board] Duplicate Board instance detected on '{name}' - " +
                              $"'{Instance.name}' already holds Instance. Destroying this one.");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameManager = FindAnyObjectByType<GameManager>();
        if (stageManager == null) stageManager = FindAnyObjectByType<StageManager>();
        if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (madnessBoardModifiers == null) madnessBoardModifiers = FindAnyObjectByType<MadnessBoardModifiers>();
        if (madnessMeter == null) madnessMeter = FindAnyObjectByType<MadnessMeter>();
        Debug.Log($"[Board] Awake - grid {width}x{height}");
        grid = new GridModel(width, height);
        symbolSpawner = new SymbolSpawner(grid, symbolPrefab, symbolPrefabs, symbolParent);
        scoreTracker = new ScoreTracker(playerRunStats);
        lockingSystem = new LockingSystem(grid)
        {
            DestroySymbolWhenUnlocked = destroySymbolWhenUnlocked,
            LockedTilesFallWithGravity = lockedTilesFallWithGravity,
            AllowSwappingLockedTiles = allowSwappingLockedTiles,
            ScorePerLockHit = scorePerLockHit,
            InitialLockPlacements = initialLockPlacements,
            SpawnLocksOnRefill = spawnLocksOnRefill,
            LockSpawnChance = lockSpawnChance,
            LockSpawnOptions = lockSpawnOptions,
            FrozenTileSpawnMode = frozenTileSpawnMode,
            FrozenTileBottomRowCount = frozenTileBottomRowCount,
            FrozenTileOutsideBottomRowsChanceMultiplier = frozenTileOutsideBottomRowsChanceMultiplier
        };
        madnessSystem = new MadnessSystem(grid, this, symbolSpawner, playerHealth, playerRunStats, madnessBoardModifiers, madnessMeter, gameManager, this)
        {
            SpawnMadnessOnRefill = spawnMadnessOnRefill,
            MadnessSpawnChance = madnessSpawnChance,
            MadnessSpawnOptions = madnessSpawnOptions,
            TreatMadnessSymbolsAsWildcards = treatMadnessSymbolsAsWildcards,
            MadnessConvertHighlightStagger = madnessConvertHighlightStagger
        };
        gravityController = new GravityController(grid, symbolSpawner, lockingSystem, madnessSystem, fallDuration, GridToWorld);
        specialEffectSystem = new SpecialEffectSystem(grid);
        burningSystem = new BurningSystem(grid, symbolSpawner, this, playerRunStats)
        {
            BurnDurationMoves = burnDurationMoves,
            ScorePerBurntTile = scorePerBurntTile,
            SparkPrefab = burningSparkPrefab,
            SparkParent = burningSparkParent,
            SparkFlightDuration = burningSparkFlightDuration,
            SparkArcHeight = burningSparkArcHeight
        };
        saveIO = new BoardSaveIO(grid, symbolSpawner, scoreTracker, playerHealth, playerRunStats, stageManager, GridToWorld);
        swapController = new SwapController(grid, GridToWorld, swapDuration,
            () => IsGameBusy,
            () => gameManager == null || gameManager.AllowsPlayerInput,
            () => lockingSystem.AllowSwappingLockedTiles,
            (a, b) => StartCoroutine(TrySwap(a, b)));

        matchResolver = new MatchResolver(grid, gravityController, specialEffectSystem, madnessSystem, lockingSystem, burningSystem,
            scoreTracker, symbolSpawner, playerHealth, playerRunStats, madnessBoardModifiers, gameManager, GridToWorld,
            ShouldSkipRefillGeneration, () => isStageClearing, () => stageClearGraceActive,
            () => stageClearGraceMovesRemaining, () => stageClearGraceRandomSpecialChance)
        {
            IntersectionsCreateBombs = intersectionsCreateBombs,
            EligibleRandomSpecialTypes = eligibleRandomSpecialTypes,
            MaxConsecutiveRandomTriggers = maxConsecutiveRandomTriggers,
            RandomSpecialTriggerChance = randomSpecialTriggerChance,
            EnableRandomSpecialOnGravity = enableRandomSpecialOnGravity,
            MaxCascadeSteps = maxCascadeSteps
        };

        freeSpinsController = new FreeSpinsController(grid, symbolSpawner, matchResolver, gameManager, this, fallDuration, GridToWorld,
            freeSpinsColumnStartStagger, freeSpinsReelSpinDuration, freeSpinsReelTumbleStepDuration, freeSpinsMinimumForcedChain);

        tileCollectorController = new TileCollectorController(grid, symbolSpawner, gravityController, this, this, matchResolver, madnessSystem);
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
            var resumeState = saveIO.LoadFromSave(saved);
            moveCount = resumeState.MoveCount;
            stageClearGraceActive = resumeState.GraceActive;
            stageClearGraceMovesRemaining = resumeState.GraceMovesRemaining;
            stageClearGraceRandomSpecialChance = resumeState.GraceRandomSpecialChance;
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
        var groups = MatchFinder.FindMatchGroups(grid.RawGrid, width, height, madnessSystem.TreatMadnessSymbolsAsWildcards);
        if (groups.Count == 0)
        {
            RestoreGameManagerRestingState();
            yield break;
        }

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

    /// <summary>True when the board is in a fully "settled" state safe to persist - not mid
    /// stage-clear cleanup, not waiting on a pending clear, not in the extra-moves grace period.
    /// See BoardSaveIO for why this guard exists.</summary>
    private bool IsSafeToSave() => !isStageClearing && !stageClearPendingAfterResolution;

    /// <summary>
    /// Sets the grid's active-cell mask without touching occupants or triggering a repopulate -
    /// for the save-resume path, where StageManager needs the mask in place BEFORE
    /// InitializeBoard()/BoardSaveIO.LoadFromSave spawns symbols back in, so gravity treats
    /// holes correctly from the first move onward. (ResetForStage is the fresh-stage equivalent;
    /// it clears+repopulates too, which isn't wanted here since LoadFromSave does that itself
    /// from the save data.) Null/empty shape resets to a full rectangle.
    /// </summary>
    public void ApplyBoardShape(BoardShapeData shape) =>
        grid.ApplyShape(shape != null && !shape.IsEmpty ? shape.ToMask2D() : null);

    #region Setup

    private void PopulateBoard(InitialLockPlacement[] overrideLockPlacements = null)
    {
        int spawned = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (!grid.IsActive(x, y)) continue; // hole - part of the stage's shape, stays permanently empty

                SymbolType type;
                do { type = symbolSpawner.RandomType(); }
                while (symbolSpawner.CreatesImmediateMatch(x, y, type));

                if (symbolSpawner.Spawn(x, y, type, SpecialType.None, GridToWorld(x, y)) != null) spawned++;
            }
        }
        Debug.Log($"[Board] PopulateBoard finished - spawned {spawned}/{grid.ActiveCellCount} active cells (board {width}x{height})");

        lockingSystem.ApplyInitialLockPlacements(overrideLockPlacements);
    }

    public void ClearBoard()
    {
        ClearExistingSymbols();
        scoreTracker.Reset();
        moveCount = 0;
        swapController.ClearSelection();
        isBusy = false;
        isStageClearing = false;
        ResetStageClearGraceState();
        madnessBoardModifiers?.Clear();
    }

    /// <param name="proceduralShape">
    /// When provided (typically ProceduralStageGenerator.GenerateShape's output), this overrides
    /// stage.shape - same relationship proceduralLockPlacements has with stage.initialLockPlacements.
    /// If both are null/empty, the board is a full rectangle - the existing behavior for every
    /// stage that predates shape support.
    /// </param>
    public void ResetForStage(StageDefinition stage, InitialLockPlacement[] proceduralLockPlacements = null,
        BoardShapeData proceduralShape = null)
    {
        ClearBoard();
        ApplyStageRules(stage);

        var shapeSource = proceduralShape != null && !proceduralShape.IsEmpty
            ? proceduralShape
            : stage != null ? stage.shape : null;
        grid.ApplyShape(shapeSource != null && !shapeSource.IsEmpty ? shapeSource.ToMask2D() : null);

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
        matchResolver.EnableRandomSpecialOnGravity = stage.enableRandomSpecialOnGravity;
        lockingSystem.SpawnLocksOnRefill = stage.spawnLocksOnRefill;
        lockingSystem.DestroySymbolWhenUnlocked = stage.destroySymbolWhenUnlocked;
        lockingSystem.LockedTilesFallWithGravity = stage.lockedTilesFallWithGravity;
        gravityController.HolesBlockGravity = stage.holesBlockGravity;
        lockingSystem.FrozenTileSpawnMode = stage.frozenTileSpawnMode;
        lockingSystem.FrozenTileBottomRowCount = stage.frozenTileBottomRowCount;
        matchResolver.MaxConsecutiveRandomTriggers = stage.maxConsecutiveRandomTriggers;

        // Player-run modifiers (from powerups picked between stages) shift the stage's baseline
        // chances up/down rather than replacing them - see PlayerRunStats for what feeds these.
        float specialBonus = playerRunStats != null ? playerRunStats.RandomSpecialChanceBonus : 0f;
        float lockReduction = playerRunStats != null ? playerRunStats.LockChanceReduction : 0f;
        matchResolver.RandomSpecialTriggerChance = Mathf.Clamp01(stage.wonkyChance + specialBonus);
        lockingSystem.LockSpawnChance = Mathf.Clamp01(stage.lockSpawnChance - lockReduction);
    }

    /// <summary>
    /// Adds bonus score outside the normal per-cell clear calculation - e.g. a Madness effect's
    /// payout (see MadnessScoreGrowthEffect). Goes through the same score multiplier and event as
    /// every other scoring path, so it stacks correctly with powerups.
    /// </summary>
    public void AddBonusScore(int amount)
    {
        scoreTracker.AddScore(amount); 
    }

    public List<Vector2Int> ConvertRandomSymbols(SymbolType? fromColor, SymbolType toColor, int count, Vector2Int? excludePosition = null) =>
        madnessSystem.ConvertRandomSymbols(fromColor, toColor, count, excludePosition);

    /// <summary>Guaranteed-ignites every eligible tile around `center` (see BurningSystem.
    /// IgniteAllNeighbors) - for a Madness Symbol effect that lights everything around it ablaze
    /// on death (see MadnessIgniteAllNeighborsEffect). No-op if BurningSystem hasn't been
    /// constructed yet (shouldn't happen once Board.Awake finishes).</summary>
    public void IgniteAllNeighbors(Vector2Int center) => burningSystem?.IgniteAllNeighbors(center);

    public List<Vector2Int> RandomizeSymbolColors(int count, Vector2Int? excludePosition = null, bool guaranteeColorChange = true) =>
        madnessSystem.RandomizeSymbolColors(count, excludePosition, guaranteeColorChange);

    /// <summary>
    /// Runs exactly one Free Spins reel spin (reroll every column, land, guarantee a match, resolve
    /// it through the normal MatchResolver pipeline). Entry point for FreeSpinsManager, which owns
    /// the SPIN button and spin-count bookkeeping and is expected to only call this while
    /// GameManager is in FeatureMode - Board doesn't check that itself here, same as
    /// ConvertRandomSymbols/RandomizeSymbolColors above trusting their own callers.
    /// </summary>
    public IEnumerator PlayFreeSpin() => freeSpinsController.SpinOnce();

    /// <summary>Lets FreeSpinsManager override FreeSpinsController.GuaranteedWonkyChainThreshold
    /// (see that property's doc) from its own Inspector field at runtime, since FreeSpinsManager
    /// never touches freeSpinsController directly - it only calls PlayFreeSpin(). Setting this
    /// before a spin starts (FreeSpinsManager.StartFeatureMode does this) is enough; SpinOnce reads
    /// the property fresh on every call, so it also takes effect immediately if changed mid-mode.
    /// No-op if the board hasn't constructed freeSpinsController yet (e.g. called before Awake).</summary>
    public int FreeSpinsGuaranteedWonkyChainThreshold
    {
        get => freeSpinsController != null ? freeSpinsController.GuaranteedWonkyChainThreshold : 0;
        set { if (freeSpinsController != null) freeSpinsController.GuaranteedWonkyChainThreshold = value; }
    }

    /// <summary>Forwards to ScoreTracker.EventMultiplier (see its doc) - lets a short-lived
    /// board-wide event like Disco Dance Disco boost every AddScore call for its duration without
    /// touching PlayerRunStats.ScoreMultiplier. 1 = no effect; DiscoDanceDiscoManager is
    /// responsible for setting this back to 1 once its event ends.</summary>
    public float ScoreEventMultiplier
    {
        get => scoreTracker != null ? scoreTracker.EventMultiplier : 1f;
        set { if (scoreTracker != null) scoreTracker.EventMultiplier = Mathf.Max(0f, value); }
    }

    /// <summary>Starts every currently-occupied cell's Symbol.PlayDanceLoop (see Disco Dance
    /// Disco / DiscoDanceDiscoManager) - purely cosmetic, doesn't touch grid state. Symbols that
    /// spawn AFTER this call (e.g. a refill mid-event) don't automatically join the dance - only
    /// what's on the board at the moment this is called.</summary>
    public void StartDiscoDance(float cycleDuration, float punchScaleAmount, float punchRotationDegrees)
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y].Occupant?.PlayDanceLoop(cycleDuration, punchScaleAmount, punchRotationDegrees);

        symbolSpawner?.SetDanceForNewSpawns(true, cycleDuration, punchScaleAmount, punchRotationDegrees);
    }

    /// <summary>Stops every currently-occupied cell's dance loop (see StartDiscoDance) and snaps
    /// scale/rotation back to resting, and stops applying the dance to newly-spawned symbols too.</summary>
    public void StopDiscoDance()
    {
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                grid[x, y].Occupant?.StopDance();

        symbolSpawner?.SetDanceForNewSpawns(false);
    }

    /// <summary>Entry point for TileCollectorManager - runs one timed Tile Collector session on
    /// the real board grid. See TileCollectorController.</summary>
    public IEnumerator PlayTileCollectorSession(float duration, int scorePerTile) =>
        tileCollectorController.RunSession(duration, scorePerTile);

    private void ClearExistingSymbols()
    {
        grid.ClearAll(occ => Destroy(occ.gameObject));
    }

    private Vector3 GridToWorld(int x, int y) =>
        new Vector3(origin.x + x * cellSize, origin.y + y * cellSize, 0f);

    /// <summary>Public accessor for grid-to-world conversion (used by UI/VFX systems like
    /// SpecialSymbolEventRelay's combo popup, which need it but sit outside Board itself).</summary>
    public Vector3 GridToWorldPosition(int x, int y) => GridToWorld(x, y);

    #endregion

    #region Input / Swapping

    /// <summary>Optional - lazily auto-found in TrySwap on first use (rather than in Awake) to
    /// avoid needing to touch Board's existing Awake wiring. Fine to leave unassigned if this
    /// scene doesn't use the Grace Move Chance system at all.</summary>
    [SerializeField] private GraceMoveController graceMoveController;

    private IEnumerator TrySwap(Symbol a, Symbol b)
    {
        isBusy = true;
        gameManager?.SetState(GameManager.GameplayState.Playing);
        yield return swapController.SwapRoutine(a, b);

        // Peek (not Consume) here, before ANYTHING damage-capable runs - the no-match penalty a
        // few lines down needs to already be suppressed if this turns out to be a Grace Move, but
        // whether this swap ultimately counts as a real move isn't known until swapIsValid below,
        // so the actual Consume() call is deferred until then (see the comment there).
        if (graceMoveController == null) graceMoveController = FindAnyObjectByType<GraceMoveController>();
        bool isGraceMove = graceMoveController != null && graceMoveController.PeekArmed();
        if (isGraceMove) playerHealth?.SetDamageImmune(true);

        var matchGroups = MatchFinder.FindMatchGroups(grid.RawGrid, width, height, madnessSystem.TreatMadnessSymbolsAsWildcards);
        Debug.Log($"[Board] Post-swap scan: {matchGroups.Count} group(s) - " +
                   string.Join(" | ", matchGroups.Select(g =>
                       $"cells={g.Cells.Count} lines={g.Lines.Count} intersection={g.IsIntersection} longestRun={g.LongestRun}")));

        if (matchGroups.Count == 0)
            ApplyHealthPenalty(); // no-ops on its own via PlayerHealth.IsDamageImmune if isGraceMove

        bool swapIsValid = matchGroups.Count > 0 || allowNonMatchingSwaps;
        if (!swapIsValid)
        {
            yield return swapController.SwapRoutine(a, b); // revert - no match, invalid move
            isBusy = false;
            // Not a real move - turn immunity back off without ever calling Consume(), so a still-
            // armed Grace Move stays armed for the player's actual next attempt instead of being
            // wasted on a swap that got reverted.
            if (isGraceMove) playerHealth?.SetDamageImmune(false);
            RestoreGameManagerRestingState();
            yield break;
        }

        // Now that this swap is confirmed to count as a real move, actually consume the armed
        // Grace Move (fires GraceMoveConsumedEvent so UI fades its highlight) and tell
        // RegisterPlayerMove so StageManager can exclude it from a MoveCount goal.
        if (isGraceMove) graceMoveController.Consume();

        RegisterPlayerMove(isGraceMove);
        ConsumeStageClearGraceMove();
        yield return matchResolver.TryRandomSpecialOnGraceMove();

        // This swap counts as an accepted player move - every Temporary lock on the board
        // melts one layer, and every burning tile's countdown ticks down, regardless of whether
        // this move itself was actually matched.
        bool locksDestroyedThisMove = lockingSystem.MeltAllTemporaryLocks();
        bool tilesBurntOutThisMove = burningSystem.TickAllBurningTiles();

        if (locksDestroyedThisMove || tilesBurntOutThisMove)
        {
            if (ShouldSkipRefillGeneration() || stageClearPendingAfterResolution)
            {
                matchGroups = MatchFinder.FindMatchGroups(grid.RawGrid, width, height, madnessSystem.TreatMadnessSymbolsAsWildcards); // melting/burning can reveal new matches
            }
            else
            {
                yield return gravityController.Collapse();
                matchGroups = MatchFinder.FindMatchGroups(grid.RawGrid, width, height, madnessSystem.TreatMadnessSymbolsAsWildcards); // melting/burning can reveal new matches
            }
        }

        if (matchGroups.Count > 0)
            yield return ResolveMatches(matchGroups);

        if (stageClearPendingAfterResolution && !isStageClearing)
        {
            stageClearPendingAfterResolution = false;
            stageManager?.FinalizeStageClear();
        }

        madnessSystem.TickSurvival();

        // TickSurvival can itself create new matches (e.g. an onSurvivedMoveEffect that repaints
        // several cells to the same color via ConvertRandomSymbols/RandomizeSymbolColors) - unlike
        // the swap's own matches or the lock-melt case above, nothing else would ever rescan for
        // these, so they'd otherwise just sit there matched-but-undetected until the player
        // happened to swap something adjacent to them.
        var postSurvivalMatches = MatchFinder.FindMatchGroups(grid.RawGrid, width, height, madnessSystem.TreatMadnessSymbolsAsWildcards);
        if (postSurvivalMatches.Count > 0)
            yield return ResolveMatches(postSurvivalMatches);

        // Immunity covered everything above (the no-match penalty, match resolution, TickSurvival)
        // since it was switched on right at the top - turn it off now that the move is fully done.
        if (isGraceMove) playerHealth?.SetDamageImmune(false);

        // Roll for whether the NEXT move becomes a Grace Move - only reached for a real, counted
        // move (the invalid/reverted path above returns early before this), and happens whether
        // or not THIS move itself was a Grace Move, so consecutive Grace Moves can still chain on
        // a lucky roll.
        graceMoveController?.RollForNextMove();

        isBusy = false;
        RestoreGameManagerRestingState();
    }

    private void RegisterPlayerMove(bool wasGraceMove = false)
    {
        // A Grace Move no longer bumps the running total at all - it's a genuinely free move,
        // not just excluded from a MoveCount stage goal. moveCount stays exactly where it was,
        // so HUD/save data don't tick either; the event still fires (with the unchanged
        // moveCount) so other listeners - MadnessFeatureTrigger's guaranteed-fallback pacing,
        // StageManager's now-redundant-but-harmless WasGraceMove check - still see that a move
        // happened.
        if (!wasGraceMove) moveCount++;
        EventBus.Publish(new PlayerMoveEvent(moveCount, wasGraceMove));
    }

    private void ApplyHealthPenalty(int amount = 1)
    {
        if (playerHealth != null)
            playerHealth.TakeDamage(amount);
    }

    public void SelectSymbol(Symbol symbol)
    {
        if (tileCollectorController != null && tileCollectorController.IsActive)
        {
            tileCollectorController.TryCollect(symbol);
            return;
        }
        swapController.SelectSymbol(symbol);
    }

    public void SwipeSymbol(Symbol symbol, Vector2Int direction)
    {
        // Any gesture on a tile just collects it during Tile Collector - a slightly-dragged tap
        // shouldn't fail to register just because it crossed the swipe threshold.
        if (tileCollectorController != null && tileCollectorController.IsActive)
        {
            tileCollectorController.TryCollect(symbol);
            return;
        }
        swapController.SwipeSymbol(symbol, direction);
    }
    #endregion

    #region Matching / Cascades

    private IEnumerator ResolveMatches(List<MatchGroup> initialGroups)
    {
        yield return matchResolver.Resolve(initialGroups);
        saveIO.TrySave(saveIO.BuildSaveData(moveCount, stageClearGraceActive, stageClearGraceMovesRemaining,
            stageClearGraceRandomSpecialChance, stageManager != null && stageManager.IsAwaitingPowerupSelection), IsSafeToSave());
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

            // Was two independent DOScale calls on the same transform (fighting each other
            // instead of actually popping) - Append them into a real grow-then-shrink sequence.
            DOTween.Sequence()
                .Append(symbol.transform.DOScale(0.15f, 0.1f).SetEase(Ease.InBack))
                .Append(symbol.transform.DOScale(0f, 0.1f).SetEase(Ease.InBack))
                .OnComplete(() => Destroy(symbol.gameObject));
            //Destroy(symbol.gameObject, 0.2f);

            // Explode in waves (several symbols per pause) rather than one-by-one, so a full
            // board doesn't take remaining.Count * delay seconds to finish clearing.
            bool isLastInBoard = i == remaining.Count - 1;
            bool isWaveBoundary = (i + 1) % stageClearExplosionsPerWave == 0;
            if (!isLastInBoard && isWaveBoundary)
                yield return new WaitForSeconds(stageClearExplosionWaveDelay);
        }

        isBusy = false;
        isStageClearing = false;
        onComplete?.Invoke();
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
        yield return matchResolver.TryRandomSpecialOnGravity(chainCount: 1, forceOnce: true);

        // A forced bonus can itself create a fresh match (e.g. a color-clear leaving 3 in a
        // row after refill) - resolve that too, same as it would during normal play.
        var groups = MatchFinder.FindMatchGroups(grid.RawGrid, width, height, madnessSystem.TreatMadnessSymbolsAsWildcards);
        if (groups.Count > 0) yield return ResolveMatches(groups);

        isBusy = false;
        RestoreGameManagerRestingState();
    }

    #endregion

    #region Locking / Freezing - Public API

    /// <summary>Explicitly locks/freezes a cell - for level-design or gameplay scripts (e.g. an ability that freezes a tile).</summary>
    public void LockCell(int x, int y, int layers, LockBehavior behavior, int movesPerLayer = 3) =>
        lockingSystem.LockCell(x, y, layers, behavior, movesPerLayer);

    /// <summary>Immediately and fully removes any lock on a cell, regardless of remaining layers - for power-ups, currency-unlock, etc.</summary>
    public void UnlockCell(int x, int y) =>
        lockingSystem.UnlockCell(x, y);
    
    public bool IsCellLocked(int x, int y) =>
        lockingSystem.IsCellLocked(x, y);

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
        var pos = lockingSystem.LockRandomTileForTesting(behavior);
        if (pos.HasValue)
            Debug.Log($"[Board] Locked tile at {pos.Value} ({behavior}, 2 layers) for testing.");
        else
            Debug.LogWarning("[Board] No unlocked tile available to lock.");
    }

    #endregion

    #region Save / Load

    /// <summary>Call this from GameManager on pause/quit for a simple, reliable save point.</summary>
    public void SaveNow()
    {
        saveIO.TrySave(saveIO.BuildSaveData(moveCount, stageClearGraceActive, stageClearGraceMovesRemaining, stageClearGraceRandomSpecialChance, stageManager != null && stageManager.IsAwaitingPowerupSelection), IsSafeToSave());
    }

    [ContextMenu("Delete Save And Log Path")]
    private void DeleteSaveFromEditor()
    {
        Debug.Log($"[Board] Save file path: {Application.persistentDataPath}");
        SaveSystem.DeleteSave();
        Debug.Log("[Board] Save deleted. Press Play again for a fresh board.");
    }

    #endregion

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
