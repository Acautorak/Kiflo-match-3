using UnityEngine;

/// <summary>
/// Player-facing on/off switch for tutorials, persisted across sessions via PlayerPrefs (same
/// storage as TutorialSequencer's per-step "seen" flags). Wire a Settings menu toggle to
/// TutorialsEnabled directly - e.g. `toggle.isOn = TutorialSettings.TutorialsEnabled;` on open,
/// `toggle.onValueChanged.AddListener(v => TutorialSettings.TutorialsEnabled = v);` to save it.
/// Turning this off doesn't mark any step as "seen" - it just suppresses showing anything while
/// off, so re-enabling later resumes exactly where it would have been.
/// </summary>
public static class TutorialSettings
{
    private const string EnabledKey = "tutorials_enabled";

    public static bool TutorialsEnabled
    {
        get => PlayerPrefs.GetInt(EnabledKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
