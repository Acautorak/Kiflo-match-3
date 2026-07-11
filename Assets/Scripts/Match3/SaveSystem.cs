using System;
using System.IO;
using UnityEngine;
using Match3.Events;

namespace Match3.Save
{
    /// <summary>
    /// Handles persistence to disk as JSON via Application.persistentDataPath.
    /// Writes to a temp file and keeps a rolling .bak copy, so a crash or an
    /// interrupted write during Save() can never leave the player with a
    /// corrupted or half-written save file.
    /// </summary>
    public static class SaveSystem
    {
        private const string FileName = "match3_save.json";
        private const string BackupSuffix = ".bak";

        private static string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        private static string BackupPath => SavePath + BackupSuffix;

        public static SaveData Current { get; private set; }

        public static void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    string json = File.ReadAllText(SavePath);
                    Current = JsonUtility.FromJson<SaveData>(json);
                }
                else if (File.Exists(BackupPath))
                {
                    string json = File.ReadAllText(BackupPath);
                    Current = JsonUtility.FromJson<SaveData>(json);
                }
                else
                {
                    Current = new SaveData();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveSystem] Load failed, starting a fresh save: {e.Message}");
                Current = new SaveData();
            }

            Current ??= new SaveData();
        }

        public static void Save()
        {
            try
            {
                if (File.Exists(SavePath))
                    File.Copy(SavePath, BackupPath, true);

                string json = JsonUtility.ToJson(Current, prettyPrint: true);
                string tempPath = SavePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Copy(tempPath, SavePath, true);
                File.Delete(tempPath);

                EventBus.Publish(new SaveCompletedEvent(true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveSystem] Save failed: {e.Message}");
                EventBus.Publish(new SaveCompletedEvent(false));
            }
        }

        public static void RecordLevelResult(int levelId, int stars, int score)
        {
            var existing = Current.Results.Find(r => r.LevelId == levelId);
            if (existing == null)
            {
                Current.Results.Add(new LevelResult { LevelId = levelId, Stars = stars, BestScore = score });
            }
            else
            {
                existing.Stars = Math.Max(existing.Stars, stars);
                existing.BestScore = Math.Max(existing.BestScore, score);
            }

            if (levelId >= Current.HighestUnlockedLevel)
                Current.HighestUnlockedLevel = levelId + 1;

            Save();
        }

        public static void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            if (File.Exists(BackupPath)) File.Delete(BackupPath);
            Current = new SaveData();
        }
    }
}
