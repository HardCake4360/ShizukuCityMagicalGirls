using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class AchievementCharacterVisual
{
    [SerializeField] private AchievementCharacterType characterType;
    [SerializeField] private Sprite portrait;
    [SerializeField] private Sprite textBox;

    public AchievementCharacterType CharacterType => characterType;
    public Sprite Portrait => portrait;
    public Sprite TextBox => textBox;
}

[DisallowMultipleComponent]
public sealed class AchievementListManager : MonoBehaviour
{
    [Header("List UI")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private AchievementElement elementPrefab;

    [Header("Display Order")]
    [Tooltip("이 순서가 미달성 그룹과 달성 그룹 내부의 표시 순서가 됩니다.")]
    [SerializeField] private List<AchievementDefinition> achievements =
        new List<AchievementDefinition>();

    [Header("Character Visuals")]
    [SerializeField] private List<AchievementCharacterVisual> characterVisuals =
        new List<AchievementCharacterVisual>();

    private readonly List<AchievementElement> spawnedElements =
        new List<AchievementElement>();
    private readonly Dictionary<string, AchievementElement> elementByAchievementId =
        new Dictionary<string, AchievementElement>(StringComparer.Ordinal);
    private readonly Dictionary<AchievementCharacterType, AchievementCharacterVisual>
        visualByCharacter =
            new Dictionary<AchievementCharacterType, AchievementCharacterVisual>();

    private bool subscribed;
    private bool hasBuiltList;
    private AchievementManager subscribedAchievementManager;

    public bool HasBuiltList => hasBuiltList;
    public IReadOnlyList<AchievementDefinition> Achievements => achievements;

    private void OnEnable()
    {
        Subscribe();
    }

    private void Start()
    {
        EnsureBuilt();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    /// <summary>
    /// 현재 씬에서 아직 목록을 만들지 않았을 때만 최초 정렬 및 생성을 수행합니다.
    /// </summary>
    public void EnsureBuilt()
    {
        if (!hasBuiltList)
        {
            RebuildForSceneLoad();
        }
    }

    /// <summary>
    /// 씬 최초 로딩과 같은 전체 재생성이 필요할 때 호출합니다.
    /// 완료 처리 이벤트에서는 이 메서드를 호출하지 않습니다.
    /// </summary>
    public void RebuildForSceneLoad()
    {
        ClearElements();
        BuildVisualLookup();

        if (listRoot == null || elementPrefab == null)
        {
            Debug.LogError(
                "AchievementListManager requires both a list root and an element prefab.",
                this
            );
            hasBuiltList = false;
            return;
        }

        AchievementManager manager = AchievementManager.Instance;
        HashSet<string> addedIds = new HashSet<string>(StringComparer.Ordinal);

        // 미완료 항목을 ALM 등록 순서대로 먼저 생성합니다.
        AppendGroup(manager, false, addedIds);
        // 완료 항목은 같은 등록 순서를 유지한 채 뒤에 생성합니다.
        AppendGroup(manager, true, addedIds);
        hasBuiltList = true;
    }

    /// <summary>
    /// 현재 생성된 AE만 상태 갱신합니다. 생성/삭제/재정렬은 하지 않습니다.
    /// </summary>
    public void RefreshExistingElements()
    {
        for (int i = 0; i < spawnedElements.Count; i++)
        {
            AchievementElement element = spawnedElements[i];
            if (element?.Progress != null)
            {
                element.RefreshState(element.Progress);
            }
        }
    }

    private void AppendGroup(
        AchievementManager manager,
        bool completedGroup,
        HashSet<string> addedIds
    )
    {
        for (int i = 0; i < achievements.Count; i++)
        {
            AchievementDefinition definition = achievements[i];
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
            {
                continue;
            }

            if (addedIds.Contains(definition.Id))
            {
                continue;
            }

            if (!manager.TryGetProgress(definition.Id, out AchievementProgress progress))
            {
                Debug.LogWarning(
                    $"Achievement '{definition.Id}' is not registered in AchievementManager. "
                    + "Place its definition under Resources/Achievements.",
                    definition
                );
                continue;
            }

            if (progress.State == AchievementState.Inactive)
            {
                continue;
            }

            bool isCompleted = progress.State == AchievementState.Completed;
            if (isCompleted != completedGroup)
            {
                continue;
            }

            SpawnElement(progress);
            addedIds.Add(definition.Id);
        }
    }

    private void SpawnElement(AchievementProgress progress)
    {
        AchievementElement element = Instantiate(elementPrefab, listRoot);
        AchievementCharacterVisual visual = GetVisual(progress.Definition.CharacterType);
        element.Bind(
            progress,
            visual?.Portrait,
            visual?.TextBox,
            CompleteAchievement
        );

        spawnedElements.Add(element);
        elementByAchievementId[progress.Definition.Id] = element;
    }

    private bool CompleteAchievement(string achievementId)
    {
        return AchievementManager.Instance.TryComplete(achievementId);
    }

    private void HandleProgressChanged(AchievementProgress progress)
    {
        if (progress?.Definition == null)
        {
            return;
        }

        if (elementByAchievementId.TryGetValue(
                progress.Definition.Id,
                out AchievementElement element
            )
            && element != null)
        {
            // 제자리에서 버튼/완료 마크/Dim만 변경합니다.
            element.RefreshState(progress);
        }
    }

    private void BuildVisualLookup()
    {
        visualByCharacter.Clear();
        for (int i = 0; i < characterVisuals.Count; i++)
        {
            AchievementCharacterVisual visual = characterVisuals[i];
            if (visual == null)
            {
                continue;
            }

            if (visualByCharacter.ContainsKey(visual.CharacterType))
            {
                Debug.LogWarning(
                    $"Duplicate achievement character visual for '{visual.CharacterType}' was ignored.",
                    this
                );
                continue;
            }

            visualByCharacter.Add(visual.CharacterType, visual);
        }
    }

    private AchievementCharacterVisual GetVisual(AchievementCharacterType characterType)
    {
        visualByCharacter.TryGetValue(
            characterType,
            out AchievementCharacterVisual visual
        );
        return visual;
    }

    private void Subscribe()
    {
        if (subscribed)
        {
            return;
        }

        subscribedAchievementManager = AchievementManager.Instance;
        if (subscribedAchievementManager == null)
            return;
        subscribedAchievementManager.ProgressChanged += HandleProgressChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
        {
            return;
        }

        if (subscribedAchievementManager != null)
        {
            subscribedAchievementManager.ProgressChanged -= HandleProgressChanged;
        }
        subscribedAchievementManager = null;
        subscribed = false;
    }

    private void ClearElements()
    {
        for (int i = 0; i < spawnedElements.Count; i++)
        {
            if (spawnedElements[i] != null)
            {
                Destroy(spawnedElements[i].gameObject);
            }
        }

        spawnedElements.Clear();
        elementByAchievementId.Clear();
        hasBuiltList = false;
    }
}
