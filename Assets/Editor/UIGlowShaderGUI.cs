using UnityEditor;
using UnityEngine;

public sealed class UIGlowShaderGUI : ShaderGUI
{
    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        EditorGUILayout.HelpBox(
            "UICustomPolygonRenderer: 머터리얼만 할당하면 Glow가 생성됩니다.\n" +
            "Image / RawImage: 같은 오브젝트에 UIGlow 컴포넌트를 추가하세요.\n" +
            "Glow Intensity는 번짐 강도, Glow Brightness는 발광 밝기, Glow Falloff는 외곽 감쇠입니다.",
            MessageType.Info);
        EditorGUI.BeginChangeCheck();
        base.OnGUI(materialEditor, properties);
        if (EditorGUI.EndChangeCheck())
        {
            // ExecuteAlways callbacks are not guaranteed every editor frame. Queue a loop so
            // radius/falloff edits rebuild geometry and masked material copies immediately.
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
        EditorGUILayout.Space();
        if (GUILayout.Button("씬뷰를 게임 UI 미리보기에 맞추기"))
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                // Overlay UI is displayed after game-camera post-processing. Scene view can
                // otherwise tone-map that UI along with the world. Change preview only.
                view.sceneViewState.showImageEffects = false;
                view.Repaint();
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }
    }
}
