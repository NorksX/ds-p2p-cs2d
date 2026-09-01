using UnityEngine;
using LiteNetLib;
using LiteNetLib.Utils;
using System;
using System.Collections.Generic;
using System.Net;

/// <summary>
/// Measures round-trip time to every other participant and ranks them, so host election is
/// decided by measured network quality instead of by an arbitrary GUID ordering.
///
/// Probes are CONNECTIONLESS UDP on the game socket. The topology is a star - clients hold no
/// connection to each other - so a connection-based probe would need a permanent mesh. Sending
/// unconnected instead needs no topology change at all: LiteNetLib routes UnconnectedMessage
/// to OnNetworkReceiveUnconnected before any peer lookup, so one code path covers the host
/// (connected) and other clients (not connected) identically. Same trick LanDiscovery uses.
///
/// Each pong carries the responder's own measurement row, so every peer assembles the full
/// matrix directly from its pongs. Nothing is relayed through the host - which matters twice:
/// the host is the node being judged, and during a failure migration there is no host at all.
///
/// Deliberately a plain class owned by NetworkManager, not a MonoBehaviour, so this needs no
/// scene wiring.
/// </summary>
public class HostQualityMonitor
{
    private const string PingTag = "CS2D_PING";
    private const string PongTag = "CS2D_PONG";
    private const int MaxRowEntries = 8;

    private readonly NetworkManager owner;
    private readonly NetDataWriter writer = new NetDataWriter();

    // What we measured ourselves - our row of the matrix.
    private readonly Dictionary<string, RttStats> local = new Dictionary<string, RttStats>();

    // What everyone else reported measuring, learned from their pongs.
    private readonly Dictionary<string, RemoteRow> remoteRows = new Dictionary<string, RemoteRow>();

    private float lastProbeTime;
    private float lastProactiveCheckTime;
    private float lastMigrationTime;

    private int sustainedCount;
    private string pendingChallengerId;

    private readonly List<string> participants = new List<string>();
    private readonly List<string> candidates = new List<string>();
    private readonly List<string> rowIds = new List<string>();
    private readonly List<string> staleIds = new List<string>();

    private struct RemoteEntry
    {
        public float estimatedRtt;
        public float devRtt;
        public int samples;
    }

    private class RemoteRow
    {
        public readonly Dictionary<string, RemoteEntry> entries = new Dictionary<string, RemoteEntry>();
        public float receivedAt;
    }

    public HostQualityMonitor(NetworkManager owner)
    {
        this.owner = owner;
    }

    private NetworkConfig Config => owner.Config;
    private float Window => Config != null ? Config.rttStatsWindow / 1000f : 30f;
    private int MinSamples => owner.RttMinSamples;
    private float DeviationWeight => Config != null ? Config.rttDeviationWeight : 4f;

//probing

    public void Tick()
    {
        if (Config == null)
            return;

        if (owner.State != ConnectionState.InLobby && owner.State != ConnectionState.HostMigration)
            return;

        if (Time.time - lastProbeTime >= owner.RttProbeInterval / 1000f)
        {
            SendProbes();
            lastProbeTime = Time.time;
        }

        EvaluateProactiveOnSchedule();
    }

    private void SendProbes()
    {
        foreach (var entry in owner.Roster)
        {
            if (entry.playerId == owner.LocalPlayerId) continue;
            if (string.IsNullOrEmpty(entry.ipAddress) || entry.listenPort <= 0) continue;

            RttStats stats = StatsFor(entry.playerId);
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            stats.NoteProbeSent(stamp);

            writer.Reset();
            writer.Put(PingTag);
            writer.Put(owner.LocalPlayerId);
            writer.Put(stamp);

            owner.SendUnconnected(writer, entry.ipAddress, entry.listenPort);
        }
    }

    public void HandleUnconnected(IPEndPoint from, NetPacketReader reader)
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

        if (tag == PingTag)
            AnswerPing(from, reader);
        else if (tag == PongTag)
            AcceptPong(reader);
    }

    private void AnswerPing(IPEndPoint asker, NetPacketReader reader)
    {
        try
        {
            reader.GetString();          // sender id, not needed to answer
            long stamp = reader.GetLong();

            writer.Reset();
            writer.Put(PongTag);
            writer.Put(owner.LocalPlayerId);
            writer.Put(stamp);

            // Our own simulated distance travels with the reply, so the pinger can charge the
            // link for both endpoints. Without this the injected latency would only inflate our
            // row of the matrix, while cost() reads the column - and the knob would do nothing.
            writer.Put(owner.SimulatedExtraMs);

            WriteLocalRow(writer);

            owner.SendUnconnected(writer, asker);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HostQuality] Malformed ping from {asker}: {e.Message}");
        }
    }

    private void AcceptPong(NetPacketReader reader)
    {
        try
        {
            string responderId = reader.GetString();
            long stamp = reader.GetLong();
            float responderExtraMs = reader.GetFloat();

            // Both endpoints' simulated distance, so the injected latency is a property of the
            // link and shows up symmetrically in everyone's measurements.
            float linkExtraMs = owner.SimulatedExtraMs + responderExtraMs;

            RttStats stats = StatsFor(responderId);
            stats.TryAcceptReply(stamp, Time.time, Config.rttAlpha, Config.rttBeta, linkExtraMs);

            ReadRemoteRow(responderId, reader);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[HostQuality] Malformed pong: {e.Message}");
        }
    }

    private void WriteLocalRow(NetDataWriter w)
    {
        rowIds.Clear();

        foreach (var kvp in local)
        {
            if (kvp.Value.HasEstimate && rowIds.Count < MaxRowEntries)
                rowIds.Add(kvp.Key);
        }

        w.Put((byte)rowIds.Count);

        foreach (string id in rowIds)
        {
            RttStats s = local[id];
            w.Put(id);
            w.Put(s.EstimatedRtt);
            w.Put(s.DevRtt);
            w.Put((byte)Mathf.Min(255, s.SamplesInWindow(Time.time, Window)));
        }
    }

    private void ReadRemoteRow(string reporterId, NetPacketReader reader)
    {
        int count = reader.GetByte();

        if (!remoteRows.TryGetValue(reporterId, out RemoteRow row))
        {
            row = new RemoteRow();
            remoteRows[reporterId] = row;
        }

        row.entries.Clear();
        row.receivedAt = Time.time;

        for (int i = 0; i < count; i++)
        {
            string targetId = reader.GetString();

            row.entries[targetId] = new RemoteEntry
            {
                estimatedRtt = reader.GetFloat(),
                devRtt = reader.GetFloat(),
                samples = reader.GetByte()
            };
        }
    }

    private RttStats StatsFor(string playerId)
    {
        if (!local.TryGetValue(playerId, out RttStats stats))
        {
            stats = new RttStats();
            local[playerId] = stats;
        }

        return stats;
    }

//costs

    /// <summary>
    /// Cost of the link between two participants, in the lecture's TimeoutInterval. Falls back
    /// to the reverse direction when the forward one is missing - a probe measures a round
    /// trip, so both ends observe the same path with independent estimators. That fallback is
    /// what keeps the matrix usable during a migration, when the dead host's row is gone.
    /// </summary>
    public bool TryLinkCost(string fromId, string toId, out float cost)
    {
        cost = 0f;

        if (fromId == toId)
            return false;

        if (TryDirectedCost(fromId, toId, out cost))
            return true;

        return TryDirectedCost(toId, fromId, out cost);
    }

    private bool TryDirectedCost(string fromId, string toId, out float cost)
    {
        cost = 0f;

        if (fromId == owner.LocalPlayerId)
        {
            if (!local.TryGetValue(toId, out RttStats stats)) return false;
            if (!stats.IsUsable(Time.time, Window, MinSamples)) return false;

            cost = stats.TimeoutInterval(DeviationWeight);
            return true;
        }

        if (!remoteRows.TryGetValue(fromId, out RemoteRow row)) return false;
        if (Time.time - row.receivedAt > Window) return false;
        if (!row.entries.TryGetValue(toId, out RemoteEntry entry)) return false;
        if (entry.samples < MinSamples) return false;

        cost = entry.estimatedRtt + DeviationWeight * entry.devRtt;
        return true;
    }

    /// <summary>
    /// Mean TimeoutInterval from every participant TOWARD the target - a column of the matrix,
    /// not a row. What the target measured says nothing about whether it is well placed to
    /// serve everyone; what everyone measured toward it does. Mean rather than sum, so a
    /// candidate is not penalised merely for having fewer rows in.
    /// </summary>
    public bool TryAggregateCost(string targetId, List<string> group, out float cost, out int coverage)
    {
        float total = 0f;
        coverage = 0;
        cost = 0f;

        foreach (string p in group)
        {
            if (p == targetId) continue;

            if (TryLinkCost(p, targetId, out float link))
            {
                total += link;
                coverage++;
            }
        }

        if (coverage == 0)
            return false;

        cost = total / coverage;
        return true;
    }

    /// <summary>The TA's literal rule: whoever WE have the best ping to.</summary>
    public string PickByLocalCost(List<string> options)
    {
        string best = null;
        float bestCost = float.MaxValue;

        foreach (string id in options)
        {
            // Never rank ourselves. Our cost to ourselves is 0, so including it would make
            // every peer prefer itself and no candidate could ever collect a vote.
            if (id == owner.LocalPlayerId) continue;

            if (!local.TryGetValue(id, out RttStats stats)) continue;
            if (!stats.IsUsable(Time.time, Window, MinSamples)) continue;

            float cost = stats.TimeoutInterval(DeviationWeight);

            if (best == null || cost < bestCost ||
                (Mathf.Approximately(cost, bestCost) && string.CompareOrdinal(id, best) < 0))
            {
                bestCost = cost;
                best = id;
            }
        }

        return best;
    }

    /// <summary>
    /// Aggregate argmin. Every peer holding the same matrix computes the same answer, which is
    /// what makes it usable as a deterministic runoff when the local vote splits.
    /// </summary>
    public string PickByAggregateCost(List<string> options, List<string> group, int minCoverage)
    {
        string best = null;
        float bestCost = float.MaxValue;

        foreach (string id in options)
        {
            if (!TryAggregateCost(id, group, out float cost, out int coverage)) continue;
            if (coverage < minCoverage) continue;

            if (best == null || cost < bestCost ||
                (Mathf.Approximately(cost, bestCost) && string.CompareOrdinal(id, best) < 0))
            {
                bestCost = cost;
                best = id;
            }
        }

        return best;
    }

//proactive migration

    public void NoteMigration()
    {
        lastMigrationTime = Time.time;
        sustainedCount = 0;
        pendingChallengerId = null;
    }

    public bool TryConsumeProposal(out string challengerId)
    {
        challengerId = pendingChallengerId;
        pendingChallengerId = null;
        return challengerId != null;
    }

    private void EvaluateProactiveOnSchedule()
    {
        if (!Config.proactiveMigrationEnabled)
            return;

        if (Time.time - lastProactiveCheckTime < owner.ProactiveCheckInterval / 1000f)
            return;

        lastProactiveCheckTime = Time.time;

        // Printed every check rather than every probe: rare enough not to be spam, frequent
        // enough to watch an election form in real time.
        Debug.Log($"[HostQuality] links   {DebugSummary()}");
        Debug.Log($"[HostQuality] cost(X) {DebugCostTable()}");

        if (!ConditionHolds(out string challenger))
        {
            if (sustainedCount > 0)
                Debug.Log("[HostQuality] Proactive condition no longer holds, streak reset");

            sustainedCount = 0;
            return;
        }

        sustainedCount++;
        Debug.Log($"[HostQuality] Proactive condition holds ({sustainedCount}/{owner.ProactiveSustainedChecks}), challenger {challenger}");

        if (sustainedCount < owner.ProactiveSustainedChecks)
            return;

        sustainedCount = 0;

        // Only the challenger itself proposes; everyone else re-derives the same verdict when
        // the request arrives.
        if (challenger == owner.LocalPlayerId)
            pendingChallengerId = challenger;
    }

    /// <summary>
    /// Re-derive the proactive verdict from scratch. Used by the proposer and, independently,
    /// by every voter - which is what makes the handover a vote rather than a unilateral grab.
    /// </summary>
    public bool ValidateProactive(string candidateId)
    {
        return ConditionHolds(out string challenger) && challenger == candidateId;
    }

    private bool ConditionHolds(out string challengerId)
    {
        challengerId = null;

        if (Config == null || !Config.proactiveMigrationEnabled) return false;
        if (owner.State != ConnectionState.InLobby) return false;
        if (Time.time - lastMigrationTime < owner.ProactiveCooldown / 1000f) return false;

        string hostId = owner.CurrentHostId;
        if (string.IsNullOrEmpty(hostId)) return false;

        // With two participants the alternative is just the other person; swapping who hosts
        // changes no link, so there is nothing to gain.
        if (owner.Roster.Count < 3) return false;

        participants.Clear();
        foreach (var e in owner.Roster)
            participants.Add(e.playerId);

        // Full coverage: every participant must have a fresh measurement toward both the host
        // and the challenger. Moving a live host on partial data is not worth it.
        int required = participants.Count - 1;

        if (!TryAggregateCost(hostId, participants, out float hostCost, out int hostCoverage)) return false;
        if (hostCoverage < required) return false;

        candidates.Clear();
        foreach (string id in participants)
        {
            if (id != hostId)
                candidates.Add(id);
        }

        string best = PickByAggregateCost(candidates, participants, required);
        if (best == null) return false;

        if (!TryAggregateCost(best, participants, out float bestCost, out _)) return false;
        if (bestCost <= 0f) return false;

        if (hostCost <= owner.ProactiveThresholdFactor * bestCost)
            return false;

        // A ratio on its own is meaningless at low latency: 20ms against 70ms clears 3x while
        // being worth nothing, and migrating costs a mesh dial, a roster rebroadcast and a
        // re-registration on every peer. Demand a real absolute improvement as well.
        if (hostCost - bestCost < owner.ProactiveMinCostGap)
        {
            Debug.Log($"[HostQuality] Host is {hostCost / bestCost:F1}x worse but only {hostCost - bestCost:F0}ms of link cost - below the {owner.ProactiveMinCostGap:F0}ms floor, staying put");
            return false;
        }

        challengerId = best;
        return true;
    }

//housekeeping

    /// <summary>Drop peers that left, so a rejoin starts from a clean estimator.</summary>
    public void PruneToRoster()
    {
        staleIds.Clear();

        foreach (var kvp in local)
        {
            if (!InRoster(kvp.Key))
                staleIds.Add(kvp.Key);
        }

        foreach (string id in staleIds)
            local.Remove(id);

        staleIds.Clear();

        foreach (var kvp in remoteRows)
        {
            if (!InRoster(kvp.Key))
                staleIds.Add(kvp.Key);
        }

        foreach (string id in staleIds)
            remoteRows.Remove(id);
    }

    private bool InRoster(string playerId)
    {
        foreach (var e in owner.Roster)
        {
            if (e.playerId == playerId)
                return true;
        }

        return false;
    }

    public void ResetAll()
    {
        local.Clear();
        remoteRows.Clear();
        sustainedCount = 0;
        pendingChallengerId = null;
        lastMigrationTime = 0f;
    }

    /// <summary>
    /// Aggregate cost per participant - the column means the election actually ranks on.
    /// This is the view that explains a migration; the per-link row does not.
    /// </summary>
    public string DebugCostTable()
    {
        List<string> group = new List<string>();
        foreach (var e in owner.Roster)
            group.Add(e.playerId);

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        foreach (var e in owner.Roster)
        {
            sb.Append(e.username);
            if (e.playerId == owner.CurrentHostId) sb.Append("(host)");
            sb.Append('=');

            if (TryAggregateCost(e.playerId, group, out float cost, out int coverage))
                sb.Append($"{cost:F0}ms/{coverage}of{group.Count - 1}");
            else
                sb.Append("no-data");

            sb.Append("  ");
        }

        return sb.ToString();
    }

    public string DebugSummary()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        foreach (var e in owner.Roster)
        {
            if (e.playerId == owner.LocalPlayerId) continue;

            sb.Append(e.username).Append(": ");

            if (local.TryGetValue(e.playerId, out RttStats s) && s.HasEstimate)
                sb.Append($"est={s.EstimatedRtt:F1} dev={s.DevRtt:F1} TO={s.TimeoutInterval(DeviationWeight):F1} n={s.SamplesInWindow(Time.time, Window)}");
            else
                sb.Append("no data");

            sb.Append("  ");
        }

        return sb.ToString();
    }
}
