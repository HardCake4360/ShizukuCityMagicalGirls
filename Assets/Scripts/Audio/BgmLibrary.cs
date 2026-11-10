using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;

[Serializable]
public sealed class BgmTrackDisplayInfo
{
    [SerializeField] private BgmKey key;
    [SerializeField] private string displayTitle;
    [SerializeField] private string displayId;

    public BgmKey Key => key;
    public string DisplayTitle => displayTitle;
    public string DisplayId => displayId;
}

[CreateAssetMenu(fileName = "BgmLibrary", menuName = "Shizuku City/Audio/BGM Library")]
public sealed class BgmLibrary : ScriptableObject
{
    [SerializedDictionary("BGM Key", "Audio Clip")]
    [SerializeField] private SerializedDictionary<BgmKey, AudioClip> clips = new();

    [Header("UI Display Data")]
    [SerializeField] private List<BgmTrackDisplayInfo> displayInfo = new();

    public bool TryGetClip(BgmKey key, out AudioClip clip)
    {
        if (key == BgmKey.None)
        {
            clip = null;
            return false;
        }

        return clips.TryGetValue(key, out clip) && clip != null;
    }

    public bool TryGetDisplayInfo(BgmKey key, out BgmTrackDisplayInfo info)
    {
        for (int i = 0; i < displayInfo.Count; i++)
        {
            BgmTrackDisplayInfo candidate = displayInfo[i];
            if (candidate != null && candidate.Key == key)
            {
                info = candidate;
                return true;
            }
        }

        info = null;
        return false;
    }

    private void OnValidate()
    {
        foreach (var entry in clips)
        {
            if (entry.Key == BgmKey.None)
            {
                Debug.LogWarning("BGM Library contains the reserved None key.", this);
            }
            else if (entry.Value == null)
            {
                Debug.LogWarning($"BGM key '{entry.Key}' has no AudioClip assigned.", this);
            }
        }

        HashSet<BgmKey> displayKeys = new();
        for (int i = 0; i < displayInfo.Count; i++)
        {
            BgmTrackDisplayInfo info = displayInfo[i];
            if (info == null || info.Key == BgmKey.None)
            {
                continue;
            }

            if (!displayKeys.Add(info.Key))
            {
                Debug.LogWarning(
                    $"BGM display data contains duplicate key '{info.Key}'.",
                    this
                );
            }
        }
    }
}
