using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class QuestSummaryController : MonoBehaviour
{
    [Header("Achievement Scope")]
    [Tooltip("지정한 ALM에 등록된 해금된 도전과제를 집계합니다. 비어 있으면 전체 해금된 도전과제를 집계합니다.")]
    [SerializeField] private AchievementListManager listManager;

    [Header("Text")]
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text completionText;
    [SerializeField] private TMP_Text chapterText;
    [SerializeField] private TMP_Text characterUnlockText;

    [Header("Value Colors")]
    [Tooltip("진행도 n%의 숫자 색상입니다.")]
    [SerializeField] private Color progressPercentColor = Color.white;
    [Tooltip("n개중 n개 완료에서 앞쪽 전체 개수의 색상입니다.")]
    [SerializeField] private Color totalAchievementCountColor = Color.white;
    [Tooltip("n개중 n개 완료에서 뒤쪽 완료 개수의 색상입니다.")]
    [SerializeField] private Color completedAchievementCountColor = Color.white;
    [Tooltip("챕터 번호의 색상입니다.")]
    [SerializeField] private Color chapterNumberColor = Color.white;
    [Tooltip("다음 캐릭터 해금까지 남은 개수의 색상입니다.")]
    [SerializeField] private Color remainingAchievementCountColor = Color.white;
    [Tooltip("해금 안내에 표시되는 캐릭터 이름의 색상입니다.")]
    [SerializeField] private Color characterNameColor = Color.white;

    [Header("Characters")]
    [Tooltip("비어 있으면 Resources/Missions/Characters의 캐릭터를 사용합니다. 해금 조건 수가 작은 순서대로 표시합니다.")]
    [SerializeField] private PlayableCharacterDefinition[] characters;

    private AchievementManager achievementManager;
    private ChapterProgressionManager chapterManager;
    private PlayableCharacterDefinition[] availableCharacters;
    private bool refreshRequested;

    private void OnValidate()
    {
        // OnValidate may run off the main thread; update TMP text on the next normal frame instead.
        refreshRequested = true;
    }

    private void LateUpdate()
    {
        if (refreshRequested)
        {
            refreshRequested = false;
            Refresh();
        }
    }

    private void OnEnable()
    {
        if (AchievementManager.IsShuttingDown)
            return;
        availableCharacters = characters != null && characters.Length > 0
            ? characters : Resources.LoadAll<PlayableCharacterDefinition>("Missions/Characters");
        achievementManager = AchievementManager.Instance;
        chapterManager = ChapterProgressionManager.Instance;
        achievementManager.ProgressChanged += HandleProgressChanged;
        chapterManager.CurrentChapterChanged += HandleChapterChanged;
        Refresh();
    }

    private void OnDisable()
    {
        if (achievementManager != null)
            achievementManager.ProgressChanged -= HandleProgressChanged;
        if (chapterManager != null)
            chapterManager.CurrentChapterChanged -= HandleChapterChanged;
    }

    public void Refresh()
    {
        refreshRequested = false;
        if (achievementManager == null)
            return;
        int total = 0;
        int completed = 0;
        HashSet<string> ids = new(StringComparer.Ordinal);
        if (listManager != null)
        {
            foreach (AchievementDefinition definition in listManager.Achievements)
            {
                if (definition != null && !string.IsNullOrWhiteSpace(definition.Id)
                    && ids.Add(definition.Id)
                    && achievementManager.TryGetProgress(definition.Id, out AchievementProgress progress))
                    Count(progress, ref total, ref completed);
            }
        }
        else
        {
            foreach (AchievementProgress progress in achievementManager.AllAchievements)
                Count(progress, ref total, ref completed);
        }

        int percent = total > 0 ? Mathf.RoundToInt(100f * completed / total) : 0;
        SetText(progressText, $"진행도 {ColorValue(percent.ToString(), progressPercentColor)}%");
        SetText(completionText, $"{ColorValue(total.ToString(), totalAchievementCountColor)}개중 "
            + $"{ColorValue(completed.ToString(), completedAchievementCountColor)}개 완료");
        ChapterDefinition chapter = chapterManager != null ? chapterManager.CurrentChapter : null;
        SetText(chapterText, chapter != null
            ? $"챕터 {ColorValue(chapter.ChapterNumber.ToString(), chapterNumberColor)}" : "챕터 --");

        // Unlocks use all saved achievements, independently of the current Quest list's scope.
        int globalCompleted = CharacterUnlockProgress.CountCompleted(achievementManager.AllAchievements);
        PlayableCharacterDefinition next = CharacterUnlockProgress.FindNextUnlock(availableCharacters, globalCompleted);
        if (next != null)
        {
            int remaining = Mathf.Max(0, next.RequiredCompletedAchievements - globalCompleted);
            string name = string.IsNullOrWhiteSpace(next.DisplayName) ? next.name : next.DisplayName;
            SetText(characterUnlockText, $"{ColorValue(remaining.ToString(), remainingAchievementCountColor)}개 클리어 시 "
                + $"{ColorValue($"<noparse>{name}</noparse>", characterNameColor)} 언락");
        }
        else
        {
            bool hasCharacters = false;
            foreach (PlayableCharacterDefinition character in availableCharacters ?? Array.Empty<PlayableCharacterDefinition>())
            {
                // Ignore legacy placeholders which have neither an initial unlock nor an unlock condition.
                if (character == null || (!character.InitiallyUnlocked && character.RequiredCompletedAchievements <= 0))
                    continue;
                hasCharacters = true;
            }
            SetText(characterUnlockText, !hasCharacters ? "캐릭터 정보 없음"
                : "모든 캐릭터 언락 완료");
        }
    }

    private static void Count(AchievementProgress progress, ref int total, ref int completed)
    {
        if (progress == null || progress.State == AchievementState.Inactive)
            return;
        total++;
        if (progress.State == AchievementState.Completed)
            completed++;
    }

    private void HandleProgressChanged(AchievementProgress _) => Refresh();
    private void HandleChapterChanged(ChapterDefinition _) => Refresh();
    private static string ColorValue(string value, Color color) =>
        $"<color=#{ColorUtility.ToHtmlStringRGBA(color)}>{value}</color>";

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
        {
            text.richText = true;
            text.overrideColorTags = false;
            text.text = value;
        }
    }
}
