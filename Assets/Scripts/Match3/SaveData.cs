using System;

[Serializable]
public class BoardSaveData
{
    public int width;
    public int height;
    public int score;
    public CellSaveData[] cells; // flattened, index = x * height + y
}

[Serializable]
public class CellSaveData
{
    public bool hasSymbol;
    public SymbolType type;
    public SpecialType special;
}
