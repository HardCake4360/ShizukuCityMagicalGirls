using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerWeaponController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Player owner;
    [SerializeField] private Camera aimCamera;
    [SerializeField] private Transform aimOrigin;

    [Header("Render Texture Aim Correction")]
    [SerializeField] private RectTransform renderTextureView;
    [SerializeField] private Camera uiCamera;
    [SerializeField] private bool clampPointerToRenderTextureView;

    [Header("Weapons")]
    [SerializeField] private Weapon mainWeapon;
    [SerializeField] private Weapon specialWeapon;

    [Header("Debug")]
    [SerializeField] private bool drawAimLine;
    [SerializeField] private float aimLineLength = 3f;

    private Vector2 currentAimPoint;
    private Vector2 currentAimDirection = Vector2.right;
    private readonly Dictionary<object, float> aimTurnSpeedLimits =
        new Dictionary<object, float>();
    private InputAction mainAttackAction;
    private InputAction specialAttackAction;
    private bool mainPressAccepted;
    private bool specialPressAccepted;
    private readonly MouseSensitivityPointer sensitivityPointer = new();

    public Player Owner => owner;
    public Vector2 CurrentAimPoint => currentAimPoint;
    public Vector2 CurrentAimDirection => currentAimDirection;
    public bool HasAimTurnSpeedLimit => aimTurnSpeedLimits.Count > 0;
    public Transform AimOrigin => aimOrigin != null ? aimOrigin : transform;
    public bool IsAttacking =>
        (mainWeapon != null && mainWeapon.IsAttacking)
        || (specialWeapon != null && specialWeapon.IsAttacking);
    public bool CanChangeDirectionWhileHovering =>
        (mainWeapon != null && mainWeapon.ChangeDirectionWhileHovering)
        || (specialWeapon != null && specialWeapon.ChangeDirectionWhileHovering);
    public bool HasAttackInput =>
        HasMainAttackInput || HasSpecialAttackInput;
    public bool HasMainAttackInput => mainAttackAction != null && !InputBlocked(mainAttackAction) && mainAttackAction.IsPressed();
    public bool HasSpecialAttackInput => specialAttackAction != null && !InputBlocked(specialAttackAction) && specialAttackAction.IsPressed();
    private bool InputBlocked(InputAction action) => owner != null && owner.IsGameplayInputBlocked(action);
    public void SetAimCamera(Camera camera) { aimCamera = camera; UpdateAimCache(); }
    public IEnumerable<InputAction> GetGameplayInputActions()
    {
        InitializeInputActions();
        yield return mainAttackAction;
        yield return specialAttackAction;
    }
    public bool IsMainWeaponAttacking => mainWeapon != null && mainWeapon.IsAttacking;
    public bool IsSpecialWeaponAttacking => specialWeapon != null && specialWeapon.IsAttacking;
    public bool IsAimingForCamera => isActiveAndEnabled
        && (owner == null || (!owner.IsDead && !owner.IsBattleEntryLocked))
        && (IsCameraAimWeapon(mainWeapon) || IsCameraAimWeapon(specialWeapon));
    private static bool IsCameraAimWeapon(Weapon weapon) =>
        weapon != null && weapon.isActiveAndEnabled && weapon.IsCameraAiming;
    public Weapon CurrentAimWeapon
    {
        get
        {
            if (specialWeapon != null && specialWeapon.IsAttacking)
            {
                return specialWeapon;
            }

            if (mainWeapon != null && mainWeapon.IsAttacking)
            {
                return mainWeapon;
            }

            if (mainWeapon != null && mainWeapon.ChangeDirectionWhileHovering)
            {
                return mainWeapon;
            }

            return specialWeapon != null && specialWeapon.ChangeDirectionWhileHovering
                ? specialWeapon
                : null;
        }
    }

    private void Awake()
    {
        CacheReferences();
        InitializeWeapons();
        UpdateAimCache();
    }

    private void OnEnable()
    {
        sensitivityPointer.Reset();
        InitializeInputActions();

        mainAttackAction.Enable();
        specialAttackAction.Enable();
        // Button performed respects trigger press thresholds; started can be a tiny analog touch.
        mainAttackAction.performed += OnMainAttackPerformed;
        mainAttackAction.canceled += OnMainAttackCanceled;
        specialAttackAction.performed += OnSpecialAttackPerformed;
        specialAttackAction.canceled += OnSpecialAttackCanceled;
    }

    private void OnDisable()
    {
        // Releasing a charged weapon can fire it. Death cancels attacks instead.
        if (owner == null || (!owner.IsDead && !owner.IsBattleEntryLocked))
        {
            ReleaseWeapon(mainWeapon);
            ReleaseSpecialWeapon();
        }
        aimTurnSpeedLimits.Clear();
        mainPressAccepted = specialPressAccepted = false;

        if (mainAttackAction != null)
        {
            mainAttackAction.performed -= OnMainAttackPerformed;
            mainAttackAction.canceled -= OnMainAttackCanceled;
            mainAttackAction.Disable();
        }

        if (specialAttackAction != null)
        {
            specialAttackAction.performed -= OnSpecialAttackPerformed;
            specialAttackAction.canceled -= OnSpecialAttackCanceled;
            specialAttackAction.Disable();
        }
    }

    private void OnDestroy()
    {
        mainAttackAction?.Dispose();
        specialAttackAction?.Dispose();
    }

    private void Update()
    {
        UpdateAimCache();
        HandleHeldWeapons();

        if (drawAimLine)
        {
            Debug.DrawRay(AimOrigin.position, currentAimDirection * aimLineLength, Color.magenta);
        }
    }

    private void OnValidate()
    {
        aimLineLength = Mathf.Max(0f, aimLineLength);
    }

    private void CacheReferences()
    {
        if (owner == null)
        {
            owner = GetComponent<Player>();
        }

        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }

        if (uiCamera == null && renderTextureView != null)
        {
            Canvas canvas = renderTextureView.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                uiCamera = canvas.worldCamera;
            }
        }
    }

    private void InitializeWeapons()
    {
        FindWeaponsBySlot();
        mainWeapon?.Initialize(owner);
        specialWeapon?.Initialize(owner);
    }

    private void FindWeaponsBySlot()
    {
        Weapon[] weapons = GetComponentsInChildren<Weapon>(true);
        for (int i = 0; i < weapons.Length; i++)
        {
            Weapon weapon = weapons[i];
            if (weapon == null)
            {
                continue;
            }

            if (mainWeapon == null && weapon.SlotType == WeaponSlotType.MainWeapon)
            {
                mainWeapon = weapon;
            }
            else if (specialWeapon == null && weapon.SlotType == WeaponSlotType.SpecialWeapon)
            {
                specialWeapon = weapon;
            }
            else if (weapon.SlotType == WeaponSlotType.DualWeapon)
            {
                if (mainWeapon == null)
                {
                    mainWeapon = weapon;
                }

                if (specialWeapon == null)
                {
                    specialWeapon = weapon;
                }
            }
        }
    }

    private void InitializeInputActions()
    {
        if (mainAttackAction != null && specialAttackAction != null)
        {
            return;
        }

        mainAttackAction = new InputAction(name: "Main Attack", type: InputActionType.Button);
        mainAttackAction.AddBinding("<Mouse>/leftButton");
        mainAttackAction.AddBinding("<Gamepad>/rightTrigger");

        specialAttackAction = new InputAction(name: "Special Attack", type: InputActionType.Button);
        specialAttackAction.AddBinding("<Mouse>/rightButton");
        specialAttackAction.AddBinding("<Gamepad>/leftTrigger");
    }

    private void HandleHeldWeapons()
    {
        if (HasMainAttackInput && mainPressAccepted)
        {
            mainWeapon?.OnAttackHeld();
        }

        if (HasSpecialAttackInput && specialPressAccepted)
        {
            if (specialWeapon is ISpecialAttackWeapon specialAttackWeapon)
            {
                specialAttackWeapon.OnSpecialAttackHeld();
            }
            else
            {
                specialWeapon?.OnAttackHeld();
            }
        }
    }

    private void OnMainAttackPerformed(InputAction.CallbackContext context)
    {
        if (owner != null && !owner.TryAcceptGameplayInput(context.action)) return;
        mainPressAccepted = true;
        mainWeapon?.OnAttackPressed();
    }

    private void OnMainAttackCanceled(InputAction.CallbackContext context)
    {
        if (!mainPressAccepted) return;
        mainPressAccepted = false;
        if (InputBlocked(context.action)) return;
        ReleaseWeapon(mainWeapon);
    }

    private void OnSpecialAttackPerformed(InputAction.CallbackContext context)
    {
        if (owner != null && !owner.TryAcceptGameplayInput(context.action)) return;
        specialPressAccepted = true;
        if (specialWeapon is ISpecialAttackWeapon specialAttackWeapon)
        {
            specialAttackWeapon.OnSpecialAttackPressed();
        }
        else
        {
            specialWeapon?.OnAttackPressed();
        }
    }

    private void OnSpecialAttackCanceled(InputAction.CallbackContext context)
    {
        if (!specialPressAccepted) return;
        specialPressAccepted = false;
        if (InputBlocked(context.action)) return;
        ReleaseSpecialWeapon();
    }

    private void ReleaseSpecialWeapon()
    {
        if (specialWeapon is ISpecialAttackWeapon specialAttackWeapon)
        {
            specialAttackWeapon.OnSpecialAttackReleased();
            return;
        }

        ReleaseWeapon(specialWeapon);
    }

    private void ReleaseWeapon(Weapon weapon)
    {
        if (weapon != null && weapon.IsAttacking)
        {
            weapon.OnAttackReleased();
        }
    }

    private void UpdateAimCache()
    {
        Vector3 origin = AimOrigin.position;
        currentAimPoint = GetAimWorldPoint(origin);
        Vector2 targetDirection = CalculateAimDirection(origin, currentAimPoint, currentAimDirection);

        if (!TryGetAimTurnSpeedLimit(out float turnSpeedLimit))
        {
            currentAimDirection = targetDirection;
            return;
        }

        float currentAngle = Mathf.Atan2(currentAimDirection.y, currentAimDirection.x) * Mathf.Rad2Deg;
        float targetAngle = Mathf.Atan2(targetDirection.y, targetDirection.x) * Mathf.Rad2Deg;
        float nextAngle = Mathf.MoveTowardsAngle(
            currentAngle,
            targetAngle,
            turnSpeedLimit * Time.deltaTime
        );
        currentAimDirection = new Vector2(
            Mathf.Cos(nextAngle * Mathf.Deg2Rad),
            Mathf.Sin(nextAngle * Mathf.Deg2Rad)
        );
    }

    public Vector2 GetAimDirection()
    {
        return GetAimDirection(AimOrigin.position);
    }

    public Vector2 GetAimDirection(Vector3 origin)
    {
        if (HasAimTurnSpeedLimit)
        {
            return currentAimDirection.sqrMagnitude > 0.0001f
                ? currentAimDirection.normalized
                : Vector2.right;
        }

        Vector2 aimPoint = GetAimWorldPoint(origin);
        return CalculateAimDirection(origin, aimPoint, currentAimDirection);
    }

    public void SetAimTurnSpeedLimit(object source, float degreesPerSecond)
    {
        if (source == null)
        {
            return;
        }

        aimTurnSpeedLimits[source] = Mathf.Max(0f, degreesPerSecond);
    }

    public void RemoveAimTurnSpeedLimit(object source)
    {
        if (source != null)
        {
            aimTurnSpeedLimits.Remove(source);
        }
    }

    public void RemoveAllAimTurnSpeedLimits()
    {
        aimTurnSpeedLimits.Clear();
    }

    public bool TryGetAimTurnSpeedLimit(out float turnSpeedLimit)
    {
        turnSpeedLimit = float.PositiveInfinity;
        foreach (float limit in aimTurnSpeedLimits.Values)
        {
            turnSpeedLimit = Mathf.Min(turnSpeedLimit, limit);
        }

        return !float.IsPositiveInfinity(turnSpeedLimit);
    }

    public Vector2 GetAimWorldPoint()
    {
        return GetAimWorldPoint(AimOrigin.position);
    }

    public Vector2 GetAimWorldPoint(Vector3 origin)
    {
        Camera cameraToUse = aimCamera != null ? aimCamera : Camera.main;
        if (cameraToUse == null)
        {
            return origin;
        }

        Vector3 screenPosition = GetPointerScreenPosition();
        screenPosition = GetCorrectedCameraScreenPosition(cameraToUse, screenPosition);
        screenPosition.z = Mathf.Abs(cameraToUse.transform.position.z - origin.z);
        return cameraToUse.ScreenToWorldPoint(screenPosition);
    }

    private Vector3 GetCorrectedCameraScreenPosition(Camera cameraToUse, Vector3 pointerScreenPosition)
    {
        if (renderTextureView == null)
        {
            return pointerScreenPosition;
        }

        Camera eventCamera = GetRenderTextureViewEventCamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            renderTextureView,
            pointerScreenPosition,
            eventCamera,
            out Vector2 localPoint
        ))
        {
            return pointerScreenPosition;
        }

        Rect rect = renderTextureView.rect;
        if (rect.width <= 0.0001f || rect.height <= 0.0001f)
        {
            return pointerScreenPosition;
        }

        float normalizedX = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float normalizedY = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);

        if (clampPointerToRenderTextureView)
        {
            normalizedX = Mathf.Clamp01(normalizedX);
            normalizedY = Mathf.Clamp01(normalizedY);
        }

        Vector2 renderTargetSize = GetCameraRenderTargetSize(cameraToUse);
        return new Vector3(
            normalizedX * renderTargetSize.x,
            normalizedY * renderTargetSize.y,
            pointerScreenPosition.z
        );
    }

    private Camera GetRenderTextureViewEventCamera()
    {
        if (uiCamera != null)
        {
            return uiCamera;
        }

        if (renderTextureView == null)
        {
            return null;
        }

        Canvas canvas = renderTextureView.GetComponentInParent<Canvas>();
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return canvas.worldCamera;
    }

    private static Vector2 GetCameraRenderTargetSize(Camera cameraToUse)
    {
        if (cameraToUse.targetTexture != null)
        {
            return new Vector2(cameraToUse.targetTexture.width, cameraToUse.targetTexture.height);
        }

        return new Vector2(cameraToUse.pixelWidth, cameraToUse.pixelHeight);
    }

    private Vector3 GetPointerScreenPosition()
    {
        Vector2 rawPosition = Mouse.current != null
            ? Mouse.current.position.ReadValue() : (Vector2)UnityEngine.Input.mousePosition;
        if (!Application.isFocused || Time.timeScale <= 0f)
        {
            sensitivityPointer.Reset();
            return rawPosition;
        }
        Vector2? inputDelta = Mouse.current != null ? Mouse.current.delta.ReadValue() : (Vector2?)null;
        return sensitivityPointer.GetPosition(rawPosition, GameSettings.MouseSensitivityMultiplier,
            new Rect(0f, 0f, Screen.width, Screen.height), Time.frameCount, inputDelta);
    }

    private void OnApplicationFocus(bool focused) => sensitivityPointer.Reset();

    private static Vector2 CalculateAimDirection(Vector3 origin, Vector2 aimPoint, Vector2 fallbackDirection)
    {
        Vector2 direction = aimPoint - (Vector2)origin;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection : Vector2.right;
        }

        return direction.normalized;
    }
}
