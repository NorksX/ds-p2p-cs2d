using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Net;
using System.Net.Sockets;
using System.Collections.Generic;

public struct DiscoveredHost
{
    public string address;
    public int port;
    public int players;
    public int maxPlayers;
    public float lastSeen;

    public bool IsFull => players >= maxPlayers;
    public string Endpoint => $"{address}:{port}";
}

/// <summary>
/// LAN host discovery over UDP broadcast.
///
/// Deliberately a SEPARATE NetManager from the game one. The game socket's port is whatever a
/// peer happened to bind - and after a migration the host is on its old ephemeral port, so
/// there is no fixed port to broadcast to. This one always sits on a well-known discovery port
/// while hosting, and answers with the real game endpoint. It holds no connections, so it can
/// be stopped and rebound freely when the host role moves.
/// </summary>
public class LanDiscovery : MonoBehaviour, INetEventListener
{
    public static LanDiscovery Instance { get; private set; }

    [SerializeField] private int discoveryPort = 47777;
    [Tooltip("Drop hosts we have not heard from in this long")]
    [SerializeField] private float hostTimeout = 5f;

    private const string RequestTag = "CS2D_FIND";
    private const string ResponseTag = "CS2D_HERE";

    private NetManager manager;
    private bool boundAsHost;
    private readonly NetDataWriter writer = new NetDataWriter();
    private readonly Dictionary<string, DiscoveredHost> found = new Dictionary<string, DiscoveredHost>();
    private readonly List<string> staleHosts = new List<string>();

    public IEnumerable<DiscoveredHost> Hosts => found.Values;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        manager?.Stop();

        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        bool shouldHost = NetworkManager.Instance != null && NetworkManager.Instance.IsHost;

        // Rebind when the role changes - this is why discovery is kept connectionless.
        if (manager == null || shouldHost != boundAsHost)
            Rebind(shouldHost);

        manager?.PollEvents();
        DropStaleHosts();
    }

    private void Rebind(bool asHost)
    {
        manager?.Stop();

        manager = new NetManager(this)
        {
            UnconnectedMessagesEnabled = true,
            BroadcastReceiveEnabled = true
        };

        // Host owns the well-known port; a searching client just needs any port.
        bool started = asHost ? manager.Start(discoveryPort) : manager.Start();

        if (!started)
        {
            // Usually another instance on this machine already holds the port. Not fatal:
            // whoever holds it is answering, and we can still search from an ephemeral port.
            Debug.LogWarning($"[LanDiscovery] Could not bind {(asHost ? discoveryPort.ToString() : "an ephemeral port")}; retrying as searcher");
            manager.Start();
            boundAsHost = false;
            return;
        }

        boundAsHost = asHost;
    }

    /// <summary>Broadcast a search. Replies arrive over the next few hundred ms.</summary>
    public void Refresh()
    {
        if (manager == null)
            return;

        writer.Reset();
        writer.Put(RequestTag);

        if (!manager.SendBroadcast(writer, discoveryPort))
            Debug.LogWarning("[LanDiscovery] Broadcast failed");
    }

    private void DropStaleHosts()
    {
        staleHosts.Clear();

        foreach (var kvp in found)
        {
            if (Time.time - kvp.Value.lastSeen > hostTimeout)
                staleHosts.Add(kvp.Key);
        }

        foreach (string key in staleHosts)
            found.Remove(key);
    }

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetPacketReader reader, UnconnectedMessageType messageType)
    {
        string tag;

        try
        {
            tag = reader.GetString();
        }
        catch (Exception)
        {
            return; // not ours
        }

        if (tag == RequestTag)
        {
            AnswerSearch(remoteEndPoint);
            return;
        }

        if (tag == ResponseTag)
            RecordHost(remoteEndPoint, reader);
    }

    private void AnswerSearch(IPEndPoint asker)
    {
        if (NetworkManager.Instance == null || !NetworkManager.Instance.IsHost)
            return;

        writer.Reset();
        writer.Put(ResponseTag);

        // The real game port, which is NOT the discovery port and NOT necessarily gamePort:
        // a migrated host is still on the ephemeral port it bound as a client.
        writer.Put(NetworkManager.Instance.LocalPort);
        writer.Put(NetworkManager.Instance.Roster.Count);
        writer.Put(NetworkManager.Instance.MaxPlayers);

        manager.SendUnconnectedMessage(writer, asker);
    }

    private void RecordHost(IPEndPoint from, NetPacketReader reader)
    {
        try
        {
            DiscoveredHost host = new DiscoveredHost
            {
                address = from.Address.ToString(),
                port = reader.GetInt(),
                players = reader.GetInt(),
                maxPlayers = reader.GetInt(),
                lastSeen = Time.time
            };

            // Log only on first sight - the source address is whichever adapter the host
            // answered from, which on a machine with a VPN adapter may not be reachable.
            if (!found.ContainsKey(host.Endpoint))
                Debug.Log($"[LanDiscovery] Found host {host.Endpoint} ({host.players}/{host.maxPlayers})");

            found[host.Endpoint] = host;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LanDiscovery] Malformed response from {from}: {e.Message}");
        }
    }

    // Discovery is connectionless; the rest of the listener is deliberately unused.
    public void OnPeerConnected(NetPeer peer) { }
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo) { }
    public void OnNetworkError(IPEndPoint endPoint, SocketError socketError) { }
    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channelNumber, DeliveryMethod deliveryMethod) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
    public void OnConnectionRequest(ConnectionRequest request) { request.Reject(); }
}
