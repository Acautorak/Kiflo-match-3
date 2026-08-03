#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class DebugTool
{
    [MenuItem("Tools/Delete Save And Log Path %`")]
    private static void DeleteSave()
    {
        Debug.Log($"[Board] Save file path: {Application.persistentDataPath}");
        SaveSystem.DeleteSave();
        Debug.Log("[Board] Save deleted. Press Play again for a fresh board.");

        ClearTutorialSeenFlags();
    }

    /// <summary>
    /// PlayerPrefs (used to track which tutorial steps have already been shown) persists across
    /// separate Play Mode sessions unlike the save file, so a fresh-board test via the shortcut
    /// above would otherwise still silently skip any showOnce=true tutorial step you'd already
    /// seen. Folded in here so one shortcut gives you a genuinely fresh run.
    /// </summary>
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
        Debug.Log($"[DebugTool] Cleared {cleared} tutorial seen-flag(s) out of {guids.Length} TutorialStepDefinition asset(s) found in the project.");
    }
}
#endif
