using UnityEngine;

public class ZombieFollow : MonoBehaviour
{
    public float moveSpeed = 2f;
    public float attackRange = 1f;
    public float attackCooldown = 1f;

    [Tooltip("Smaller than the collider so one-tile gaps stay passable")]
    [SerializeField] private float footprintRadius = 0.35f;

    [Header("Crowding")]
    [Tooltip("Layer the other zombies are on")]
    [SerializeField] private LayerMask zombieMask = 1 << 6;
    [Tooltip("Neighbours closer than this push back")]
    [SerializeField] private float personalSpace = 0.9f;
    [Tooltip("How hard crowding competes with chasing. Steering, not hard blocking, so a " +
             "swarm still flows through gaps instead of jamming solid.")]
    [SerializeField] private float separationStrength = 1.6f;

    private Transform target;
    private PlayerHealth targetHealth; // Cache component
    private float lastAttackTime;
    private Rigidbody2D body;
    private Collider2D bodyCollider;
    private ContactFilter2D neighbourFilter;
    private readonly Collider2D[] neighbours = new Collider2D[8];

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();

        neighbourFilter = new ContactFilter2D
        {
            useLayerMask = true,
            layerMask = zombieMask,
            useTriggers = false
        };
    }

    /// <summary>
    /// Push away from crowding neighbours. Weighted by closeness so distant ones barely
    /// matter, and blended into the chase direction rather than blocking it - hard collision
    /// between dozens of zombies converging on one player deadlocks the whole swarm.
    /// </summary>
    private Vector2 ComputeSeparation(Vector2 position)
    {
        if (bodyCollider == null)
            return Vector2.zero;

        int count = Physics2D.OverlapCircle(position, personalSpace, neighbourFilter, neighbours);
        Vector2 push = Vector2.zero;

        for (int i = 0; i < count; i++)
        {
            if (neighbours[i] == null || neighbours[i] == bodyCollider)
                continue;

            Vector2 away = position - (Vector2)neighbours[i].transform.position;
            float distance = away.magnitude;

            // Exactly coincident (e.g. spawned on the same point) has no direction to escape
            // along, so pick one rather than dividing by zero.
            if (distance < 0.0001f)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                away = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                distance = 0.0001f;
            }

            push += away / distance * (1f - Mathf.Clamp01(distance / personalSpace));
        }

        return push;
    }

    // Deliberately no Start-time host check. Migration can hand us authority at any moment,
    // and a component that latched its role at Start would stay frozen forever afterwards.
    private const float TargetRefreshInterval = 0.5f;
    private float nextTargetRefresh;


    private void UpdateTarget()
    {
        Transform closestEnemy = null;
        float minDistance = Mathf.Infinity;
        
        // 1. Use PlayerSpawner if available (Multiplayer)
        if (PlayerSpawner.Instance != null)
        {
            foreach (var kvp in PlayerSpawner.Instance.GetAllPlayers())
            {
                NetworkedPlayer player = kvp.Value;
                if (player == null) continue;

                // Corpses are not targets - they used to be swarmed for the whole respawn delay.
                if (IsDead(player.gameObject)) continue;

                float dist = Vector2.Distance(transform.position, player.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestEnemy = player.transform;
                }
            }
        }
        // 2. Fallback to Tag search (Singleplayer / Testing)
        else
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            foreach (GameObject player in players)
            {
                if (IsDead(player)) continue;

                float dist = Vector2.Distance(transform.position, player.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestEnemy = player.transform;
                }
            }
        }

        // A dead current target must be dropped even if nothing else is in range, otherwise
        // the zombie keeps standing on the body until something closer appears.
        if (target != null && IsDead(target.gameObject))
        {
            target = null;
            targetHealth = null;
        }

        // Update Target & Cache
        if (closestEnemy != null && closestEnemy != target)
        {
            target = closestEnemy;
            targetHealth = target.GetComponent<PlayerHealth>(); // Update cache only when target changes
        }
        else if (closestEnemy == null)
        {
            target = null;
            targetHealth = null;
        }
    }

    private void Update()
    {
        // AI runs on the host only; clients receive positions and interpolate them.
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
            return;

        if (Time.time >= nextTargetRefresh)
        {
            nextTargetRefresh = Time.time + TargetRefreshInterval;
            UpdateTarget();
        }

        if (target == null) return;

        Vector2 toTarget = (Vector2)target.position - (Vector2)transform.position;
        float distance = toTarget.magnitude;

        // Move if not too close
        if (distance > attackRange)
        {
            Vector2 current = transform.position;

            // Follow the shared flow field so walls are routed around rather than pressed
            // against. Straight-line is only the fallback when no route exists.
            if (ZombieSpawner.Instance == null ||
                !ZombieSpawner.Instance.TryGetFlowDirection(current, out Vector2 heading))
            {
                heading = toTarget.normalized;
            }

            heading = (heading + ComputeSeparation(current) * separationStrength).normalized;

            Vector2 desired = current + heading * moveSpeed * Time.deltaTime;

            if (WalkableMap.Instance != null)
                desired = WalkableMap.Instance.ConstrainMove(current, desired, footprintRadius);

            // Face where we are actually going, not through the wall at the player.
            Vector2 travelled = desired - current;
            if (travelled.sqrMagnitude > 0.000001f)
            {
                float moveAngle = Mathf.Atan2(travelled.y, travelled.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, moveAngle);
            }

            transform.position = new Vector3(desired.x, desired.y, transform.position.z);

            if (body != null)
                body.position = desired;
        }
        else
        {
            float angle = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        // Attack if in range
        if (distance <= attackRange)
        {
            TryAttack();
        }
    }


    private static bool IsDead(GameObject player)
    {
        PlayerHealth health = player.GetComponent<PlayerHealth>();
        return health != null && health.IsDead;
    }

    private void TryAttack()
    {
        if (Time.time - lastAttackTime < attackCooldown) return;

        if (targetHealth != null)
        {
            targetHealth.TakeDamage(2);
            lastAttackTime = Time.time;
        }
    }
}
