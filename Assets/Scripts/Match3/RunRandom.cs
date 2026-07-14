/// <summary>
/// Produces a fresh, deterministic System.Random for a given (runSeed, depth) pair. Using a
/// separate RNG per depth - rather than one long-lived stream - means regenerating "stage 12"
/// always gives the identical result whether it's the first stage ever generated this session
/// or the twelfth, and regardless of how many other streams (e.g. lock placement) have been
/// pulled from in between. This is what lets save/load restore a procedural run from just
/// (runSeed, currentStageIndex) instead of having to persist the full generated stage list.
/// </summary>
public static class RunRandom
{
    public static System.Random ForDepth(int runSeed, int depth, int stream = 0)
    {
        unchecked
        {
            int h = 17;
            h = h * 31 + runSeed;
            h = h * 31 + depth;
            h = h * 31 + stream;
            // A cheap avalanche so nearby seeds/depths don't produce visibly correlated streams.
            h ^= (h << 13);
            h ^= (int)((uint)h >> 17);
            h ^= (h << 5);
            return new System.Random(h);
        }
    }
}
