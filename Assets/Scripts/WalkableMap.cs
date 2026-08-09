using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// The painted tiles define the playable area - anything without a tile is out of bounds.
/// Queried rather than collided against, so the movement step stays a pure function and can
/// be replayed during reconciliation.
/// </summary>
public class WalkableMap : MonoBehaviour
{
    public static WalkableMap Instance { get; private set; }

    [SerializeField] private Tilemap tilemap;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (tilemap == null)
            tilemap = GetComponent<Tilemap>();

        if (tilemap == null)
            Debug.LogError("[WalkableMap] No Tilemap assigned or found - the map will be unbounded");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool IsWalkable(Vector2 worldPosition)
    {
        if (tilemap == null)
            return true;

        return tilemap.HasTile(tilemap.WorldToCell(worldPosition));
    }
}
