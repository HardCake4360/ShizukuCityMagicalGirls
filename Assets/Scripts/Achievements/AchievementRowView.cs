using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class AchievementRowView : MonoBehaviour
{
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private TMP_Text descriptionLabel;
    [SerializeField] private TMP_Text progressLabel;
    [SerializeField] private Button completeButton;

    private AchievementProgress progress;
    private Action<string> completionRequested;

    private void Awake()
    {
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

    public void Bind(AchievementProgress achievementProgress, Action<string> onCompletionRequested)
    {
        progress = achievementProgress;
        completionRequested = onCompletionRequested;

        if (nameLabel != null)
        {
            nameLabel.text = progress.Definition.DisplayName;
        }

        if (descriptionLabel != null)
        {
            descriptionLabel.text = progress.Definition.Description;
        }

        if (progressLabel != null)
        {
            progressLabel.text = progress.State == AchievementState.Completed
                ? "Completed"
                : $"{progress.CurrentProgress} / {progress.Definition.TargetProgress}";
        }

        if (completeButton != null)
        {
            bool canComplete = progress.State == AchievementState.ConditionCleared;
            completeButton.gameObject.SetActive(canComplete);
            completeButton.interactable = canComplete;
        }
    }

    private void RequestCompletion()
    {
        if (progress != null && progress.State == AchievementState.ConditionCleared)
        {
            completionRequested?.Invoke(progress.Definition.Id);
        }
    }
}
