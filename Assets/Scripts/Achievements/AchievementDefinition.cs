using UnityEngine;

public enum AchievementCharacterType
{
    None = 0,
    Rikka = 1,
    Yukari = 2,
    Sera = 3,
    Arisa = 4,
    Mayu = 5
}

[CreateAssetMenu(fileName = "Achievement", menuName = "Shizuku City/Achievement/Definition")]
public sealed class AchievementDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;

    [Header("Character")]
    [SerializeField] private AchievementCharacterType characterType;

    [Header("Activation")]
    [SerializeField] private bool initiallyActive;

    [Header("Condition")]
    [SerializeField] private AchievementEventType eventType;
    [SerializeField] private string customEventId;
    [SerializeField, Min(1)] private int targetProgress = 1;

    [Header("Completion Actions")]
    [SerializeField] private AchievementCompletionAction[] completionActions;

    public string Id => id;
    public string DisplayName => displayName;
    public string Description => description;
    public AchievementCharacterType CharacterType => characterType;
    public bool InitiallyActive => initiallyActive;
    public AchievementEventType EventType => eventType;
    public string CustomEventId => customEventId;
    public int TargetProgress => Mathf.Max(1, targetProgress);
    public AchievementCompletionAction[] CompletionActions => completionActions;

    public bool Matches(AchievementEventType reportedType, string reportedCustomEventId)
    {
        if (eventType != reportedType)
        {
            return false;
        }

        return eventType != AchievementEventType.Custom
            || string.Equals(customEventId, reportedCustomEventId, System.StringComparison.Ordinal);
    }

    private void OnValidate()
    {
        id = id?.Trim();
        targetProgress = Mathf.Max(1, targetProgress);
    }
}
