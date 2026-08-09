using UnityEngine;

/// <summary>
/// Network identity for a zombie. Ids are assigned by the host and are the only thing
/// clients use to match a spawned zombie to the authoritative state.
/// </summary>
public class NetworkedZombie : MonoBehaviour
{
    public int zombieId;

    public ZombieHealth health;
    public RemoteInterpolator interpolator;

    private void Awake()
    {
        if (health == null)
            health = GetComponent<ZombieHealth>();
    }
}
