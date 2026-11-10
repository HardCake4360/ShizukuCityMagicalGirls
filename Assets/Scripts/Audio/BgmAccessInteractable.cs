public sealed class BgmAccessInteractable : InteractableBase
{
    protected override void OnInteract()
    {
        BgmPlaylistPanelController panel = FindFirstObjectByType<BgmPlaylistPanelController>();
        panel?.Open();
    }
}
