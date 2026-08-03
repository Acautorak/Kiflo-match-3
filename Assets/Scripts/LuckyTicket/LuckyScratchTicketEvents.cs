/// <summary>
/// EventBus events specific to the Lucky Scratch Ticket feature mode. Kept in their own file
/// (rather than appended to FeatureModeEvents.cs) so nothing you already have needs editing -
/// merge them in later if you'd rather keep all feature-mode events in one place.
/// </summary>

/// <summary>Fired the instant a single panel finishes scratching and its reward has been
/// applied - VFX/SFX/combo-UI can hook this the same way KebabKarnageProgressEvent is used for
/// its HUD.</summary>
public readonly struct LuckyScratchPanelRevealedEvent
{
    public readonly int PanelIndex;
    public readonly int TotalPanels;
    public readonly ScratchRewardDefinition Reward;

    public LuckyScratchPanelRevealedEvent(int panelIndex, int totalPanels, ScratchRewardDefinition reward)
    {
        PanelIndex = panelIndex;
        TotalPanels = totalPanels;
        Reward = reward;
    }
}

/// <summary>Fired after every panel reveal (including forced/timeout reveals), for a progress
/// bar or "3/6 scratched" HUD label - mirrors KebabKarnageProgressEvent's role for its timer.</summary>
public readonly struct LuckyScratchProgressEvent
{
    public readonly int PanelsRevealed;
    public readonly int TotalPanels;

    public LuckyScratchProgressEvent(int panelsRevealed, int totalPanels)
    {
        PanelsRevealed = panelsRevealed;
        TotalPanels = totalPanels;
    }
}
