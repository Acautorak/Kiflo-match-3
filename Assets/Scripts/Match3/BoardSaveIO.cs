using UnityEngine;

/// <summary>
/// Owns mapping between live board state and BoardSaveData, plus the actual SaveSystem.Save/Load
/// calls. Extracted from Board's "Save / Load" region.
///
/// Also guards against saving during the narrow, non-resumable stage-clear-cleanup animation
/// window (see TrySave) - grace period and "awaiting powerup selection" are both legitimate,
/// fully resumable states now (their state is captured below and restored by Board/StageManager
/// on load); only the brief explosion-animation window (Board.isStageClearing) and the
/// synchronous hand-off right after it (stageClearPendingAfterResolution) are skipped, since
/// there's no meaningful mid-animation state to resume into - quitting exactly then rolls back
/// to the last grace-period save instead.
/// </summary>
/// <summary>Everything Board needs to restore on itself after a load, beyond what LoadFromSave
/// already applies directly (score/health/run-stats/grid) - returned rather than written
/// directly since Board owns these fields, not BoardSaveIO.</summary>
public readonly struct BoardResumeState
{
    public readonly int MoveCount;
    public readonly bool GraceActive;
    public readonly int GraceMovesRemaining;
    public readonly float GraceRandomSpecialChance;

    public BoardResumeState(int moveCount, bool graceActive, int graceMovesRemaining, float graceRandomSpecialChance)
    {
        MoveCount = moveCount;
        GraceActive = graceActive;
        GraceMovesRemaining = graceMovesRemaining;
        GraceRandomSpecialChance = graceRandomSpecialChance;
    }
}

public class BoardSaveIO
{
    private readonly GridModel grid;
    private readonly SymbolSpawner spawner;
    private readonly ScoreTracker scoreTracker;
    private readonly PlayerHealth playerHealth;
    private readonly PlayerRunStats playerRunStats;
    private readonly StageManager stageManager;
    private readonly System.Func<int, int, Vector3> gridToWorld;

    public BoardSaveIO(GridModel grid, SymbolSpawner spawner, ScoreTracker scoreTracker,
        PlayerHealth playerHealth, PlayerRunStats playerRunStats, StageManager stageManager,
        System.Func<int, int, Vector3> gridToWorld)
    {
        this.grid = grid;
        this.spawner = spawner;
        this.scoreTracker = scoreTracker;
        this.playerHealth = playerHealth;
        this.playerRunStats = playerRunStats;
        this.stageManager = stageManager;
        this.gridToWorld = gridToWorld;
    }

    public BoardSaveData BuildSaveData(int moveCount, bool graceActive, int graceMovesRemaining,
        float graceRandomSpecialChance, bool isAwaitingPowerupSelection)
    {
        var data = new BoardSaveData
        {
            width = grid.Width,
            height = grid.Height,
            score = scoreTracker.CurrentScore,
            moveCount = moveCount,
            currentHealth = playerHealth != null ? playerHealth.CurrentHealth : 0,
            maxHealth = playerHealth != null ? playerHealth.MaxHealth : 0,
            currentStageIndex = stageManager != null ? stageManager.CurrentStageIndex : -1,
            runSeed = stageManager != null ? stageManager.RunSeed : 0,
            collectGoalProgress = stageManager != null ? stageManager.GetCollectProgressSnapshot() : System.Array.Empty<int>(),
            runStats = playerRunStats != null ? playerRunStats.BuildSaveData() : null,
            graceActive = graceActive,
            graceMovesRemaining = graceMovesRemaining,
            graceRandomSpecialChance = graceRandomSpecialChance,
            isAwaitingPowerupSelection = isAwaitingPowerupSelection,
            cells = new CellSaveData[grid.Width * grid.Height]
        };

        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                data.cells[x * grid.Height + y] = occ == null
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

    /// <summary>
    /// Saves only if `isSafeToSave` is true - see class remarks for why. Board computes the
    /// condition (it owns the stage-clear-transition flags) and passes it in each time, so this
    /// class doesn't need to duplicate or reach into that state - it just declines to persist a
    /// snapshot mid-transition.
    /// </summary>
    public void TrySave(BoardSaveData data, bool isSafeToSave)
    {
        if (!isSafeToSave)
        {
            Debug.Log("[BoardSaveIO] Skipped save - board is mid stage-clear transition, waiting for a stable state.");
            return;
        }
        SaveSystem.Save(data);
    }

    /// <summary>
    /// Populates the grid from a save, restores score/health/run-stats, and hands the stage
    /// index/seed/collect-progress to StageManager. Returns the moveCount and grace-period state
    /// the caller should apply to itself - not written directly since Board owns those fields.
    /// </summary>
    public BoardResumeState LoadFromSave(BoardSaveData data)
    {
        scoreTracker.RestoreRaw(data.score);

        if (playerHealth != null)
        {
            playerHealth.ResetForNewRun();
            if (data.maxHealth > 0)
                playerHealth.SetHealth(data.currentHealth, data.maxHealth);
        }

        playerRunStats?.RestoreFromSave(data.runStats);

        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var cellData = data.cells[x * grid.Height + y];
                if (!cellData.hasSymbol) continue;

                var symbol = spawner.Spawn(x, y, cellData.type, cellData.special, gridToWorld(x, y));
                if (symbol != null && cellData.lockLayers > 0)
                    symbol.RestoreLockState(cellData.lockLayers, cellData.lockBehavior, cellData.movesPerLayer, cellData.movesUntilNextAutoUnlock);
            }

        if (stageManager != null && data.currentStageIndex >= 0)
            stageManager.LoadStageState(data.currentStageIndex, data.runSeed, data.collectGoalProgress,
                data.graceActive, data.graceMovesRemaining, data.isAwaitingPowerupSelection);

        EventBus.Publish(new ScoreChangedEvent(scoreTracker.CurrentScore, 0));
        EventBus.Publish(new PlayerMoveEvent(data.moveCount));

        if (data.isAwaitingPowerupSelection && stageManager != null)
            EventBus.Publish(new StageCompletedEvent(stageManager.CurrentStageIndex, scoreTracker.CurrentScore));

        return new BoardResumeState(data.moveCount, data.graceActive, data.graceMovesRemaining, data.graceRandomSpecialChance);
    }
}
