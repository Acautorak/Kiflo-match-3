#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only utility: PlayerPrefs (used to track which tutorial steps have already been shown)
/// persists across separate Play Mode sessions, unlike most other state - so testing a
/// showOnce=true tutorial step twice in a row silently shows nothing the second time unless this
/// gets cleared first. Doesn't need to be in an Editor-only folder since it's wrapped in
/// UNITY_EDITOR, but works fine there too if you'd rather keep it isolated from builds.
/// </summary>
public static class TutorialDebugMenu
{
    [MenuItem("Tools/Match3/Clear Tutorial Seen Flags")]
    private static void ClearTutorialSeenFlags()
    {
        // PlayerPrefs has no "delete by prefix" API, so this relies on TutorialSequencer's
        // SeenKeyPrefix convention - if that constant ever changes, update this to match.
        const string prefix = "tutorial_seen_";

        var guids = AssetDatabase.FindAssets("t:TutorialStepDefinition");
        int cleared = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var step = AssetDatabase.LoadAssetAtPath<TutorialStepDefinition>(path);
            if (step == null) continue;

            string key = prefix + step.Id;
            if (PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.DeleteKey(key);
                cleared++;
            }
        }

        PlayerPrefs.Save();
        Debug.Log($"[TutorialDebugMenu] Cleared {cleared} tutorial seen-flag(s) out of {guids.Length} TutorialStepDefinition asset(s) found in the project.");
    }
}
#endif
