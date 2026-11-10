using UnityEngine;

public class GroundChecker : MonoBehaviour
{
    [SerializeField] private BoxCollider2D Collider;

    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private bool logGroundState;

    public bool CheckIfTouchingGround()
    {
        return Collider.IsTouchingLayers(groundLayer);
    }

    private void Update()
    {
        if (logGroundState && CheckIfTouchingGround())
        {
            Debug.Log("grounding");
        }
    }
}
