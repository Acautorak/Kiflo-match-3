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
    }
}
#endif