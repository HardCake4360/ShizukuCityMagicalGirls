using UnityEditor;
using UnityEngine;

namespace ShizukuCity.SpritePhysicsRig.Editor
{
    [CustomEditor(typeof(DeformableSpriteRenderer))]
    public sealed class DeformableSpriteRendererInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            DeformableSpriteRenderer renderer = (DeformableSpriteRenderer)target;
            if (!string.IsNullOrEmpty(renderer.LastError))
            {
                EditorGUILayout.HelpBox(renderer.LastError, MessageType.Error);
            }
            else if (renderer.IsBuilt)
            {
                DeformableSpriteRenderer.RuntimeDiagnostics diagnostics = renderer.Diagnostics;
                EditorGUILayout.HelpBox(
                    $"Rig mesh ready: {renderer.RuntimeMesh.vertexCount} vertices, {renderer.BoneCount} bones.\n"
                    + $"Simulation steps: {diagnostics.SimulatedFrameCount}, "
                    + $"collision queries/corrections: {diagnostics.CollisionQueryCount}/"
                    + $"{diagnostics.CollisionCorrectionCount}"
                    + (diagnostics.Teleported ? "\nLast step reset after a teleport." : string.Empty),
                    MessageType.Info);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Rebuild"))
            {
                renderer.Rebuild();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("Reset Physics"))
            {
                renderer.ResetSimulation();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Refresh Collision Ignore List"))
            {
                renderer.RefreshCollisionIgnoreList();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Open Rig Editor"))
            {
                SpriteRigEditorWindow.Open(renderer.RigAsset);
            }
        }
    }
}
