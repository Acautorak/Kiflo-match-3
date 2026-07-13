using UnityEngine;

/// <summary>
/// One weighted entry in Board's random lock-spawn pool, used when a newly refilled tile
/// rolls to spawn already locked. Add/remove/duplicate entries in the Inspector to bias
/// which lock configurations show up more often - no code required.
/// </summary>
[System.Serializable]
public class LockSpawnOption
{
    [Min(1)] public int layers = 1;
    public LockBehavior behavior = LockBehavior.Temporary;
    [Tooltip("Only used when behavior = Temporary: how many player moves before this loses one layer automatically.")]
    [Min(1)] public int movesPerLayer = 3;
    [Min(0f)] public float weight = 1f;
}
