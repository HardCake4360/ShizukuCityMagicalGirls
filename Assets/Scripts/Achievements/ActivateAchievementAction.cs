using UnityEngine;

[CreateAssetMenu(fileName = "ActivateAchievement", menuName = "Shizuku City/Achievement/Actions/Activate Achievement")]
public sealed class ActivateAchievementAction : AchievementCompletionAction
{
    [SerializeField] private AchievementDefinition achievementToActivate;

    public override void Execute(AchievementManager manager, AchievementProgress completedAchievement)
    {
        if (achievementToActivate != null)
        {
            manager.Activate(achievementToActivate.Id);
        }
    }
}
