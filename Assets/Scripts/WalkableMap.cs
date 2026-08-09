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

    public bool HasTilemap => tilemap != null;
    public BoundsInt CellBounds => tilemap != null ? tilemap.cellBounds : default;

    public Vector3Int WorldToCell(Vector2 worldPosition)
        => tilemap != null ? tilemap.WorldToCell(worldPosition) : Vector3Int.zero;

    public Vector2 CellCenter(Vector3Int cell)
        => tilemap != null ? (Vector2)tilemap.GetCellCenterWorld(cell) : Vector2.zero;

    public bool HasTileAt(Vector3Int cell)
        => tilemap == null || tilemap.HasTile(cell);

    public bool IsWalkable(Vector2 worldPosition)
    {
        if (tilemap == null)
            return true;

        return tilemap.HasTile(tilemap.WorldToCell(worldPosition));
    }

    /// <summary>
    /// Samples the footprint rather than just the centre, so nothing can stand half over the
    /// void. Keep the radius smaller than the collider or one-tile gaps become impassable.
    /// </summary>
    public bool CanStandAt(Vector2 position, float footprintRadius)
    {
        if (tilemap == null)
            return true;

        return IsWalkable(position)
            && IsWalkable(position + new Vector2(footprintRadius, 0f))
            && IsWalkable(position + new Vector2(-footprintRadius, 0f))
            && IsWalkable(position + new Vector2(0f, footprintRadius))
            && IsWalkable(position + new Vector2(0f, -footprintRadius));
    }

    /// <summary>
    /// Clamp a move to the playable area, falling back to single-axis motion so running into
    /// an edge slides along it instead of sticking.
    /// </summary>
    public Vector2 ConstrainMove(Vector2 from, Vector2 to, float footprintRadius)
    {
        if (tilemap == null || CanStandAt(to, footprintRadius))
            return to;

        // Already outside the playable area - do not trap them there.
        if (!CanStandAt(from, footprintRadius))
            return to;

        Vector2 xOnly = new Vector2(to.x, from.y);
        if (CanStandAt(xOnly, footprintRadius))
            return xOnly;

        Vector2 yOnly = new Vector2(from.x, to.y);
        if (CanStandAt(yOnly, footprintRadius))
            return yOnly;

        return from;
    }
}
