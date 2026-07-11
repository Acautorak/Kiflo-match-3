public enum SymbolType
{
    Red,
    Blue,
    Green,
    Yellow,
    Purple,
}

public enum SpecialType
{
    None,
    RowClear,     // clears the whole row (match of 4, horizontal)
    ColumnClear,  // clears the whole column (match of 4, vertical)
    Bomb,         // clears a 3x3 area (reserved for L/T shaped matches if you add that detection)
    ColorClear    // clears every symbol of one color (match of 5+)
}
