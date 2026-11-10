using UnityEngine;

public abstract class AchievementCompletionAction : ScriptableObject
{
    public abstract void Execute(AchievementManager manager, AchievementProgress completedAchievement);
}
