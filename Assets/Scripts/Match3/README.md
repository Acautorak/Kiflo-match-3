# Unity Match-3 Starter Kit

## Files
| File | Role |
|---|---|
| `SymbolType.cs` | `SymbolType` (colors) and `SpecialType` (row/column/bomb/color clear) enums |
| `EventBus.cs` | Static generic pub/sub bus (`Subscribe<T>`, `Publish<T>`, `Unsubscribe<T>`) |
| `GameEvents.cs` | Event payload structs published on the bus |
| `SymbolVisualConfig.cs` | ScriptableObject mapping type+special → Sprite |
| `Symbol.cs` | Prefab script: identity, grid position, move tween, click input |
| `Cell.cs` | One grid slot |
| `MatchFinder.cs` | Pure static match-scanning (rows/columns, 3+ runs) |
| `Board.cs` | Grid, swapping, cascade resolution, special-symbol creation/activation, save hooks |
| `SaveData.cs` | `BoardSaveData` / `CellSaveData` (JsonUtility-serializable) |
| `SaveSystem.cs` | Simple JSON file save/load in `Application.persistentDataPath` |
| `SpecialSymbolEventRelay.cs` | Open, inspector-wireable `UnityEvent`s for special-symbol matches |
| `GameManager.cs` | Glue: score/combo logging, saves on pause/quit |

## Scene setup
1. **Prefabs**: make one prefab per `SymbolType` (or one shared prefab + `SymbolVisualConfig`).
   Each needs a `SpriteRenderer`, a `Collider2D` (for `OnMouseDown`), and the `Symbol` script.
2. **Board**: empty GameObject with `Board.cs`. Assign `symbolPrefabs` in enum order
   (Red, Blue, Green, Yellow, Purple, Orange), a `symbolParent` transform, and grid size.
3. **GameManager**: empty GameObject with `GameManager.cs`, reference the `Board`.
4. **Effects relay** (optional but recommended): empty GameObject with
   `SpecialSymbolEventRelay.cs`. Wire `OnRowClear` / `OnColumnClear` / `OnBomb` /
   `OnColorClear` / `OnAnySpecialMatch` in the Inspector to your VFX/SFX/UI methods —
   no code changes needed to add a new reaction to a special match.
5. **SymbolVisualConfig asset**: `Assets > Create > Match3 > Symbol Visual Config`,
   fill in sprites per type/special, assign it to each `Symbol` prefab.

## How matching works
- 3-in-a-row/column → cleared normally.
- 4-in-a-row/column → the middle cell becomes a `RowClear`/`ColumnClear` special.
- 5+-in-a-row/column → the middle cell becomes a `ColorClear` special.
- Matching a special symbol activates it (`Board.ActivateSpecial`), which publishes
  `SpecialSymbolMatchedEvent` with the exact affected cells — this is the event
  `SpecialSymbolEventRelay` exposes to designers.
- Cascades: after a clear, the board collapses/refills and re-scans; each pass
  increments `ChainCount` on `ChainMatchedEvent`, so scoring/UI can apply a combo
  multiplier without any extra bookkeeping.

## Extending
- **Bomb / L-T shape specials**: `SpecialType.Bomb` and its 3x3 activation are already
  implemented in `Board.ActivateSpecial`; you just need to detect L/T intersections in
  `MatchFinder` (currently only straight runs are detected) and route them to
  `specialsToCreate` in `Board.ResolveMatches`.
- **New special type**: add to the enum, a sprite slot in `SymbolVisualConfig`, a case
  in `ActivateSpecial`, and (optionally) a new `UnityEvent` in `SpecialSymbolEventRelay`.
- **Different save backend**: only `SaveSystem.cs` needs to change — its public API
  (`Save`, `Load`, `HasSave`, `DeleteSave`) is the only thing `Board.cs` calls.
- **New listeners** (UI score counter, audio manager, analytics): just
  `EventBus.Subscribe<T>` in `OnEnable` / `Unsubscribe` in `OnDisable`, no reference to
  `Board` required.
