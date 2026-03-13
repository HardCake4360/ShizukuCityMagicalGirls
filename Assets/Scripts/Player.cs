using UnityEngine;

public class Player : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] bool printDebugLog;
    [SerializeField] bool drawDirectionLine;

    [Header("Physic Setting")]
    [SerializeField] float gravity = 9.8f;
    [SerializeField] float maxFallSpeed = 15f;
    [SerializeField] float maxSpeed = 10f;
    [SerializeField] float acceleration = 15f;
    [SerializeField] float drag = 3f; // 관성을 서서히 줄여줄 공기 저항
    [SerializeField] float maxRollSpeed = 180f;
    [SerializeField] float rollAccel = 360f;

    [Header("Debug view")]
    [SerializeField] Vector3 velocity;
    [SerializeField] Vector2 direction;

    GroundChecker groundChecker;

    private float currentRollSpeed = 0f;
    private bool grounded;

    void Start()
    {
        groundChecker = GetComponentInChildren<GroundChecker>();
        velocity = Vector3.zero;
        direction = Vector2.up;
        grounded = false;
    }

    void Update()
    {
        grounded = groundChecker.CheckIfTouchingGround();

        rotate();
        updateVelocity(); // 함수 이름 변경

        transform.Translate(velocity * Time.deltaTime);

        // debug zone
        if (drawDirectionLine)
        {
            Debug.DrawRay(transform.position, direction * 2f, Color.red);
        }
    }

    // 관성이 적용된 새로운 물리 계산 함수
    void updateVelocity()
    {
        // 1. 중력 적용 (기존 속도에 아래로 당기는 힘을 더해줌)
        if (grounded && velocity.y <= 0) // 땅에 닿아있고 아래로 떨어지는 중일 때만
        {
            velocity.y = 0; // 땅을 뚫지 않도록 Y축 속도 멈춤
        }
        else
        {
            velocity.y -= gravity * Time.deltaTime; // 매 프레임 중력 누적
            velocity.y = Mathf.Max(velocity.y, -maxFallSpeed); // 최대 낙하 속도 제한
        }

        // 2. 추진력(관성) 적용
        if (Input.GetKey(KeyCode.Space))
        {
            // 핵심: 바라보는 방향(direction)으로 가속도를 곱해서 기존 속도에 '더해줍니다(+)'
            velocity += (Vector3)(direction * acceleration * Time.deltaTime);

            // 속도가 무한정 빨라지지 않게 전체 속도 제한
            if (velocity.magnitude > maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }
        }
        else
        {
            // 3. 공기 저항 적용 (스페이스바를 뗐을 때 서서히 멈추는 관성 감속)
            // X축(좌우) 속도를 0을 향해 서서히 줄여 미끄러지듯 멈추게 함
            velocity.x = Mathf.MoveTowards(velocity.x, 0, drag * Time.deltaTime);

            // 공중으로 솟구치고 있을 때도 공기 저항을 받아 부드럽게 정점으로 가도록 함
            if (velocity.y > 0)
            {
                velocity.y = Mathf.MoveTowards(velocity.y, 0, drag * Time.deltaTime);
            }
        }
    }

    void rotate()
    {
        float targetRollSpeed = 0f;

        if (Input.GetKey(KeyCode.A))
        {
            targetRollSpeed = maxRollSpeed;
        }
        else if (Input.GetKey(KeyCode.D))
        {
            targetRollSpeed = -maxRollSpeed;
        }

        currentRollSpeed = Mathf.MoveTowards(currentRollSpeed, targetRollSpeed, rollAccel * Time.deltaTime);
        float angleToRotate = currentRollSpeed * Time.deltaTime;

        direction = Quaternion.Euler(0, 0, angleToRotate) * direction;
        direction = direction.normalized;
    }
}