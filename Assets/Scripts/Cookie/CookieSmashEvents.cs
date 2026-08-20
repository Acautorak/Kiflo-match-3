/// <summary>
/// Fired once per tap while Cookie Smash is active, mirroring KebabKarnageProgressEvent's role -
/// HUD/UI binds to this rather than polling CookieSmashManager directly. Move this struct into
/// GameEvents.cs alongside FeatureModeRequestedEvent/FeatureModeStartedEvent/FeatureModeEndedEvent
/// if you'd rather keep every event definition in one file - it's split out here only because I
/// don't have that file's current contents to safely append to.
/// </summary>
public readonly struct CookieSmashProgressEvent
{
    public readonly int TapsLanded;
    public readonly int TapsRequired;

    public CookieSmashProgressEvent(int tapsLanded, int tapsRequired)
    {
        TapsLanded = tapsLanded;
        TapsRequired = tapsRequired;
    }
}
