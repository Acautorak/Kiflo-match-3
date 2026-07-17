using UnityEngine;

/// <summary>
/// One weighted entry in Board's random Madness-spawn pool, used when a newly refilled tile rolls
/// to spawn as a Madness Symbol. Add/remove/duplicate entries in the Inspector to bias which
/// Madness Symbols show up more often - no code required.
/// </summary>
[System.Serializable]
public class MadnessSpawnOption
{
    public MadnessSymbolDefinition definition;
    [Min(0f)] public float weight = 1f;
}
