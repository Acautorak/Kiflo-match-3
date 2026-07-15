using UnityEngine;

/// <summary>
/// Designer-facing asset: the pool PowerupManager draws from between stages. Create via
/// Assets > Create > Match3 > Roguelike > Powerup Pool Config. Add/remove/reweight
/// PowerupDefinition entries here - no code required.
/// </summary>
[CreateAssetMenu(fileName = "PowerupPoolConfig", menuName = "Match3/Roguelike/Powerup Pool Config")]
public class PowerupPoolConfig : ScriptableObject
{
    public PowerupDefinition[] powerups;
    [Tooltip("How many distinct powerups to offer at once. Clamped to the pool size if the pool is smaller.")]
    [Min(1)] public int choicesOffered = 3;
}
