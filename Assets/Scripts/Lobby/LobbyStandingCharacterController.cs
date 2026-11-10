using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Events;
using UnityEngine.Playables;

[Serializable]
public sealed class LobbyStandingCharacterEntry
{
    [SerializeField] private TraverseTarget target;
    [SerializeField] private PlayableCharacterDefinition playableCharacter;
    [SerializeField] private Sprite standingSprite;
    [SerializeField] private AnimationClip transformationBefore;
    [SerializeField] private AnimationClip transformationAfter;
    [SerializeField] private bool useCutinStartPosition;
    [Tooltip("transformCutinCircle의 anchoredPosition3D입니다. SetPositon 버튼으로 현재 위치를 저장합니다.")]
    [SerializeField] private Vector3 cutinStartPosition;

    public TraverseTarget Target => target;
    public PlayableCharacterDefinition PlayableCharacter => playableCharacter;
    public Sprite StandingSprite => standingSprite;
    public AnimationClip TransformationBefore => transformationBefore;
    public AnimationClip TransformationAfter => transformationAfter;
    public bool UseCutinStartPosition => useCutinStartPosition;
    public Vector3 CutinStartPosition => cutinStartPosition;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer), typeof(Animator))]
public sealed class LobbyStandingCharacterController : MonoBehaviour
{
    [Header("Character Focus")]
    [SerializeField] private TraverseCursor cursor;
    [SerializeField] private SpriteRenderer standingRenderer;
    [SerializeField] private List<LobbyStandingCharacterEntry> characters = new();
    [SerializeField] private bool hideWhenNoMatchingCharacter = true;

    [Header("Transformation Cut-in")]
    [Tooltip("SceneAnimator/Canvas/transformCutinCircle의 RectTransform을 할당합니다.")]
    [SerializeField] private RectTransform transformationCutinCircle;

    [Header("Transformation")]
    [SerializeField] private Animator transformationAnimator;
    [SerializeField] private bool useUnscaledTime = true;
    [SerializeField] private UnityEvent onTransformationStarted;
    [Tooltip("Invoke this after the before clip and before the after clip. Connect the future cover-panel logic here.")]
    [SerializeField] private UnityEvent onBetweenTransformationPhases;
    [SerializeField] private UnityEvent onTransformationCompleted;

    private LobbyStandingCharacterEntry focusedCharacter;
    private LobbyStandingCharacterEntry transformingCharacter;
    private LobbyStandingCharacterEntry preparedCharacter;
    private PlayableGraph animationGraph;
    private bool hasAnimationGraph;
    private Coroutine phaseCoroutine;
    private bool afterPhaseCompleted;

    public LobbyStandingCharacterEntry FocusedCharacter => focusedCharacter;
    public bool IsPlayingTransformation { get; private set; }
    public RectTransform TransformationCutinCircle => transformationCutinCircle;

    private void Reset()
    {
        standingRenderer = GetComponent<SpriteRenderer>();
        transformationAnimator = GetComponent<Animator>();
    }

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        SubscribeCursor();
        RefreshStandingImage();
    }

    private void OnDisable()
    {
        UnsubscribeCursor();
        StopPhasePlayback();
        afterPhaseCompleted = true;
        IsPlayingTransformation = false;
        preparedCharacter = null;
    }

    public void RefreshStandingImage()
    {
        ShowCharacter(cursor != null ? cursor.CurrentTarget : null);
    }

    public void ShowCharacter(TraverseTarget target)
    {
        if (IsPlayingTransformation || preparedCharacter != null)
        {
            return;
        }

        focusedCharacter = FindCharacter(target);
        if (focusedCharacter != null)
        {
            standingRenderer.sprite = focusedCharacter.StandingSprite;
            standingRenderer.enabled = focusedCharacter.StandingSprite != null;
            return;
        }

        if (hideWhenNoMatchingCharacter)
        {
            standingRenderer.enabled = false;
        }
    }

    public IEnumerator PlayFocusedTransformation()
    {
        if (!BeginFocusedTransformation())
        {
            yield break;
        }

        StartBeforeTransformation();
        while (phaseCoroutine != null)
        {
            yield return null;
        }

        NotifyBetweenTransformationPhases();
        StartAfterTransformation();
        yield return WaitForAfterTransformation();
        CompleteFocusedTransformation();
    }

    /// <summary>Snapshot the icon choice before the launch transition hides/disables the cursor.</summary>
    public bool TryPrepareLaunchCharacter(out PlayableCharacterDefinition character)
    {
        character = null;
        preparedCharacter = null;
        TraverseTarget target = cursor != null
            ? (cursor.IsSelected ? cursor.SelectedTarget : cursor.CurrentTarget)
            : focusedCharacter?.Target;
        LobbyStandingCharacterEntry entry = FindCharacter(target);
        if (entry?.PlayableCharacter == null || !entry.PlayableCharacter.IsUnlocked
            || entry.PlayableCharacter.GameplayPrefab == null)
        {
            Debug.LogError("Assign an unlocked Playable Character with a Gameplay Prefab to the focused standing entry.", this);
            return false;
        }
        preparedCharacter = entry;
        focusedCharacter = entry;
        character = entry.PlayableCharacter;
        return true;
    }

    public void CancelPreparedLaunchCharacter()
    {
        preparedCharacter = null;
        RefreshStandingImage();
    }

    public bool BeginFocusedTransformation()
    {
        if (IsPlayingTransformation)
        {
            return false;
        }

        transformingCharacter = preparedCharacter ?? focusedCharacter;
        preparedCharacter = null;
        if (transformingCharacter == null)
        {
            transformingCharacter = FindCharacter(cursor != null ? cursor.CurrentTarget : null);
        }

        focusedCharacter = transformingCharacter;
        if (transformingCharacter != null
            && (transformingCharacter.StandingSprite != null
                || transformingCharacter.TransformationBefore != null
                || transformingCharacter.TransformationAfter != null))
        {
            standingRenderer.enabled = true;
        }

        afterPhaseCompleted = false;
        IsPlayingTransformation = true;
        onTransformationStarted?.Invoke();
        return true;
    }

    public void StartBeforeTransformation()
    {
        if (!IsPlayingTransformation)
        {
            return;
        }

        StartPhase(
            transformingCharacter != null
                ? transformingCharacter.TransformationBefore
                : null,
            false
        );
    }

    public void NotifyBetweenTransformationPhases()
    {
        if (IsPlayingTransformation)
        {
            onBetweenTransformationPhases?.Invoke();
        }
    }

    /// <summary>Use the snapshotted launch character, even after the selection UI clears its focus.</summary>
    public bool ApplyFocusedCutinStartPosition()
    {
        LobbyStandingCharacterEntry character = transformingCharacter ?? preparedCharacter ?? focusedCharacter;
        if (character == null || !character.UseCutinStartPosition) return false;
        if (transformationCutinCircle == null)
        {
            Debug.LogWarning("Assign Transformation Cutin Circle before using a character cut-in start position.", this);
            return false;
        }
        transformationCutinCircle.anchoredPosition3D = character.CutinStartPosition;
        return true;
    }

    public void StartAfterTransformation()
    {
        if (!IsPlayingTransformation)
        {
            afterPhaseCompleted = true;
            return;
        }

        afterPhaseCompleted = false;
        StartPhase(
            transformingCharacter != null
                ? transformingCharacter.TransformationAfter
                : null,
            true
        );
    }

    public IEnumerator WaitForAfterTransformation()
    {
        while (IsPlayingTransformation && !afterPhaseCompleted)
        {
            yield return null;
        }
    }

    public void CompleteFocusedTransformation()
    {
        if (!IsPlayingTransformation)
        {
            return;
        }

        StopPhasePlayback();
        afterPhaseCompleted = true;
        IsPlayingTransformation = false;
        transformingCharacter = null;
        onTransformationCompleted?.Invoke();
    }

    private void StartPhase(AnimationClip clip, bool completesAfterPhase)
    {
        StopPhasePlayback();
        if (clip == null || transformationAnimator == null)
        {
            if (completesAfterPhase)
            {
                afterPhaseCompleted = true;
            }
            return;
        }

        phaseCoroutine = StartCoroutine(PlayClip(clip, completesAfterPhase));
    }

    private IEnumerator PlayClip(AnimationClip clip, bool completesAfterPhase)
    {
        animationGraph = PlayableGraph.Create($"Standing Transformation - {clip.name}");
        animationGraph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(
            animationGraph,
            "Standing Character",
            transformationAnimator
        );
        AnimationClipPlayable playable = AnimationClipPlayable.Create(animationGraph, clip);
        playable.SetApplyFootIK(false);
        playable.SetApplyPlayableIK(false);
        output.SetSourcePlayable(playable);
        hasAnimationGraph = true;
        animationGraph.Play();
        animationGraph.Evaluate(0f);

        float elapsed = 0f;
        float duration = Mathf.Max(0f, clip.length);
        while (elapsed < duration)
        {
            float delta = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (delta <= 0f)
            {
                yield return null;
                continue;
            }

            float step = Mathf.Min(delta, duration - elapsed);
            elapsed += step;
            animationGraph.Evaluate(step);
            yield return null;
        }

        Sprite finalFrameSprite = completesAfterPhase && standingRenderer != null
            ? standingRenderer.sprite
            : null;
        StopAnimationGraph();
        if (completesAfterPhase && standingRenderer != null)
        {
            standingRenderer.sprite = finalFrameSprite;
            standingRenderer.enabled = finalFrameSprite != null;
        }

        phaseCoroutine = null;
        if (completesAfterPhase)
        {
            afterPhaseCompleted = true;
        }
    }

    private LobbyStandingCharacterEntry FindCharacter(TraverseTarget target)
    {
        if (target == null)
        {
            return null;
        }

        for (int i = 0; i < characters.Count; i++)
        {
            LobbyStandingCharacterEntry character = characters[i];
            if (character != null && character.Target == target)
            {
                return character;
            }
        }

        return null;
    }

    private void HandleCurrentTargetChanged(TraverseTarget target)
    {
        ShowCharacter(target);
    }

    private void SubscribeCursor()
    {
        if (cursor == null)
        {
            return;
        }

        cursor.CurrentTargetChanged -= HandleCurrentTargetChanged;
        cursor.CurrentTargetChanged += HandleCurrentTargetChanged;
    }

    private void UnsubscribeCursor()
    {
        if (cursor != null)
        {
            cursor.CurrentTargetChanged -= HandleCurrentTargetChanged;
        }
    }

    private void ResolveReferences()
    {
        if (standingRenderer == null)
        {
            standingRenderer = GetComponent<SpriteRenderer>();
        }

        if (transformationAnimator == null)
        {
            transformationAnimator = GetComponent<Animator>();
        }
    }

    private void StopAnimationGraph()
    {
        if (!hasAnimationGraph)
        {
            return;
        }

        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }

        hasAnimationGraph = false;
    }

    private void StopPhasePlayback()
    {
        if (phaseCoroutine != null)
        {
            StopCoroutine(phaseCoroutine);
            phaseCoroutine = null;
        }

        StopAnimationGraph();
    }

    private void OnValidate()
    {
        ResolveReferences();

        HashSet<TraverseTarget> targets = new();
        for (int i = 0; i < characters.Count; i++)
        {
            LobbyStandingCharacterEntry character = characters[i];
            if (character == null || character.Target == null)
            {
                Debug.LogWarning($"Standing character entry {i} has no TraverseTarget.", this);
            }
            else if (!targets.Add(character.Target))
            {
                Debug.LogWarning($"Standing character entry {i} uses a duplicate TraverseTarget.", this);
            }
        }
    }
}
