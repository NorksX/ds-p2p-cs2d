using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Shooting")]
    [SerializeField] private float shootRange = 20f;
    [SerializeField] private LayerMask shootMask;
    [SerializeField] private GameObject tracerPrefab;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    /* ================= MOVEMENT ================= */

    public void SimulateMovement(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f)
        {
            rb.linearVelocity = Vector2.zero; // if your Unity complains, change to rb.velocity
            return;
        }

        rb.linearVelocity = input.normalized * moveSpeed;
    }

    /* ================= LOOK ================= */

    public void SimulateLook(Vector2 aimDir)
    {
        if (aimDir.sqrMagnitude < 0.0001f)
            return;

        float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    /* ================= SHOOTING ================= */

    public void SimulateShoot(Vector2 aimDir)
    {
        if (aimDir.sqrMagnitude < 0.0001f)
            return;

        Vector2 origin = transform.position;
        Vector2 direction = aimDir.normalized;

        RaycastHit2D hit = Physics2D.Raycast(origin, direction, shootRange, shootMask);

        Vector2 endPoint = origin + direction * shootRange;

        if (hit.collider != null)
        {
            endPoint = hit.point;

            ZombieHealth zombie = hit.collider.GetComponent<ZombieHealth>();
            if (zombie != null)
            {
                zombie.TakeDamage(2);
            }
        }

        SpawnTracer(origin, endPoint);
    }

    private void SpawnTracer(Vector2 start, Vector2 end)
    {
        if (tracerPrefab == null)
            return;

        GameObject tracer = Instantiate(tracerPrefab);

        Vector2 dir = end - start;
        float distance = dir.magnitude;
        Vector2 direction = dir.normalized;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        tracer.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        Vector3 scale = tracer.transform.localScale;
        scale.x = distance;
        tracer.transform.localScale = scale;

        tracer.transform.position = start + direction * (distance * 0.5f);
    }
}
