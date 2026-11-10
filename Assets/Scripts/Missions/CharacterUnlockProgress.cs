using System.Collections.Generic;
using System;
using UnityEngine;

/// <summary>Unlocks are derived from saved achievement states, not from a second save file.</summary>
public static class CharacterUnlockProgress
{
    private static readonly HashSet<UnityEngine.Object> debugUnlockSources = new();
    public static event Action DebugUnlockOverrideChanged;
    public static bool DebugUnlockAllCharacters
    {
        get
        {
            debugUnlockSources.RemoveWhere(source => source == null);
            return debugUnlockSources.Count > 0;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetDebugUnlockOverrides()
    {
        debugUnlockSources.Clear();
        DebugUnlockOverrideChanged = null;
    }

    public static void SetDebugUnlockOverride(UnityEngine.Object source, bool enabled)
    {
        if (ReferenceEquals(source, null)) return;
        bool wasEnabled = DebugUnlockAllCharacters;
        if (enabled && source != null) debugUnlockSources.Add(source);
        else debugUnlockSources.Remove(source);
        if (wasEnabled != DebugUnlockAllCharacters) DebugUnlockOverrideChanged?.Invoke();
    }

    public static int CompletedCount => AchievementManager.TryGetExisting(out AchievementManager manager)
        ? manager.CompletedCount : 0;

    public static int CountCompleted(IEnumerable<AchievementProgress> achievements)
    {
        int count = 0;
        HashSet<string> ids = new(System.StringComparer.Ordinal);
        if (achievements == null)
            return count;
        foreach (AchievementProgress progress in achievements)
        {
            if (progress?.Definition != null && progress.State == AchievementState.Completed
                && !string.IsNullOrWhiteSpace(progress.Definition.Id) && ids.Add(progress.Definition.Id))
                count++;
        }
        return count;
    }

    public static bool IsUnlocked(PlayableCharacterDefinition character, int completedCount)
    {
        return character != null && (DebugUnlockAllCharacters || IsUnlockedByProgress(character, completedCount));
    }

    private static bool IsUnlockedByProgress(PlayableCharacterDefinition character, int completedCount)
    {
        return character != null && (character.InitiallyUnlocked
            || (character.RequiredCompletedAchievements > 0
                && completedCount >= character.RequiredCompletedAchievements));
    }

    public static PlayableCharacterDefinition FindNextUnlock(
        IEnumerable<PlayableCharacterDefinition> characters, int completedCount)
    {
        PlayableCharacterDefinition next = null;
        if (characters == null)
            return null;
        foreach (PlayableCharacterDefinition character in characters)
        {
            if (character == null || character.RequiredCompletedAchievements <= 0
                // Debug availability must not alter actual quest/unlock-progress messaging.
                || IsUnlockedByProgress(character, completedCount))
                continue;
            if (next == null || character.RequiredCompletedAchievements < next.RequiredCompletedAchievements)
                next = character;
        }
        return next;
    }
}
