using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sits between "stage cleared" and "next stage starts". Listens for StageCompletedEvent, rolls
/// a weighted-random offer from poolConfig using the same deterministic RunRandom pattern used
/// for stage generation (a given run seed always offers the same powerups at the same point, on
/// its own RNG stream so it never draws from the same sequence as stage-stat or lock-placement
/// rolls), and publishes PowerupChoicesOfferedEvent for the UI to render.
///
/// If no pool is configured (or it rolls zero eligible choices), this auto-advances via
/// StageManager.AdvanceToNextStage() so the game never gets stuck waiting on a picker that isn't
/// wired up yet - powerups are opt-in scaffolding, not a hard requirement.
/// </summary>
public class PowerupManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StageManager stageManager;
    [SerializeField] private PlayerRunStats playerRunStats;
    [SerializeField] private PlayerHealth playerHealth;

    [Header("Pool")]
    [Tooltip("Add/remove/reweight entries on the asset itself - no code required.")]
    [SerializeField] private PowerupPoolConfig poolConfig;

    private PowerupDefinition[] pendingOffer = System.Array.Empty<PowerupDefinition>();

    public IReadOnlyList<PowerupDefinition> PendingOffer => pendingOffer;

    private void OnEnable()
    {
        // Defensively unsubscribe first: if OnEnable ever fires twice without a matching
        // OnDisable in between (e.g. a duplicate PowerupManager instance in the scene), this
        // stops HandleStageCompleted from firing more than once per real StageCompletedEvent -
        // which would otherwise double-apply whichever powerup gets selected afterward.
        EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);
        EventBus.Subscribe<StageCompletedEvent>(HandleStageCompleted);
    }
    private void OnDisable() => EventBus.Unsubscribe<StageCompletedEvent>(HandleStageCompleted);

    private void HandleStageCompleted(StageCompletedEvent evt)
    {
        pendingOffer = RollOffers(evt.StageIndex);

        if (pendingOffer.Length == 0)
        {
            if (stageManager != null) stageManager.AdvanceToNextStage();
            return;
        }

        EventBus.Publish(new PowerupChoicesOfferedEvent(pendingOffer));
    }

    /// <summary>Called by the UI (see PowerupSelectionUI) when the player taps a choice.</summary>
    public void SelectPowerup(PowerupDefinition powerup)
    {
        pendingOffer = System.Array.Empty<PowerupDefinition>();

        if (powerup != null)
        {
            Debug.Log($"[PowerupManager] SelectPowerup: applying '{powerup.title}' ({powerup.name})");
            powerup.Apply(playerRunStats, playerHealth);
            EventBus.Publish(new PowerupSelectedEvent(powerup));
        }

        if (stageManager != null)
            stageManager.AdvanceToNextStage();
    }

    private PowerupDefinition[] RollOffers(int completedStageIndex)
    {
        if (poolConfig == null || poolConfig.powerups == null || poolConfig.powerups.Length == 0)
            return System.Array.Empty<PowerupDefinition>();

        var pool = new List<PowerupDefinition>();
        var seenOnce = new HashSet<PowerupDefinition>();
        foreach (var p in poolConfig.powerups)
        {
            if (p == null) continue;
            // Duplicate entries in the pool asset mean this powerup can be offered (and picked)
            // more than once per stage-clear screen, which will silently double its effect every
            // time it's chosen - a likely culprit if a stat is climbing faster than expected.
            if (!seenOnce.Add(p))
                Debug.LogWarning($"[PowerupManager] '{p.title}' ({p.name}) appears more than once in {poolConfig.name}'s powerups list - it can be offered in multiple slots at once.");
            pool.Add(p);
        }
        if (pool.Count == 0) return System.Array.Empty<PowerupDefinition>();

        int seed = stageManager != null ? stageManager.RunSeed : 0;
        var rng = RunRandom.ForDepth(seed, completedStageIndex, stream: 2);

        int count = Mathf.Min(poolConfig.choicesOffered, pool.Count);
        var offer = new PowerupDefinition[count];
        for (int i = 0; i < count; i++)
        {
            var pick = WeightedPool.Pick(pool, p => p.weight, rng);
            offer[i] = pick;
            pool.Remove(pick);
        }
        return offer;
    }
}
