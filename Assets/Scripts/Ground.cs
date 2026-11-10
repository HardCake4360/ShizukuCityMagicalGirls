using UnityEngine;

public class Ground : MonoBehaviour
{
    Player player;
    [SerializeField] float bottomHeight;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        player = FindFirstObjectByType<Player>();
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = new Vector3(player.transform.localPosition.x, bottomHeight, 0);
    }
}
