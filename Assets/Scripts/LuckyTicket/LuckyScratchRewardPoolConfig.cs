using UnityEngine;

/// <summary>
/// The pool LuckyScratchTicketManager rolls each panel's reward from - same
/// asset-not-code-driven pattern as PowerupPoolConfig. Add/remove/reweight entries here without
/// touching any script.
/// </summary>
[CreateAssetMenu(fileName = "New Scratch Reward Pool", menuName = "Match3/Lucky Scratch Ticket/Reward Pool Config")]
public class LuckyScratchRewardPoolConfig : ScriptableObject
{
    [Tooltip("Every possible reward this ticket can roll, each with its own odds via its own weight " +
             "field. Include at least one Damage entry (see ScratchRewardDefinition.Kind) if you want " +
             "genuine risk, not just a free-reward screen.")]
    public ScratchRewardDefinition[] rewards;
}
