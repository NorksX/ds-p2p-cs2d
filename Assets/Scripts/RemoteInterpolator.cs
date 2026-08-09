using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Renders a remote player slightly in the past and interpolates between received snapshots.
///
/// Their input cannot be predicted, so the only way to move them smoothly is to hold a short
/// buffer and play it back with a delay. Without this they teleport once per state update,
/// which looks choppy even on a flawless connection.
/// </summary>
public class RemoteInterpolator : MonoBehaviour
{
    private struct Snapshot
    {
        public float time;
        public Vector2 position;
        public float rotation;
    }

    // Roughly three state updates at 30Hz - enough to ride out jitter without feeling laggy.
    private const float RenderDelay = 0.1f;
    private const float MaxBufferAge = 1f;

    private readonly List<Snapshot> snapshots = new List<Snapshot>();
    private Rigidbody2D body;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    public void Push(Vector2 position, float rotation)
    {
        snapshots.Add(new Snapshot { time = Time.time, position = position, rotation = rotation });

        float cutoff = Time.time - MaxBufferAge;
        while (snapshots.Count > 2 && snapshots[0].time < cutoff)
            snapshots.RemoveAt(0);
    }

    private void Update()
    {
        // After a migration this component survives on a peer that has just become host.
        // The host simulates these players itself, so replaying buffered snapshots on top
        // fights that authority every frame - it looks like vibration and the player cannot
        // be moved.
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsHost)
        {
            snapshots.Clear();
            return;
        }

        if (snapshots.Count == 0)
            return;

        float renderTime = Time.time - RenderDelay;

        // Not enough history yet, or we have outrun the buffer: hold the newest state.
        if (snapshots.Count == 1 || renderTime >= snapshots[snapshots.Count - 1].time)
        {
            Snapshot newest = snapshots[snapshots.Count - 1];
            Apply(newest.position, newest.rotation);
            return;
        }

        for (int i = 0; i < snapshots.Count - 1; i++)
        {
            Snapshot from = snapshots[i];
            Snapshot to = snapshots[i + 1];

            if (renderTime < from.time || renderTime > to.time)
                continue;

            float span = to.time - from.time;
            float t = span > 0.0001f ? (renderTime - from.time) / span : 1f;

            Apply(Vector2.Lerp(from.position, to.position, t),
                  Mathf.LerpAngle(from.rotation, to.rotation, t));
            return;
        }

        // renderTime predates everything we hold - show the oldest we have.
        Apply(snapshots[0].position, snapshots[0].rotation);
    }

    // Body and transform together: these objects are collision obstacles for other peers'
    // sweeps, so moving only the transform would leave everyone querying stale positions.
    private void Apply(Vector2 position, float rotation)
    {
        if (body != null)
            body.position = position;

        transform.position = new Vector3(position.x, position.y, transform.position.z);
        transform.rotation = Quaternion.Euler(0f, 0f, rotation);
    }
}
