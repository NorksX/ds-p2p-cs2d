using System.Collections.Generic;
using UnityEngine;

public class LocalInputBuffer : MonoBehaviour
{
    [SerializeField] private int keepTicks = 240;

    private readonly Dictionary<int, InputCommand> buffer = new Dictionary<int, InputCommand>();

    public void Store(InputCommand cmd)
    {
        buffer[cmd.tick] = cmd;

        int minTick = cmd.tick - keepTicks;
        if (minTick > 0)
        {
            for (int t = minTick - 60; t < minTick; t++)
                buffer.Remove(t);
        }
    }

    public bool TryGet(int tick, out InputCommand cmd)
    {
        return buffer.TryGetValue(tick, out cmd);
    }
}
