using UnityEngine;

[ExecuteAlways]
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(SpriteRenderer))]
public class HorizontalTiledBackground : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [Tooltip("Extra width on each side of the camera, in world units.")]
    [Min(0f)] [SerializeField] private float horizontalPadding = 2f;

    public Camera TargetCamera => targetCamera;

    public void SetTargetCamera(Camera camera)
    {
        targetCamera = camera;
        Refresh();
    }

    private void Reset()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        targetCamera = Camera.main;
    }

    private void OnValidate()
    {
        horizontalPadding = Mathf.Max(0f, horizontalPadding);
        // Validation can run during serialization/CheckConsistency. Sprite geometry setters send
        // native notifications, so leave all renderer/transform writes to ExecuteAlways LateUpdate.
    }

    private void LateUpdate()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
        // A disabled scene Player still holds a non-null camera reference after a Lobby launch.
        // Preserve authored editor references, but never follow an inactive camera during gameplay.
        if (targetCamera == null || (Application.isPlaying && !targetCamera.isActiveAndEnabled))
            targetCamera = Camera.main;
        if (spriteRenderer == null || spriteRenderer.sprite == null ||
            targetCamera == null || !targetCamera.orthographic)
            return;

        float scaleX = Mathf.Abs(spriteRenderer.transform.lossyScale.x);
        if (scaleX <= 0.0001f) return;

        if (spriteRenderer.drawMode != SpriteDrawMode.Tiled) spriteRenderer.drawMode = SpriteDrawMode.Tiled;
        if (spriteRenderer.tileMode != SpriteTileMode.Continuous) spriteRenderer.tileMode = SpriteTileMode.Continuous;
        float cameraWidth = 2f * targetCamera.orthographicSize * targetCamera.aspect;
        Vector2 size = spriteRenderer.size;
        size.x = (cameraWidth + 2f * horizontalPadding) / scaleX;
        if ((spriteRenderer.size - size).sqrMagnitude > 0.00000001f) spriteRenderer.size = size;

        // Center the visible bounds, including sprites with an off-center pivot.
        Vector3 position = spriteRenderer.transform.position;
        float offsetX = targetCamera.transform.position.x - spriteRenderer.bounds.center.x;
        if (Mathf.Abs(offsetX) > 0.0001f)
        {
            position.x += offsetX;
            spriteRenderer.transform.position = position;
        }
    }
}
