using System.Collections.Generic;
using UnityEngine;

public class LocalInputBuffer : MonoBehaviour
{
    [SerializeField] private int keepTicks = 240;

    private readonly Dictionary<int, InputCommand> buffer = new Dictionary<int, InputCommand>();

    private int oldestTick;

    public void Store(InputCommand cmd)
    {
        if (buffer.Count == 0)
            oldestTick = cmd.tick;

        buffer[cmd.tick] = cmd;

        // Ticks are monotonic, so sweeping from the oldest frees everything that falls out.
        int minTick = cmd.tick - keepTicks;
        while (oldestTick < minTick)
        {
            buffer.Remove(oldestTick);
            oldestTick++;
        }
    }

    public bool TryGet(int tick, out InputCommand cmd)
    {
        return buffer.TryGetValue(tick, out cmd);
    }
}
