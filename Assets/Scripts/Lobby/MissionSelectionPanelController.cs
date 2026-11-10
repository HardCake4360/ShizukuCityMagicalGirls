using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MissionSelectionPanelController : MonoBehaviour
{
    private static readonly Color PanelColor = new Color(0.055f, 0.075f, 0.12f, 0.96f);
    private static readonly Color FieldColor = new Color(0.12f, 0.16f, 0.24f, 1f);
    private static readonly Color AccentColor = new Color(0.32f, 0.76f, 0.95f, 1f);

    [Header("Definitions")]
    [SerializeField] private MissionDefinition[] missionOverrides;
    [SerializeField] private PlayableCharacterDefinition[] characters;
    [Tooltip("When assigned, character icons are the source of truth instead of the draft dropdown.")]
    [SerializeField] private LobbyStandingCharacterController characterSelectionSource;

    [Header("UI")]
    [SerializeField] private TMP_Dropdown missionDropdown;
    [SerializeField] private TMP_Dropdown characterDropdown;
    [SerializeField] private TMP_Text missionNameLabel;
    [SerializeField] private TMP_Text missionDescriptionLabel;
    [SerializeField] private TMP_Text missionObjectiveLabel;
    [SerializeField] private TMP_Text characterDescriptionLabel;
    [SerializeField] private Image missionImage;
    [SerializeField] private Image characterPortrait;
    [SerializeField] private Button launchButton;

    [Header("Launch")]
    [SerializeField] private string gameplaySceneName = "GamePlayScene";
    [SerializeField, Min(0f)] private float minimumLoadDuration = 1f;

    private readonly List<MissionDefinition> availableMissions = new();
    private readonly List<PlayableCharacterDefinition> availableCharacters = new();
    private PlayableCharacterDefinition displayedCharacter;
    private AchievementManager subscribedAchievementManager;

    private void Awake()
    {
        if (missionDropdown == null || characterDropdown == null || launchButton == null)
        {
            Debug.LogError("Mission selection UI references are incomplete.", this);
            enabled = false;
            return;
        }

        BuildOptions();
        missionDropdown.onValueChanged.AddListener(HandleMissionChanged);
        characterDropdown.onValueChanged.AddListener(HandleCharacterChanged);
        launchButton.onClick.AddListener(LaunchSelectedMission);
        RefreshDetails();
        ApplyCharacterSelectorVisibility();
    }

    private void Update()
    {
        if (characterSelectionSource != null && displayedCharacter != GetSelectedCharacter())
            RefreshDetails();
    }

    private void OnEnable()
    {
        if (characterDropdown == null || AchievementManager.IsShuttingDown)
            return;
        subscribedAchievementManager = AchievementManager.Instance;
        subscribedAchievementManager.ProgressChanged += HandleAchievementChanged;
        CharacterUnlockProgress.DebugUnlockOverrideChanged += HandleDebugUnlockChanged;
        RefreshCharacterOptions();
        RefreshDetails();
    }

    private void OnDisable()
    {
        CharacterUnlockProgress.DebugUnlockOverrideChanged -= HandleDebugUnlockChanged;
        if (subscribedAchievementManager != null)
            subscribedAchievementManager.ProgressChanged -= HandleAchievementChanged;
        subscribedAchievementManager = null;
    }

    private void HandleAchievementChanged(AchievementProgress progress)
    {
        if (progress.State != AchievementState.Completed)
            return;
        RefreshCharacterOptions();
        RefreshDetails();
    }

    private void HandleDebugUnlockChanged()
    {
        RefreshCharacterOptions();
        RefreshDetails();
    }

    public void SetCharacterSelectionSource(LobbyStandingCharacterController source)
    {
        characterSelectionSource = source;
        ApplyCharacterSelectorVisibility();
        RefreshDetails();
    }

    private void ApplyCharacterSelectorVisibility()
    {
        if (characterDropdown != null)
            characterDropdown.gameObject.SetActive(characterSelectionSource == null);
    }

    public static GameObject CreateRuntimeDraft(Transform canvasTransform, TMP_FontAsset font)
    {
        GameObject root = CreateUiObject("Mission Selection Draft", canvasTransform);
        root.SetActive(false);

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.anchoredPosition = new Vector2(260f, 0f);
        rootRect.sizeDelta = new Vector2(620f, 640f);
        root.AddComponent<Image>().color = PanelColor;

        VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(30, 30, 26, 26);
        layout.spacing = 12f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        TMP_Text title = CreateText("Title", root.transform, "출격 준비", 34f, FontStyles.Bold);
        SetPreferredHeight(title.gameObject, 48f);

        TMP_Text missionSection = CreateText("Mission Section", root.transform, "임무", 20f, FontStyles.Bold);
        SetPreferredHeight(missionSection.gameObject, 28f);
        TMP_Dropdown missionDropdown = CreateDropdown("Mission Dropdown", root.transform);
        SetPreferredHeight(missionDropdown.gameObject, 48f);

        TMP_Text missionName = CreateText("Mission Name", root.transform, string.Empty, 24f, FontStyles.Bold);
        missionName.color = AccentColor;
        SetPreferredHeight(missionName.gameObject, 34f);
        TMP_Text missionDescription = CreateText("Mission Description", root.transform, string.Empty, 18f, FontStyles.Normal);
        SetPreferredHeight(missionDescription.gameObject, 58f);
        TMP_Text missionObjective = CreateText("Mission Objective", root.transform, string.Empty, 18f, FontStyles.Bold);
        SetPreferredHeight(missionObjective.gameObject, 30f);

        TMP_Text characterSection = CreateText("Character Section", root.transform, "캐릭터", 20f, FontStyles.Bold);
        SetPreferredHeight(characterSection.gameObject, 28f);
        TMP_Dropdown characterDropdown = CreateDropdown("Character Dropdown", root.transform);
        SetPreferredHeight(characterDropdown.gameObject, 48f);
        TMP_Text characterDescription = CreateText("Character Description", root.transform, string.Empty, 18f, FontStyles.Normal);
        SetPreferredHeight(characterDescription.gameObject, 58f);

        GameObject spacer = CreateUiObject("Spacer", root.transform);
        spacer.AddComponent<LayoutElement>().flexibleHeight = 1f;

        Button launchButton = CreateButton("Launch Button", root.transform, "선택한 임무로 출격");
        SetPreferredHeight(launchButton.gameObject, 58f);

        if (font != null)
        {
            TMP_Text[] labels = root.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i].font = font;
            }
        }
        else
        {
            Debug.LogWarning("Draft mission UI has no Korean TMP font assigned.", root);
        }

        MissionSelectionPanelController controller = root.AddComponent<MissionSelectionPanelController>();
        controller.missionDropdown = missionDropdown;
        controller.characterDropdown = characterDropdown;
        controller.missionNameLabel = missionName;
        controller.missionDescriptionLabel = missionDescription;
        controller.missionObjectiveLabel = missionObjective;
        controller.characterDescriptionLabel = characterDescription;
        controller.launchButton = launchButton;
        return root;
    }

    private void OnDestroy()
    {
        if (missionDropdown != null)
        {
            missionDropdown.onValueChanged.RemoveListener(HandleMissionChanged);
        }

        if (characterDropdown != null)
        {
            characterDropdown.onValueChanged.RemoveListener(HandleCharacterChanged);
        }

        if (launchButton != null)
        {
            launchButton.onClick.RemoveListener(LaunchSelectedMission);
        }
    }

    public void RefreshForCurrentChapter()
    {
        BuildOptions();
        RefreshDetails();
    }

    public void LaunchSelectedMission()
    {
        TryLaunchSelectedMission();
    }

    public bool TryLaunchSelectedMission()
    {
        if (!TryPrepareSelectedMission())
        {
            return false;
        }

        return TryLoadPreparedMission();
    }

    public bool TryPrepareSelectedMission()
    {
        return TryPrepareSelectedMission(GetSelectedCharacter());
    }

    public bool TryPrepareSelectedMission(PlayableCharacterDefinition selectedCharacter,
        AnimationClip battleEntryTransformation = null)
    {
        MissionDefinition mission = GetSelectedMission();
        if (!MissionSessionManager.Instance.BeginMission(mission, selectedCharacter, battleEntryTransformation))
        {
            Debug.LogError("Select a mission and a character with a valid Player gameplay prefab before launching.", this);
            return false;
        }

        return true;
    }

    public bool TryLoadPreparedMission(bool useBattleEntry = false, float whiteOutDuration = 0.4f)
    {
        if (MissionSessionManager.Instance.CurrentRequest == null)
        {
            Debug.LogError("Prepare a mission before loading the gameplay scene.", this);
            return false;
        }

        bool loadStarted = useBattleEntry
            ? SceneLoadManager.Instance.LoadBattleScene(gameplaySceneName, minimumLoadDuration, whiteOutDuration)
            : SceneLoadManager.Instance.LoadScene(
            gameplaySceneName,
            minimumLoadDuration,
            null
        );
        if (!loadStarted)
        {
            MissionSessionManager.Instance.ClearMission();
        }

        return loadStarted;
    }

    public void SetInternalLaunchButtonVisible(bool visible)
    {
        if (launchButton != null)
        {
            launchButton.gameObject.SetActive(visible);
        }
    }

    private void BuildOptions()
    {
        availableMissions.Clear();
        ChapterDefinition currentChapter = ChapterProgressionManager.Instance.CurrentChapter;
        MissionDefinition[] sourceMissions = missionOverrides != null && missionOverrides.Length > 0
            ? missionOverrides
            : currentChapter != null ? currentChapter.Missions : null;

        if (sourceMissions != null)
        {
            for (int i = 0; i < sourceMissions.Length; i++)
            {
                MissionDefinition mission = sourceMissions[i];
                if (mission != null && (currentChapter == null || mission.Chapter == currentChapter))
                {
                    availableMissions.Add(mission);
                }
            }
        }

        missionDropdown.ClearOptions();
        missionDropdown.AddOptions(availableMissions.ConvertAll(
            mission => string.IsNullOrWhiteSpace(mission.DisplayName) ? mission.name : mission.DisplayName
        ));
        missionDropdown.SetValueWithoutNotify(0);
        RefreshCharacterOptions();
    }

    private void RefreshCharacterOptions()
    {
        PlayableCharacterDefinition previous = GetSelectedCharacter();
        availableCharacters.Clear();
        int completedCount = CharacterUnlockProgress.CompletedCount;
        PlayableCharacterDefinition[] sourceCharacters = characters != null && characters.Length > 0
            ? characters
            : Resources.LoadAll<PlayableCharacterDefinition>("Missions/Characters");
        if (sourceCharacters != null)
        {
            for (int i = 0; i < sourceCharacters.Length; i++)
            {
                if (CharacterUnlockProgress.IsUnlocked(sourceCharacters[i], completedCount))
                {
                    availableCharacters.Add(sourceCharacters[i]);
                }
            }
        }

        characterDropdown.ClearOptions();
        characterDropdown.AddOptions(availableCharacters.ConvertAll(
            character => string.IsNullOrWhiteSpace(character.DisplayName) ? character.name : character.DisplayName
        ));
        characterDropdown.SetValueWithoutNotify(Mathf.Max(0, availableCharacters.IndexOf(previous)));
    }

    private void HandleMissionChanged(int value)
    {
        RefreshDetails();
    }

    private void HandleCharacterChanged(int value)
    {
        RefreshDetails();
    }

    private void RefreshDetails()
    {
        MissionDefinition mission = GetSelectedMission();
        PlayableCharacterDefinition character = GetSelectedCharacter();

        displayedCharacter = character;

        SetText(missionNameLabel, mission != null ? mission.DisplayName : "미션 없음");
        SetText(missionDescriptionLabel, mission != null ? mission.Description : string.Empty);
        SetText(
            missionObjectiveLabel,
            mission != null ? $"목표 격추 수: {mission.TargetKillCount}" : string.Empty
        );
        SetText(
            characterDescriptionLabel,
            character != null ? character.Description : string.Empty
        );

        SetImage(missionImage, mission != null ? mission.Image : null);
        SetImage(characterPortrait, character != null ? character.Portrait : null);
        if (launchButton != null)
            launchButton.interactable = mission != null && character != null && character.IsUnlocked
                && !SceneLoadManager.IsLoadingNow;
    }

    private MissionDefinition GetSelectedMission()
    {
        int index = missionDropdown != null ? missionDropdown.value : -1;
        return index >= 0 && index < availableMissions.Count ? availableMissions[index] : null;
    }

    private PlayableCharacterDefinition GetSelectedCharacter()
    {
        if (characterSelectionSource != null)
            return characterSelectionSource.FocusedCharacter?.PlayableCharacter;
        int index = characterDropdown != null ? characterDropdown.value : -1;
        return index >= 0 && index < availableCharacters.Count ? availableCharacters[index] : null;
    }

    private static void SetText(TMP_Text label, string value)
    {
        if (label != null)
        {
            label.text = value;
        }
    }

    private static void SetImage(Image image, Sprite sprite)
    {
        if (image != null)
        {
            image.sprite = sprite;
            image.enabled = sprite != null;
        }
    }

    private static GameObject CreateUiObject(string name, Transform parent)
    {
        GameObject instance = new GameObject(name, typeof(RectTransform));
        instance.transform.SetParent(parent, false);
        return instance;
    }

    private static TMP_Text CreateText(string name, Transform parent, string value, float size, FontStyles style)
    {
        TextMeshProUGUI label = CreateUiObject(name, parent).AddComponent<TextMeshProUGUI>();
        label.text = value;
        label.fontSize = size;
        label.fontStyle = style;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    private static TMP_Dropdown CreateDropdown(string name, Transform parent)
    {
        GameObject root = CreateUiObject(name, parent);
        Image image = root.AddComponent<Image>();
        image.color = FieldColor;
        TMP_Dropdown dropdown = root.AddComponent<TMP_Dropdown>();

        TMP_Text caption = CreateText("Label", root.transform, string.Empty, 19f, FontStyles.Normal);
        RectTransform captionRect = caption.rectTransform;
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(16f, 4f);
        captionRect.offsetMax = new Vector2(-50f, -4f);

        TMP_Text arrow = CreateText("Arrow", root.transform, "▼", 18f, FontStyles.Bold);
        arrow.alignment = TextAlignmentOptions.Center;
        RectTransform arrowRect = arrow.rectTransform;
        arrowRect.anchorMin = new Vector2(1f, 0f);
        arrowRect.anchorMax = Vector2.one;
        arrowRect.pivot = new Vector2(1f, 0.5f);
        arrowRect.sizeDelta = new Vector2(44f, 0f);
        arrowRect.anchoredPosition = Vector2.zero;

        GameObject template = CreateUiObject("Template", root.transform);
        RectTransform templateRect = template.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -2f);
        templateRect.sizeDelta = new Vector2(0f, 180f);
        template.AddComponent<Image>().color = PanelColor;
        ScrollRect scrollRect = template.AddComponent<ScrollRect>();

        GameObject viewport = CreateUiObject("Viewport", template.transform);
        Stretch(viewport.GetComponent<RectTransform>(), 4f);
        viewport.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        GameObject content = CreateUiObject("Content", viewport.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = Vector2.one;
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject item = CreateUiObject("Item", content.transform);
        Image itemBackground = item.AddComponent<Image>();
        itemBackground.color = FieldColor;
        Toggle toggle = item.AddComponent<Toggle>();
        toggle.targetGraphic = itemBackground;
        SetPreferredHeight(item, 42f);
        TMP_Text itemLabel = CreateText("Item Label", item.transform, "Option", 18f, FontStyles.Normal);
        Stretch(itemLabel.rectTransform, 10f);

        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        dropdown.targetGraphic = image;
        dropdown.template = templateRect;
        dropdown.captionText = caption;
        dropdown.itemText = itemLabel;
        template.SetActive(false);
        return dropdown;
    }

    private static Button CreateButton(string name, Transform parent, string label)
    {
        GameObject root = CreateUiObject(name, parent);
        Image image = root.AddComponent<Image>();
        image.color = AccentColor;
        Button button = root.AddComponent<Button>();
        button.targetGraphic = image;
        TMP_Text text = CreateText("Label", root.transform, label, 21f, FontStyles.Bold);
        text.color = new Color(0.02f, 0.06f, 0.1f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        Stretch(text.rectTransform, 8f);
        return button;
    }

    private static void SetPreferredHeight(GameObject target, float height)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = target.AddComponent<LayoutElement>();
        }
        layout.preferredHeight = height;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
