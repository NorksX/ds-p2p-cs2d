using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    
    [Header("Auto-find Local Player")]
    [SerializeField] private bool autoFindLocalPlayer = true;
    
    private void LateUpdate()
    {
        // Try to find local player if we don't have a target
        if (target == null && autoFindLocalPlayer)
        {
            FindLocalPlayer();
        }
        
        if (target == null) return;

        transform.position = new Vector3(
            target.position.x,
            target.position.y,
            transform.position.z
        );
    }
    
    private void FindLocalPlayer()
    {
        // Find all NetworkedPlayer components
        NetworkedPlayer[] players = FindObjectsOfType<NetworkedPlayer>();
        
        foreach (var player in players)
        {
            if (player.isLocalPlayer)
            {
                target = player.transform;
                Debug.Log("Camera found and is now following local player");
                break;
            }
        }
    }
}
