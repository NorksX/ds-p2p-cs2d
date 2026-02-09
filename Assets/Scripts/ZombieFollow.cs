using UnityEngine;

public class ZombieFollow : MonoBehaviour
{
    public float moveSpeed = 2f;
    public float attackRange = 1f;
    public float attackCooldown = 1f;

    private Transform target;
    private PlayerHealth targetHealth; // Cache component
    private float lastAttackTime;

    private void Start()
    {
        // Check for targets every 0.5s instead of every frame
        InvokeRepeating(nameof(UpdateTarget), 0f, 0.5f);
    }
    
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
                float dist = Vector2.Distance(transform.position, player.transform.position);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    closestEnemy = player.transform;
                }
            }
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
        if (target == null) return;

        Vector2 dir = target.position - transform.position;
        float distance = dir.magnitude;

        // Rotate
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0, 0, angle);

        // Move if not too close
        if (distance > attackRange)
        {
            transform.position += (Vector3)(dir.normalized * moveSpeed * Time.deltaTime);
        }

        // Attack if in range
        if (distance <= attackRange)
        {
            TryAttack();
        }
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
