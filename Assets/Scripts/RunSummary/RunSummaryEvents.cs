/// <summary>
/// Published once, right after GameOverEvent, with the finalized stats for the run that just
/// ended. RunSummaryPanel (or any other listener - analytics, a leaderboard submit) reacts
/// without needing a direct reference to RunSummaryTracker.
/// </summary>
public readonly struct RunSummaryReadyEvent
{
    public readonly RunSummaryData Data;
    public RunSummaryReadyEvent(RunSummaryData data) => Data = data;
}
