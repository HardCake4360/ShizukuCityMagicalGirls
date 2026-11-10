using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class BgmVolumeControlUI : MonoBehaviour
{
    private enum HoverArea
    {
        Button,
        Slider
    }

    [Header("UI")]
    [SerializeField] private Button volumeButton;
    [SerializeField] private GameObject sliderRoot;
    [SerializeField] private Slider volumeSlider;
    [SerializeField, Min(0f)] private float hideDelay = 0.12f;

    private BgmManager manager;
    private Coroutine hideCoroutine;
    private bool pointerOverButton;
    private bool pointerOverSlider;
    private bool suppressSliderCallback;

    private void Awake()
    {
        EnsureHoverArea(volumeButton != null ? volumeButton.gameObject : null, HoverArea.Button);
        EnsureHoverArea(sliderRoot, HoverArea.Slider);

        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.wholeNumbers = false;
            volumeSlider.onValueChanged.AddListener(SetVolumeFromSlider);
        }

        CloseImmediate();
    }

    private void OnEnable()
    {
        manager = BgmManager.Instance;
        manager.VolumeChanged += HandleVolumeChanged;
        HandleVolumeChanged(manager.Volume);
    }

    private void OnDisable()
    {
        if (manager != null)
        {
            manager.VolumeChanged -= HandleVolumeChanged;
        }

        StopHideCoroutine();
        pointerOverButton = false;
        pointerOverSlider = false;
        SetSliderRootActive(false);
    }

    private void OnDestroy()
    {
        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveListener(SetVolumeFromSlider);
        }
    }

    public void CloseImmediate()
    {
        StopHideCoroutine();
        pointerOverButton = false;
        pointerOverSlider = false;
        SetSliderRootActive(false);
    }

    private void SetVolumeFromSlider(float value)
    {
        if (!suppressSliderCallback)
        {
            BgmManager.Instance.SetVolume(value);
        }
    }

    private void HandleVolumeChanged(float value)
    {
        if (volumeSlider == null)
        {
            return;
        }

        suppressSliderCallback = true;
        volumeSlider.SetValueWithoutNotify(value);
        suppressSliderCallback = false;
    }

    private void NotifyPointerState(HoverArea area, bool hovering)
    {
        if (area == HoverArea.Button)
        {
            pointerOverButton = hovering;
        }
        else
        {
            pointerOverSlider = hovering;
        }

        if (hovering)
        {
            StopHideCoroutine();
            SetSliderRootActive(true);
        }
        else
        {
            ScheduleHide();
        }
    }

    private void ScheduleHide()
    {
        StopHideCoroutine();
        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        if (hideDelay > 0f)
        {
            yield return new WaitForSecondsRealtime(hideDelay);
        }

        hideCoroutine = null;
        if (!pointerOverButton && !pointerOverSlider)
        {
            SetSliderRootActive(false);
        }
    }

    private void StopHideCoroutine()
    {
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }
    }

    private void SetSliderRootActive(bool active)
    {
        if (sliderRoot != null && sliderRoot.activeSelf != active)
        {
            sliderRoot.SetActive(active);
        }
    }

    private void EnsureHoverArea(GameObject target, HoverArea area)
    {
        if (target == null)
        {
            return;
        }

        BgmVolumeHoverArea hoverArea = target.GetComponent<BgmVolumeHoverArea>();
        if (hoverArea == null)
        {
            hoverArea = target.AddComponent<BgmVolumeHoverArea>();
        }

        hoverArea.Initialize(
            hovering => NotifyPointerState(area, hovering)
        );
    }

    private void OnValidate()
    {
        hideDelay = Mathf.Max(0f, hideDelay);
    }
}

[DisallowMultipleComponent]
public sealed class BgmVolumeHoverArea : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler
{
    private System.Action<bool> pointerStateChanged;

    public void Initialize(System.Action<bool> callback)
    {
        pointerStateChanged = callback;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerStateChanged?.Invoke(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerStateChanged?.Invoke(false);
    }
}
