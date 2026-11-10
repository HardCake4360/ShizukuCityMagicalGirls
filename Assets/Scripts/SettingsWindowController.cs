using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Keep this controller outside the panel so closed settings can still receive input.</summary>
[DisallowMultipleComponent]
public sealed class SettingsWindowController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button settingsButton;
    [Tooltip("Settings window root containing SettingsPanelController. Must not contain this controller.")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private bool startOpen;
    [SerializeField] private bool toggleWithEscape = true;

    private Button boundButton;
    private InputAction toggleAction;

    public bool IsOpen => settingsPanel != null && settingsPanel.activeSelf;
    private bool HasSafePanel => settingsPanel != null && settingsPanel != gameObject
        && !transform.IsChildOf(settingsPanel.transform);

    private void Awake()
    {
        if (settingsPanel != null && !HasSafePanel)
        {
            Debug.LogError("SettingsWindowController must be placed outside the settings panel on an always-active object.", this);
            return;
        }
        SetOpen(startOpen);
    }

    private void OnEnable()
    {
        UnbindButton();
        boundButton = settingsButton;
        if (boundButton != null) boundButton.onClick.AddListener(Toggle);
        if (toggleAction == null)
            toggleAction = new InputAction("Toggle Settings", InputActionType.Button, "<Keyboard>/escape");
        toggleAction.performed -= HandleEscape;
        toggleAction.performed += HandleEscape;
        toggleAction.Enable();
    }

    private void OnDisable()
    {
        UnbindButton();
        if (toggleAction != null)
        {
            toggleAction.performed -= HandleEscape;
            toggleAction.Disable();
        }
        Close();
    }

    private void OnDestroy() => toggleAction?.Dispose();

    private void UnbindButton()
    {
        if (boundButton != null) boundButton.onClick.RemoveListener(Toggle);
        boundButton = null;
    }

    private void HandleEscape(InputAction.CallbackContext context)
    {
        if (toggleWithEscape) Toggle();
    }

    public void Toggle() => SetOpen(!IsOpen);
    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);
    public void SetOpen(bool open)
    {
        if (!HasSafePanel || settingsPanel.activeSelf == open) return;
        settingsPanel.SetActive(open);
        if (!open) GameSettings.Save();
    }
}
