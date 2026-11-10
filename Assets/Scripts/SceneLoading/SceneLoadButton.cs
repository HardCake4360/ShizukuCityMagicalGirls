using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public sealed class SceneLoadButton : MonoBehaviour
{
    [SerializeField] private string destinationScene;
    [SerializeField, Min(0f)] private float minimumLoadDuration = 1f;
    [SerializeField] private SceneTransitionHook specialTransition;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        SceneLoadManager.LoadingStateChanged += HandleLoadingStateChanged;
        HandleLoadingStateChanged(SceneLoadManager.IsLoadingNow);
    }

    private void OnDisable()
    {
        SceneLoadManager.LoadingStateChanged -= HandleLoadingStateChanged;
    }

    public void Load()
    {
        SceneLoadManager.Instance.LoadScene(
            destinationScene,
            minimumLoadDuration,
            specialTransition
        );
    }

    private void HandleLoadingStateChanged(bool isLoading)
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        button.interactable = !isLoading;
    }
}
