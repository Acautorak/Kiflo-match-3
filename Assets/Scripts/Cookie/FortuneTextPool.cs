using UnityEngine;

/// <summary>
/// Designer-facing asset: a pool of fortune-cookie-style one-liners shown in the panel after
/// Cookie Smash breaks. Deliberately separate from PowerupDefinition's own title/description -
/// the fortune line is flavor text, unrelated to whichever powerup actually got rolled, so the
/// same fortune can pair with any reward and vice versa. Create via
/// Assets > Create > Match3 > Roguelike > Fortune Text Pool.
/// </summary>
[CreateAssetMenu(fileName = "FortuneTextPool", menuName = "Match3/Roguelike/Fortune Text Pool")]
public class FortuneTextPool : ScriptableObject
{
    [TextArea]
    [Tooltip("One fortune per line/entry. A random one is picked (see PickRandom) each time Cookie Smash breaks.")]
    public string[] fortunes =
    {
        "A match made today sweetens tomorrow.",
        "The board favors the bold swap.",
        "Fortune crumbles for those who wait too long.",
        "A cascade is coming - be ready to catch it.",
    };

    /// <summary>Returns a random entry, or a safe placeholder if the pool is empty/unassigned.</summary>
    public string PickRandom()
    {
        if (fortunes == null || fortunes.Length == 0)
            return "...";

        return fortunes[Random.Range(0, fortunes.Length)];
    }
}
