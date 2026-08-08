using UnityEngine;

/// <summary>
/// Owns the "Grace Move Chance" lifecycle - a powerup-granted stat (PlayerRunStats.GraceMoveChance)
/// that rolls after every accepted move to decide whether the FOLLOWING move becomes a Grace Move:
/// no damage taken, doesn't count toward a MoveCount-type stage goal, but healing still works
/// normally. Not the same system as StageManager's stage-clear grace period (remainingGraceMoves) -
/// that's a per-stage bonus-moves-before-clearing mechanic; this is a per-move powerup proc that
/// just happens to share the word "grace" in the design brief.
///
/// Board is the only thing that should call PeekArmed/Consume/RollForNextMove - it needs the
/// three kept separate (rather than one "ConsumeIfArmed" call) because whether a given swap
/// ultimately counts as a real move isn't known until partway through Board.TrySwap, but whether
/// to suppress damage needs to be known from the very start of it (see the comment in
/// Board.TrySwap for the exact sequencing). RollForNextMove is only ever called once a move is
/// confirmed to count - an aborted/reverted invalid swap doesn't get a fresh roll, and doesn't
/// consume a still-armed Grace Move either (it stays armed for the player's next real attempt).
/// </summary>
public class GraceMoveController : MonoBehaviour
{
    [SerializeField] private PlayerRunStats playerRunStats;

    /// <summary>True once a chance roll has succeeded and the player's next real move will be a
    /// Grace Move - stays true across any reverted/invalid attempts in between.</summary>
    public bool IsArmed { get; private set; }

    private void Awake()
    {
        if (playerRunStats == null) playerRunStats = FindAnyObjectByType<PlayerRunStats>();
    }

    /// <summary>Read-only check, no side effects - Board uses this at the very start of a swap to
    /// decide whether to switch on PlayerHealth's damage immunity before anything damage-capable
    /// (the no-match penalty, match resolution, Madness effects) has a chance to run.</summary>
    public bool PeekArmed() => IsArmed;

    /// <summary>Actually consumes the armed Grace Move - call only once a swap is confirmed to be
    /// a real, counted move (not an aborted/reverted one). Fires GraceMoveConsumedEvent so UI can
    /// fade its highlight back out.</summary>
    public void Consume()
    {
        if (!IsArmed) return;
        IsArmed = false;
        Debug.Log("[GraceMoveController] Grace Move consumed.");
        EventBus.Publish(new GraceMoveConsumedEvent());
    }

    /// <summary>Rolls PlayerRunStats.GraceMoveChance to decide if the NEXT move becomes a Grace
    /// Move - call once per real, counted move (regardless of whether that move itself was a
    /// Grace Move), after it's fully resolved. A no-op if already armed, so a lucky roll can't be
    /// silently overwritten/wasted by a second one before the player has had a chance to use it.</summary>
    public void RollForNextMove()
    {
        if (IsArmed) return;
        if (playerRunStats == null) return;

        float chance = playerRunStats.GraceMoveChance;
        if (chance <= 0f) return;

        if (Random.value < chance)
        {
            IsArmed = true;
            Debug.Log($"[GraceMoveController] Grace Move armed for the next move (chance was {chance:P0}).");
            EventBus.Publish(new GraceMoveArmedEvent());
        }
    }
}
