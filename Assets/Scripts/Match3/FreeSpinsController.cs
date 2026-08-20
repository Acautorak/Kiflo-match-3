using System.Collections;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Owns a single Free Spins reel spin. Columns spin relatively simultaneously: column x starts
/// ColumnStartStagger seconds after column x-1 (not after column x-1 finishes), each running as
/// its own coroutine via `coroutineRunner` so they genuinely overlap - since every column tumbles
/// for the same duration, they naturally stop in the same order they started, the classic "reels
/// start together, stop left to right a beat apart" slot feel.
///
/// Each column's tumble is a proper conveyor scroll, not a full-column flicker: every tick, the
/// bottom-most symbol continues moving further down past the visible board (then gets recycled
/// once off-screen) while every other symbol in the column shifts down exactly one cell and one
/// fresh random symbol drops in from above the top - so symbols visibly travel down through and
/// off the bottom of the board while new ones enter the top, same as a real slot machine reel,
/// rather than the whole column popping out and back in each tick. See ShiftColumnDown.
///
/// Once every column has landed, the board is checked for a natural match. If nothing landed
/// naturally, a match is forced by reusing the exact same mechanism Board's "Random Special
/// Effect (Gravity Bonus)" feature already uses (MatchResolver.TryRandomSpecialOnGravity,
/// forced) - repeated MinimumForcedChainLength times in a row (each with an incrementing
/// chainCount) so a forced result builds up a proper multi-step combo instead of always being a
/// single blast. Whatever that forced sequence's own refill turns up is then re-checked and, if
/// it cascades into a further natural match, run through the normal MatchResolver.Resolve
/// pipeline too - so a spin's result always ends up going through the same scoring/cascade/
/// specials/Madness-effect code path a regular move would.
///
/// Separately, once a spin's result (natural or forced) actually runs through MatchResolver.
/// Resolve, its cascade gets a wonky-proc guarantee of its own: while that cascade's chain count
/// is below GuaranteedWonkyChainThreshold, the odds of a random-special proc on each gravity
/// settle are forced to 100% (via MatchResolver.TriggerChanceOverride) instead of the normal
/// accumulated rate, and EnableRandomSpecialOnGravity is forced on for the duration regardless of
/// the stage's own setting - both are restored to whatever they were once resolution finishes.
///
/// Board owns and constructs this (see Board.Awake/PlayFreeSpin) the same way it owns
/// GravityController - FreeSpinsManager (the SPIN-button-facing MonoBehaviour) never touches grid
/// internals directly, it only calls Board.PlayFreeSpin() once per spin.
///
/// Note: TryRandomSpecialOnGravity requires Board's Eligible Random Special Types list (see
/// MatchResolver.EligibleRandomSpecialTypes / Board's "Random Special Effect (Gravity Bonus)"
/// Inspector section) to be non-empty - if that list is left empty, forced steps are a no-op
/// (same as the real gravity-bonus feature would be) and a spin with no natural match simply ends
/// with nothing forced, however high MinimumForcedChainLength is set. Fill in at least one entry
/// there if you want Free Spins to guarantee a result every time.
/// </summary>
public class FreeSpinsController
{
    private readonly GridModel grid;
    private readonly SymbolSpawner spawner;
    private readonly MatchResolver matchResolver;
    private readonly GameManager gameManager;
    private readonly MonoBehaviour coroutineRunner;
    private readonly float fallDuration;
    private readonly System.Func<int, int, Vector3> gridToWorld;

    /// <summary>Delay before each successive column STARTS spinning (not before it finishes) -
    /// 0 = every column starts simultaneously and lands simultaneously too.</summary>
    public float ColumnStartStagger { get; set; }

    /// <summary>How long (seconds) each column visibly conveyor-scrolls before its final landing
    /// tick. 0 = skip scrolling entirely and go straight to one landing shift.</summary>
    public float ReelSpinDuration { get; set; }

    /// <summary>How long each individual conveyor tick takes - lower = faster-scrolling reel.
    /// Only matters while ReelSpinDuration > 0. Also sets the floor on how many ticks a spin
    /// runs: at least grid.Height ticks always happen regardless of ReelSpinDuration, so every
    /// original symbol is guaranteed to have scrolled off and been replaced by a freshly-rolled
    /// one by the time a column lands - see SpinColumn.</summary>
    public float ReelTumbleStepDuration { get; set; }

    /// <summary>How many forced explosions to chain together (via MatchResolver.
    /// TryRandomSpecialOnGravity, forced) when a spin lands with no natural match. 1 = a single
    /// blast (the original behavior); higher values build up a bigger forced combo before
    /// handing off to the normal cascade check.</summary>
    public int MinimumForcedChainLength { get; set; }

    /// <summary>While a spin's cascade (see MatchResolver.Resolve's chainCount / ChainMatchedEvent.
    /// ChainCount) is still below this value, the wonky/random-special trigger chance for that
    /// cascade is forced to 100% (and EnableRandomSpecialOnGravity is forced on) instead of using
    /// the normal accumulated rate (stage wonkyChance + PlayerRunStats.RandomSpecialChanceBonus).
    /// Once the cascade reaches this chain count, it reverts to whatever that accumulated rate
    /// already was. Only affects the natural cascade run through MatchResolver.Resolve below -
    /// the separate forced-chain loop above (MinimumForcedChainLength) always triggers regardless
    /// of chance already, since it calls TryRandomSpecialOnGravity with forceOnce=true. 0 or below
    /// disables the guarantee entirely, so every chain count uses the normal accumulated rate.</summary>
    public int GuaranteedWonkyChainThreshold { get; set; }

    public FreeSpinsController(GridModel grid, SymbolSpawner spawner, MatchResolver matchResolver,
        GameManager gameManager, MonoBehaviour coroutineRunner, float fallDuration,
        System.Func<int, int, Vector3> gridToWorld, float columnStartStagger = 0.08f,
        float reelSpinDuration = 0.6f, float reelTumbleStepDuration = 0.08f,
        int minimumForcedChainLength = 1, int guaranteedWonkyChainThreshold = 2)
    {
        this.grid = grid;
        this.spawner = spawner;
        this.matchResolver = matchResolver;
        this.gameManager = gameManager;
        this.coroutineRunner = coroutineRunner;
        this.fallDuration = fallDuration;
        this.gridToWorld = gridToWorld;

        ColumnStartStagger = columnStartStagger;
        ReelSpinDuration = reelSpinDuration;
        ReelTumbleStepDuration = reelTumbleStepDuration;
        MinimumForcedChainLength = Mathf.Max(1, minimumForcedChainLength);
        GuaranteedWonkyChainThreshold = guaranteedWonkyChainThreshold;
    }

    /// <summary>Runs exactly one spin: overlapping conveyor-scroll reels, guarantee a result,
    /// resolve it. Caller (Board.PlayFreeSpin, driven by FreeSpinsManager) is responsible for
    /// everything outside a single spin - spin counts, the SPIN button, entering/exiting
    /// FeatureMode.</summary>
    public IEnumerator SpinOnce()
    {
        gameManager?.SetState(GameManager.GameplayState.ResolvingFreeSpin);

        yield return RerollAllColumnsOverlapping();

        var groups = MatchFinder.FindMatchGroups(grid.RawGrid, grid.Width, grid.Height);
        if (groups.Count == 0)
        {
            // Force a chain of explosions rather than a single one - each step still bypasses
            // EnableRandomSpecialOnGravity and the chance roll (forceOnce=true guarantees exactly
            // one trigger per call), it just runs MinimumForcedChainLength times in a row with an
            // incrementing chainCount so ChainMatchedEvent/combo UI sees a real multi-step chain.
            int forcedSteps = Mathf.Max(1, MinimumForcedChainLength);
            for (int step = 1; step <= forcedSteps; step++)
                yield return matchResolver.TryRandomSpecialOnGravity(chainCount: step, forceOnce: true);

            // The forced chain's own refill may have landed a further natural match - pick it up
            // so it cascades through the normal pipeline too ("continue resolving chains").
            groups = MatchFinder.FindMatchGroups(grid.RawGrid, grid.Width, grid.Height);
        }

        if (groups.Count > 0)
        {
            // Guarantee wonky/random-special procs while this spin's cascade is still short (see
            // GuaranteedWonkyChainThreshold), then fall back to whatever Board already had
            // configured (stage wonkyChance + PlayerRunStats bonus, and whatever
            // EnableRandomSpecialOnGravity was) once the cascade passes the threshold - saved and
            // restored here so nothing leaks into ordinary (non-Free-Spins) play sharing this same
            // MatchResolver instance.
            bool previousEnabled = matchResolver.EnableRandomSpecialOnGravity;
            var previousOverride = matchResolver.TriggerChanceOverride;

            matchResolver.EnableRandomSpecialOnGravity = true;
            int threshold = GuaranteedWonkyChainThreshold;
            matchResolver.TriggerChanceOverride = chainCount =>
                chainCount < threshold ? 1f : matchResolver.RandomSpecialTriggerChance;

            try
            {
                yield return matchResolver.Resolve(groups);
            }
            finally
            {
                matchResolver.EnableRandomSpecialOnGravity = previousEnabled;
                matchResolver.TriggerChanceOverride = previousOverride;
            }
        }

        // Back to the "waiting on the next spin / SPIN button" part of Free Spins. FreeSpinsManager
        // decides from here whether to call Board.PlayFreeSpin() again or GameManager.ExitFeatureMode().
        gameManager?.SetState(GameManager.GameplayState.FeatureMode);
    }

    /// <summary>Kicks off every column's spin as its own coroutine, ColumnStartStagger seconds
    /// apart, then waits for all of them to finish.</summary>
    private IEnumerator RerollAllColumnsOverlapping()
    {
        int width = grid.Width;
        int completed = 0;

        for (int x = 0; x < width; x++)
        {
            int columnIndex = x; // capture for the closure below
            coroutineRunner.StartCoroutine(RunColumnAndMarkComplete(columnIndex, () => completed++));

            bool isLastColumn = x == width - 1;
            if (!isLastColumn && ColumnStartStagger > 0f)
                yield return new WaitForSeconds(ColumnStartStagger);
        }

        yield return new WaitUntil(() => completed >= width);
    }

    private IEnumerator RunColumnAndMarkComplete(int x, System.Action onComplete)
    {
        yield return SpinColumn(x);
        onComplete();
    }

    /// <summary>Runs one column's full visible spin: repeated conveyor ticks (see
    /// ShiftColumnDown) at ReelTumbleStepDuration speed, then one final tick at the normal
    /// (slower) fallDuration as the landing beat. Always runs at least grid.Height ticks total
    /// regardless of ReelSpinDuration, so every symbol that was in the column before this spin
    /// is guaranteed to have scrolled off and been replaced - ReelSpinDuration below that floor
    /// just means the floor (grid.Height * ReelTumbleStepDuration) sets the actual spin length
    /// instead.
    ///
    /// Tumble ticks use Ease.Linear (constant velocity) rather than MoveTo's default OutQuad -
    /// with OutQuad, every single-cell shift decelerates to a stop and the next tick then
    /// re-accelerates from zero, which reads as a stutter even though there's no actual pause
    /// between ticks. Linear keeps velocity continuous across tick boundaries so the whole
    /// column reads as one continuous scroll. Only the final landing tick keeps the default
    /// eased motion, so the reel still visibly decelerates into place at the very end.</summary>
    private IEnumerator SpinColumn(int x)
    {
        float stepDuration = Mathf.Max(0.01f, ReelTumbleStepDuration);
        int requestedTicks = Mathf.RoundToInt(ReelSpinDuration / stepDuration);
        int tumbleTicks = Mathf.Max(grid.Height, requestedTicks);

        for (int i = 0; i < tumbleTicks; i++)
            yield return ShiftColumnDown(x, stepDuration, Ease.Linear).WaitForCompletion();

        yield return ShiftColumnDown(x, fallDuration, Ease.OutQuad).WaitForCompletion();
    }

    /// <summary>
    /// One conveyor tick for column x: the bottom-most occupant (row 0) continues moving further
    /// down to one cell below the visible board, then is returned to SymbolSpawner's pool once
    /// that exit finishes; every other occupant shifts down exactly one cell (updating both grid
    /// registration and Symbol.GridPosition); a fresh random symbol spawns one cell above the top
    /// and falls into the newly-vacated top row. All three movements share `duration` and `ease`
    /// so the whole column reads as one continuous motion rather than three separate ones.
    /// </summary>
    private Sequence ShiftColumnDown(int x, float duration, Ease ease)
    {
        var sequence = DOTween.Sequence();

        var exiting = grid[x, 0].Occupant;
        if (exiting != null)
        {
            var exitTween = exiting.MoveTo(gridToWorld(x, -1), duration, ease);
            exitTween.OnComplete(() => spawner.Despawn(exiting));
            sequence.Join(exitTween);
        }

        for (int y = 1; y < grid.Height; y++)
        {
            var occ = grid[x, y].Occupant;
            grid[x, y].Occupant = null;
            grid[x, y - 1].Occupant = occ;
            if (occ == null) continue;

            occ.GridPosition = new Vector2Int(x, y - 1);
            sequence.Join(occ.MoveTo(gridToWorld(x, y - 1), duration, ease));
        }

        var newType = spawner.RandomType();
        int topRow = grid.Height - 1;
        var instance = spawner.Spawn(x, topRow, newType, SpecialType.None, gridToWorld(x, grid.Height));
        if (instance != null)
            sequence.Join(instance.MoveTo(gridToWorld(x, topRow), duration, ease));

        return sequence;
    }
}
