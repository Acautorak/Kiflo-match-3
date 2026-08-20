using System;
using UnityEngine;

/// <summary>
/// A stage's board shape: which cells within the (fixed) Width x Height bounding box are
/// playable. Stored flat (row-major, index = x * height + y) rather than as a 2D array so it
/// serializes cleanly in the Inspector and in StageDefinition. Deliberately dumb/uncompressed -
/// even a large 12x12 board is only 144 bools, so there's no need for RLE or bitpacking.
///
/// Null or empty (IsEmpty == true) means "no shape" - GridModel.ApplyShape treats that the
/// same as passing null: a full rectangle, every cell active. That's what every existing stage
/// gets automatically, since StageDefinition.shape defaults to null.
/// </summary>
[Serializable]
public class BoardShapeData
{
    public int width;
    public int height;
    [Tooltip("Row-major flat mask, index = x * height + y. true = playable cell, false = hole.")]
    public bool[] mask;

    public bool IsEmpty => mask == null || mask.Length == 0;

    /// <summary>Converts to the bool[,] GridModel.ApplyShape expects. Returns null (full
    /// rectangle) if this shape is empty.</summary>
    public bool[,] ToMask2D()
    {
        if (IsEmpty) return null;

        var result = new bool[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                result[x, y] = mask[x * height + y];
        return result;
    }

    public static BoardShapeData FromMask2D(bool[,] mask2D)
    {
        if (mask2D == null) return null;

        int w = mask2D.GetLength(0);
        int h = mask2D.GetLength(1);
        var flat = new bool[w * h];
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                flat[x * h + y] = mask2D[x, y];

        return new BoardShapeData { width = w, height = h, mask = flat };
    }
}

/// <summary>
/// A named, weighted, reusable shape a designer authors once (e.g. "Diamond", "Plus Sign",
/// "Jagged Top") and that both hand-picked stages and ProceduralStageGenerator can draw from.
/// Add a field like `public ShapeTemplate[] shapeTemplates;` to StageGenerationConfig to give
/// the generator a pool to pick from - see ProceduralStageGenerator.GenerateShape.
/// </summary>
[Serializable]
public class ShapeTemplate
{
    public string name;
    public BoardShapeData shape;
    [Min(0f)] public float weight = 1f;
}
