using UnityEngine;

[RequireComponent(typeof(PolygonCollider2D), typeof(LineRenderer))]
public sealed class EnemyDirectionalShield : MonoBehaviour
{
    [SerializeField] private DefensiveEnemy owner;
    [SerializeField, Range(1f, 179f)] private float coverageAngle = 160f;
    [SerializeField, Min(0.1f)] private float radius = 1.1f;
    [SerializeField, Min(0.01f)] private float thickness = 0.12f;
    [SerializeField, Range(4, 64)] private int segments = 24;
    [SerializeField] private Color shieldColor = new Color(0.2f, 0.8f, 1f, 0.65f);
    [SerializeField, Min(0f)] private float hitFlashDuration = 0.12f;

    private LineRenderer line;
    private Material runtimeMaterial;
    private float flashUntil;

    private void Awake()
    {
        if (owner == null)
            owner = GetComponentInParent<DefensiveEnemy>();
        gameObject.layer = owner != null ? owner.gameObject.layer : gameObject.layer;
        line = GetComponent<LineRenderer>();
        BuildGeometry();
    }

    private void Update()
    {
        Color color = Time.time < flashUntil ? Color.white : shieldColor;
        line.startColor = color;
        line.endColor = color;
    }

    private void BuildGeometry()
    {
        int count = Mathf.Clamp(segments, 4, 64);
        float safeRadius = Mathf.Max(0.1f, radius);
        float width = Mathf.Clamp(thickness, 0.01f, safeRadius);
        float halfAngle = Mathf.Clamp(coverageAngle, 1f, 179f) * 0.5f;
        Vector2[] polygon = new Vector2[(count + 1) * 2];
        line.useWorldSpace = false;
        line.loop = false;
        line.positionCount = count + 1;
        line.widthMultiplier = 1f;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = shieldColor;
        line.endColor = shieldColor;
        line.sortingOrder = 3;
        for (int i = 0; i <= count; i++)
        {
            float angle = Mathf.Lerp(-halfAngle, halfAngle, (float)i / count) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            line.SetPosition(i, direction * safeRadius);
            polygon[i] = direction * (safeRadius + width * 0.5f);
            polygon[polygon.Length - 1 - i] = direction * (safeRadius - width * 0.5f);
        }
        PolygonCollider2D collider = GetComponent<PolygonCollider2D>();
        collider.isTrigger = true;
        collider.pathCount = 1;
        collider.SetPath(0, polygon);
        Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            runtimeMaterial = new Material(shader);
            line.sharedMaterial = runtimeMaterial;
        }
    }

    public bool Blocks(Vector2 hitPoint, Vector2 hitDirection, GameObject source)
    {
        if (!isActiveAndEnabled || owner == null || owner.IsDead)
            return false;

        Vector2 incomingFrom = -hitDirection;
        if (incomingFrom.sqrMagnitude <= 0.000001f && source != null)
            incomingFrom = (Vector2)source.transform.position - (Vector2)owner.transform.position;
        if (incomingFrom.sqrMagnitude <= 0.000001f)
            incomingFrom = hitPoint - (Vector2)owner.transform.position;
        if (incomingFrom.sqrMagnitude <= 0.000001f)
            return false;

        return Vector2.Angle(transform.right, incomingFrom) <= coverageAngle * 0.5f;
    }

    public void ShowBlockedHit() => flashUntil = Time.time + hitFlashDuration;

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeMaterial);
            else
                DestroyImmediate(runtimeMaterial);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = shieldColor;
        Vector3 previous = transform.TransformPoint(Quaternion.Euler(0f, 0f, -coverageAngle * 0.5f) * Vector3.right * radius);
        for (int i = 1; i <= Mathf.Max(4, segments); i++)
        {
            float angle = Mathf.Lerp(-coverageAngle * 0.5f, coverageAngle * 0.5f, (float)i / Mathf.Max(4, segments));
            Vector3 point = transform.TransformPoint(Quaternion.Euler(0f, 0f, angle) * Vector3.right * radius);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
