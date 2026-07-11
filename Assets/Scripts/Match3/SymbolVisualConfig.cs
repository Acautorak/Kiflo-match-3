using System;
using UnityEngine;

/// <summary>
/// Designer-facing asset: assign one entry per SymbolType, with a sprite for the
/// normal state plus each special variant. Create via Assets > Create > Match3 > Symbol Visual Config.
/// </summary>
[CreateAssetMenu(fileName = "SymbolVisualConfig", menuName = "Match3/Symbol Visual Config")]
public class SymbolVisualConfig : ScriptableObject
{
    [Serializable]
    public class SymbolSprites
    {
        public SymbolType type;
        public Sprite normal;
        public Sprite rowClear;
        public Sprite columnClear;
        public Sprite bomb;
        public Sprite colorClear;
    }

    public SymbolSprites[] sprites;

    public Sprite GetSprite(SymbolType type, SpecialType special)
    {
        foreach (var s in sprites)
        {
            if (s.type != type) continue;

            return special switch
            {
                SpecialType.RowClear => s.rowClear,
                SpecialType.ColumnClear => s.columnClear,
                SpecialType.Bomb => s.bomb,
                SpecialType.ColorClear => s.colorClear,
                _ => s.normal
            };
        }
        return null;
    }
}
