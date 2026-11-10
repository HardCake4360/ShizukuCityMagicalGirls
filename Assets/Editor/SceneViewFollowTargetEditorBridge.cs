#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SceneViewFollowTargetEditorBridge
{
    static SceneViewFollowTargetEditorBridge()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private static void OnEditorUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        SceneViewFollowTarget follower = Object.FindFirstObjectByType<SceneViewFollowTarget>();
        if (follower == null || !follower.FollowDuringPlayMode)
        {
            return;
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
        {
            return;
        }

        Transform target = follower.Target;
        if (target == null)
        {
            return;
        }

        sceneView.pivot = target.position + follower.WorldOffset;

        if (follower.LockRotation)
        {
            sceneView.rotation = follower.LockedRotation;
        }

        if (follower.LockSize)
        {
            sceneView.size = follower.SceneViewSize;
        }

        sceneView.Repaint();
    }
}
#endif
