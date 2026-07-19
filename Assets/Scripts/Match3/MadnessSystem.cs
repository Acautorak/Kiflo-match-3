using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Owns Madness Symbol behavior: firing effects at their three trigger points (spawn,
/// survived-move, cleared - see MadnessSymbolDefinition), per-move survival ticking, the
/// board-wide color-convert/randomize helpers those effects call into, and refill-time spawn
/// rolls from the weighted MadnessSpawnOption pool. Extracted from Board's two "Madness Symbols"
/// header regions.
///
/// Needs a `Board` reference (not just GridModel) because MadnessContext - passed to every
/// MadnessEffect.Execute() - carries one for effects like MadnessScoreGrowthEffect that call
/// ctx.Board.AddBonusScore(...). That's an existing part of the MadnessEffect contract (see
/// MadnessEffect.cs / MadnessContext) and isn't something this pass changes - it does mean
/// Board and MadnessSystem hold references to each other, which is a bit of a coupling smell,
/// but resolving it would mean reworking the MadnessContext struct and every effect asset that
/// reads ctx.Board, which is a separate, bigger change.
/// </summary>
public class MadnessSystem
{
    private readonly GridModel grid;
    private readonly Board board;
    private readonly SymbolSpawner spawner;
    private readonly PlayerHealth playerHealth;
    private readonly PlayerRunStats playerRunStats;
    private readonly MadnessBoardModifiers boardModifiers;
    private readonly MadnessMeter meter;
    private readonly GameManager gameManager;
    private readonly MonoBehaviour coroutineRunner;

    public bool SpawnMadnessOnRefill { get; set; } = false;
    public float MadnessSpawnChance { get; set; } = 0.03f;
    public MadnessSpawnOption[] MadnessSpawnOptions { get; set; }
    public bool TreatMadnessSymbolsAsWildcards { get; set; } = false;
    public float MadnessConvertHighlightStagger { get; set; } = 0.06f;

    public MadnessSystem(GridModel grid, Board board, SymbolSpawner spawner, PlayerHealth playerHealth,
        PlayerRunStats playerRunStats, MadnessBoardModifiers boardModifiers, MadnessMeter meter,
        GameManager gameManager, MonoBehaviour coroutineRunner)
    {
        this.grid = grid;
        this.board = board;
        this.spawner = spawner;
        this.playerHealth = playerHealth;
        this.playerRunStats = playerRunStats;
        this.boardModifiers = boardModifiers;
        this.meter = meter;
        this.gameManager = gameManager;
        this.coroutineRunner = coroutineRunner;
    }

    /// <summary>Builds a MadnessContext and runs every effect in `effects` against it. Shared by
    /// all three trigger points (spawn, survived-move, cleared).</summary>
    public void FireEffects(MadnessEffect[] effects, Symbol symbol, Vector2Int pos, int chainCount)
    {
        if (effects == null || effects.Length == 0 || symbol == null) return;

        var ctx = new MadnessContext(board, playerHealth, playerRunStats, boardModifiers, meter,
            symbol, pos, symbol.Type, symbol.MadnessMovesSurvived, chainCount);

        foreach (var effect in effects)
            effect?.Execute(ctx);
    }

    /// <summary>
    /// Called once per accepted player move, after any cascades from that move have fully
    /// resolved. Every Madness Symbol still on the board - i.e. NOT cleared this move - ticks
    /// its survived-move counter and fires its onSurvivedMoveEffects. Also ticks
    /// boardModifiers down (e.g. an ignited color's remaining duration) on the same cadence.
    /// </summary>
    public void TickSurvival()
    {
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || !occ.IsMadness) continue;

                occ.TickMadnessSurvival();
                FireEffects(occ.MadnessDefinition.onSurvivedMoveEffects, occ, new Vector2Int(x, y), chainCount: 1);
            }

        boardModifiers?.TickMove();
    }

    /// <summary>
    /// Converts up to `count` randomly chosen board symbols to `toColor` in place. Locked tiles
    /// are skipped, and `excludePosition` - typically the Madness Symbol's own cell - is never a
    /// target. Pass fromColor to only convert symbols currently of that one color; leave it null
    /// to make every color eligible. Symbols already `toColor` are skipped so `count` isn't
    /// wasted on no-ops. Returns the positions actually converted (may be fewer than `count` if
    /// not enough eligible cells exist). This is a repaint only - it doesn't clear cells, award
    /// score, or deal damage; combine with other effects for that.
    /// </summary>
    public List<Vector2Int> ConvertRandomSymbols(SymbolType? fromColor, SymbolType toColor, int count, Vector2Int? excludePosition = null)
    {
        var converted = new List<Vector2Int>();
        if (count <= 0) return converted;

        var candidates = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || occ.IsLocked) continue;
                if (excludePosition.HasValue && x == excludePosition.Value.x && y == excludePosition.Value.y) continue;
                if (fromColor.HasValue && occ.Type != fromColor.Value) continue;
                if (occ.Type == toColor) continue;
                candidates.Add(new Vector2Int(x, y));
            }

        for (int i = 0; i < count && candidates.Count > 0; i++)
        {
            int index = Random.Range(0, candidates.Count);
            var pos = candidates[index];
            candidates.RemoveAt(index);

            grid[pos.x, pos.y].Occupant.SetType(toColor);
            converted.Add(pos);
        }

        if (converted.Count > 0)
        {
            EventBus.Publish(new SymbolsConvertedEvent(toColor, converted.ToArray()));
            coroutineRunner.StartCoroutine(PlayStaggeredConvertHighlights(converted));
        }

        return converted;
    }

    /// <summary>
    /// Randomizes up to `count` other board symbols to independently random colors - unlike
    /// ConvertRandomSymbols, each affected cell rolls its own color rather than all sharing one
    /// target. Locked tiles are skipped, and `excludePosition` is never a target. When
    /// guaranteeColorChange is true, a symbol won't randomly re-roll back onto its own current
    /// color (re-rolls a few times rather than looping forever). Returns the positions actually
    /// randomized.
    /// </summary>
    public List<Vector2Int> RandomizeSymbolColors(int count, Vector2Int? excludePosition = null, bool guaranteeColorChange = true)
    {
        var affected = new List<Vector2Int>();
        if (count <= 0) return affected;

        var candidates = new List<Vector2Int>();
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var occ = grid[x, y].Occupant;
                if (occ == null || occ.IsLocked) continue;
                if (excludePosition.HasValue && x == excludePosition.Value.x && y == excludePosition.Value.y) continue;
                candidates.Add(new Vector2Int(x, y));
            }

        var newColors = new List<SymbolType>();
        for (int i = 0; i < count && candidates.Count > 0; i++)
        {
            int index = Random.Range(0, candidates.Count);
            var pos = candidates[index];
            candidates.RemoveAt(index);

            var occ = grid[pos.x, pos.y].Occupant;
            var newColor = spawner.RandomType();
            if (guaranteeColorChange)
            {
                int guard = 0;
                while (newColor == occ.Type && guard++ < 8) newColor = spawner.RandomType();
            }

            occ.SetType(newColor);
            affected.Add(pos);
            newColors.Add(newColor);
        }

        if (affected.Count > 0)
        {
            EventBus.Publish(new SymbolsRandomizedEvent(affected.ToArray(), newColors.ToArray()));
            coroutineRunner.StartCoroutine(PlayStaggeredConvertHighlights(affected));
        }

        return affected;
    }

    /// <summary>
    /// Plays Symbol.PlayConvertHighlight() across `positions` one at a time, waiting
    /// MadnessConvertHighlightStagger between each start, so a multi-symbol color change (from
    /// ConvertRandomSymbols or RandomizeSymbolColors) reads as a sequence sweeping across the
    /// board rather than everything flashing at once. Marks GameManager as
    /// ResolvingMadnessColorChange while it runs - deliberately doesn't restore the resting
    /// state itself afterward, since whatever the enclosing match-resolution flow sets next (or
    /// Board's own RestoreGameManagerRestingState call) is what should win. Purely cosmetic and
    /// fire-and-forget otherwise - the actual color change already happened synchronously before
    /// this runs. Skips a position if its cell has since become empty.
    /// </summary>
    private IEnumerator PlayStaggeredConvertHighlights(List<Vector2Int> positions)
    {
        gameManager?.SetState(GameManager.GameplayState.ResolvingMadnessColorChange);

        foreach (var pos in positions)
        {
            var occ = grid[pos.x, pos.y].Occupant;
            occ?.PlayConvertHighlight();

            if (MadnessConvertHighlightStagger > 0f)
                yield return new WaitForSeconds(MadnessConvertHighlightStagger);
        }
    }

    public bool ShouldSpawnOnRefill() => SpawnMadnessOnRefill && Random.value < MadnessSpawnChance;

    public MadnessSymbolDefinition PickWeightedOption()
    {
        if (MadnessSpawnOptions == null || MadnessSpawnOptions.Length == 0) return null;

        float total = MadnessSpawnOptions.Sum(o => o.definition != null ? Mathf.Max(0f, o.weight) : 0f);
        if (total <= 0f) return null;

        float roll = Random.Range(0f, total);
        float cumulative = 0f;
        foreach (var o in MadnessSpawnOptions)
        {
            if (o.definition == null) continue;
            cumulative += Mathf.Max(0f, o.weight);
            if (roll <= cumulative) return o.definition;
        }
        return MadnessSpawnOptions[MadnessSpawnOptions.Length - 1]?.definition;
    }
}
