using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
public sealed class AchievementElement : MonoBehaviour
{
    [Header("Character Visual")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private Image textBoxImage;

    [Header("Text")]
    [SerializeField] private TMP_Text achievementNameText;
    [SerializeField] private TMP_Text descriptionText;

    [Header("Completion")]
    [Tooltip("ConditionCleared 상태일 때만 표시되는 완료 버튼입니다.")]
    [SerializeField] private Button completeButton;
    [Tooltip("Completed 상태일 때 표시할 이미지 슬롯입니다.")]
    [SerializeField] private Image completionImage;
    [Tooltip("완료 이미지 슬롯에 표시할 스프라이트입니다.")]
    [SerializeField] private Sprite completionSprite;

    [Header("Completed Dimming")]
    [SerializeField] private CanvasGroup dimCanvasGroup;
    [SerializeField, Range(0f, 1f)] private float completedAlpha = 0.45f;

    private AchievementProgress progress;
    private Func<string, bool> completionRequested;
    private float normalAlpha = 1f;
    private bool hasCapturedNormalAlpha;

    public AchievementProgress Progress => progress;
    public string AchievementId => progress?.Definition?.Id;

    private void Reset()
    {
        dimCanvasGroup = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        ResolveReferences();
        CaptureNormalAlpha();

        if (completeButton != null)
        {
            completeButton.onClick.AddListener(RequestCompletion);
        }
    }

    private void OnDestroy()
    {
        if (completeButton != null)
        {
            completeButton.onClick.RemoveListener(RequestCompletion);
        }
    }

    public void Bind(
        AchievementProgress achievementProgress,
        Sprite portraitSprite,
        Sprite textBoxSprite,
        Func<string, bool> onCompletionRequested
    )
    {
        progress = achievementProgress;
        completionRequested = onCompletionRequested;
        ResolveReferences();
        CaptureNormalAlpha();

        if (portraitImage != null)
        {
            portraitImage.sprite = portraitSprite;
            portraitImage.enabled = portraitSprite != null;
        }

        if (textBoxImage != null)
        {
            textBoxImage.sprite = textBoxSprite;
            textBoxImage.enabled = textBoxSprite != null;
        }

        if (achievementNameText != null)
        {
            achievementNameText.text = progress?.Definition?.DisplayName ?? string.Empty;
        }

        if (descriptionText != null)
        {
            descriptionText.text = progress?.Definition?.Description ?? string.Empty;
        }

        RefreshState(achievementProgress);
    }

    public void RefreshState(AchievementProgress achievementProgress)
    {
        if (achievementProgress == null)
        {
            return;
        }

        if (progress != null
            && progress.Definition != achievementProgress.Definition
            && progress.Definition.Id != achievementProgress.Definition.Id)
        {
            return;
        }

        progress = achievementProgress;
        bool conditionCleared = progress.State == AchievementState.ConditionCleared;
        bool completed = progress.State == AchievementState.Completed;

        if (completeButton != null)
        {
            completeButton.gameObject.SetActive(conditionCleared);
            completeButton.interactable = conditionCleared;
        }

        if (completionImage != null)
        {
            if (completionSprite != null)
            {
                completionImage.sprite = completionSprite;
            }

            completionImage.gameObject.SetActive(completed);
        }

        ApplyCompletedDimming(completed);
    }

    private void RequestCompletion()
    {
        if (progress == null || progress.State != AchievementState.ConditionCleared)
        {
            return;
        }

        if (completeButton != null)
        {
            completeButton.interactable = false;
        }

        bool completed = completionRequested?.Invoke(progress.Definition.Id) ?? false;
        if (!completed)
        {
            RefreshState(progress);
        }
    }

    private void ApplyCompletedDimming(bool completed)
    {
        if (dimCanvasGroup == null)
        {
            return;
        }

        CaptureNormalAlpha();
        dimCanvasGroup.alpha = completed
            ? normalAlpha * completedAlpha
            : normalAlpha;
    }

    private void ResolveReferences()
    {
        if (dimCanvasGroup == null)
        {
            dimCanvasGroup = GetComponent<CanvasGroup>();
        }
    }

    private void CaptureNormalAlpha()
    {
        if (hasCapturedNormalAlpha || dimCanvasGroup == null)
        {
            return;
        }

        normalAlpha = dimCanvasGroup.alpha;
        hasCapturedNormalAlpha = true;
    }

    private void OnValidate()
    {
        completedAlpha = Mathf.Clamp01(completedAlpha);
        ResolveReferences();
    }
}
