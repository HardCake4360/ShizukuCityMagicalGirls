using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(LobbyStandingCharacterEntry))]
public sealed class LobbyStandingCharacterEntryDrawer : PropertyDrawer
{
    private static readonly string[] Fields =
    {
        "target", "playableCharacter", "standingSprite", "transformationBefore", "transformationAfter",
        "useCutinStartPosition", "cutinStartPosition"
    };
    private static float Spacing => EditorGUIUtility.standardVerticalSpacing;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) return height;
        foreach (string name in Fields)
        {
            SerializedProperty child = property.FindPropertyRelative(name);
            if (child != null) height += Spacing + EditorGUI.GetPropertyHeight(child, true);
        }
        return height + Spacing + EditorGUIUtility.singleLineHeight;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);
        Rect row = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
        if (property.isExpanded)
        {
            EditorGUI.indentLevel++;
            foreach (string name in Fields)
            {
                SerializedProperty child = property.FindPropertyRelative(name);
                if (child == null) continue;
                row.y += row.height + Spacing;
                row.height = EditorGUI.GetPropertyHeight(child, true);
                EditorGUI.PropertyField(row, child, true);
            }
            row.y += row.height + Spacing;
            row.height = EditorGUIUtility.singleLineHeight;
            using (new EditorGUI.DisabledScope(!CanCapture(property)))
            {
                if (GUI.Button(EditorGUI.IndentedRect(row), new GUIContent("SetPositon",
                    "현재 Transformation Cutin Circle의 UI 위치를 이 캐릭터에 저장하고 Use Cutin Start Position을 켭니다.")))
                    CapturePosition(property);
            }
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }

    private static bool CanCapture(SerializedProperty property)
    {
        foreach (Object owner in property.serializedObject.targetObjects)
            if (owner is LobbyStandingCharacterController controller && controller.TransformationCutinCircle != null)
                return true;
        return false;
    }

    public static bool CapturePosition(SerializedProperty entryProperty)
    {
        entryProperty.serializedObject.ApplyModifiedProperties();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Set Character Cut-in Start Position");
        bool captured = false;
        foreach (Object owner in entryProperty.serializedObject.targetObjects)
        {
            if (owner is not LobbyStandingCharacterController controller || controller.TransformationCutinCircle == null)
                continue;
            using SerializedObject serialized = new(owner);
            SerializedProperty entry = serialized.FindProperty(entryProperty.propertyPath);
            SerializedProperty position = entry?.FindPropertyRelative("cutinStartPosition");
            SerializedProperty enabled = entry?.FindPropertyRelative("useCutinStartPosition");
            if (position == null || enabled == null) continue;
            position.vector3Value = controller.TransformationCutinCircle.anchoredPosition3D;
            enabled.boolValue = true;
            serialized.ApplyModifiedProperties();
            captured = true;
        }
        Undo.CollapseUndoOperations(group);
        entryProperty.serializedObject.Update();
        return captured;
    }
}
