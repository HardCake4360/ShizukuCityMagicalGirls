using UnityEngine;

[CreateAssetMenu(fileName = "Character", menuName = "Shizuku City/Missions/Playable Character")]
public sealed class PlayableCharacterDefinition : ScriptableObject
{
    [SerializeField] private string id;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string description;
    [SerializeField] private Sprite portrait;
    [SerializeField] private GameObject gameplayPrefab;
    [SerializeField] private bool initiallyUnlocked = true;
    [Tooltip("기본 해금이 아닌 캐릭터를 해금하기 위해 필요한 누적 Completed 도전과제 수입니다. 0은 조건 미설정입니다.")]
    [SerializeField, Min(0)] private int requiredCompletedAchievements;

    public string Id => id;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Portrait => portrait;
    public GameObject GameplayPrefab => gameplayPrefab;
    public bool InitiallyUnlocked => initiallyUnlocked;
    public int RequiredCompletedAchievements => Mathf.Max(0, requiredCompletedAchievements);
    public bool IsUnlocked => initiallyUnlocked
        || CharacterUnlockProgress.IsUnlocked(this, CharacterUnlockProgress.CompletedCount);

    private void OnValidate()
    {
        id = id?.Trim();
        requiredCompletedAchievements = Mathf.Max(0, requiredCompletedAchievements);
    }
}
