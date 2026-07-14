using UnityEngine;

/// <summary>
/// A designer-tunable [min, max] integer range. Paired with a 0-1 difficulty value (see
/// StageGenerationConfig.difficultyCurve) via Lerp() to produce one concrete number per stage.
/// </summary>
[System.Serializable]
public struct IntRange
{
    public int min;
    public int max;

    public int Lerp(float t) => Mathf.RoundToInt(Mathf.Lerp(min, max, Mathf.Clamp01(t)));

    /// <summary>Inclusive random pick between min and max, using the run's seeded RNG.</summary>
    public int RandomInclusive(System.Random rng) => min >= max ? min : rng.Next(min, max + 1);
}

/// <summary>A designer-tunable [min, max] float range, Lerp'd the same way as IntRange.</summary>
[System.Serializable]
public struct FloatRange
{
    public float min;
    public float max;

    public float Lerp(float t) => Mathf.Lerp(min, max, Mathf.Clamp01(t));
}
