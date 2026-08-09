using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Breadth-first distance field over the walkable tiles, flooded from every player at once.
///
/// Chasing is many-to-few, so one multi-source BFS per rebuild is far cheaper than a path per
/// zombie: the flood is O(cells) and each zombie then reads its direction in O(1). Because all
/// players seed the same flood, every zombie automatically walks toward the nearest one.
/// </summary>
public class ZombieFlowField
{
    private const int Unreachable = int.MaxValue;

    private int[] distance;
    private BoundsInt bounds;
    private int width;
    private int height;
    private bool built;

    private readonly Queue<int> frontier = new Queue<int>();

    private static readonly Vector2Int[] Orthogonal =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0),
        new Vector2Int(0, 1), new Vector2Int(0, -1),
    };

    public void Rebuild(WalkableMap map, List<Vector2> sources)
    {
        built = false;

        if (map == null || !map.HasTilemap || sources.Count == 0)
            return;

        bounds = map.CellBounds;
        width = bounds.size.x;
        height = bounds.size.y;

        if (width <= 0 || height <= 0)
            return;

        int cellCount = width * height;

        if (distance == null || distance.Length != cellCount)
            distance = new int[cellCount];

        for (int i = 0; i < cellCount; i++)
            distance[i] = Unreachable;

        frontier.Clear();

        foreach (Vector2 source in sources)
        {
            Vector3Int cell = map.WorldToCell(source);

            if (!TryIndex(cell, out int index))
                continue;

            if (!map.HasTileAt(cell) || distance[index] == 0)
                continue;

            distance[index] = 0;
            frontier.Enqueue(index);
        }

        while (frontier.Count > 0)
        {
            int index = frontier.Dequeue();
            int next = distance[index] + 1;

            int x = bounds.xMin + (index % width);
            int y = bounds.yMin + (index / width);

            for (int i = 0; i < Orthogonal.Length; i++)
            {
                Vector3Int neighbour = new Vector3Int(x + Orthogonal[i].x, y + Orthogonal[i].y, bounds.zMin);

                if (!TryIndex(neighbour, out int neighbourIndex))
                    continue;

                if (distance[neighbourIndex] != Unreachable)
                    continue;

                if (!map.HasTileAt(neighbour))
                    continue;

                distance[neighbourIndex] = next;
                frontier.Enqueue(neighbourIndex);
            }
        }

        built = true;
    }

    /// <summary>
    /// Direction toward the neighbouring cell that is closer to a player, or false when the
    /// caller is off-map or walled off entirely.
    /// </summary>
    public bool TryGetDirection(WalkableMap map, Vector2 worldPosition, out Vector2 direction)
    {
        direction = Vector2.zero;

        if (!built || map == null)
            return false;

        Vector3Int cell = map.WorldToCell(worldPosition);

        if (!TryIndex(cell, out int index) || distance[index] == Unreachable)
            return false;

        int best = distance[index];
        Vector3Int bestCell = cell;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                    continue;

                Vector3Int neighbour = new Vector3Int(cell.x + dx, cell.y + dy, cell.z);

                if (!TryIndex(neighbour, out int neighbourIndex))
                    continue;

                if (distance[neighbourIndex] >= best)
                    continue;

                // Only cut a corner when both orthogonal cells are open, otherwise zombies
                // clip through wall corners diagonally.
                if (dx != 0 && dy != 0)
                {
                    if (!map.HasTileAt(new Vector3Int(cell.x + dx, cell.y, cell.z)) ||
                        !map.HasTileAt(new Vector3Int(cell.x, cell.y + dy, cell.z)))
                        continue;
                }

                best = distance[neighbourIndex];
                bestCell = neighbour;
            }
        }

        if (bestCell == cell)
            return false;

        Vector2 target = map.CellCenter(bestCell);
        Vector2 delta = target - worldPosition;

        if (delta.sqrMagnitude < 0.000001f)
            return false;

        direction = delta.normalized;
        return true;
    }

    private bool TryIndex(Vector3Int cell, out int index)
    {
        index = -1;

        int x = cell.x - bounds.xMin;
        int y = cell.y - bounds.yMin;

        if (x < 0 || y < 0 || x >= width || y >= height)
            return false;

        index = x + y * width;
        return true;
    }
}
