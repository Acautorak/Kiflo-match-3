using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns prefab resolution and instantiation of Symbol instances - what used to be
/// Board.GetPrefab / Board.RandomType / Board.CreatesImmediateMatch / Board.SpawnSymbol.
/// Board still decides *when* and *where* to spawn (populate, refill, special-creation,
/// load-from-save); this class only knows *how* to turn a (type, special, position) into
/// a live Symbol instance.
///
/// Takes the raw Cell[,] grid by reference rather than a full GridModel for now, so this
/// extraction doesn't force touching every other grid[x,y] access site in Board at the same
/// time - that's the next incremental step.
/// </summary>
public class SymbolSpawner
{
    private readonly Symbol symbolPrefab;
    private readonly Symbol[] symbolPrefabs;
    private readonly Transform symbolParent;
    private readonly GridModel grid;

    // Pooled per current SymbolType rather than in one shared bucket, since projects using the
    // per-type symbolPrefabs array have a genuinely different prefab/GameObject per type - an
    // instance built from the Red prefab can't become Blue just by calling SetType. Nothing
    // populates this unless a caller opts in via Despawn (see FreeSpinsController, which
    // recycles reel symbols every spin instead of Destroy/Instantiate-ing them). Every other
    // existing call site that still calls Object.Destroy directly is completely unaffected.
    private readonly Dictionary<SymbolType, Queue<Symbol>> pool = new Dictionary<SymbolType, Queue<Symbol>>();

    public SymbolSpawner(GridModel grid, Symbol symbolPrefab, Symbol[] symbolPrefabs, Transform symbolParent)
    {
        this.grid = grid;
        this.symbolPrefab = symbolPrefab;
        this.symbolPrefabs = symbolPrefabs;
        this.symbolParent = symbolParent;
    }

    public Symbol GetPrefab(SymbolType type)
    {
        if (symbolPrefabs != null && symbolPrefabs.Length > 0)
        {
            int index = (int)type;
            if (index < 0 || index >= symbolPrefabs.Length)
            {
                Debug.LogError($"[SymbolSpawner] symbolPrefabs has {symbolPrefabs.Length} entries but SymbolType has more values. " +
                                $"Either fill in all {System.Enum.GetValues(typeof(SymbolType)).Length} slots, or clear the array and assign symbolPrefab instead.");
                return symbolPrefab != null ? symbolPrefab : symbolPrefabs[0];
            }
            return symbolPrefabs[index];
        }

        if (symbolPrefab == null)
            Debug.LogError("[SymbolSpawner] No prefab assigned. Set symbolPrefab (single shared prefab) or fill symbolPrefabs (one per SymbolType) in the Inspector.");

        return symbolPrefab;
    }

    public SymbolType RandomType()
    {
        var values = System.Enum.GetValues(typeof(SymbolType));
        return (SymbolType)values.GetValue(Random.Range(0, values.Length));
    }

    /// <summary>True if placing `type` at (x,y) would immediately complete a 3-run with the
    /// two cells above/left of it. Used during initial population to avoid pre-made matches.</summary>
    public bool CreatesImmediateMatch(int x, int y, SymbolType type)
    {
        if (x >= 2 && !grid[x - 1, y].IsEmpty && !grid[x - 2, y].IsEmpty
            && grid[x - 1, y].Occupant.Type == type && grid[x - 2, y].Occupant.Type == type)
            return true;

        if (y >= 2 && !grid[x, y - 1].IsEmpty && !grid[x, y - 2].IsEmpty
            && grid[x, y - 1].Occupant.Type == type && grid[x, y - 2].Occupant.Type == type)
            return true;

        return false;
    }

    /// <summary>
    /// Instantiates a Symbol at `worldPos`, registers it in the grid at (x,y), and optionally
    /// applies an initial lock. Returns null (and logs) if no prefab is resolvable for `type`.
    /// This is now the single spawn path - previously Board.SpawnSymbol and the inline
    /// Instantiate() call inside CollapseAndRefill duplicated this logic slightly differently;
    /// both now go through here.
    /// </summary>
    public Symbol Spawn(int x, int y, SymbolType type, SpecialType special, Vector3 worldPos,
        int lockLayers = 0, LockBehavior lockBehavior = LockBehavior.None, int movesPerLayer = 3)
    {
        Symbol instance = TryTakeFromPool(type);
        if (instance != null)
        {
            instance.transform.position = worldPos;
            instance.gameObject.SetActive(true);
            instance.Initialize(type, special, new Vector2Int(x, y));
        }
        else
        {
            var prefab = GetPrefab(type);
            if (prefab == null)
            {
                Debug.LogError($"[SymbolSpawner] Spawn({x},{y},{type}) aborted - GetPrefab returned null");
                return null;
            }

            instance = Object.Instantiate(prefab, worldPos, Quaternion.identity, symbolParent);
            instance.Initialize(type, special, new Vector2Int(x, y));
        }

        if (lockLayers > 0 && lockBehavior != LockBehavior.None)
            instance.SetLock(lockLayers, lockBehavior, movesPerLayer);

        grid[x, y].Occupant = instance;
        return instance;
    }

    /// <summary>
    /// Returns `instance` to the pool (deactivated, still parented under symbolParent) instead of
    /// destroying it, bucketed by its CURRENT Type so a later Spawn(type) can only reuse instances
    /// that came from the matching prefab. Caller is responsible for clearing the grid slot itself
    /// (SymbolSpawner doesn't know which cell, if any, currently references this instance) and for
    /// not calling this on something also reachable elsewhere (e.g. mid-tween) since the pool takes
    /// ownership from here.
    /// </summary>
    public void Despawn(Symbol instance)
    {
        if (instance == null) return;

        instance.gameObject.SetActive(false);

        if (!pool.TryGetValue(instance.Type, out var queue))
        {
            queue = new Queue<Symbol>();
            pool[instance.Type] = queue;
        }
        queue.Enqueue(instance);
    }

    private Symbol TryTakeFromPool(SymbolType type)
    {
        if (!pool.TryGetValue(type, out var queue) || queue.Count == 0) return null;

        Symbol instance;
        do
        {
            instance = queue.Dequeue();
        } while (instance == null && queue.Count > 0); // skip over any pooled instance destroyed elsewhere in the meantime

        return instance;
    }
}
