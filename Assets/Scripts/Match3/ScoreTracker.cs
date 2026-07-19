using UnityEngine;

/// <summary>
/// Owns run score: the current total, the multiplier applied to raw gains (sourced from
/// PlayerRunStats), and the ScoreChangedEvent publish that must accompany every change.
///
/// Extracted because the sequence "apply multiplier -> add to total -> publish
/// ScoreChangedEvent" was duplicated three times in Board (ResolveMatches,
/// TryRandomSpecialOnGraceMove, TryRandomSpecialOnGravity) plus once more, slightly
/// differently, in AddBonusScore. One bug fix or new modifier now only needs to happen here.
/// </summary>
public class ScoreTracker
{
    private readonly PlayerRunStats runStats;

    public int CurrentScore { get; private set; }

    public ScoreTracker(PlayerRunStats runStats)
    {
        this.runStats = runStats;
    }

    /// <summary>
    /// Applies the run's score multiplier to a raw delta, adds it to the running total, and
    /// publishes ScoreChangedEvent. Used for both per-cell clear totals and bonus payouts -
    /// safe to call with 0 (no-op, no event).
    /// </summary>
    public void AddScore(int rawDelta)
    {
        if (rawDelta == 0) return;
        int delta = runStats != null ? Mathf.RoundToInt(rawDelta * runStats.ScoreMultiplier) : rawDelta;
        CurrentScore += delta;
        EventBus.Publish(new ScoreChangedEvent(CurrentScore, delta));
    }

    /// <summary>Resets to zero and publishes a zero-delta ScoreChangedEvent, matching the
    /// original ClearBoard behavior (UI listeners rely on this to reset their display).</summary>
    public void Reset()
    {
        CurrentScore = 0;
        EventBus.Publish(new ScoreChangedEvent(CurrentScore, 0));
    }

    /// <summary>For save/load: sets the value directly without applying the multiplier or
    /// firing a mid-gameplay delta event. Caller is responsible for publishing afterward if a
    /// UI refresh is needed (LoadFromSave already does this once, for the whole board state).</summary>
    public void RestoreRaw(int score)
    {
        CurrentScore = score;
    }
}
