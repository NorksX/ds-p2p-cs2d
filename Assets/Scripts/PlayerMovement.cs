using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private LayerMask obstacleMask = 1;
    [SerializeField] private float footprintRadius = 0.35f;

    [Header("Shooting")]
    [SerializeField] private float shootRange = 20f;
    [SerializeField] private LayerMask shootMask;
    [SerializeField] private GameObject tracerPrefab;

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private ContactFilter2D obstacleFilter;
    private float separationRadius;

    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private readonly Collider2D[] overlapHits = new Collider2D[8];

    // Leaves a sliver of space at contact so the next sweep does not start already touching.
    private const float Skin = 0.01f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();

        obstacleFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = obstacleMask,
            useTriggers = false
        };

        // From the shape, not bounds: bounds are still zero this early in the lifecycle.
        CircleCollider2D circle = bodyCollider as CircleCollider2D;
        float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
        separationRadius = (circle != null ? circle.radius * scale : 0.5f) + Skin;
    }

    /// <summary>
    /// One simulation step. Deliberately a pure function of (position, input): it sweeps and
    /// assigns position itself rather than handing a velocity to the solver, so reconciliation
    /// can replay it many times in a single frame.
    /// </summary>
    public void SimulateMovement(Vector2 input)
    {
        SeparateFromOverlaps();

        if (input.sqrMagnitude < 0.0001f)
            return;

        float dt = TickManager.Instance != null ? TickManager.Instance.TickInterval : 1f / 30f;
        Move(input.normalized * moveSpeed * dt);
    }

    // Players block each other like walls, but never push each other: each resolves only its
    // own movement, treating everyone else as static. Nothing is displaced, so two machines
    // have no shared solver outcome to disagree about.
    private void Move(Vector2 delta)
    {
        Vector2 start = rb.position;

        // Travel, then spend whatever is left sliding along the surface that stopped us.
        for (int pass = 0; pass < 2; pass++)
        {
            float distance = delta.magnitude;
            if (distance < 0.00001f)
                break;

            Vector2 dir = delta / distance;
            int count = rb.Cast(dir, obstacleFilter, castHits, distance + Skin);

            if (count == 0)
            {
                // rb.Cast sweeps from the body's current position, so commit before re-casting.
                SetPosition(rb.position + delta);
                break;
            }

            RaycastHit2D nearest = castHits[0];
            for (int i = 1; i < count; i++)
            {
                if (castHits[i].distance < nearest.distance)
                    nearest = castHits[i];
            }

            float travel = Mathf.Max(0f, nearest.distance - Skin);
            SetPosition(rb.position + dir * travel);

            Vector2 remaining = dir * (distance - travel);
            delta = remaining - Vector2.Dot(remaining, nearest.normal) * nearest.normal;
        }

        SetPosition(ConstrainToWalkable(start, rb.position));
    }

    // Shared with zombies, so both obey the map the same way.
    private Vector2 ConstrainToWalkable(Vector2 from, Vector2 to)
    {
        if (WalkableMap.Instance == null)
            return to;

        return WalkableMap.Instance.ConstrainMove(from, to, footprintRadius);
    }

    // Two players wedged inside each other can never escape by sweeping, because every
    // direction is blocked. Push apart first so movement always has somewhere to go.
    private void SeparateFromOverlaps()
    {
        if (bodyCollider == null)
            return;

        int count = Physics2D.OverlapCircle(rb.position, separationRadius, obstacleFilter, overlapHits);

        for (int i = 0; i < count; i++)
        {
            if (overlapHits[i] == bodyCollider)
                continue;

            ColliderDistance2D separation = bodyCollider.Distance(overlapHits[i]);

            // distance is negative while overlapping, so this moves us away from the normal.
            if (separation.isOverlapped)
                SetPosition(rb.position + separation.normal * separation.distance);
        }
    }

    public Vector2 Position => rb != null ? rb.position : (Vector2)transform.position;

    /// <summary>
    /// Move without sweeping. Used when applying authoritative state, where the position is
    /// already correct by definition and must not be re-resolved against local obstacles.
    /// </summary>
    public void Teleport(Vector2 position)
    {
        SetPosition(position);
    }

    // Body and transform are kept in lockstep: the body is what Cast queries, the transform is
    // what the camera, state broadcast and shooting all read.
    private void SetPosition(Vector2 position)
    {
        rb.position = position;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }


    public void SimulateLook(Vector2 aimDir)
    {
        if (aimDir.sqrMagnitude < 0.0001f)
            return;

        float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }


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

            // TakeDamage is host-gated, so a client's shot is purely a visual tracer.
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
