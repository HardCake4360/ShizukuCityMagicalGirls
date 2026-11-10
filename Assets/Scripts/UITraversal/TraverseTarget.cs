using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TraverseTarget : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("Cursor Target")]
    [Tooltip("커서가 이동할 위치입니다. 비어 있으면 이 오브젝트의 Transform을 사용합니다.")]
    [SerializeField] private Transform cursorAnchor;
    [Tooltip("Cursor Anchor의 로컬 좌표 기준 오프셋입니다. 기준 Transform의 회전과 배율을 함께 반영합니다.")]
    [SerializeField] private Vector3 cursorOffset;

    [Header("Availability")]
    [Tooltip("연결된 Selectable이 상호작용 불가능하면 이 타겟도 순회에서 제외합니다.")]
    [SerializeField] private bool respectSelectableInteractable = true;
    [SerializeField] private Selectable selectable;
    [Tooltip("캐릭터 아이콘일 때 할당합니다. 미해금 캐릭터는 호버/클릭/키보드 순회 대상에서 제외됩니다.")]
    [SerializeField] private PlayableCharacterDefinition requiredCharacter;

    [Header("Selection Visual")]
    [Tooltip("지정하면 선택 시 투명도 처리에 사용합니다. 비어 있으면 이 오브젝트의 CanvasGroup 또는 하위 Graphic을 자동으로 사용합니다.")]
    [SerializeField] private CanvasGroup opacityCanvasGroup;

    [Header("Locked Character Visual")]
    [SerializeField, Range(0f, 1f)] private float lockedCharacterAlpha = 0.4f;
    [SerializeField, Min(0f)] private float unlockDimTransitionDuration = 0.2f;
    [SerializeField] private AnimationCurve unlockDimTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    public event Action<TraverseTarget> PointerEntered;
    public event Action<TraverseTarget> PointerExited;
    public event Action<TraverseTarget, PointerEventData.InputButton> PointerClicked;

    public Transform CursorAnchor => cursorAnchor != null ? cursorAnchor : transform;
    public Vector3 CursorOffset => cursorOffset;
    public Vector3 CursorWorldPosition => CursorAnchor.TransformPoint(cursorOffset);
    public bool IsPointerHovering { get; private set; }

    public void SetCursorOffset(Vector3 offset) => cursorOffset = offset;

    private Graphic[] opacityGraphics;
    private Color[] originalGraphicColors;
    private float originalCanvasGroupAlpha;
    private bool hasOpacityOverride;
    private Coroutine opacityCoroutine;
    private float pendingOpacityMultiplier = 1f;
    private bool clearOverrideAfterPendingTransition;
    private float selectionOpacityMultiplier = 1f;
    private bool isCharacterLocked;
    private AchievementManager subscribedAchievementManager;

    public bool IsTraversable
    {
        get
        {
            if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
            {
                return false;
            }

            if (requiredCharacter != null && !requiredCharacter.IsUnlocked)
                return false;

            return !respectSelectableInteractable
                || selectable == null
                || selectable.IsInteractable();
        }
    }

    private void Reset()
    {
        cursorAnchor = transform;
        selectable = GetComponent<Selectable>();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CharacterUnlockProgress.DebugUnlockOverrideChanged -= HandleDebugUnlockChanged;
        CharacterUnlockProgress.DebugUnlockOverrideChanged += HandleDebugUnlockChanged;
        if (requiredCharacter != null && !AchievementManager.IsShuttingDown)
        {
            subscribedAchievementManager = AchievementManager.Instance;
            if (subscribedAchievementManager != null)
                subscribedAchievementManager.ProgressChanged += HandleAchievementChanged;
        }
        RefreshUnlockVisual(true);
    }

    private void OnDisable()
    {
        CharacterUnlockProgress.DebugUnlockOverrideChanged -= HandleDebugUnlockChanged;
        if (subscribedAchievementManager != null)
            subscribedAchievementManager.ProgressChanged -= HandleAchievementChanged;
        subscribedAchievementManager = null;
        if (opacityCoroutine != null)
        {
            StopOpacityTransition();
            ApplyOpacityMultiplier(pendingOpacityMultiplier);
            if (clearOverrideAfterPendingTransition)
            {
                hasOpacityOverride = false;
            }
        }

        if (IsPointerHovering)
        {
            IsPointerHovering = false;
            PointerExited?.Invoke(this);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        IsPointerHovering = true;
        PointerEntered?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        IsPointerHovering = false;
        PointerExited?.Invoke(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        PointerClicked?.Invoke(this, eventData.button);
    }

    public void SetDimmed(bool dimmed, float alphaMultiplier)
    {
        SetDimmed(dimmed, alphaMultiplier, 0f, null, true);
    }

    public void SetDimmed(
        bool dimmed,
        float alphaMultiplier,
        float duration,
        AnimationCurve curve,
        bool useUnscaledTime
    )
    {
        selectionOpacityMultiplier = dimmed ? Mathf.Clamp01(alphaMultiplier) : 1f;
        isCharacterLocked = requiredCharacter != null && !requiredCharacter.IsUnlocked;
        ApplyCombinedOpacity(duration, curve, useUnscaledTime);
    }

    public void RestoreOpacity()
    {
        RestoreOpacity(0f, null, true);
    }

    public void RestoreOpacity(
        float duration,
        AnimationCurve curve,
        bool useUnscaledTime
    )
    {
        // Cursor cancellation only removes selection dimming, never the character lock dimming.
        selectionOpacityMultiplier = 1f;
        isCharacterLocked = requiredCharacter != null && !requiredCharacter.IsUnlocked;
        ApplyCombinedOpacity(duration, curve, useUnscaledTime);
    }

    public void RefreshUnlockVisual(bool immediate = false)
    {
        isCharacterLocked = requiredCharacter != null && !requiredCharacter.IsUnlocked;
        ApplyCombinedOpacity(immediate ? 0f : unlockDimTransitionDuration, unlockDimTransitionCurve, true);
    }

    private void HandleAchievementChanged(AchievementProgress _)
    {
        bool locked = requiredCharacter != null && !requiredCharacter.IsUnlocked;
        if (locked != isCharacterLocked)
            RefreshUnlockVisual();
    }

    private void HandleDebugUnlockChanged() => RefreshUnlockVisual(!Application.isPlaying);

    private void ApplyCombinedOpacity(float duration, AnimationCurve curve, bool useUnscaledTime)
    {
        // Use the darker of the two reasons; do not multiply 0.4 * 0.4 for locked unselected icons.
        float multiplier = Mathf.Min(selectionOpacityMultiplier,
            isCharacterLocked ? Mathf.Clamp01(lockedCharacterAlpha) : 1f);
        bool restoreOriginal = multiplier >= 1f;
        if (!hasOpacityOverride && !restoreOriginal)
            CaptureOpacity();
        if (!hasOpacityOverride)
            return;
        StartOpacityTransition(multiplier, restoreOriginal, duration, curve, useUnscaledTime);
    }

    private void StartOpacityTransition(
        float alphaMultiplier,
        bool clearOverrideAtCompletion,
        float duration,
        AnimationCurve curve,
        bool useUnscaledTime
    )
    {
        StopOpacityTransition();
        pendingOpacityMultiplier = alphaMultiplier;
        clearOverrideAfterPendingTransition = clearOverrideAtCompletion;
        if (duration <= 0f || !isActiveAndEnabled)
        {
            ApplyOpacityMultiplier(alphaMultiplier);
            if (clearOverrideAtCompletion)
            {
                hasOpacityOverride = false;
            }
            return;
        }

        opacityCoroutine = StartCoroutine(
            AnimateOpacity(
                alphaMultiplier,
                clearOverrideAtCompletion,
                duration,
                curve,
                useUnscaledTime
            )
        );
    }

    private IEnumerator AnimateOpacity(
        float alphaMultiplier,
        bool clearOverrideAtCompletion,
        float duration,
        AnimationCurve curve,
        bool useUnscaledTime
    )
    {
        float elapsed = 0f;
        if (opacityCanvasGroup != null)
        {
            float startAlpha = opacityCanvasGroup.alpha;
            float targetAlpha = originalCanvasGroupAlpha * alphaMultiplier;
            while (elapsed < duration)
            {
                elapsed += useUnscaledTime
                    ? Time.unscaledDeltaTime
                    : Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float curvedProgress = curve != null
                    ? curve.Evaluate(progress)
                    : progress;
                opacityCanvasGroup.alpha = Mathf.LerpUnclamped(
                    startAlpha,
                    targetAlpha,
                    curvedProgress
                );
                yield return null;
            }
        }
        else if (opacityGraphics != null && originalGraphicColors != null)
        {
            int count = Mathf.Min(opacityGraphics.Length, originalGraphicColors.Length);
            Color[] startColors = new Color[count];
            Color[] targetColors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                Graphic graphic = opacityGraphics[i];
                startColors[i] = graphic != null ? graphic.color : Color.white;
                targetColors[i] = originalGraphicColors[i];
                targetColors[i].a *= alphaMultiplier;
            }

            while (elapsed < duration)
            {
                elapsed += useUnscaledTime
                    ? Time.unscaledDeltaTime
                    : Time.deltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float curvedProgress = curve != null
                    ? curve.Evaluate(progress)
                    : progress;
                for (int i = 0; i < count; i++)
                {
                    if (opacityGraphics[i] != null)
                    {
                        opacityGraphics[i].color = Color.LerpUnclamped(
                            startColors[i],
                            targetColors[i],
                            curvedProgress
                        );
                    }
                }
                yield return null;
            }
        }

        ApplyOpacityMultiplier(alphaMultiplier);
        if (clearOverrideAtCompletion)
        {
            hasOpacityOverride = false;
        }
        opacityCoroutine = null;
    }

    private void ApplyOpacityMultiplier(float alphaMultiplier)
    {
        if (opacityCanvasGroup != null)
        {
            opacityCanvasGroup.alpha = originalCanvasGroupAlpha * alphaMultiplier;
            return;
        }

        if (opacityGraphics == null || originalGraphicColors == null)
        {
            return;
        }

        int count = Mathf.Min(opacityGraphics.Length, originalGraphicColors.Length);
        for (int i = 0; i < count; i++)
        {
            Graphic graphic = opacityGraphics[i];
            if (graphic == null)
            {
                continue;
            }

            Color color = originalGraphicColors[i];
            color.a *= alphaMultiplier;
            graphic.color = color;
        }
    }

    private void StopOpacityTransition()
    {
        if (opacityCoroutine == null)
        {
            return;
        }

        StopCoroutine(opacityCoroutine);
        opacityCoroutine = null;
    }

    private void CaptureOpacity()
    {
        ResolveOpacityReferences();
        if (opacityCanvasGroup != null)
        {
            originalCanvasGroupAlpha = opacityCanvasGroup.alpha;
            hasOpacityOverride = true;
            return;
        }

        if (opacityGraphics == null || opacityGraphics.Length == 0)
        {
            return;
        }

        originalGraphicColors = new Color[opacityGraphics.Length];
        for (int i = 0; i < opacityGraphics.Length; i++)
        {
            originalGraphicColors[i] = opacityGraphics[i] != null
                ? opacityGraphics[i].color
                : Color.white;
        }
        hasOpacityOverride = true;
    }

    private void OnValidate()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (cursorAnchor == null)
        {
            cursorAnchor = transform;
        }

        if (selectable == null)
        {
            selectable = GetComponent<Selectable>();
        }

        ResolveOpacityReferences();
    }

    private void ResolveOpacityReferences()
    {
        if (opacityCanvasGroup == null)
        {
            opacityCanvasGroup = GetComponent<CanvasGroup>();
        }

        if (opacityCanvasGroup == null
            && (opacityGraphics == null || opacityGraphics.Length == 0))
        {
            opacityGraphics = GetComponentsInChildren<Graphic>(true);
        }
    }
}
