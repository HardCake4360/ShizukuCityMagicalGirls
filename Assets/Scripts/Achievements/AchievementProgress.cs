using System;

public sealed class AchievementProgress
{
    public AchievementProgress(
        AchievementDefinition definition,
        AchievementState state,
        int currentProgress,
        bool completionActionsApplied = false
    )
    {
        Definition = definition;
        State = state;
        CurrentProgress = Math.Max(0, Math.Min(currentProgress, definition.TargetProgress));
        CompletionActionsApplied = completionActionsApplied;
    }

    public AchievementDefinition Definition { get; }
    public AchievementState State { get; internal set; }
    public int CurrentProgress { get; internal set; }
    public bool CompletionActionsApplied { get; internal set; }
}

[Serializable]
public sealed class AchievementProgressSaveData
{
    public string id;
    public int state;
    public int currentProgress;
    public bool completionActionsApplied;
}

[Serializable]
public sealed class AchievementSaveData
{
    public int version = 1;
    public System.Collections.Generic.List<AchievementProgressSaveData> achievements = new();
}
