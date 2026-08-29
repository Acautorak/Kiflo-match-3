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
    [Tooltip("Optional. When assigned, gets reset every StartNewRun() and its BonusGraceMoves/" +
             "RandomSpecialChanceBonus are folded into the stage-clear grace period below.")]
    [SerializeField] private PlayerRunStats playerRunStats;
    [Tooltip("Optional. Run-scoped (not stage-scoped) - reset every StartNewRun() alongside PlayerRunStats and PlayerHealth.")]
    [SerializeField] private MadnessMeter madnessMeter;

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
    private readonly List<BoardShapeData> generatedShapes = new List<BoardShapeData>();

    private int currentStageIndex = -1;
    private StageDefinition currentStage;
    private bool isTransitioning;
    private bool isStageCleared;
    private bool isStageClearPending;
    private bool isAwaitingPowerupSelection;
    /// <summary>Set when a stage's goal is reached while GameManager.IsInFeatureMode is true (e.g.
    /// a Free Spins cascade pushes score over the goal mid-spin) - CompleteStage() bails out early
    /// in that case instead of yanking the board out from under the running feature, and this
    /// remembers to retry once FeatureModeEndedEvent confirms the feature has actually finished.
    /// See CompleteStage/HandleFeatureModeEnded.</summary>
    private bool stageCompletionPendingFeatureEnd;
    private int remainingGraceMoves;
    private int[] collectProgressByTarget = System.Array.Empty<int>();

    public int CurrentStageIndex => currentStageIndex;
    public int RunSeed => runSeed;
    public bool IsAwaitingPowerupSelection => isAwaitingPowerupSelection;

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
    public float CurrentRandomSpecialChance => currentStage != null ? currentStage.wonkyChance : 0f;
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
        EventBus.Subscribe<FeatureModeEndedEvent>(HandleFeatureModeEnded);
        EventBus.Subscribe<ChainMatchedEvent>(HandleChainMatchedForGoal);
        EventBus.Subscribe<MadnessSymbolClearedEvent>(HandleMadnessSymbolClearedForGoal);
        EventBus.Subscribe<HealthChangedEvent>(HandleHealthChangedForGoal);
        EventBus.Subscribe<GameStateChangedEvent>(HandleGameStateChangedForGoal);
    }

    private void OnDisable()
    {
        EventBus.Unsubscribe<ScoreChangedEvent>(HandleScoreChanged);
        EventBus.Unsubscribe<PlayerMoveEvent>(HandlePlayerMove);
        EventBus.Unsubscribe<SymbolMatchedEvent>(HandleSymbolMatched);
        EventBus.Unsubscribe<GameOverEvent>(HandleGameOver);
        EventBus.Unsubscribe<FeatureModeEndedEvent>(HandleFeatureModeEnded);
        EventBus.Unsubscribe<ChainMatchedEvent>(HandleChainMatchedForGoal);
        EventBus.Unsubscribe<MadnessSymbolClearedEvent>(HandleMadnessSymbolClearedForGoal);
        EventBus.Unsubscribe<HealthChangedEvent>(HandleHealthChangedForGoal);
        EventBus.Unsubscribe<GameStateChangedEvent>(HandleGameStateChangedForGoal);
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
            // is the point where we tell it to actually load its saved grid state. The shape
            // must be applied BEFORE InitializeBoard() populates cells from the save: the save
            // data itself never places a symbol in a hole (PopulateBoard/GravityController
            // already skip inactive cells before a save is ever written), so visuals come back
            // correct either way - but without this, GridModel's active mask stays "full
            // rectangle" and GravityController would treat what should be disconnected segments
            // as one tall column, misbehaving on the very next match.
            if (board != null)
            {
                board.ApplyBoardShape(GetShape(currentStageIndex));
                board.InitializeBoard();
            }
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

    /// <summary>The generated (or null = full rectangle) shape for a given depth - see
    /// ProceduralStageGenerator.GenerateShape. Cached the same way stages/locks are.</summary>
    private BoardShapeData GetShape(int index)
    {
        EnsureGenerated(index);
        return (index >= 0 && index < generatedShapes.Count) ? generatedShapes[index] : null;
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

            // Shape first - lock placement generation takes the resulting mask so locks never
            // land on a hole (see GenerateInitialLockPlacements' shapeMask param).
            var shape = ProceduralStageGenerator.GenerateShape(depth, runSeed, generationConfig, boardWidth, boardHeight);
            generatedShapes.Add(shape);

            generatedStages.Add(ProceduralStageGenerator.GenerateStage(depth, runSeed, generationConfig));
            generatedLockPlacements.Add(ProceduralStageGenerator.GenerateInitialLockPlacements(
                depth, runSeed, generationConfig, boardWidth, boardHeight, shape?.ToMask2D()));
        }
    }

    private int GenerateRandomSeed()
    {
        int seed = System.Guid.NewGuid().GetHashCode();
        return seed == 0 ? 1 : seed;
    }

    public void LoadStageState(int index, int savedRunSeed = 0, int[] savedCollectProgress = null,
        bool restoreGraceActive = false, int restoredGraceMovesRemaining = 0, bool awaitingPowerupSelection = false)
    {
        runSeed = savedRunSeed;
        currentStageIndex = index;
        currentStage = GetStage(index);
        InitializeCollectProgress(savedCollectProgress);
        stageCompletionPendingFeatureEnd = false;

        if (restoreGraceActive)
        {
            isTransitioning = true;
            isStageClearPending = true;
            isStageCleared = false;
            isAwaitingPowerupSelection = false;
            remainingGraceMoves = restoredGraceMovesRemaining;

            if (gameManager != null)
                gameManager.SetState(GameManager.GameplayState.GracePeriod);
        }
        else if (awaitingPowerupSelection)
        {
            isTransitioning = true;
            isStageClearPending = false;
            isStageCleared = true;
            isAwaitingPowerupSelection = true;
            remainingGraceMoves = 0;

            if (gameManager != null)
                gameManager.SetState(GameManager.GameplayState.Idle);
        }
        else
        {
            isTransitioning = false;
            isStageClearPending = false;
            isStageCleared = false;
            isAwaitingPowerupSelection = false;
            remainingGraceMoves = 0;
        }
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
        isAwaitingPowerupSelection = false;
        stageCompletionPendingFeatureEnd = false;
        remainingGraceMoves = 0;
        movesTowardGoal = 0;
        madnessClearedCount = 0;
        cascadeCollectAccumulator = 0;
        noDamageStreakMoves = 0;
        lastKnownHealthForStreak = -1;
        healthDroppedThisMove = false;
        moveWasRegisteredSinceLastSettle = false;
        InitializeCollectProgress();

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.Idle);

        if (board != null)
            board.ResetForStage(currentStage, GetLockPlacements(index), GetShape(index));

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

        isAwaitingPowerupSelection = false;
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
        generatedShapes.Clear();

        if (playerRunStats != null)
            playerRunStats.ResetForNewRun();

        if (madnessMeter != null)
            madnessMeter.ResetForNewRun();

        if (board != null)
            board.ClearBoard();

        StartStage(0);
    }

    [Header("Stage Clear")]
    [Tooltip("Delay (seconds) between a stage's goal being finalized and the powerup selection " +
             "screen actually appearing - gives a 'Stage Complete!' beat instead of an instant cut, " +
             "especially now that a stage with 0 grace moves finalizes immediately rather than " +
             "waiting out any bonus moves first. 0 = no delay, same as before.")]
    [Min(0f)]
    [SerializeField] private float stageClearDelay = 1f;

    public void FinalizeStageClear()
    {
        if (currentStage == null || !isStageClearPending || isStageCleared) return;

        isStageCleared = true;
        isStageClearPending = false;
        isAwaitingPowerupSelection = true;

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.StageClearing);

        if (stageClearDelay > 0f)
            StartCoroutine(FinishStageClearAfterDelay());
        else
            FinishStageClear();
    }

    private IEnumerator FinishStageClearAfterDelay()
    {
        yield return new WaitForSeconds(stageClearDelay);
        FinishStageClear();
    }

    /// <summary>The actual "tell everything a stage is done" work, deferred behind stageClearDelay
    /// (see FinalizeStageClear) - board cleanup + StageCompletedEvent, which PowerupManager listens
    /// for to show the choice screen.</summary>
    private void FinishStageClear()
    {
        if (board != null)
        {
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} clearing.");
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

    /// <summary>
    /// Tracked separately from PlayerMoveEvent.MoveCount (Board's own running total, which still
    /// increments for a Grace Move and feeds HUD/save data) specifically so a Grace Move can be
    /// genuinely excluded from a MoveCount-type goal - if this instead compared directly against
    /// evt.MoveCount, a Grace Move's contribution to that shared running total would still push a
    /// LATER real move over the goal "for free", even if the check on the Grace Move's own event
    /// were skipped. Reset to 0 in StartStage alongside remainingGraceMoves.
    /// </summary>
    private int movesTowardGoal;

    private int madnessClearedCount;
    /// <summary>Live progress for MadnessCleared - exposed the same way CurrentCollectProgress is, for UI.</summary>
    public int CurrentMadnessClearedCount => madnessClearedCount;

    private int cascadeCollectAccumulator;

    private int noDamageStreakMoves;
    /// <summary>Live progress for SurviveNoDamage - exposed the same way CurrentCollectProgress is, for UI.</summary>
    public int CurrentNoDamageStreak => noDamageStreakMoves;
    private int lastKnownHealthForStreak = -1;
    /// <summary>True if ANY damage occurred since the currently-open move started - judged (and
    /// cleared) once that move's resolution fully settles, see HandleGameStateChangedForGoal.</summary>
    private bool healthDroppedThisMove;
    /// <summary>True once a real (non-Grace) move has been registered that still needs judging
    /// against the no-damage streak - set by HandlePlayerMove, consumed by
    /// HandleGameStateChangedForGoal once the board actually settles.</summary>
    private bool moveWasRegisteredSinceLastSettle;

    private void HandlePlayerMove(PlayerMoveEvent evt)
    {
        if (currentStage == null) return;
        if (evt.WasGraceMove) return; // Grace Moves never count toward any stage requirement

        if (currentStage.goalType == StageGoalType.SurviveNoDamage)
        {
            moveWasRegisteredSinceLastSettle = true;
            return;
        }

        if (currentStage.goalType != StageGoalType.MoveCount) return;

        movesTowardGoal++;
        if (movesTowardGoal >= currentStage.goalValue)
            CompleteStage();
    }

    private void HandleSymbolMatched(SymbolMatchedEvent evt)
    {
        if (currentStage == null || currentStage.goalType != StageGoalType.Collect) return;
        var targets = currentStage.collectTargets;
        if (targets == null || collectProgressByTarget == null) return;

        // Disco Dance Disco (see DiscoDanceDiscoManager) counts each matched symbol multiple
        // times toward a Collect goal while its event is active - 1 the rest of the time. Read
        // directly off the singleton (same pattern as Board.Instance elsewhere) rather than an
        // event, since this needs the current value at the moment of each match, not a cached one.
        int perMatchAmount = DiscoDanceDiscoManager.Instance != null ? DiscoDanceDiscoManager.Instance.CollectMultiplier : 1;

        bool changed = false;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i].symbolType != evt.Type) continue;
            if (collectProgressByTarget[i] >= targets[i].count) continue;

            // Clamp to the target's count rather than overshooting it just because the
            // multiplier happened to be mid-stride when the last few needed were cleared.
            collectProgressByTarget[i] = Mathf.Min(targets[i].count, collectProgressByTarget[i] + perMatchAmount);
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

    /// <summary>
    /// Handles both ChainCombo and CascadeCollect - both key off the same event, just checking a
    /// different thing (cascade DEPTH vs total symbols cleared across the cascade), so one
    /// subscription covers both rather than two near-identical ones.
    /// </summary>
    private void HandleChainMatchedForGoal(ChainMatchedEvent evt)
    {
        if (currentStage == null) return;

        if (currentStage.goalType == StageGoalType.ChainCombo)
        {
            if (evt.ChainCount >= currentStage.goalValue)
                CompleteStage();
            return;
        }

        if (currentStage.goalType == StageGoalType.CascadeCollect)
        {
            // ChainCount == 1 means a fresh cascade is starting (see ChainMatchedEvent's own field
            // doc) - reset the accumulator so an earlier move's leftover total can't carry over.
            if (evt.ChainCount == 1) cascadeCollectAccumulator = 0;
            cascadeCollectAccumulator += evt.SymbolsCleared;

            if (cascadeCollectAccumulator >= currentStage.goalValue)
                CompleteStage();
        }
    }

    private void HandleMadnessSymbolClearedForGoal(MadnessSymbolClearedEvent evt)
    {
        if (currentStage == null || currentStage.goalType != StageGoalType.MadnessCleared) return;

        madnessClearedCount++;
        if (madnessClearedCount >= currentStage.goalValue)
            CompleteStage();
    }

    /// <summary>
    /// Just tracks whether damage happened - doesn't judge the streak itself. Judging happens in
    /// HandleGameStateChangedForGoal once a move's ENTIRE resolution has settled, not here,
    /// because Board.RegisterPlayerMove (PlayerMoveEvent) fires BEFORE match resolution/cascade
    /// damage for that same move - judging directly off PlayerMoveEvent would misattribute a
    /// move's own damage to the move that comes AFTER it instead.
    /// </summary>
    private void HandleHealthChangedForGoal(HealthChangedEvent evt)
    {
        if (lastKnownHealthForStreak >= 0 && evt.CurrentHealth < lastKnownHealthForStreak)
            healthDroppedThisMove = true;

        lastKnownHealthForStreak = evt.CurrentHealth;
    }

    private void HandleGameStateChangedForGoal(GameStateChangedEvent evt)
    {
        if (currentStage == null || currentStage.goalType != StageGoalType.SurviveNoDamage) return;
        if (!moveWasRegisteredSinceLastSettle) return; // settled without a real move happening - nothing to judge

        bool settled = evt.Current == GameManager.GameplayState.Idle || evt.Current == GameManager.GameplayState.GracePeriod;
        if (!settled) return;

        if (healthDroppedThisMove)
            noDamageStreakMoves = 0;
        else
            noDamageStreakMoves++;

        moveWasRegisteredSinceLastSettle = false;
        healthDroppedThisMove = false;

        if (noDamageStreakMoves >= currentStage.goalValue)
            CompleteStage();
    }

    /// <summary>Playtest-only: forces the current stage to complete right now, exactly as if its
    /// real goal had just been reached - goes through the same CompleteStage() path a genuine
    /// completion would (grace period if the stage has one configured, or the immediate
    /// FinalizeStageClear()+delay path if it has 0 - see CompleteStage/FinalizeStageClear), so it
    /// exercises the real flow instead of a separate shortcut. Called by DebugFeatureModeMenu.</summary>
    public void DebugForceCompleteStage()
    {
        if (currentStage == null)
        {
            Debug.LogWarning("[StageManager] DebugForceCompleteStage: no current stage.");
            return;
        }

        Debug.Log($"[StageManager] DEBUG: forcing stage {currentStageIndex + 1} to complete.");
        CompleteStage();
    }

    private void CompleteStage()
    {
        if (currentStage == null || isTransitioning || isStageClearPending) return;
        if (currentStage.goalType == StageGoalType.None)
            return;

        // Don't yank the board out from under a running feature mode (Free Spins reels still
        // tumbling, Kebab Karnage asteroids still falling, etc.) just because a match resolved
        // mid-feature pushed the goal over the line - defer and let HandleFeatureModeEnded retry
        // this exact call once the feature actually finishes (FeatureModeEndedEvent).
        if (gameManager != null && gameManager.IsInFeatureMode)
        {
            if (!stageCompletionPendingFeatureEnd)
            {
                stageCompletionPendingFeatureEnd = true;
                Debug.Log($"[StageManager] Stage {currentStageIndex + 1} goal reached while a feature mode " +
                          "is active - deferring stage clear until the feature ends.");
            }
            return;
        }

        isTransitioning = true;
        isStageClearPending = true;
        isStageCleared = false;
        int bonusGraceMoves = playerRunStats != null ? playerRunStats.BonusGraceMoves : 0;
        float specialChanceBonus = playerRunStats != null ? playerRunStats.RandomSpecialChanceBonus : 0f;
        remainingGraceMoves = currentStage.gracePeriodMoves + bonusGraceMoves;

        // No grace moves for this stage (increasingly the normal case now that the separate
        // Grace Move Chance system - GraceMoveController - covers the "occasional free move"
        // niche instead) - skip the whole GracePeriod state dance and go straight into powerup
        // selection. Without this the game gets stuck in GracePeriod forever: ConsumeStageClearGraceMove
        // only finalizes once remainingGraceMoves ticks down to 0 via an actual player move, but it
        // early-returns and does nothing when remainingGraceMoves is ALREADY 0, since there's no
        // move left to consume.
        if (remainingGraceMoves <= 0)
        {
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} goal reached with 0 grace moves - clearing immediately into powerup selection.");
            FinalizeStageClear();
            return;
        }

        if (gameManager != null)
            gameManager.SetState(GameManager.GameplayState.GracePeriod);

        if (board != null)
            board.SetGraceStateActive(true);

        if (board != null)
        {
            Debug.Log($"[StageManager] Stage {currentStageIndex + 1} goal reached. Player has {remainingGraceMoves} extra moves before clear.");
            float graceSpecialChance = Mathf.Clamp01(currentStage.gracePeriodRandomSpecialChance + specialChanceBonus);
            board.BeginStageClearGracePeriod(remainingGraceMoves, graceSpecialChance);
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

    /// <summary>Retries a CompleteStage() call that was deferred because a feature mode was still
    /// running when the stage's goal was actually reached - see CompleteStage. By the time this
    /// fires, GameManager.IsInFeatureMode is already false (the feature manager clears it before
    /// publishing FeatureModeEndedEvent), so CompleteStage proceeds normally this time.</summary>
    private void HandleFeatureModeEnded(FeatureModeEndedEvent evt)
    {
        if (!stageCompletionPendingFeatureEnd) return;
        stageCompletionPendingFeatureEnd = false;
        CompleteStage();
    }
}
