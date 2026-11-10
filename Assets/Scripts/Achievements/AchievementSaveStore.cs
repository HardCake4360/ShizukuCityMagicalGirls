using System;
using System.IO;
using UnityEngine;

public interface IAchievementSaveStore
{
    AchievementSaveData Load();
    void Save(AchievementSaveData data);
}

public sealed class JsonAchievementSaveStore : IAchievementSaveStore
{
    private const string FileName = "achievements.json";

    private string SavePath => Path.Combine(Application.persistentDataPath, FileName);

    public AchievementSaveData Load()
    {
        if (TryLoadFile(SavePath, out AchievementSaveData saveData))
        {
            return saveData;
        }

        string backupPath = SavePath + ".bak";
        if (TryLoadFile(backupPath, out saveData))
        {
            Debug.LogWarning("Primary achievement save was unavailable. Loaded the backup save instead.");
            return saveData;
        }

        return new AchievementSaveData();
    }

    public void Save(AchievementSaveData data)
    {
        try
        {
            string directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temporaryPath = SavePath + ".tmp";
            string backupPath = SavePath + ".bak";
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(data, true));

            if (File.Exists(SavePath))
            {
                File.Replace(temporaryPath, SavePath, backupPath);
            }
            else
            {
                File.Move(temporaryPath, SavePath);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"Failed to save achievement data: {exception.Message}");
        }
    }

    private static bool TryLoadFile(string path, out AchievementSaveData saveData)
    {
        saveData = null;
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            saveData = JsonUtility.FromJson<AchievementSaveData>(json);
            return saveData != null;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Failed to load achievement save '{path}': {exception.Message}");
            return false;
        }
    }
}
