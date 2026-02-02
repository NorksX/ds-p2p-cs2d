using UnityEngine;

public class ZombieFollow : MonoBehaviour
{
    public float moveSpeed = 2f;
    public float attackRange = 1f;
    public float attackCooldown = 1f;

    private Transform target;
    private float lastAttackTime;

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            target = player.transform;
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

        PlayerHealth playerHealth = target.GetComponent<PlayerHealth>();
        if (playerHealth != null)
        {
            playerHealth.TakeDamage(2);
            lastAttackTime = Time.time;
        }
    }
}
