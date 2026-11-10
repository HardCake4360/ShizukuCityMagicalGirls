using NUnit.Framework;
using UnityEngine;

public sealed class AchievementSaveDataTests
{
    [Test]
    public void JsonRoundTripPreservesPersistentAchievementFields()
    {
        AchievementSaveData source = new AchievementSaveData();
        source.achievements.Add(new AchievementProgressSaveData
        {
            id = "first_battle",
            state = (int)AchievementState.ConditionCleared,
            currentProgress = 7,
            completionActionsApplied = false
        });

        string json = JsonUtility.ToJson(source);
        AchievementSaveData restored = JsonUtility.FromJson<AchievementSaveData>(json);

        Assert.That(restored, Is.Not.Null);
        Assert.That(restored.version, Is.EqualTo(1));
        Assert.That(restored.achievements, Has.Count.EqualTo(1));
        Assert.That(restored.achievements[0].id, Is.EqualTo("first_battle"));
        Assert.That(
            restored.achievements[0].state,
            Is.EqualTo((int)AchievementState.ConditionCleared)
        );
        Assert.That(restored.achievements[0].currentProgress, Is.EqualTo(7));
        Assert.That(restored.achievements[0].completionActionsApplied, Is.False);
    }

    [Test]
    public void JsonRoundTripPreservesCompletedActionApplicationState()
    {
        AchievementSaveData source = new AchievementSaveData();
        source.achievements.Add(new AchievementProgressSaveData
        {
            id = "completed_challenge",
            state = (int)AchievementState.Completed,
            currentProgress = 1,
            completionActionsApplied = true
        });

        AchievementSaveData restored = JsonUtility.FromJson<AchievementSaveData>(
            JsonUtility.ToJson(source)
        );

        Assert.That(restored.achievements[0].completionActionsApplied, Is.True);
    }
}
