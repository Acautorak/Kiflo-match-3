using System.IO;
using UnityEngine;

/// <summary>
/// Deliberately simple: one JSON file in persistentDataPath via JsonUtility.
/// Swap the read/write internals for PlayerPrefs, cloud saves, or encryption later
/// without touching any calling code (Board.cs only calls Save/Load/HasSave).
/// </summary>
public static class SaveSystem
{
    private const string FileName = "match3_save.json";
    private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    public static void Save(BoardSaveData data)
    {
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(data));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveSystem] Save failed: {e.Message}");
        }
    }

    public static BoardSaveData Load()
    {
        if (!File.Exists(SavePath)) return null;

        try
        {
            return JsonUtility.FromJson<BoardSaveData>(File.ReadAllText(SavePath));
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveSystem] Load failed: {e.Message}");
            return null;
        }
    }

    public static bool HasSave() => File.Exists(SavePath);

    public static void DeleteSave()
    {
        if (File.Exists(SavePath)) File.Delete(SavePath);
    }
}
