using System;

namespace Match3.Grid
{
    public enum SpecialKind
    {
        None,
        RowClear,
        ColumnClear,
        Bomb,       // clears a 3x3 area
        ColorBomb   // clears every tile of one color
    }

    [Serializable]
    public struct Tile
    {
        public int ColorId;       // -1 = empty cell
        public SpecialKind Special;
        public bool IsObstacle;   // e.g. ice/jelly sitting on top of a cell
        public int ObstacleHp;    // hits required before the obstacle clears

        public readonly bool IsEmpty => ColorId < 0;

        public static Tile Empty => new Tile { ColorId = -1, Special = SpecialKind.None };

        public static Tile Normal(int colorId) => new Tile { ColorId = colorId, Special = SpecialKind.None };
    }
}
