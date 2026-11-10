using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class CursorStateController : MonoBehaviour
{
    public enum CursorVisualState
    {
        Normal,
        Hover,
        Aim
    }

    [Header("Cursor Sprites")]
    [SerializeField] private Sprite normalCursor;
    [SerializeField] private Sprite hoverCursor;
    [SerializeField] private Sprite aimCursor;

    [Header("Cursor Settings")]
    [SerializeField] private CursorMode cursorMode = CursorMode.Auto;

    [Header("Debug Override")]
    [SerializeField] private bool useStateOverride = false;
    [SerializeField] private CursorVisualState overriddenState = CursorVisualState.Normal;

    private static CursorStateController instance;
    private static bool aimModeEnabled;
    private static bool worldInteractableHovered;

    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

    private Texture2D normalTexture;
    private Texture2D hoverTexture;
    private Texture2D aimTexture;
    private CursorVisualState currentState;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        CacheCursorTextures();
        ApplyCursor(GetDesiredState(), force: true);
    }

    private void OnEnable()
    {
        CacheCursorTextures();
        ApplyCursor(GetDesiredState(), force: true);
    }

    private void Update()
    {
        ApplyCursor(GetDesiredState(), force: false);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    public static void SetAimMode(bool enabled)
    {
        aimModeEnabled = enabled;
        instance?.ApplyCursor(instance.GetDesiredState(), force: true);
    }

    public static bool IsAimModeEnabled()
    {
        return aimModeEnabled;
    }

    public static void SetWorldInteractableHover(bool hovered)
    {
        worldInteractableHovered = hovered;
        instance?.ApplyCursor(instance.GetDesiredState(), force: true);
    }

    private CursorVisualState GetDesiredState()
    {
        if (useStateOverride)
        {
            return overriddenState;
        }

        if (aimModeEnabled)
        {
            return CursorVisualState.Aim;
        }

        return worldInteractableHovered || IsHoveringButton()
            ? CursorVisualState.Hover
            : CursorVisualState.Normal;
    }

    private bool IsHoveringButton()
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
        {
            position = Mouse.current != null ? Mouse.current.position.ReadValue() : (Vector2)Input.mousePosition
        };

        raycastResults.Clear();
        EventSystem.current.RaycastAll(pointerEventData, raycastResults);

        for (int i = 0; i < raycastResults.Count; i++)
        {
            GameObject hitObject = raycastResults[i].gameObject;
            if (hitObject == null)
            {
                continue;
            }

            if (hitObject.GetComponent<Button>() != null || hitObject.GetComponentInParent<Button>() != null)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyCursor(CursorVisualState nextState, bool force)
    {
        if (!force && currentState == nextState)
        {
            return;
        }

        currentState = nextState;

        Texture2D texture = normalTexture;
        Vector2 hotspot = GetHotspot(normalCursor);

        if (nextState == CursorVisualState.Hover)
        {
            texture = hoverTexture != null ? hoverTexture : normalTexture;
            hotspot = hoverCursor != null ? GetHotspot(hoverCursor) : hotspot;
        }
        else if (nextState == CursorVisualState.Aim)
        {
            texture = aimTexture != null ? aimTexture : normalTexture;
            hotspot = aimCursor != null ? GetHotspot(aimCursor) : hotspot;
        }

        Cursor.SetCursor(texture, hotspot, cursorMode);
    }

    private void CacheCursorTextures()
    {
        normalTexture = CreateCursorTexture(normalCursor);
        hoverTexture = CreateCursorTexture(hoverCursor);
        aimTexture = CreateCursorTexture(aimCursor);
    }

    private Texture2D CreateCursorTexture(Sprite sprite)
    {
        if (sprite == null)
        {
            return null;
        }

        Rect rect = sprite.rect;
        Texture2D sourceTexture = sprite.texture;
        Color[] pixels = sourceTexture.GetPixels(
            Mathf.FloorToInt(rect.x),
            Mathf.FloorToInt(rect.y),
            Mathf.FloorToInt(rect.width),
            Mathf.FloorToInt(rect.height)
        );

        Texture2D cursorTexture = new Texture2D(
            Mathf.FloorToInt(rect.width),
            Mathf.FloorToInt(rect.height),
            TextureFormat.RGBA32,
            false
        );
        cursorTexture.SetPixels(pixels);
        cursorTexture.Apply();
        return cursorTexture;
    }

    private Vector2 GetHotspot(Sprite sprite)
    {
        if (sprite == null)
        {
            return Vector2.zero;
        }

        return new Vector2(sprite.pivot.x, sprite.rect.height - sprite.pivot.y);
    }
}
