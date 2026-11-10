using System.IO;
using UnityEditor;
using UnityEngine;

public static class AchievementSaveDebugTools
{
    [MenuItem("Tools/Achievements/Delete Local Save")]
    private static void DeleteLocalSave()
    {
        string savePath = Path.Combine(Application.persistentDataPath, "achievements.json");
        DeleteIfExists(savePath);
        DeleteIfExists(savePath + ".tmp");
        DeleteIfExists(savePath + ".bak");
        Debug.Log($"Achievement save data deleted from '{savePath}'. Restart Play Mode to reload defaults.");
    }

    [MenuItem("Tools/Achievements/Delete Local Save", true)]
    private static bool CanDeleteLocalSave()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
