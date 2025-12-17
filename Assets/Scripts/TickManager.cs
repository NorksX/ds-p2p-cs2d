using UnityEngine;

public class TickManager : MonoBehaviour
{
    public static TickManager Instance;

    [Header("Tick Settings")]
    [Tooltip("Simulation ticks per second")]
    public int tickRate = 30;

    public int CurrentTick { get; private set; }

    private float tickInterval;
    private float accumulatedTime;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        tickInterval = 1f / tickRate;
        CurrentTick = 0;
        accumulatedTime = 0f;
    }

    private void Update()
    {
        accumulatedTime += Time.deltaTime;

        while (accumulatedTime >= tickInterval)
        {
            accumulatedTime -= tickInterval;
            AdvanceTick();
        }
    }

    private void AdvanceTick()
    {
        CurrentTick++;

        Debug.Log("Tick: " + CurrentTick);
    }
}
