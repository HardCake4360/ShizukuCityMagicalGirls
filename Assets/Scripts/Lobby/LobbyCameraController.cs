using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class LobbyCameraController : MonoBehaviour
{
    [Header("Horizontal Movement")]
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0.01f)] private float followSmoothTime = 0.2f;
    [SerializeField] private Vector2 horizontalPositionLimits = new Vector2(-5f, 5f);

    private InputAction moveAction;
    private float targetHorizontalPosition;
    private float followVelocity;

    private void Awake()
    {
        InitializeInput();
        ClampPositionToLimits();
        targetHorizontalPosition = transform.position.x;
    }

    private void OnEnable()
    {
        InitializeInput();
        ClampPositionToLimits();
        targetHorizontalPosition = transform.position.x;
        followVelocity = 0f;
        moveAction.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        followVelocity = 0f;
    }

    private void OnDestroy()
    {
        moveAction?.Dispose();
    }

    private void Update()
    {
        float horizontalInput = moveAction != null ? moveAction.ReadValue<float>() : 0f;
        targetHorizontalPosition = Mathf.Clamp(
            targetHorizontalPosition + horizontalInput * moveSpeed * Time.deltaTime,
            horizontalPositionLimits.x,
            horizontalPositionLimits.y
        );

        Vector3 position = transform.position;
        position.x = Mathf.SmoothDamp(
            position.x,
            targetHorizontalPosition,
            ref followVelocity,
            followSmoothTime,
            Mathf.Infinity,
            Time.deltaTime
        );
        transform.position = position;
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        followSmoothTime = Mathf.Max(0.01f, followSmoothTime);
        if (horizontalPositionLimits.x > horizontalPositionLimits.y)
        {
            horizontalPositionLimits = new Vector2(
                horizontalPositionLimits.y,
                horizontalPositionLimits.x
            );
        }

        if (!Application.isPlaying)
        {
            ClampPositionToLimits();
            targetHorizontalPosition = transform.position.x;
        }
    }

    private void InitializeInput()
    {
        if (moveAction != null)
        {
            return;
        }

        moveAction = new InputAction(
            name: "Lobby Camera Move",
            type: InputActionType.Value,
            expectedControlType: "Axis"
        );
        moveAction.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a")
            .With("Positive", "<Keyboard>/d");
    }

    private void ClampPositionToLimits()
    {
        Vector3 position = transform.position;
        position.x = Mathf.Clamp(
            position.x,
            horizontalPositionLimits.x,
            horizontalPositionLimits.y
        );
        transform.position = position;
    }
}
