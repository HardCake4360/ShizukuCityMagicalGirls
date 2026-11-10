using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(UIAnimator))]
public sealed class UIAnimatorEditor : Editor
{
    private const float Spacing = 2f;

    private SerializedProperty managerProperty;
    private SerializedProperty applyInitialStateOnStartProperty;
    private SerializedProperty initialStateIdProperty;
    private SerializedProperty initialStateIsImmediateProperty;
    private SerializedProperty transitionsProperty;
    private SerializedProperty onStateEnteredProperty;

    private ReorderableList transitionList;
    private readonly List<string> stateIds = new();
    private readonly HashSet<string> stateIdSet = new(StringComparer.Ordinal);
    private readonly List<string> catalogueErrors = new();

    private void OnEnable()
    {
        managerProperty = serializedObject.FindProperty("manager");
        applyInitialStateOnStartProperty = serializedObject.FindProperty("applyInitialStateOnStart");
        initialStateIdProperty = serializedObject.FindProperty("initialStateId");
        initialStateIsImmediateProperty = serializedObject.FindProperty("initialStateIsImmediate");
        transitionsProperty = serializedObject.FindProperty("transitions");
        onStateEnteredProperty = serializedObject.FindProperty("onStateEntered");

        transitionList = new ReorderableList(
            serializedObject,
            transitionsProperty,
            false,
            true,
            true,
            true
        );
        transitionList.drawHeaderCallback = rect =>
            EditorGUI.LabelField(rect, "Transitions", EditorStyles.boldLabel);
        transitionList.drawElementCallback = DrawTransition;
        transitionList.elementHeightCallback = GetTransitionHeight;
        transitionList.onAddCallback = AddTransition;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        RefreshStateCatalogue();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromMonoBehaviour((UIAnimator)target),
                typeof(UIAnimator),
                false
            );
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("State Source", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(managerProperty);
        DrawCatalogueErrors();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Initial State", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(applyInitialStateOnStartProperty);
        if (applyInitialStateOnStartProperty.boolValue)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                DrawStateSelector(initialStateIdProperty, null, "State");
                EditorGUILayout.PropertyField(
                    initialStateIsImmediateProperty,
                    new GUIContent("Apply Immediately")
                );
                if (string.IsNullOrWhiteSpace(initialStateIdProperty.stringValue))
                {
                    EditorGUILayout.HelpBox(
                        "Select an initial state.",
                        MessageType.Error
                    );
                }
                else if (!stateIdSet.Contains(initialStateIdProperty.stringValue))
                {
                    EditorGUILayout.HelpBox(
                        $"Initial state '{initialStateIdProperty.stringValue}' is not defined by the UAM.",
                        MessageType.Error
                    );
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Transition(index)는 아래 리스트의 실제 순번을 사용합니다. 항목을 삭제하면 그 뒤의 외부 호출 인덱스도 달라집니다.",
            MessageType.Info
        );
        transitionList.DoLayoutList();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onStateEnteredProperty);

        if (Application.isPlaying)
        {
            UIAnimator animator = (UIAnimator)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Current State", animator.CurrentStateId);
                EditorGUILayout.IntField("Current State Index", animator.CurrentStateIndex);
                EditorGUILayout.Toggle("Dispatching", animator.IsDispatching);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void RefreshStateCatalogue()
    {
        stateIds.Clear();
        stateIdSet.Clear();
        catalogueErrors.Clear();

        UIAnimManager manager = managerProperty.objectReferenceValue as UIAnimManager;
        if (manager == null)
        {
            catalogueErrors.Add("UIAnimManager가 지정되지 않았습니다.");
            return;
        }

        IReadOnlyList<UIAnimStateDefinition> states = manager.States;
        for (int i = 0; i < states.Count; i++)
        {
            UIAnimStateDefinition state = states[i];
            string stateId = state?.Id;
            if (string.IsNullOrWhiteSpace(stateId))
            {
                catalogueErrors.Add($"UAM 상태 [{i}]의 ID가 비어 있습니다.");
                continue;
            }

            if (!stateIdSet.Add(stateId))
            {
                catalogueErrors.Add($"UAM 상태 ID '{stateId}'가 중복되었습니다.");
                continue;
            }

            stateIds.Add(stateId);
        }
    }

    private void DrawCatalogueErrors()
    {
        for (int i = 0; i < catalogueErrors.Count; i++)
        {
            EditorGUILayout.HelpBox(catalogueErrors[i], MessageType.Error);
        }
    }

    private void DrawTransition(
        Rect rect,
        int index,
        bool isActive,
        bool isFocused
    )
    {
        SerializedProperty transition = transitionsProperty.GetArrayElementAtIndex(index);
        SerializedProperty id = transition.FindPropertyRelative("id");
        SerializedProperty fromAny = transition.FindPropertyRelative("fromAnyState");
        SerializedProperty fromState = transition.FindPropertyRelative("fromStateId");
        SerializedProperty toState = transition.FindPropertyRelative("toStateId");
        SerializedProperty immediate = transition.FindPropertyRelative("immediate");
        SerializedProperty allowReentry = transition.FindPropertyRelative("allowReentry");
        SerializedProperty onDispatched = transition.FindPropertyRelative("onDispatched");

        float y = rect.y + Spacing;
        float lineHeight = EditorGUIUtility.singleLineHeight;
        string sourceLabel = fromAny.boolValue
            ? "Any State"
            : DisplayStateId(fromState.stringValue);
        string targetLabel = DisplayStateId(toState.stringValue);

        EditorGUI.LabelField(
            Line(rect, ref y, lineHeight),
            $"Transition({index})   {sourceLabel}  →  {targetLabel}",
            EditorStyles.boldLabel
        );
        EditorGUI.PropertyField(Line(rect, ref y, lineHeight), id, new GUIContent("ID (Optional)"));
        DrawStateSelector(
            Line(rect, ref y, lineHeight),
            fromState,
            fromAny,
            "From"
        );
        DrawStateSelector(Line(rect, ref y, lineHeight), toState, null, "To");
        EditorGUI.PropertyField(Line(rect, ref y, lineHeight), immediate);
        EditorGUI.PropertyField(Line(rect, ref y, lineHeight), allowReentry);

        float eventHeight = EditorGUI.GetPropertyHeight(onDispatched, true);
        EditorGUI.PropertyField(
            Line(rect, ref y, eventHeight),
            onDispatched,
            new GUIContent("On Dispatched"),
            true
        );

        List<string> warnings = GetTransitionWarnings(index, transition);
        for (int i = 0; i < warnings.Count; i++)
        {
            float warningHeight = GetHelpBoxHeight(warnings[i], rect.width);
            EditorGUI.HelpBox(
                Line(rect, ref y, warningHeight),
                warnings[i],
                MessageType.Error
            );
        }
    }

    private float GetTransitionHeight(int index)
    {
        SerializedProperty transition = transitionsProperty.GetArrayElementAtIndex(index);
        SerializedProperty onDispatched = transition.FindPropertyRelative("onDispatched");
        float height = Spacing;
        height += (EditorGUIUtility.singleLineHeight + Spacing) * 6f;
        height += EditorGUI.GetPropertyHeight(onDispatched, true) + Spacing;

        List<string> warnings = GetTransitionWarnings(index, transition);
        float width = Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 80f);
        for (int i = 0; i < warnings.Count; i++)
        {
            height += GetHelpBoxHeight(warnings[i], width) + Spacing;
        }
        return height;
    }

    private List<string> GetTransitionWarnings(int index, SerializedProperty transition)
    {
        List<string> warnings = new();
        SerializedProperty id = transition.FindPropertyRelative("id");
        SerializedProperty fromAny = transition.FindPropertyRelative("fromAnyState");
        SerializedProperty fromState = transition.FindPropertyRelative("fromStateId");
        SerializedProperty toState = transition.FindPropertyRelative("toStateId");

        if (!fromAny.boolValue)
        {
            AddStateWarning(warnings, fromState.stringValue, "From");
        }
        AddStateWarning(warnings, toState.stringValue, "To");

        if (!string.IsNullOrWhiteSpace(id.stringValue))
        {
            for (int i = 0; i < transitionsProperty.arraySize; i++)
            {
                if (i == index)
                {
                    continue;
                }

                SerializedProperty otherId = transitionsProperty
                    .GetArrayElementAtIndex(i)
                    .FindPropertyRelative("id");
                if (string.Equals(id.stringValue, otherId.stringValue, StringComparison.Ordinal))
                {
                    warnings.Add($"전환 ID '{id.stringValue}'가 중복되었습니다.");
                    break;
                }
            }
        }

        return warnings;
    }

    private void AddStateWarning(List<string> warnings, string stateId, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(stateId))
        {
            warnings.Add($"{fieldName} 상태를 선택해야 합니다.");
        }
        else if (!stateIdSet.Contains(stateId))
        {
            warnings.Add($"{fieldName} 상태 '{stateId}'가 UAM에 없습니다.");
        }
    }

    private void DrawStateSelector(
        SerializedProperty stateProperty,
        SerializedProperty anyStateProperty,
        string label
    )
    {
        Rect rect = EditorGUILayout.GetControlRect();
        DrawStateSelector(rect, stateProperty, anyStateProperty, label);
    }

    private void DrawStateSelector(
        Rect rect,
        SerializedProperty stateProperty,
        SerializedProperty anyStateProperty,
        string label
    )
    {
        bool allowAny = anyStateProperty != null;
        List<string> labels = new();
        List<string> values = new();

        if (allowAny)
        {
            labels.Add("<Any State>");
            values.Add(string.Empty);
        }

        int selectIndex = labels.Count;
        labels.Add("<Select State>");
        values.Add(string.Empty);

        for (int i = 0; i < stateIds.Count; i++)
        {
            labels.Add(stateIds[i]);
            values.Add(stateIds[i]);
        }

        bool isAny = allowAny && anyStateProperty.boolValue;
        int currentIndex = isAny ? 0 : selectIndex;
        if (!isAny && !string.IsNullOrWhiteSpace(stateProperty.stringValue))
        {
            currentIndex = values.FindIndex(
                allowAny ? 2 : 1,
                value => string.Equals(value, stateProperty.stringValue, StringComparison.Ordinal)
            );
            if (currentIndex < 0)
            {
                currentIndex = labels.Count;
                labels.Add($"<Missing: {stateProperty.stringValue}>");
                values.Add(stateProperty.stringValue);
            }
        }

        EditorGUI.BeginProperty(rect, new GUIContent(label), stateProperty);
        int newIndex = EditorGUI.Popup(rect, label, currentIndex, labels.ToArray());
        EditorGUI.EndProperty();

        if (newIndex == currentIndex || newIndex < 0 || newIndex >= values.Count)
        {
            return;
        }

        if (allowAny)
        {
            anyStateProperty.boolValue = newIndex == 0;
        }
        stateProperty.stringValue = allowAny && newIndex == 0
            ? string.Empty
            : values[newIndex];
    }

    private void AddTransition(ReorderableList list)
    {
        int index = transitionsProperty.arraySize;
        transitionsProperty.arraySize++;
        SerializedProperty transition = transitionsProperty.GetArrayElementAtIndex(index);
        transition.FindPropertyRelative("id").stringValue = string.Empty;
        transition.FindPropertyRelative("fromAnyState").boolValue = false;
        transition.FindPropertyRelative("fromStateId").stringValue = string.Empty;
        transition.FindPropertyRelative("toStateId").stringValue = string.Empty;
        transition.FindPropertyRelative("immediate").boolValue = false;
        transition.FindPropertyRelative("allowReentry").boolValue = false;

        SerializedProperty calls = transition
            .FindPropertyRelative("onDispatched")
            ?.FindPropertyRelative("m_PersistentCalls.m_Calls");
        calls?.ClearArray();
        list.index = index;
    }

    private static Rect Line(Rect container, ref float y, float height)
    {
        Rect line = new(container.x, y, container.width, height);
        y += height + Spacing;
        return line;
    }

    private static float GetHelpBoxHeight(string message, float width)
    {
        return Mathf.Max(
            EditorGUIUtility.singleLineHeight * 2f,
            EditorStyles.helpBox.CalcHeight(new GUIContent(message), width)
        );
    }

    private static string DisplayStateId(string stateId)
    {
        return string.IsNullOrWhiteSpace(stateId) ? "<unset>" : stateId;
    }
}
