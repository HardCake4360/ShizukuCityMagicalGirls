public sealed class AchievementAccessInteractable : InteractableBase
{
    protected override void OnInteract()
    {
        AchievementPanelController panel = FindFirstObjectByType<AchievementPanelController>();
        if (panel != null)
        {
            panel.Open();
        }
    }
}
