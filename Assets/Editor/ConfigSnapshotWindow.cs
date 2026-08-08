#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Editor-only tool: backs up every ScriptableObject asset (StageGenerationConfig,
/// PowerupDefinition, ScratchRewardDefinition, MadnessSymbolDefinition/MadnessEffect subclasses,
/// etc.) AND every scene file under a chosen folder, by copying their raw files - so values
/// tweaked live in Play Mode, on either a config asset OR a scene object, can be reverted in one
/// click before cutting a build. A scene file is just as vulnerable to this as an asset: normally
/// Unity discards Play Mode changes to scene objects on exiting Play Mode, but if your project has
/// Enter Play Mode Options set to skip scene reload (a common perf optimization), those changes
/// persist into the actual open scene and can get saved to disk like any other edit.
///
/// Backs up whole files rather than round-tripping through JSON/serialization, so a restore is an
/// exact byte-for-byte revert - no risk of object references or field types being handled
/// awkwardly by a round-trip, and it works identically for both assets and scenes since both are
/// just text files on disk.
///
/// Backups live OUTSIDE Assets/, at <ProjectRoot>/ConfigSnapshot/ - deliberately NOT under
/// Assets/, since Unity would otherwise try to import the backup files as brand new duplicate
/// assets (with their own GUIDs) the moment they appeared there, which is exactly what this tool
/// needs to avoid.
///
/// Usage: Tools > Match3 > Config Snapshot. Take Snapshot before you start playtesting/tweaking
/// values; Restore From Snapshot right before you build. Restore is blocked while in Play Mode -
/// reloading a scene file from disk mid-Play doesn't mean anything, since Play Mode runs on a
/// separate in-memory copy; stop Play Mode first.
/// </summary>
public class ConfigSnapshotWindow : EditorWindow
{
    private const string SnapshotFolderName = "ConfigSnapshot";
    private const string ManifestFileName = "manifest.txt";
    private const string SearchFolderPrefKey = "Match3.ConfigSnapshot.SearchFolder";

    private string searchFolder = "Assets";

    private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;
    private static string SnapshotFolder => Path.Combine(ProjectRoot, SnapshotFolderName);
    private static string ManifestPath => Path.Combine(SnapshotFolder, ManifestFileName);

    [MenuItem("Tools/Match3/Config Snapshot")]
    public static void Open() => GetWindow<ConfigSnapshotWindow>("Config Snapshot");

    private void OnEnable()
    {
        searchFolder = EditorPrefs.GetString(SearchFolderPrefKey, "Assets");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Backs up every ScriptableObject asset AND scene file under the folder below by " +
            "copying their raw files - protects config/scene values (chances, weights, damage, " +
            "an object you nudged in the Hierarchy, etc.) you tweak live in Play Mode, since " +
            "Unity writes those straight to disk with no guaranteed auto-revert. Take a snapshot " +
            "before playtesting; restore right before you build.",
            MessageType.Info);

        EditorGUILayout.Space();

        EditorGUI.BeginChangeCheck();
        searchFolder = EditorGUILayout.TextField(
            new GUIContent("Search Folder", "Only assets/scenes under this folder (relative to the project root) are included - defaults to the whole Assets folder. Narrow this if scanning everything is slow or picks up things you don't want backed up."),
            searchFolder);
        if (EditorGUI.EndChangeCheck())
            EditorPrefs.SetString(SearchFolderPrefKey, searchFolder);

        EditorGUILayout.Space();

        if (File.Exists(ManifestPath))
        {
            var lines = File.ReadAllLines(ManifestPath);
            string takenAt = lines.Length > 0 ? lines[0] : "unknown";
            int sceneCount = 0, assetCount = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) sceneCount++;
                else assetCount++;
            }
            EditorGUILayout.LabelField($"Last snapshot: {takenAt}  ({assetCount} asset(s), {sceneCount} scene(s))");
        }
        else
        {
            EditorGUILayout.LabelField("No snapshot taken yet.");
        }

        EditorGUILayout.Space();

        if (EditorApplication.isPlaying)
            EditorGUILayout.HelpBox("Restore is disabled while in Play Mode - stop Play Mode first.", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Take Snapshot", GUILayout.Height(30)))
                TakeSnapshot();

            GUI.enabled = File.Exists(ManifestPath) && !EditorApplication.isPlaying;
            if (GUILayout.Button("Restore From Snapshot", GUILayout.Height(30)))
                RestoreSnapshot();
            GUI.enabled = true;
        }
    }

    private void TakeSnapshot()
    {
        var guids = new List<string>();
        guids.AddRange(AssetDatabase.FindAssets("t:ScriptableObject", new[] { searchFolder }));
        guids.AddRange(AssetDatabase.FindAssets("t:Scene", new[] { searchFolder }));

        if (guids.Count == 0)
        {
            EditorUtility.DisplayDialog("Config Snapshot", $"No ScriptableObject assets or scenes found under '{searchFolder}'.", "OK");
            return;
        }

        if (Directory.Exists(SnapshotFolder))
            Directory.Delete(SnapshotFolder, recursive: true);
        Directory.CreateDirectory(SnapshotFolder);

        var manifestLines = new List<string> { DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        int copied = 0;

        foreach (var guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath)) continue;

            string sourceFullPath = Path.Combine(ProjectRoot, assetPath);
            if (!File.Exists(sourceFullPath)) continue;

            string backupFullPath = Path.Combine(SnapshotFolder, guid + ".bak");
            File.Copy(sourceFullPath, backupFullPath, overwrite: true);

            manifestLines.Add($"{guid}|{assetPath}");
            copied++;
        }

        File.WriteAllLines(ManifestPath, manifestLines);
        int sceneCopies = manifestLines.FindAll(l => l.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)).Count;
        Debug.Log($"[ConfigSnapshot] Snapshot taken - {copied} file(s) backed up to {SnapshotFolder} ({sceneCopies} scene(s), {copied - sceneCopies} asset(s)).");
        Repaint();
    }

    private void RestoreSnapshot()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Config Snapshot", "Can't restore while in Play Mode - stop Play Mode first. A scene file reload doesn't mean anything while Play Mode is running its own separate in-memory copy of the scene.", "OK");
            return;
        }

        if (!File.Exists(ManifestPath))
        {
            EditorUtility.DisplayDialog("Config Snapshot", "No snapshot found to restore.", "OK");
            return;
        }

        var lines = File.ReadAllLines(ManifestPath);
        int assetCount = Mathf.Max(0, lines.Length - 1);
        string takenAt = lines.Length > 0 ? lines[0] : "unknown";

        bool confirmed = EditorUtility.DisplayDialog(
            "Restore Config Snapshot",
            $"This will overwrite {assetCount} asset(s)/scene(s) with the values from the snapshot taken at {takenAt}, discarding any changes made since - including any UNSAVED changes in an open scene that gets restored, since it will be reloaded from disk. This cannot be undone. Continue?",
            "Restore", "Cancel");
        if (!confirmed) return;

        int restored = 0;
        int skipped = 0;
        var restoredScenePaths = new List<string>();

        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split('|');
            if (parts.Length != 2) continue;
            string guid = parts[0];
            string recordedPath = parts[1];

            string backupFullPath = Path.Combine(SnapshotFolder, guid + ".bak");
            if (!File.Exists(backupFullPath))
            {
                Debug.LogWarning($"[ConfigSnapshot] Missing backup for '{recordedPath}' (guid {guid}) - skipped.");
                skipped++;
                continue;
            }

            // Resolve the asset's CURRENT path from its guid rather than trusting the manifest's
            // recorded path, so a rename/move made between snapshot and restore doesn't silently
            // write the backup to a stale location instead of the asset's actual current one.
            string currentPath = AssetDatabase.GUIDToAssetPath(guid);
            string targetPath = !string.IsNullOrEmpty(currentPath) ? currentPath : recordedPath;
            string targetFullPath = Path.Combine(ProjectRoot, targetPath);

            if (!File.Exists(targetFullPath))
            {
                Debug.LogWarning($"[ConfigSnapshot] Original asset no longer exists at '{targetPath}' - skipped (guid {guid}).");
                skipped++;
                continue;
            }

            File.Copy(backupFullPath, targetFullPath, overwrite: true);
            restored++;

            if (targetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                restoredScenePaths.Add(targetPath);
        }

        AssetDatabase.Refresh();
        ReloadOpenScenes(restoredScenePaths);

        Debug.Log($"[ConfigSnapshot] Restore complete - {restored} file(s) restored" + (skipped > 0 ? $", {skipped} skipped (see warnings above)." : "."));
        EditorUtility.DisplayDialog("Config Snapshot", $"Restored {restored} file(s)." + (skipped > 0 ? $"\n{skipped} were skipped - see the Console." : ""), "OK");
    }

    /// <summary>
    /// AssetDatabase.Refresh() reimports a scene file's data but does NOT retroactively change
    /// what's already loaded in the Hierarchy for a scene that's currently open - the open scene
    /// stays exactly as it was in memory until explicitly reloaded. This closes and reopens
    /// (additively, so other open scenes in a multi-scene setup are left alone) each restored
    /// scene that's currently open, restoring "active scene" status if it was the active one.
    /// </summary>
    private static void ReloadOpenScenes(List<string> restoredScenePaths)
    {
        if (restoredScenePaths.Count == 0) return;

        string activeScenePath = EditorSceneManager.GetActiveScene().path;

        foreach (var path in restoredScenePaths)
        {
            var scene = EditorSceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded) continue;

            bool wasActive = activeScenePath == path;
            EditorSceneManager.CloseScene(scene, removeScene: true);
            var reopened = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            if (wasActive) EditorSceneManager.SetActiveScene(reopened);

            Debug.Log($"[ConfigSnapshot] Reloaded open scene '{path}' from its restored file.");
        }
    }
}
#endif
