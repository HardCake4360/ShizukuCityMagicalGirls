using UnityEngine;

public class GroundChecker : MonoBehaviour
{
    private BoxCollider2D Collider;

    [SerializeField] private LayerMask groundLayer;

    private void Start()
    {
        Collider = GetComponent<BoxCollider2D>();
    }

    public bool CheckIfTouchingGround()
    {
        return Collider.IsTouchingLayers(groundLayer);
    }

    private void Update()
    {
        if (CheckIfTouchingGround())
        {
            Debug.Log("grounding");
        }
    }
}
