using System.Collections;
using UnityEngine;

/// <summary>
/// Owns a single Tile Collector session: for a fixed duration, every tap on a board tile
/// immediately collects it (scores it, despawns it, applies gravity to fill the gap) - no
/// swapping needed to trigger a clear, tapping does it directly. Any match a refill happens to
/// land afterward still goes through the normal MatchResolver.Resolve pipeline (see
/// CollectRoutine), so cascades during a session score/chain-combo/trigger Wonky procs and
/// Madness effects exactly like they would during regular play. Runs directly on the real board
/// grid (unlike FreeSpinsController's reels or a hidden mini-board) - the actual tiles ARE the
/// minigame, so the board stays visible and interactive the whole time.
///
/// Board owns and constructs this the same way it owns FreeSpinsController - TileCollectorManager
/// (the timer/HUD-facing MonoBehaviour) never touches grid internals directly, it only calls
/// Board.PlayTileCollectorSession() once per activation and reacts to TileCollectorProgressEvent
/// for its HUD. Board.SelectSymbol/SwipeSymbol redirect taps here (via IsActive) instead of the
/// normal SwapController path while a session is running.
/// </summary>
public class TileCollectorController
{
    private readonly GridModel grid;
    private readonly SymbolSpawner spawner;
    private readonly GravityController gravityController;
    private readonly MonoBehaviour coroutineRunner;
    private readonly Board board;
    private readonly MatchResolver matchResolver;
    private readonly MadnessSystem madnessSystem;

    /// <summary>True for the whole duration of RunSession, including brief windows where a tap's
    /// collect+collapse is still animating - Board checks this to redirect input here.</summary>
    public bool IsActive { get; private set; }

    private bool isProcessingTap;
    private int tilesCollected;
    private int activeScorePerTile;

    public TileCollectorController(GridModel grid, SymbolSpawner spawner, GravityController gravityController,
        MonoBehaviour coroutineRunner, Board board, MatchResolver matchResolver, MadnessSystem madnessSystem)
    {
        this.grid = grid;
        this.spawner = spawner;
        this.gravityController = gravityController;
        this.coroutineRunner = coroutineRunner;
        this.board = board;
        this.matchResolver = matchResolver;
        this.madnessSystem = madnessSystem;
    }

    /// <summary>Runs one timed session. Caller (Board.PlayTileCollectorSession, driven by
    /// TileCollectorManager) awaits this and treats its completion as the mode ending.</summary>
    public IEnumerator RunSession(float duration, int scorePerTile)
    {
        IsActive = true;
        isProcessingTap = false;
        tilesCollected = 0;
        activeScorePerTile = scorePerTile > 0 ? scorePerTile : 25;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            EventBus.Publish(new TileCollectorProgressEvent(elapsed, duration, tilesCollected));
            yield return null;
            elapsed += Time.deltaTime;
        }

        // Let a tap that landed right at the buzzer finish its collect+collapse instead of being
        // cut off mid-animation - IsActive stays true (so TryCollect below still no-ops new taps,
        // since a fresh tap shouldn't start once time's up, but the in-flight one gets to land).
        while (isProcessingTap) yield return null;

        IsActive = false;
        EventBus.Publish(new TileCollectorProgressEvent(duration, duration, tilesCollected));
    }

    /// <summary>Called by Board.SelectSymbol/SwipeSymbol while a session is active - both a tap
    /// and a swipe just collect whatever tile they landed on, so any gesture works.</summary>
    public void TryCollect(Symbol symbol)
    {
        if (!IsActive || isProcessingTap || symbol == null) return;
        coroutineRunner.StartCoroutine(CollectRoutine(symbol, activeScorePerTile));
    }

    private IEnumerator CollectRoutine(Symbol symbol, int scoreAmount)
    {
        isProcessingTap = true;

        var pos = symbol.GridPosition;
        var type = symbol.Type;

        EventBus.Publish(new TileCollectedEvent(type, pos));
        board.AddBonusScore(scoreAmount);

        grid[pos.x, pos.y].Occupant = null;
        spawner.Despawn(symbol);

        yield return gravityController.Collapse();

        // The refill can land a genuine 3+ match purely by chance - route it through the exact
        // same MatchResolver.Resolve pipeline a normal swap uses, so a cascade during a Tile
        // Collector session still scores, chain-combos (ChainMatchedEvent, the combo popup),
        // triggers Wonky procs, and fires Madness onClearedEffects, instead of just sitting there
        // unrecognized because nothing ever rescanned for it.
        var matchGroups = MatchFinder.FindMatchGroups(grid.RawGrid, grid.Width, grid.Height, madnessSystem.TreatMadnessSymbolsAsWildcards);
        if (matchGroups.Count > 0)
            yield return matchResolver.Resolve(matchGroups);

        tilesCollected++;
        isProcessingTap = false;
    }
}
