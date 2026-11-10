using UnityEngine;

public class SceneViewFollowTarget : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, 0f, -10f);
    [SerializeField] private bool followDuringPlayMode = true;
    [SerializeField] private bool lockRotation = false;
    [SerializeField] private Vector3 eulerAngles;
    [SerializeField] private bool lockSize = false;
    [SerializeField] private float sceneViewSize = 10f;

    public Transform Target => target != null ? target : transform;
    public Vector3 WorldOffset => worldOffset;
    public bool FollowDuringPlayMode => followDuringPlayMode;
    public bool LockRotation => lockRotation;
    public Quaternion LockedRotation => Quaternion.Euler(eulerAngles);
    public bool LockSize => lockSize;
    public float SceneViewSize => sceneViewSize;

    private void Reset()
    {
        target = transform;
    }

    private void OnValidate()
    {
        sceneViewSize = Mathf.Max(0.1f, sceneViewSize);
    }
}
