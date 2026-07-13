using UnityEngine;

/// <summary>
/// One designer-authored locked tile placement, set up in the Board Inspector for a
/// fresh level layout. Ignored when loading from a save (lock state is saved/restored
/// automatically once a game is in progress).
/// </summary>
[System.Serializable]
public class InitialLockPlacement
{
    public Vector2Int position;
    [Min(1)] public int layers = 1;
    public LockBehavior behavior = LockBehavior.Permanent;
    [Tooltip("Only used when behavior = Temporary: how many player moves before this loses one layer automatically.")]
    [Min(1)] public int movesPerLayer = 3;
}
