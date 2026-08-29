using System.Collections.Generic;

/// <summary>
/// Immutable snapshot of one completed run's stats, built by RunSummaryTracker and published via
/// RunSummaryReadyEvent when the run ends (GameOverEvent).
/// </summary>
public class RunSummaryData
{
    public readonly int FinalScore;
    public readonly int StagesCleared;
    public readonly int TotalMoves;
    public readonly int DamageTaken;
    public readonly int LongestChain;
    public readonly int TotalSymbolsCleared;
    public readonly int MadnessSymbolsCleared;
    public readonly float RunDurationSeconds;
    public readonly IReadOnlyList<PowerupDefinition> PowerupsCollected;

    public RunSummaryData(int finalScore, int stagesCleared, int totalMoves, int damageTaken,
        int longestChain, int totalSymbolsCleared, int madnessSymbolsCleared,
        float runDurationSeconds, IReadOnlyList<PowerupDefinition> powerupsCollected)
    {
        FinalScore = finalScore;
        StagesCleared = stagesCleared;
        TotalMoves = totalMoves;
        DamageTaken = damageTaken;
        LongestChain = longestChain;
        TotalSymbolsCleared = totalSymbolsCleared;
        MadnessSymbolsCleared = madnessSymbolsCleared;
        RunDurationSeconds = runDurationSeconds;
        PowerupsCollected = powerupsCollected;
    }
}
