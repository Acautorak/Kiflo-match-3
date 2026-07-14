using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Weighted-random selection helper. Works with anything that has a float weight, via a
/// selector delegate - so it can pick from LockSpawnOption[] (which already has a `weight`
/// field) just as easily as from the small *Weight wrapper classes below.
/// </summary>
public static class WeightedPool
{
    public static T Pick<T>(IList<T> items, System.Func<T, float> weightSelector, System.Random rng)
    {
        if (items == null || items.Count == 0) return default;

        float total = 0f;
        for (int i = 0; i < items.Count; i++)
            total += Mathf.Max(0f, weightSelector(items[i]));

        if (total <= 0f) return items[0];

        double roll = rng.NextDouble() * total;
        for (int i = 0; i < items.Count; i++)
        {
            roll -= Mathf.Max(0f, weightSelector(items[i]));
            if (roll <= 0.0) return items[i];
        }
        return items[items.Count - 1];
    }
}

/// <summary>One weighted entry in a stage-goal-type pool. Add/remove/reweight in the Inspector.</summary>
[System.Serializable]
public class GoalTypeWeight
{
    public StageGoalType type = StageGoalType.Score;
    [Min(0f)] public float weight = 1f;
}

/// <summary>One weighted entry in a frozen-tile-mode pool. Add/remove/reweight in the Inspector.</summary>
[System.Serializable]
public class FrozenModeWeight
{
    public FrozenTileSpawnMode mode = FrozenTileSpawnMode.GenerateNewFrozenTiles;
    [Min(0f)] public float weight = 1f;
}
