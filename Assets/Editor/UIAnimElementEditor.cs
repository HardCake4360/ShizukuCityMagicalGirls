using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UIAnimElement))]
[CanEditMultipleObjects]
public sealed class UIAnimElementEditor : Editor
{
    private SerializedProperty targetProperty;
    private SerializedProperty startActiveProperty;
    private SerializedProperty applyStartStateOnAwakeProperty;
    private SerializedProperty activePoseProperty;
    private SerializedProperty useActivePoseProperty;
    private SerializedProperty inactivePoseProperty;
    private SerializedProperty useInactivePoseProperty;
    private SerializedProperty hoveringPoseProperty;
    private SerializedProperty useHoveringPoseProperty;
    private SerializedProperty pathTypeProperty;
    private SerializedProperty durationProperty;
    private SerializedProperty speedCurveProperty;
    private SerializedProperty useUnscaledTimeProperty;
    private SerializedProperty onActivatedProperty;
    private SerializedProperty onDeactivatedProperty;

    private void OnEnable()
    {
        targetProperty = serializedObject.FindProperty("target");
        startActiveProperty = serializedObject.FindProperty("startActive");
        applyStartStateOnAwakeProperty = serializedObject.FindProperty("applyStartStateOnAwake");
        activePoseProperty = serializedObject.FindProperty("activePose");
        useActivePoseProperty = serializedObject.FindProperty("useActivePose");
        inactivePoseProperty = serializedObject.FindProperty("inactivePose");
        useInactivePoseProperty = serializedObject.FindProperty("useInactivePose");
        hoveringPoseProperty = serializedObject.FindProperty("hoveringPose");
        useHoveringPoseProperty = serializedObject.FindProperty("useHoveringPose");
        pathTypeProperty = serializedObject.FindProperty("pathType");
        durationProperty = serializedObject.FindProperty("duration");
        speedCurveProperty = serializedObject.FindProperty("speedCurve");
        useUnscaledTimeProperty = serializedObject.FindProperty("useUnscaledTime");
        onActivatedProperty = serializedObject.FindProperty("onActivated");
        onDeactivatedProperty = serializedObject.FindProperty("onDeactivated");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((UIAnimElement)target), typeof(UIAnimElement), false);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(targetProperty);
        EditorGUILayout.PropertyField(startActiveProperty);
        EditorGUILayout.PropertyField(applyStartStateOnAwakeProperty);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Poses", EditorStyles.boldLabel);
        DrawOptionalPose(useActivePoseProperty, activePoseProperty, "Active Pose", true);
        DrawOptionalPose(useInactivePoseProperty, inactivePoseProperty, "Inactive Pose", false);
        DrawHoveringPose();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Transition", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(pathTypeProperty);
        EditorGUILayout.PropertyField(durationProperty);
        EditorGUILayout.PropertyField(speedCurveProperty);
        EditorGUILayout.PropertyField(useUnscaledTimeProperty);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(onActivatedProperty);
        EditorGUILayout.PropertyField(onDeactivatedProperty);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPose(SerializedProperty poseProperty, string label, bool active)
    {
        EditorGUILayout.PropertyField(poseProperty, new GUIContent(label), true);
        using (new EditorGUI.IndentLevelScope())
        {
            if (GUILayout.Button("Set Pose From Current Transform"))
            {
                CapturePose(active);
            }
        }
        EditorGUILayout.Space(3f);
    }

    private void DrawOptionalPose(
        SerializedProperty enabledProperty,
        SerializedProperty poseProperty,
        string label,
        bool active
    )
    {
        EditorGUILayout.PropertyField(enabledProperty, new GUIContent($"Use {label}"));
        using (new EditorGUI.DisabledScope(!enabledProperty.boolValue))
        {
            DrawPose(poseProperty, label, active);
        }
    }

    private void CapturePose(bool active)
    {
        serializedObject.ApplyModifiedProperties();
        Undo.RecordObjects(targets, active ? "Set UAE Active Pose" : "Set UAE Inactive Pose");

        for (int i = 0; i < targets.Length; i++)
        {
            UIAnimElement element = (UIAnimElement)targets[i];
            if (active)
            {
                element.CaptureActivePose();
            }
            else
            {
                element.CaptureInactivePose();
            }

            EditorUtility.SetDirty(element);
            PrefabUtility.RecordPrefabInstancePropertyModifications(element);
        }

        serializedObject.Update();
    }

    private void DrawHoveringPose()
    {
        EditorGUILayout.PropertyField(useHoveringPoseProperty, new GUIContent("Use Hovering Pose"));
        using (new EditorGUI.DisabledScope(!useHoveringPoseProperty.boolValue))
        {
            EditorGUILayout.PropertyField(hoveringPoseProperty, new GUIContent("Hovering Pose"), true);
            using (new EditorGUI.IndentLevelScope())
            {
                if (GUILayout.Button("Set Pose From Current Transform"))
                {
                    serializedObject.ApplyModifiedProperties();
                    Undo.RecordObjects(targets, "Set UAE Hovering Pose");
                    for (int i = 0; i < targets.Length; i++)
                    {
                        UIAnimElement element = (UIAnimElement)targets[i];
                        element.CaptureHoveringPose();
                        EditorUtility.SetDirty(element);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(element);
                    }
                    serializedObject.Update();
                }
            }
        }
        EditorGUILayout.Space(3f);
    }
}
