using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StageManager : MonoBehaviour
{
    [Header("Stage Setup")]
    [SerializeField] private Board board;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private GameManager gameManager;
    [SerializeField] private bool autoStartFirstStage = true;

    [Header("Procedural Generation")]
    [Tooltip("Designer-tunable asset that drives generation - see StageGenerationConfig for every " +
             "knob (difficulty curve, goal ranges, lock/freeze unlock depths, weighted pools, etc). " +
             "Required; stages cannot be generated without one.")]
    [SerializeField] private StageGenerationConfig generationConfig;
    [Tooltip("Seed for this run's procedural generation. Leave at 0 to have StartNewRun() pick a " +
             "random seed automatically. The active seed is saved with the game so a restored run " +
             "regenerates byte-identical stages.")]
    [SerializeField] private int runSeed = 0;

    private readonly List<StageDefinition> generatedStages = new List<StageDefinition>();
    private readonly List<InitialLockPlacement[]> generatedLockPlacements = new List<InitialLockPlacement[]>();

    private int currentStageIndex = -1;
    private StageDefinition currentStage;
    private bool isTransitioning;
    private bool isStageCleared;
    private bool isStageClearPending;
    private int remainingGraceMoves;
    private int[] collectProgressByTarget = System.Array.Empty<int>();

    public int CurrentStageIndex => currentStageIndex;
    public int RunSeed => runSeed;

    /// <summary>The full generated definition for the stage currently in play - read-only from the outside.</summary>
    public StageDefinition CurrentStage => currentStage;
    public string CurrentStageName => currentStage != null ? currentStage.name : null;
    public string CurrentStageDescription => currentStage != null ? currentStage.description : null;
    public StageGoalType CurrentGoalType => currentStage != null ? currentStage.goalType : StageGoalType.None;
    public int CurrentGoalValue => currentStage != null ? currentStage.goalValue : 0;

    /// <summary>One entry per Collect target: which symbol type, how many cleared so far, and the target count.</summary>
    public readonly struct CollectGoalProgressEntry
    {
        public readonly SymbolType SymbolType;
        public readonly int Current;
        public readonly int Target;
        public CollectGoalProgressEntry(SymbolType symbolType, int current, int target)
        {
            SymbolType = symbolType;
            Current = current;
            Target = target;
        }
    }

    /// <summary>Empty when the current stage's goal isn't Collect.</summary>
    public IReadOnlyList<CollectGoalProgressEntry> CurrentCollectProgress => BuildCollectProgressEntries();

    public int CurrentGracePeriodMoves => currentStage != null ? currentStage.gracePeriodMoves : 0;
    public float CurrentGracePeriodRandomSpecialChance => currentStage != null ? currentStage.gracePeriodRandomSpecialChance : 0f;
    public bool CurrentAllowsNonMatchingSwaps => currentStage != null && currentStage.allowNonMatchingSwaps;
    public bool CurrentEnablesRandomSpecialOnGravity => currentStage != null && currentStage.enableRandomSpecialOnGravity;
    public float CurrentRandomSpecialChance => currentStage != null ? currentStage.randomSpecialChance : 0f;
    public bool CurrentSpawnsLocksOnRefill => currentStage != null && currentStage.spawnLocksOnRefill;
    public float CurrentLockSpawnChance => currentStage != null ? currentStage.lockSpawnChance : 0f;
    public FrozenTileSpawnMode CurrentFrozenTileSpawnMode => currentStage != null ? currentStage.frozenTileSpawnMode : FrozenTileSpawnMode.None;
    public int CurrentFrozenTileBottomRowCount => currentStage != null ? currentStage.frozenTileBottomRowCount : 0;
    public bool IsStageCleared => isStageCleared;
    public bool IsStageClearPending => isStageClearPending;
    public int RemainingGraceMoves => remainingGraceMoves;

    private List<CollectGoalProgressEntry> BuildCollectProgressEntries()
    {
        var list = new List<CollectGoalProgressEntry>();
        var targets = currentStage?.collectTargets;
        if (targets == null) return list;

        for (int i = 0; i < targets.Length; i++)
        {
            int current = (collectProgressByTarget != null && i < collectProgressByTarget.Length)
                ? collectProgressByTarget[i] : 0;
            list.Add(new CollectGoalProgressEntry(targets[i].symbolType, current, targets[i].count));
        }
        return list;
    }

    /// <summary>Raw per-target progress, for SaveSystem to persist. Null-safe: never returns null.</summary>
    public int[] GetCollectProgressSnapshot() =>
        collectProgressByTarget != null ? (int[])collectProgressByTarget.Clone() : System.Array.Empty<int>();

    private void InitializeCollectProgress(int[] savedProgress = null)
    {
        int count = currentStage?.collectTargets?.Length ?? 0;
        collectProgressByTarget = new int[count];
        if (savedProgress == null) return;

        for (int i = 0; i < count && i < savedProgress.Length; i++)
            collectProgressByTarget[i] = savedProgress[i];
    }

    private void OnEnable()
    {
        EventBus.Subscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Subscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Subscribe<SymbolMatchedEvent>(HandleSymbolMatched);
        EventBus.Subscribe<GameOverEvent>(HandleGameOver);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Unsubscribe<SymbolMatchedEvent>(HandleSymbolMatched);
        EventBus.Unsubscribe<GameOverEvent>(HandleGameOver);
    }

    private void Start()
    {
        if (!autoStartFirstStage) return;

        var saved = SaveSystem.Load();
        if (saved != null && saved.currentStageIndex >= 0)
        {
            runSeed = saved.runSeed;

            currentStageIndex = saved.currentStageIndex;
            currentStage = GetStage(currentStageIndex);
            isTransitioning = false;
            isStageCleared = false;
            InitializeCollectProgress(saved.collectGoalProgress);
            EventBus.Publish(new StageStartedEvent(currentStageIndex, currentStage));
            Debug.Log($"[StageManager] Restored saved stage {currentStageIndex + 1}: {currentStage?.name}");

            // Board.Start() defers to us when a StageManager is present (see Board.cs) - this
            // is the point where we tell it to actually load its saved grid state.
            if (board != null) board.InitializeBoard();
            return;
        }

        if (runSeed == 0)
            runSeed = GenerateRandomSeed();

        // No valid save - StartStage(0) below calls board.ResetForStage(), which populates
        // a fresh board itself, so Board never needs InitializeBoard() in this path.
        StartStage(0);
    }

    public bool HasStagesConfigured() => generationConfig != null;

    /// <summary>Returns the StageDefinition for a given depth, generating (and caching) it first if needed.</summary>
    private StageDefinition GetStage(int index)
    {
        EnsureGenerated(index);
        return (index >= 0 && index < generatedStages.Count) ? generatedStages[index] : null;
    }

    private InitialLockPlacement[] GetLockPlacements(int index)
    {
        EnsureGenerated(index);
        return (index >= 0 && index < generatedLockPlacements.Count) ? generatedLockPlacements[index] : null;
    }

    /// <summary>
    /// Generates every stage up to and including `index` that isn't cached yet. Stages are
    /// generated once and cached (not regenerated each visit) so re-entering an earlier stage
    /// index within the same session doesn't reshuffle it.
    /// </summary>
    private void EnsureGenerated(int index)
    {
        if (generationConfig == null || index < 0) return;

        int boardWidth = board != null ? board.Width : 8;
        int boardHeight = board != null ? board.Height : 8;

        while (generatedStages.Count <= index)
        {
            int depth = generatedStages.Count;
            generatedStages.Add(ProceduralStageGenerator.GenerateStage(depth, runSeed, generationConfig));
            generatedLockPlacements.Add(ProceduralStageGenerator.GenerateInitialLockPlacements(
                depth, runSeed, generationConfig, boardWidth, boardHeight));
        }
    }

    private int GenerateRandomSeed()
    {
        int seed = System.Guid.NewGuid().GetHashCode();
        return seed == 0 ? 1 : seed;
    }

    public void LoadStageState(int index, int savedRunSeed = 0, int[] savedCollectProgress = null)
    {
        runSeed = savedRunSeed;
        currentStageIndex = index;
        currentStage = GetStage(index);
        isTransitioning = false;
        isStageCleared = false;
        InitializeCollectProgress(savedCollectProgress);
    }

    public void StartStage(int index)
    {
        if (!HasStagesConfigured())
        {
            Debug.LogWarning("[StageManager] No Stage Generation Config assigned.");
            return;
        }

        if (index < 0)
        {
            Debug.LogWarning($"[StageManager] Stage index {index} is invalid.");
            return;
        }

        currentStageIndex = index;
        currentStage = GetStage(index);
        isTransitioning = false;
        isStageCleared = false;
        isStageClearPending = false;
        remainingGraceMoves = 0;
        InitializeCollectProgress();

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.Idle);

        if (board != null)
            board.ResetForStage(currentStage, GetLockPlacements(index));

        EventBus.Publish(new StageStartedEvent(currentStageIndex, currentStage));
        Debug.Log($"[StageManager] Started stage {currentStageIndex + 1}: {currentStage?.name}");
    }

    public void AdvanceToNextStage()
    {
        if (!HasStagesConfigured()) return;
        if (currentStageIndex < 0) return;
        if (!isStageCleared) return;

        if (board != null)
            board.ClearBoard();

        StartStage(currentStageIndex + 1);
    }

    public void StartNewRun()
    {
        if (playerHealth != null)
        {
            playerHealth.ResetForNewRun();
            EventBus.Publish(new HealthChangedEvent(playerHealth.CurrentHealth, playerHealth.MaxHealth));
        }

        runSeed = GenerateRandomSeed();
        generatedStages.Clear();
        generatedLockPlacements.Clear();

        if (board != null)
            board.ClearBoard();

        StartStage(0);
    }

    public void FinalizeStageClear()
    {
        if (currentStage == null || !isStageClearPending || isStageCleared) return;

        isStageCleared = true;
        isStageClearPending = false;

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.StageClearing);

        if (board != null)
        {
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} clearing after grace period.");
            board.BeginStageClearCleanup(() =>
            {
                EventBus.Publish(new StageCompletedEvent(currentStageIndex, board.CurrentScore));
            });
        }
        else
        {
            EventBus.Publish(new StageCompletedEvent(currentStageIndex, 0));
        }
    }

    private void HandleScoreChanged(ScoreChangedEvent evt)
    {
        if (currentStage == null) return;
        if (currentStage.goalType != StageGoalType.Score) return;
        if (evt.NewScore >= currentStage.goalValue)
            CompleteStage();
    }

    private void HandlePlayerMove(PlayerMoveEvent evt)
    {
        if (currentStage == null) return;
        if (currentStage.goalType != StageGoalType.MoveCount) return;
        if (evt.MoveCount >= currentStage.goalValue)
            CompleteStage();
    }

    private void HandleSymbolMatched(SymbolMatchedEvent evt)
    {
        if (currentStage == null || currentStage.goalType != StageGoalType.Collect) return;
        var targets = currentStage.collectTargets;
        if (targets == null || collectProgressByTarget == null) return;

        bool changed = false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i].symbolType != evt.Type) continue;
            if (collectProgressByTarget[i] >= targets[i].count) continue;

            collectProgressByTarget[i]++;
            changed = true;
        }

        if (changed && AllCollectTargetsMet())
            CompleteStage();
    }

    private bool AllCollectTargetsMet()
    {
        var targets = currentStage?.collectTargets;
        if (targets == null || targets.Length == 0) return false;

        for (int i = 0; i < targets.Length; i++)
            if (collectProgressByTarget[i] < targets[i].count)
                return false;
        return true;
    }

    private void CompleteStage()
    {
        if (currentStage == null || isTransitioning || isStageClearPending) return;
        if (currentStage.goalType == StageGoalType.None)
            return;

        isTransitioning = true;
        isStageClearPending = true;
        isStageCleared = false;
        remainingGraceMoves = currentStage.gracePeriodMoves;

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.GracePeriod);

        if (board != null)
            board.SetGraceStateActive(true);

        if (board != null)
        {
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} goal reached. Player has {remainingGraceMoves} extra moves before clear.");
            board.BeginStageClearGracePeriod(remainingGraceMoves, currentStage.gracePeriodRandomSpecialChance);
        }
        else
        {
            FinalizeStageClear();
        }
    }

    public void ConsumeStageClearGraceMove()
    {
        if (!isStageClearPending || remainingGraceMoves <= 0) return;

        remainingGraceMoves--;
        if (remainingGraceMoves <= 0)
            FinalizeStageClear();
    }

    private void HandleGameOver(GameOverEvent evt)
    {
        isTransitioning = true;
        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.StageClearing);
        Debug.Log("[StageManager] Player lost. Start a new run from the UI.");
    }
}
