using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Animator))]
public sealed class LobbyMissionReadyController : MonoBehaviour
{
    private const string MissionReadyState = "mission_ready";
    private const string ReturnState = "return";
    private const string DefaultTransformationCutinState = "transformCutin";
    private const string LaunchBackgroundPath = "시즈쿠공립고교 외관";
    private const string LobbyUiState = "Lobby";
    private const string LaunchReadyUiState = "launch_ready";
    private const int LaunchReadyTransitionIndex = 0;
    private const int LaunchTransitionIndex = 2;

    [Header("Scene Animation")]
    [SerializeField] private Animator sceneAnimator;
    [SerializeField] private Transform lobbyCamera;
    [Tooltip("Legacy Square keyed by the salute/return clips. Only cached/restored; not moved by the code transition.")]
    [SerializeField] private Transform coverTile;
    [Tooltip("SceneAnimator/BlackTile SpriteRenderer. Code transitions change only its alpha, never its transform.")]
    [SerializeField] private SpriteRenderer blackTile;
    [Tooltip("Transforms keyed by the salute/return clips. Do not include Canvas or UI transforms.")]
    [SerializeField] private List<Transform> animationTargets = new();
    [SerializeField, Min(0.01f)] private float codeTransitionDuration = 0.35f;
    [SerializeField] private AnimationCurve codeTransitionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Camera Transition Points (Optional)")]
    [Tooltip("Code ascent destination. Uses this Transform's world position, but preserves the camera's lobby X in camera-parent space. Keep Z at the camera depth. Empty uses salute's first-frame position.")]
    [SerializeField] private Transform ascentDestination;
    [Tooltip("Code descent start after return finishes. Uses only this Transform's world Y; keeps the camera's current world X/Z. Empty uses the camera position at return's end.")]
    [SerializeField] private Transform descentStartPoint;

    [Header("Buttons")]
    [SerializeField] private Button sortieButton;
    [SerializeField] private Button backButton;
    [SerializeField] private GameObject missionSelectionPanel;
    [SerializeField] private TMP_FontAsset draftUiFont;

    [Header("UI State")]
    [SerializeField] private UIAnimator uiAnimator;
    [Tooltip("Lobby-only UI parent. If unassigned, finds lobbyUI in this scene and adds a CanvasGroup if needed.")]
    [SerializeField] private CanvasGroup lobbyUiCanvasGroup;

    [Header("Standing Character")]
    [SerializeField] private LobbyStandingCharacterController standingCharacterController;
    [SerializeField] private string transformationCutinState = DefaultTransformationCutinState;
    [Tooltip("Seconds from the start of the before transformation clip to the start of the cut-in.")]
    [SerializeField, Min(0f)] private float beforeToCutinDelay = 0.5f;
    [Tooltip("Seconds from the start of the cut-in until white-out/loading. The after clip now plays at battle zoom-in.")]
    [SerializeField, Min(0f)] private float cutinToAfterDelay = 0.5f;
    [SerializeField] private bool useUnscaledLaunchTiming = true;
    [SerializeField, Min(0f)] private float whiteOutDuration = 0.4f;

    private Coroutine transitionCoroutine;
    private bool isReady;
    private MissionSelectionPanelController missionSelectionController;
    private LobbyCameraController lobbyCameraController;
    private Vector3 lobbyCameraPositionAtEntry;
    private Vector3 coveredCameraPosition;
    private readonly List<TransformPose> lobbyAnimationTargetPoses = new();

    private void Awake()
    {
        ResolveLobbyUiCanvasGroup();
        if (sceneAnimator == null)
        {
            sceneAnimator = GetComponent<Animator>();
        }

        if (lobbyCamera == null)
        {
            Camera mainCamera = Camera.main;
            lobbyCamera = mainCamera != null ? mainCamera.transform : sceneAnimator.transform.Find("Main Camera");
        }
        if (coverTile == null)
        {
            coverTile = sceneAnimator.transform.Find("Square");
        }
        if (blackTile == null)
        {
            Transform blackTileTransform = sceneAnimator.transform.Find("BlackTile");
            if (blackTileTransform != null)
                blackTile = blackTileTransform.GetComponent<SpriteRenderer>();
        }
        EnsureAnimationTarget(lobbyCamera);
        EnsureAnimationTarget(coverTile);
        EnsureAnimationTarget(sceneAnimator.transform.Find(LaunchBackgroundPath));

        if (lobbyCamera != null)
        {
            lobbyCameraController = lobbyCamera.GetComponent<LobbyCameraController>();
        }

        if (uiAnimator == null)
        {
            uiAnimator = FindFirstObjectByType<UIAnimator>();
        }
        if (standingCharacterController == null)
        {
            standingCharacterController = FindFirstObjectByType<LobbyStandingCharacterController>();
        }

        if (sortieButton == null || backButton == null || uiAnimator == null)
        {
            Debug.LogError(
                "Assign Sortie Button, Back Button, and UIAnimator to LobbyMissionReadyController.",
                this
            );
            enabled = false;
            return;
        }

        if (missionSelectionPanel == null)
        {
            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas != null)
            {
                missionSelectionPanel = MissionSelectionPanelController.CreateRuntimeDraft(
                    canvas.transform,
                    draftUiFont
                );
            }
            else
            {
                Debug.LogWarning("A Canvas was not found, so the draft mission selection UI could not be created.", this);
            }
        }

        if (missionSelectionPanel != null)
        {
            missionSelectionController = missionSelectionPanel.GetComponentInChildren<
                MissionSelectionPanelController
            >(true);
            missionSelectionController?.SetCharacterSelectionSource(standingCharacterController);
            missionSelectionController?.SetInternalLaunchButtonVisible(false);
        }

        sortieButton.onClick.AddListener(EnterMissionReady);
        backButton.onClick.AddListener(LeaveMissionReady);
    }

    private void Start()
    {
        CacheTransitionPoses();
        SetLobbyUiAlpha(1f, true);
        SetButtonState(false, false);
    }

    private void OnDestroy()
    {
        if (sortieButton != null)
        {
            sortieButton.onClick.RemoveListener(EnterMissionReady);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(LeaveMissionReady);
        }
    }

    public void EnterMissionReady()
    {
        if (transitionCoroutine != null)
        {
            return;
        }

        if (uiAnimator.CurrentStateId == LobbyUiState)
        {
            transitionCoroutine = StartCoroutine(EnterMissionReadyRoutine());
            return;
        }

        if (uiAnimator.CurrentStateId == LaunchReadyUiState && isReady)
        {
            if (missionSelectionController != null)
            {
                PlayableCharacterDefinition launchCharacter = null;
                if (standingCharacterController != null
                    && !standingCharacterController.TryPrepareLaunchCharacter(out launchCharacter))
                {
                    return;
                }

                bool prepared = standingCharacterController != null
                    ? missionSelectionController.TryPrepareSelectedMission(launchCharacter,
                        standingCharacterController.FocusedCharacter?.TransformationAfter)
                    : missionSelectionController.TryPrepareSelectedMission();
                if (!prepared)
                {
                    standingCharacterController?.CancelPreparedLaunchCharacter();
                    return;
                }

                if (!uiAnimator.TryTransition(LaunchTransitionIndex))
                {
                    MissionSessionManager.Instance.ClearMission();
                    standingCharacterController?.CancelPreparedLaunchCharacter();
                    return;
                }

                transitionCoroutine = StartCoroutine(LaunchMissionRoutine());
            }
            else
            {
                Debug.LogError("Mission selection UI is missing, so sortie cannot start.", this);
            }
        }
    }

    private IEnumerator LaunchMissionRoutine()
    {
        SetButtonState(true, true);
        SetLobbyCameraInputEnabled(false);

        bool hasStandingTransformation = standingCharacterController != null
            && standingCharacterController.BeginFocusedTransformation();
        if (hasStandingTransformation)
        {
            standingCharacterController.StartBeforeTransformation();
        }

        yield return WaitLaunchDelay(beforeToCutinDelay);

        PlayTransformationCutin();
        if (hasStandingTransformation)
        {
            standingCharacterController.NotifyBetweenTransformationPhases();
        }

        yield return WaitLaunchDelay(cutinToAfterDelay);

        if (hasStandingTransformation)
        {
            // The selected after clip was snapshotted into the launch request before hiding the UI.
            // Play it exactly once on the battle player, when the destination begins its zoom-in.
            standingCharacterController.CompleteFocusedTransformation();
        }

        if (!missionSelectionController.TryLoadPreparedMission(true, whiteOutDuration))
        {
            transitionCoroutine = null;
            Debug.LogError("The prepared mission could not load the gameplay scene.", this);
        }
    }

    private void PlayTransformationCutin()
    {
        string stateName = string.IsNullOrWhiteSpace(transformationCutinState)
            ? DefaultTransformationCutinState
            : transformationCutinState;
        int stateHash = Animator.StringToHash(stateName);
        if (!sceneAnimator.HasState(0, stateHash))
        {
            Debug.LogError($"SceneAnimator has no cut-in state named '{stateName}'.", this);
            return;
        }

        sceneAnimator.enabled = true;
        standingCharacterController?.ApplyFocusedCutinStartPosition();
        PlayState(stateName, 0f, 1f);
    }

    private IEnumerator WaitLaunchDelay(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += useUnscaledLaunchTiming ? Time.unscaledDeltaTime : Time.deltaTime;
            yield return null;
        }
    }

    public void LeaveMissionReady()
    {
        if (transitionCoroutine != null || !isReady)
        {
            return;
        }

        transitionCoroutine = StartCoroutine(LeaveMissionReadyRoutine());
    }

    private IEnumerator EnterMissionReadyRoutine()
    {
        SetButtonState(false, true);
        SetLobbyCameraInputEnabled(false);
        lobbyCameraPositionAtEntry = lobbyCamera.localPosition;

        sceneAnimator.enabled = false;
        Vector3 cameraCoverTarget = GetAscentDestinationPosition();
        yield return MoveCameraAndFadeBlackTile(
            lobbyCameraPositionAtEntry,
            cameraCoverTarget,
            0f,
            1f,
            0f
        );

        // Dispatch the ready UI only after the code-controlled ascent/cover has reached its endpoint.
        // Do not wait for salute: the ready UI and authored reveal can run together from this handoff.
        if (!uiAnimator.TryTransition(LaunchReadyTransitionIndex))
        {
            Debug.LogWarning("Launch-ready UI transition could not start; returning the camera to Lobby.", this);
            yield return MoveCameraAndFadeBlackTile(
                lobbyCamera.localPosition,
                lobbyCameraPositionAtEntry,
                1f,
                0f,
                1f
            );
            SetLobbyCameraInputEnabled(true);
            isReady = false;
            transitionCoroutine = null;
            SetButtonState(false, false);
            yield break;
        }

        sceneAnimator.enabled = true;
        PlayState(MissionReadyState, 0f, 1f);

        yield return WaitForStateEnd(MissionReadyState);

        // Hold the authored end pose without letting the Animator keep ownership
        // of the camera and cover tile while the ready UI is open.
        sceneAnimator.enabled = false;
        isReady = true;
        transitionCoroutine = null;
        SetButtonState(true, false);
    }

    private IEnumerator LeaveMissionReadyRoutine()
    {
        SetButtonState(false, true);
        sceneAnimator.enabled = true;
        PlayState(ReturnState, 0f, 1f);
        yield return WaitForStateEnd(ReturnState);

        Vector3 cameraCoverStart = GetDescentStartPosition();
        sceneAnimator.enabled = false;
        // Reposition to the optional descent marker while the code cover is fully opaque.
        SetBlackTileAlpha(1f);
        RestoreAnimationTargetPoses();
        lobbyCamera.localPosition = cameraCoverStart;
        yield return MoveCameraAndFadeBlackTile(
            cameraCoverStart,
            lobbyCameraPositionAtEntry,
            1f,
            0f,
            1f
        );

        SetLobbyCameraInputEnabled(true);
        isReady = false;
        transitionCoroutine = null;
        SetButtonState(false, false);
    }

    private Vector3 GetAscentDestinationPosition()
    {
        Vector3 position = CameraPointToLocalPosition(ascentDestination, coveredCameraPosition);
        position.x = lobbyCameraPositionAtEntry.x;
        return position;
    }

    private Vector3 GetDescentStartPosition()
    {
        if (descentStartPoint == null)
            return lobbyCamera.localPosition;
        // Use only marker height, even with different or rotated/scaled parents.
        Vector3 position = lobbyCamera.position;
        position.y = descentStartPoint.position.y;
        Transform cameraParent = lobbyCamera.parent;
        return cameraParent != null ? cameraParent.InverseTransformPoint(position) : position;
    }

    private Vector3 CameraPointToLocalPosition(Transform point, Vector3 fallback)
    {
        if (point == null)
            return fallback;
        // Markers can be anywhere in the scene, not necessarily siblings of the camera.
        Transform cameraParent = lobbyCamera.parent;
        return cameraParent != null ? cameraParent.InverseTransformPoint(point.position) : point.position;
    }

    private IEnumerator WaitForStateEnd(string stateName)
    {
        int stateHash = Animator.StringToHash(stateName);
        while (true)
        {
            AnimatorStateInfo state = sceneAnimator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash == stateHash
                && state.normalizedTime >= 1f)
            {
                yield break;
            }

            yield return null;
        }
    }

    private IEnumerator MoveCameraAndFadeBlackTile(
        Vector3 cameraStart,
        Vector3 cameraEnd,
        float blackTileAlphaStart,
        float blackTileAlphaEnd,
        float? lobbyUiTargetAlpha = null
    )
    {
        float alphaStart = lobbyUiCanvasGroup != null ? lobbyUiCanvasGroup.alpha : 1f;
        SetBlackTileAlpha(blackTileAlphaStart);
        if (lobbyUiTargetAlpha.HasValue)
            SetLobbyUiAlpha(alphaStart, false);
        float elapsed = 0f;
        while (elapsed < codeTransitionDuration)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / codeTransitionDuration);
            float eased = codeTransitionCurve != null && codeTransitionCurve.length > 0
                ? codeTransitionCurve.Evaluate(progress)
                : Mathf.SmoothStep(0f, 1f, progress);
            lobbyCamera.localPosition = Vector3.LerpUnclamped(cameraStart, cameraEnd, eased);
            SetBlackTileAlpha(Mathf.Lerp(blackTileAlphaStart, blackTileAlphaEnd, eased));
            if (lobbyUiTargetAlpha.HasValue)
                SetLobbyUiAlpha(Mathf.Lerp(alphaStart, lobbyUiTargetAlpha.Value, eased), false);
            yield return null;
        }
        lobbyCamera.localPosition = cameraEnd;
        SetBlackTileAlpha(blackTileAlphaEnd);
        if (lobbyUiTargetAlpha.HasValue)
            SetLobbyUiAlpha(lobbyUiTargetAlpha.Value, lobbyUiTargetAlpha.Value >= 1f);
    }

    private void SetBlackTileAlpha(float alpha)
    {
        if (blackTile == null)
            return;
        Color color = blackTile.color;
        color.a = Mathf.Clamp01(alpha);
        blackTile.color = color;
    }

    private void ResolveLobbyUiCanvasGroup()
    {
        if (lobbyUiCanvasGroup != null)
            return;

        foreach (RectTransform rect in FindObjectsByType<RectTransform>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (rect.gameObject.scene != gameObject.scene
                || !string.Equals(rect.name, "lobbyUI", System.StringComparison.OrdinalIgnoreCase))
                continue;

            lobbyUiCanvasGroup = rect.GetComponent<CanvasGroup>();
            if (lobbyUiCanvasGroup == null)
                lobbyUiCanvasGroup = rect.gameObject.AddComponent<CanvasGroup>();
            return;
        }
    }

    private void SetLobbyUiAlpha(float alpha, bool allowInteraction)
    {
        if (lobbyUiCanvasGroup == null)
            return;

        lobbyUiCanvasGroup.alpha = Mathf.Clamp01(alpha);
        lobbyUiCanvasGroup.interactable = allowInteraction;
        lobbyUiCanvasGroup.blocksRaycasts = allowInteraction;
    }

    private void CacheTransitionPoses()
    {
        if (lobbyCamera == null || blackTile == null)
        {
            Debug.LogError("Assign the lobby Camera and BlackTile SpriteRenderer for the mission transition.", this);
            enabled = false;
            return;
        }

        sceneAnimator.enabled = true;
        lobbyAnimationTargetPoses.Clear();
        HashSet<Transform> cachedTargets = new();
        for (int i = 0; i < animationTargets.Count; i++)
        {
            Transform animationTarget = animationTargets[i];
            if (animationTarget != null && cachedTargets.Add(animationTarget))
            {
                lobbyAnimationTargetPoses.Add(new TransformPose(animationTarget));
            }
        }

        if (lobbyAnimationTargetPoses.Count == 0)
        {
            Debug.LogError(
                "Assign the transforms keyed by the salute/return clips to Animation Targets.",
                this
            );
            enabled = false;
            return;
        }

        // The authored salute clip begins at the fully covered handoff pose.
        PlayState(MissionReadyState, 0f, 0f);
        coveredCameraPosition = lobbyCamera.localPosition;
        RestoreAnimationTargetPoses();
        sceneAnimator.enabled = false;
        SetBlackTileAlpha(0f);
    }

    private void RestoreAnimationTargetPoses()
    {
        for (int i = 0; i < lobbyAnimationTargetPoses.Count; i++)
        {
            lobbyAnimationTargetPoses[i].Restore();
        }
    }

    private void EnsureAnimationTarget(Transform target)
    {
        if (target != null && !animationTargets.Contains(target))
        {
            animationTargets.Add(target);
        }
    }

    private void SetLobbyCameraInputEnabled(bool value)
    {
        if (lobbyCameraController != null)
        {
            lobbyCameraController.enabled = value;
        }
    }

    private void PlayState(string stateName, float normalizedTime, float speed)
    {
        sceneAnimator.speed = speed;
        sceneAnimator.Play(stateName, 0, normalizedTime);
        sceneAnimator.Update(0f);
    }

    private void SetButtonState(bool ready, bool transitioning)
    {
        sortieButton.gameObject.SetActive(true);
        backButton.gameObject.SetActive(ready);
        if (missionSelectionPanel != null)
        {
            missionSelectionPanel.SetActive(ready && !transitioning);
        }
        sortieButton.interactable = !transitioning;
        backButton.interactable = !transitioning;
    }

    private readonly struct TransformPose
    {
        private readonly Transform target;
        private readonly Vector3 localPosition;
        private readonly Quaternion localRotation;
        private readonly Vector3 localScale;

        public TransformPose(Transform target)
        {
            this.target = target;
            localPosition = target.localPosition;
            localRotation = target.localRotation;
            localScale = target.localScale;
        }

        public void Restore()
        {
            if (target == null)
            {
                return;
            }
            target.localPosition = localPosition;
            target.localRotation = localRotation;
            target.localScale = localScale;
        }
    }

}
