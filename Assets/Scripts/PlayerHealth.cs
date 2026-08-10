using UnityEngine;

/// <summary>
/// Host-authoritative player health. Damage is only ever applied on the host; clients receive
/// the result in the state update. Health is never predicted - it is not a function of local
/// input, so there is nothing to reconcile.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 10;
    public int currentHealth;

    [Header("Death")]
    [SerializeField] private float respawnDelay = 3f;

    private float respawnAt;

    public bool IsDead => currentHealth <= 0;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    // Host only. A client calling this would kill a player the host still considers alive.
    public void TakeDamage(int amount)
    {
        if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
            return;

        if (IsDead)
            return;

        currentHealth = Mathf.Max(currentHealth - amount, 0);

        if (IsDead)
        {
            respawnAt = Time.time + respawnDelay;
            Debug.Log($"[PlayerHealth] {name} died, respawning in {respawnDelay}s");
        }
    }

    public void SetHealthFromNetwork(int value)
    {
        currentHealth = value;
    }

    private void Update()
    {
        // Respawn is a host decision; clients just see the health and position change.
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
            return;

        if (!IsDead || Time.time < respawnAt)
            return;

        Respawn();
    }

    private void Respawn()
    {
        currentHealth = maxHealth;

        NetworkedPlayer networked = GetComponent<NetworkedPlayer>();
        PlayerController controller = GetComponent<PlayerController>();

        if (networked != null && controller != null && PlayerSpawner.Instance != null)
            controller.Teleport(PlayerSpawner.Instance.GetSpawnPosition(networked.playerPosition));

        Debug.Log($"[PlayerHealth] {name} respawned");
    }
}
