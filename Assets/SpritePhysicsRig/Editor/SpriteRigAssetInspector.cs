using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig.Editor
{
    [CustomEditor(typeof(SpriteRigAsset))]
    public sealed class SpriteRigAssetInspector : UnityEditor.Editor
    {
        readonly List<RigValidationIssue> issues = new List<RigValidationIssue>();

        public override void OnInspectorGUI()
        {
            SpriteRigAsset asset = (SpriteRigAsset)target;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Rig Editor", GUILayout.Height(28f)))
            {
                SpriteRigEditorWindow.Open(asset);
            }
            if (GUILayout.Button("Validate", GUILayout.Height(28f)))
            {
                asset.Validate(issues);
            }
            EditorGUILayout.EndHorizontal();

            bool valid = asset.Validate(issues);
            if (valid)
            {
                EditorGUILayout.HelpBox("Rig data is valid.", MessageType.Info);
            }
            else
            {
                int shown = Mathf.Min(6, issues.Count);
                for (int i = 0; i < shown; i++)
                {
                    EditorGUILayout.HelpBox(issues[i].ToString(), MessageType.Error);
                }
                if (issues.Count > shown)
                {
                    EditorGUILayout.LabelField($"+ {issues.Count - shown} more issue(s)");
                }
            }

            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
    }
}
