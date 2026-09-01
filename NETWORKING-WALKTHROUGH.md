# Networking Walkthrough

Sequential call order of the whole networking layer. Every link opens the exact function.

Transport: LiteNetLib (UDP). Topology: **star** during play (host = authority), **temporary mesh** during an election.

---

## 0. Boot — singletons come up

| # | Call | What it does |
|---|---|---|
| 1 | [NetworkManager.Awake()](Assets/Scripts/Networking/NetworkManager.cs#L223) | `Instance`, `DontDestroyOnLoad`, GUID `localPlayerId`, `new NetManager(this)`, `UnconnectedMessagesEnabled = true` |
| 2 | [ParseCommandLineOverrides()](Assets/Scripts/Networking/NetworkManager.cs#L287) | `-autohost` / `-autojoin` / `-username` / `-rttExtraMs` … |
| 3 | [ApplyUsername()](Assets/Scripts/Networking/NetworkManager.cs#L320) | unique name per instance |
| 4 | [new HostQualityMonitor(nm)](Assets/Scripts/Networking/HostQualityMonitor.cs#L65) | RTT subsystem (plain class, no scene wiring) |
| 5 | [TickManager.Awake()](Assets/Scripts/TickManager.cs#L22) | fixed 30 Hz step |
| 6 | [PlayerSpawner.Awake()](Assets/Scripts/Networking/PlayerSpawner.cs#L21) | spawn registry |
| 7 | [LanDiscovery.Awake()](Assets/Scripts/Networking/LanDiscovery.cs#L49) | second, separate `NetManager` for broadcast |
| 8 | [NetworkManager.Start()](Assets/Scripts/Networking/NetworkManager.cs#L337) | coroutine: auto-host / auto-join for the scripted 4-instance demo |

Config asset read through: [NetworkConfig](Assets/Scripts/Networking/NetworkConfig.cs)

**Per-frame loops that never stop:**

- [NetworkManager.Update()](Assets/Scripts/Networking/NetworkManager.cs#L398) → `netManager.PollEvents()` · heartbeats · roster resync · `quality.Tick()` · host-timeout check
- [TickManager.Update()](Assets/Scripts/TickManager.cs#L38) → [AdvanceTick()](Assets/Scripts/TickManager.cs#L49) → `OnTick` event
- [LanDiscovery.Update()](Assets/Scripts/Networking/LanDiscovery.cs#L68) → [Rebind()](Assets/Scripts/Networking/LanDiscovery.cs#L80) when the host role moves

---

## 1. HOSTING — opening the port

1. [NetworkTestUI.OnHostButtonClicked()](Assets/Scripts/Networking/NetworkTestUI.cs#L104)
2. [NetworkManager.HostLobby(port)](Assets/Scripts/Networking/NetworkManager.cs#L962) → `netManager.Start(port)` ← **the socket binds here**
   `isHost = true`, `localSpawnSlot = 0`, `currentHostId = me`, `state = InLobby`
3. [RebuildRoster()](Assets/Scripts/Networking/NetworkManager.cs#L137) → [ApplyRoster()](Assets/Scripts/Networking/NetworkManager.cs#L192) → [PlayerSpawner.SyncToRoster()](Assets/Scripts/Networking/PlayerSpawner.cs#L129) → [SpawnLocalPlayer()](Assets/Scripts/Networking/PlayerSpawner.cs#L37)
4. [SimpleGameStarter.Update()](Assets/Scripts/Networking/SimpleGameStarter.cs#L17) sees the local player exists → swaps lobby UI for gameplay UI

---

## 2. DISCOVERY — finding the host on the LAN

Client side:

1. [ServerBrowserUI.Open()](Assets/Scripts/Networking/ServerBrowserUI.cs#L42) → [Refresh()](Assets/Scripts/Networking/ServerBrowserUI.cs#L56)
2. [LanDiscovery.Refresh()](Assets/Scripts/Networking/LanDiscovery.cs#L107) → broadcast `CS2D_FIND` to port 47777

Host side:

3. [LanDiscovery.OnNetworkReceiveUnconnected()](Assets/Scripts/Networking/LanDiscovery.cs#L133) → [AnswerSearch()](Assets/Scripts/Networking/LanDiscovery.cs#L156) → replies `CS2D_HERE` + **real game port** (`NetworkManager.LocalPort`, not `gamePort` — a migrated host sits on an ephemeral port)

Back on the client:

4. [RecordHost()](Assets/Scripts/Networking/LanDiscovery.cs#L173) → [ServerBrowserUI.Rebuild()](Assets/Scripts/Networking/ServerBrowserUI.cs#L76) draws one clickable row per host
5. [DropStaleHosts()](Assets/Scripts/Networking/LanDiscovery.cs#L119) removes hosts silent for 5 s

---

## 3. JOINING

**Client**

1. [ServerBrowserUI.JoinEndpoint()](Assets/Scripts/Networking/ServerBrowserUI.cs#L145) (or [JoinManual()](Assets/Scripts/Networking/ServerBrowserUI.cs#L125))
2. [NetworkManager.JoinLobby(ip, port)](Assets/Scripts/Networking/NetworkManager.cs#L989) → `netManager.Start()` + `Connect()`, `state = ConnectingToLobby`, deadline armed

**Host**

3. [OnConnectionRequest()](Assets/Scripts/Networking/NetworkManager.cs#L1248) → `request.Accept()`

**Client**

4. [OnPeerConnected()](Assets/Scripts/Networking/NetworkManager.cs#L1112) → sends [JoinLobbyRequest](Assets/Scripts/Networking/Messages/LobbyMessages.cs) (id, username, **own listen port**)

**Host**

5. [OnNetworkReceive()](Assets/Scripts/Networking/NetworkManager.cs#L1202) → [MessageSerializer.Deserialize()](Assets/Scripts/Networking/MessageSerializer.cs#L29) → [HandleMessage()](Assets/Scripts/Networking/NetworkManager.cs#L1276)
6. [HandleJoinLobbyRequest()](Assets/Scripts/Networking/NetworkManager.cs#L1327) → [AllocateSpawnSlot()](Assets/Scripts/Networking/NetworkManager.cs#L113) → full? reject : [RegisterPeer()](Assets/Scripts/Networking/NetworkManager.cs#L78) → sends `JoinLobbyResponse`
7. [BroadcastRoster()](Assets/Scripts/Networking/NetworkManager.cs#L157) → `SessionRosterMessage` to everyone

**Client**

8. [HandleJoinLobbyResponse()](Assets/Scripts/Networking/NetworkManager.cs#L1373) → `state = InLobby`, host registered as a peer
9. [HandleSessionRoster()](Assets/Scripts/Networking/NetworkManager.cs#L1407) → caches roster, patches host IP → [ApplyRoster()](Assets/Scripts/Networking/NetworkManager.cs#L192)

---

## 4. SPAWNING — roster-driven, no "start game" step

[ApplyRoster()](Assets/Scripts/Networking/NetworkManager.cs#L192) → [PlayerSpawner.SyncToRoster()](Assets/Scripts/Networking/PlayerSpawner.cs#L129) — **idempotent**, so it is also the late-join and post-migration path:

- missing + is me → [SpawnLocalPlayer()](Assets/Scripts/Networking/PlayerSpawner.cs#L37)
- missing + other → [SpawnRemotePlayerByInfo()](Assets/Scripts/Networking/PlayerSpawner.cs#L69) → [StripInputFrom()](Assets/Scripts/Networking/PlayerSpawner.cs#L101) + adds [RemoteInterpolator](Assets/Scripts/RemoteInterpolator.cs#L11)
- spawned but no longer in the roster → [DespawnPlayer()](Assets/Scripts/Networking/PlayerSpawner.cs#L115)

Identity component: [NetworkedPlayer](Assets/Scripts/Networking/NetworkedPlayer.cs)

Per-player components self-select local vs remote one frame later:
[InputSampler.InitializeAfterSpawn()](Assets/Scripts/InputSampler.cs#L39) · [PlayerTickSimulation.InitializeAfterSpawn()](Assets/Scripts/PlayerTickSimulation.cs#L48) · [NetworkInputSender.InitializeAfterSpawn()](Assets/Scripts/Networking/NetworkInputSender.cs#L27)

---

## 5. MOVEMENT — one tick, end to end

**Client, tick N**

1. [InputSampler.HandleTick()](Assets/Scripts/InputSampler.cs#L114) → builds [InputCommand](Assets/Scripts/InputCommand.cs) → [LocalInputBuffer.Store()](Assets/Scripts/LocalInputBuffer.cs#L12)
2. [PlayerTickSimulation.HandleTick()](Assets/Scripts/PlayerTickSimulation.cs#L79) → **prediction**: [PlayerController.SimulateMovement()](Assets/Scripts/PlayerMovement.cs#L50) + [SimulateLook()](Assets/Scripts/PlayerMovement.cs#L153), records the predicted position
3. [NetworkInputSender.HandleTick()](Assets/Scripts/Networking/NetworkInputSender.cs#L62) → `InputCommandMessage`, `Sequenced` → host

**Host, tick N**

4. [NetworkStateHost.HandleMessage()](Assets/Scripts/Networking/NetworkStateHost.cs#L51) queues inputs as they arrive
5. [NetworkStateHost.HandleTick()](Assets/Scripts/Networking/NetworkStateHost.cs#L72) → [ProcessClientInputs()](Assets/Scripts/Networking/NetworkStateHost.cs#L90) — sorts by tick, skips `tick <= lastProcessedTick`, applies **every** queued input in order
6. [BroadcastStateUpdate()](Assets/Scripts/Networking/NetworkStateHost.cs#L186) → [StateUpdateMessage](Assets/Scripts/Networking/Messages/StateUpdateMessage.cs) = position + rotation + health + **ackTick** per player

**Client, on receipt**

7. [NetworkStateReceiver.HandleMessage()](Assets/Scripts/Networking/NetworkStateReceiver.cs#L25) → [ApplyStateUpdate()](Assets/Scripts/Networking/NetworkStateReceiver.cs#L41)
   - me → [PlayerTickSimulation.ApplyAuthoritativeState()](Assets/Scripts/PlayerTickSimulation.cs#L125) → **reconciliation**: compare prediction at `ackTick`; if off by more than the tolerance → [Teleport()](Assets/Scripts/PlayerMovement.cs#L139) then replay every input after `ackTick`
   - others → [RemoteInterpolator.Push()](Assets/Scripts/RemoteInterpolator.cs#L32) → [Update()](Assets/Scripts/RemoteInterpolator.cs#L41) renders 100 ms in the past and lerps

---

## 6. SHOOTING

- Client: [InputSampler.HandleTick()](Assets/Scripts/InputSampler.cs#L114) sets `firePressed` (cooldown counted in ticks) → travels inside the same `InputCommand`
- Host: [ProcessClientInputs()](Assets/Scripts/Networking/NetworkStateHost.cs#L90) → [PlayerController.SimulateShoot()](Assets/Scripts/PlayerMovement.cs#L163) (raycast + damage) → broadcasts [ShootEventMessage](Assets/Scripts/Networking/Messages/ShootEventMessage.cs) `ReliableOrdered`
- The host's own shot: [PlayerTickSimulation.HandleTick()](Assets/Scripts/PlayerTickSimulation.cs#L104) broadcasts it directly
- Clients: [NetworkShootReceiver.HandleMessage()](Assets/Scripts/Networking/NetworkShootReceiver.cs#L26) → `SimulateShoot()` — **visual only**
- Reconciliation replays movement only, so a shot never fires twice: [PlayerTickSimulation.cs:157](Assets/Scripts/PlayerTickSimulation.cs#L157)

---

## 7. HEALTH / DEATH / RESPAWN — host authoritative

[ZombieFollow.TryAttack()](Assets/Scripts/ZombieFollow.cs#L216) → [PlayerHealth.TakeDamage()](Assets/Scripts/PlayerHealth.cs#L26) (host only)
→ health rides along inside [StateUpdateMessage](Assets/Scripts/Networking/Messages/StateUpdateMessage.cs) → client applies [SetHealthFromNetwork()](Assets/Scripts/PlayerHealth.cs#L44) — never predicted
→ [PlayerHealth.Update()](Assets/Scripts/PlayerHealth.cs#L49) → [Respawn()](Assets/Scripts/PlayerHealth.cs#L61) once the timer expires, on the host

Dead player: the host skips its input but still advances the ack ([NetworkStateHost.cs:135](Assets/Scripts/Networking/NetworkStateHost.cs#L135)); the client stops predicting ([PlayerTickSimulation.cs:92](Assets/Scripts/PlayerTickSimulation.cs#L92)).

---

## 8. ZOMBIES — host simulates, clients mirror

Host: [ZombieSpawner.HandleTick()](Assets/Scripts/Networking/ZombieSpawner.cs#L88)
→ [PruneDeadZombies()](Assets/Scripts/Networking/ZombieSpawner.cs#L279) → [RebuildFlowField()](Assets/Scripts/Networking/ZombieSpawner.cs#L104) (one flood for all zombies, [ZombieFlowField](Assets/Scripts/ZombieFlowField.cs)) → [AdvanceWaves()](Assets/Scripts/Networking/ZombieSpawner.cs#L142) → [SpawnOne()](Assets/Scripts/Networking/ZombieSpawner.cs#L188) → [BroadcastZombieState()](Assets/Scripts/Networking/ZombieSpawner.cs#L293)

AI itself: [ZombieFollow.Update()](Assets/Scripts/ZombieFollow.cs#L146) — host-gated

Client: [ZombieSpawner.HandleMessage()](Assets/Scripts/Networking/ZombieSpawner.cs#L319) → [ApplyZombieState()](Assets/Scripts/Networking/ZombieSpawner.cs#L330) → [SpawnMirror()](Assets/Scripts/Networking/ZombieSpawner.cs#L354) / [DespawnMissing()](Assets/Scripts/Networking/ZombieSpawner.cs#L377)

Damage: [ZombieHealth.TakeDamage()](Assets/Scripts/ZombieHealth.cs#L21) host-only; clients get [SetHealthFromNetwork()](Assets/Scripts/ZombieHealth.cs#L37)

---

## 9. HEARTBEATS — failure detection

- Send: [NetworkManager.Update()](Assets/Scripts/Networking/NetworkManager.cs#L398) → [SendHeartbeat()](Assets/Scripts/Networking/NetworkManager.cs#L444), `Unreliable`, to **every transport peer**
- Receive: [HandleHeartbeat()](Assets/Scripts/Networking/NetworkManager.cs#L1432) → stamps `lastHeartbeatReceiveTime` on [PeerInfo](Assets/Scripts/Networking/PeerInfo.cs)
- Detect: [CheckHostTimeout()](Assets/Scripts/Networking/NetworkManager.cs#L457) — **only the host's silence counts** (5 s) → [BeginHostMigration()](Assets/Scripts/Networking/NetworkManager.cs#L491)
- A hard socket drop short-circuits the timeout: [OnPeerDisconnected()](Assets/Scripts/Networking/NetworkManager.cs#L1148) → migration immediately

---

## 10. RTT MEASUREMENT — the matrix elections rank on

Every peer pings **every other peer**, connectionless, on the *game* socket — so clients measure each other without a mesh.

1. [HostQualityMonitor.Tick()](Assets/Scripts/Networking/HostQualityMonitor.cs#L77) → [SendProbes()](Assets/Scripts/Networking/HostQualityMonitor.cs#L94) → `CS2D_PING` + timestamp → [NetworkManager.SendUnconnected()](Assets/Scripts/Networking/NetworkManager.cs#L1100)
2. Receiver: [NetworkManager.OnNetworkReceiveUnconnected()](Assets/Scripts/Networking/NetworkManager.cs#L1223) → [HandleUnconnected()](Assets/Scripts/Networking/HostQualityMonitor.cs#L114) → [AnswerPing()](Assets/Scripts/Networking/HostQualityMonitor.cs#L133) → `CS2D_PONG` + echoed stamp + own simulated latency + [WriteLocalRow()](Assets/Scripts/Networking/HostQualityMonitor.cs#L183) (**its whole measurement row**)
3. Pinger: [AcceptPong()](Assets/Scripts/Networking/HostQualityMonitor.cs#L160) → [RttStats.TryAcceptReply()](Assets/Scripts/Networking/RttStats.cs#L31) → Jacobson/Karels in [AddSample()](Assets/Scripts/Networking/RttStats.cs#L59); [ReadRemoteRow()](Assets/Scripts/Networking/HostQualityMonitor.cs#L205) fills in everyone else's row

> `EstimatedRTT = (1-a)*Est + a*Sample` · `DevRTT = (1-b)*Dev + b*|Sample-Est|` · **link cost = [TimeoutInterval()](Assets/Scripts/Networking/RttStats.cs#L80) = Est + K*Dev**

Ranking:

- [TryLinkCost()](Assets/Scripts/Networking/HostQualityMonitor.cs#L250) — one edge, falls back to the reverse direction
- [TryAggregateCost()](Assets/Scripts/Networking/HostQualityMonitor.cs#L291) — mean cost of **everyone → target** (a column of the matrix, not a row)
- [PickByLocalCost()](Assets/Scripts/Networking/HostQualityMonitor.cs#L316) — "whoever *I* ping best" (round 1)
- [PickByAggregateCost()](Assets/Scripts/Networking/HostQualityMonitor.cs#L347) — deterministic, identical on every peer (round 2+)
- Housekeeping: [PruneToRoster()](Assets/Scripts/Networking/HostQualityMonitor.cs#L489) · [ResetAll()](Assets/Scripts/Networking/HostQualityMonitor.cs#L525) · logs [DebugCostTable()](Assets/Scripts/Networking/HostQualityMonitor.cs#L538)

---

## 11. HOST MIGRATION — reactive (host died)

1. [CheckHostTimeout()](Assets/Scripts/Networking/NetworkManager.cs#L457) *or* [OnPeerDisconnected()](Assets/Scripts/Networking/NetworkManager.cs#L1148) → [BeginHostMigration()](Assets/Scripts/Networking/NetworkManager.cs#L491) — `state = HostMigration`, prediction and input sending stop
2. [RunMigration()](Assets/Scripts/Networking/NetworkManager.cs#L521) — up to 5 rounds:
   - [DialSurvivors()](Assets/Scripts/Networking/NetworkManager.cs#L549) → dials every roster entry (star → **mesh**), accepted by [OnConnectionRequest()](Assets/Scripts/Networking/NetworkManager.cs#L1248); each side identifies itself through [HandleJoinLobbyRequest()](Assets/Scripts/Networking/NetworkManager.cs#L1327)
   - random 0.5–1 s stagger
   - [StartElection(round)](Assets/Scripts/Networking/NetworkManager.cs#L633) → [ChooseCandidate()](Assets/Scripts/Networking/NetworkManager.cs#L618) (aggregate argmin, else [LowestId()](Assets/Scripts/Networking/NetworkManager.cs#L600)) — **only the winner campaigns** → `HostElectionRequest`
3. Voters: [HandleHostElectionRequest()](Assets/Scripts/Networking/NetworkManager.cs#L652) → [PreferredHost(round)](Assets/Scripts/Networking/NetworkManager.cs#L683) (round 1 = my own ping; round 2+ = shared aggregate) → `HostElectionResponse`
4. Candidate: [HandleHostElectionResponse()](Assets/Scripts/Networking/NetworkManager.cs#L704) → [CheckElectionVictory()](Assets/Scripts/Networking/NetworkManager.cs#L714) — majority of [CountReachableParticipants()](Assets/Scripts/Networking/NetworkManager.cs#L569) (**reachable**, not the whole roster — otherwise quorum can be unreachable forever)
5. Winner: [ClaimHostRole()](Assets/Scripts/Networking/NetworkManager.cs#L726) → `isHost = true`, drops the dead host, [RefreshPeerIdentitiesFromRoster()](Assets/Scripts/Networking/NetworkManager.cs#L767), broadcasts [HostClaimMessage](Assets/Scripts/Networking/Messages/HostClaimMessage.cs) + [BroadcastRoster()](Assets/Scripts/Networking/NetworkManager.cs#L157)
6. Everyone else: [HandleHostClaim()](Assets/Scripts/Networking/NetworkManager.cs#L780) → accept the new host (split-brain tie-break: lower ordinal id wins) → [DropMeshPeers()](Assets/Scripts/Networking/NetworkManager.cs#L844) → **mesh → star** again, `state = InLobby`
7. [LanDiscovery.Update()](Assets/Scripts/Networking/LanDiscovery.cs#L68) notices the role flip → [Rebind()](Assets/Scripts/Networking/LanDiscovery.cs#L80) → the new host now answers `CS2D_FIND` on its ephemeral port
8. [RemoteInterpolator.Update()](Assets/Scripts/RemoteInterpolator.cs#L41) sees `IsHost` and drops its buffer — it now simulates those players itself

---

## 12. HOST MIGRATION — proactive (host alive, just badly placed)

1. [HostQualityMonitor.EvaluateProactiveOnSchedule()](Assets/Scripts/Networking/HostQualityMonitor.cs#L384) (every 30 s) → [ConditionHolds()](Assets/Scripts/Networking/HostQualityMonitor.cs#L431):
   at least 3 participants · past cooldown · **full coverage** · `hostCost > factor * bestCost` · `hostCost - bestCost > minCostGap` · sustained N consecutive checks
2. The challenger alone proposes: [TryConsumeProposal()](Assets/Scripts/Networking/HostQualityMonitor.cs#L377) → [NetworkManager.BeginProactiveElection()](Assets/Scripts/Networking/NetworkManager.cs#L885) — **gameplay never pauses**, roster untouched
3. [RunProactiveElection()](Assets/Scripts/Networking/NetworkManager.cs#L901) → [DialSurvivors()](Assets/Scripts/Networking/NetworkManager.cs#L549) (the live host stays in the pool as a **voter**, not a candidate) → [StartElection(1)](Assets/Scripts/Networking/NetworkManager.cs#L633) with `proactive = true`
4. Voters re-derive the verdict independently: [HandleHostElectionRequest()](Assets/Scripts/Networking/NetworkManager.cs#L652) → [ValidateProactive()](Assets/Scripts/Networking/HostQualityMonitor.cs#L426); a `yes` is also the *permission* the sitting host needs before it will step down (`grantedProactiveVoteTo`)
5. Majority → [ClaimHostRole()](Assets/Scripts/Networking/NetworkManager.cs#L726); the old host stands down in [HandleHostClaim()](Assets/Scripts/Networking/NetworkManager.cs#L780) **only** if it voted yes — otherwise any peer could demote it
6. No majority → [AbortProactiveElection()](Assets/Scripts/Networking/NetworkManager.cs#L926) — mesh torn down, nothing changed, cooldown armed by [NoteMigration()](Assets/Scripts/Networking/HostQualityMonitor.cs#L370)

---

## 13. DISCONNECT / LEAVE

**Voluntary:** [LeaveLobby()](Assets/Scripts/Networking/NetworkManager.cs#L1043) → `DisconnectAll` if host → stop socket → [quality.ResetAll()](Assets/Scripts/Networking/HostQualityMonitor.cs#L525) → clear roster → [ApplyRoster()](Assets/Scripts/Networking/NetworkManager.cs#L192) despawns everyone

**A client drops (host side):** [OnPeerDisconnected()](Assets/Scripts/Networking/NetworkManager.cs#L1148) → [UnregisterPeer()](Assets/Scripts/Networking/NetworkManager.cs#L90) → roster entry removed → [PlayerDisconnectedMessage](Assets/Scripts/Networking/Messages/PlayerDisconnectedMessage.cs) + [BroadcastRoster()](Assets/Scripts/Networking/NetworkManager.cs#L157) (slot freed)
Clients: [HandlePlayerDisconnected()](Assets/Scripts/Networking/NetworkManager.cs#L1396) → [DespawnPlayer()](Assets/Scripts/Networking/PlayerSpawner.cs#L115)
The host also forgets its stale ack: [PruneDepartedPlayers()](Assets/Scripts/Networking/NetworkStateHost.cs#L169)

**Dial never lands:** [Update()](Assets/Scripts/Networking/NetworkManager.cs#L398) deadline → [AbortConnectionAttempt()](Assets/Scripts/Networking/NetworkManager.cs#L1027) → back to the browser

**A mesh link closes:** ignored — only the host decides membership ([NetworkManager.cs:1188](Assets/Scripts/Networking/NetworkManager.cs#L1188))

---

## 14. Message catalogue

[MessageType enum](Assets/Scripts/Networking/NetworkMessage.cs#L4) · factory [MessageSerializer.CreateMessage()](Assets/Scripts/Networking/MessageSerializer.cs#L59)

| Message | Direction | Delivery | Handler |
|---|---|---|---|
| [JoinLobbyRequest / Response](Assets/Scripts/Networking/Messages/LobbyMessages.cs) | client ↔ host | ReliableOrdered | [1327](Assets/Scripts/Networking/NetworkManager.cs#L1327) / [1373](Assets/Scripts/Networking/NetworkManager.cs#L1373) |
| [PlayerDisconnected](Assets/Scripts/Networking/Messages/PlayerDisconnectedMessage.cs) | host → all | ReliableOrdered | [1396](Assets/Scripts/Networking/NetworkManager.cs#L1396) |
| [SessionRoster](Assets/Scripts/Networking/Messages/SessionRosterMessage.cs) | host → all | ReliableOrdered | [1407](Assets/Scripts/Networking/NetworkManager.cs#L1407) |
| [InputCommand](Assets/Scripts/Networking/Messages/InputCommandMessage.cs) | client → host | Sequenced | [NetworkStateHost:51](Assets/Scripts/Networking/NetworkStateHost.cs#L51) |
| [StateUpdate](Assets/Scripts/Networking/Messages/StateUpdateMessage.cs) | host → all | Sequenced | [NetworkStateReceiver:25](Assets/Scripts/Networking/NetworkStateReceiver.cs#L25) |
| [ShootEvent](Assets/Scripts/Networking/Messages/ShootEventMessage.cs) | host → all | ReliableOrdered | [NetworkShootReceiver:26](Assets/Scripts/Networking/NetworkShootReceiver.cs#L26) |
| [ZombieState](Assets/Scripts/Networking/Messages/ZombieStateMessage.cs) | host → all | Sequenced | [ZombieSpawner:319](Assets/Scripts/Networking/ZombieSpawner.cs#L319) |
| Heartbeat | everyone → everyone | Unreliable | [1432](Assets/Scripts/Networking/NetworkManager.cs#L1432) |
| [HostElectionRequest / Response](Assets/Scripts/Networking/Messages/HostElectionMessages.cs) | candidate ↔ voters | ReliableOrdered | [652](Assets/Scripts/Networking/NetworkManager.cs#L652) / [704](Assets/Scripts/Networking/NetworkManager.cs#L704) |
| [HostClaim](Assets/Scripts/Networking/Messages/HostClaimMessage.cs) | new host → all | ReliableOrdered | [780](Assets/Scripts/Networking/NetworkManager.cs#L780) |
| `CS2D_PING` / `CS2D_PONG` | everyone → everyone | **unconnected** | [HostQualityMonitor:114](Assets/Scripts/Networking/HostQualityMonitor.cs#L114) |
| `CS2D_FIND` / `CS2D_HERE` | broadcast | **unconnected** | [LanDiscovery:133](Assets/Scripts/Networking/LanDiscovery.cs#L133) |

Wire format: [MessageSerializer.Serialize()](Assets/Scripts/Networking/MessageSerializer.cs#L12) — 1 type byte + `BinaryWriter` body, per [INetworkMessage](Assets/Scripts/Networking/NetworkMessage.cs#L31).

---

## 15. Demo aids — running it, and forcing a proactive migration

Full write-up: [HOST-ELECTION.md §13–14](claude/HOST-ELECTION.md). Short version below.

### 15.1 Why simulated latency is needed at all

Loopback RTT is ~0, so all four instances score identically and the election falls through to the GUID
tiebreak — it *looks* like it works while proving nothing. Two properties make the fake latency real:

- **Per instance, not per asset.** `rttSimulatedExtraMs` lives on the shared [NetworkConfig](Assets/NetworkConfig.asset), so every instance on the machine would read the same value. `-rttExtraMs <ms>` overrides it per process — [ParseCommandLineOverrides()](Assets/Scripts/Networking/NetworkManager.cs#L287) → [SimulatedExtraMs](Assets/Scripts/Networking/NetworkManager.cs#L62) → [Overridden()](Assets/Scripts/Networking/NetworkManager.cs#L282). The asset itself is never written to. **The Editor has no command line, so it always falls back to the asset.**
- **Symmetric, a property of the link.** Each pong carries the responder's own offset ([AnswerPing()](Assets/Scripts/Networking/HostQualityMonitor.cs#L133)) and the pinger charges the sample for both ends ([AcceptPong()](Assets/Scripts/Networking/HostQualityMonitor.cs#L160)):
  `SampleRTT = realElapsed + myExtraMs + responderExtraMs`.
  Without this the offset inflates only that peer's **row**, and [TryAggregateCost()](Assets/Scripts/Networking/HostQualityMonitor.cs#L291) reads the **column** — the knob would do nothing.

### 15.2 Layout

**Editor = host** (console readable live) + **three builds** launched by [demo-4-instances.bat](claude/demo-4-instances.bat), auto-joining `127.0.0.1:7777`.

| Instance | Launched by | `-rttExtraMs` |
|---|---|---|
| Editor (host) | you, manually | asset field, starts at **0** |
| Client A `Near` | .bat | 0 |
| Client B `Mid` | .bat | 100 |
| Client C `Far` | .bat | 200 |

### 15.3 Command-line flags

Parsed in [ParseCommandLineOverrides()](Assets/Scripts/Networking/NetworkManager.cs#L287); the whitelist is `OverridableKeys` at [NetworkManager.cs:270](Assets/Scripts/Networking/NetworkManager.cs#L270).

| Flag | Purpose |
|---|---|
| `-autohost <port>` | host on boot ([Start()](Assets/Scripts/Networking/NetworkManager.cs#L337)) |
| `-autojoin <ip:port>` | dial on boot, retries for 60 s |
| `-username <name>` | otherwise every instance logs under the scene's one name |
| `-rttExtraMs <ms>` | this instance's simulated distance |
| `-rttProbeInterval <ms>` / `-rttMinSamples <n>` | warm-up speed |
| `-proactiveCheckInterval`, `-proactiveThresholdFactor`, `-proactiveSustainedChecks`, `-proactiveCooldown`, `-proactiveMinCostGap` | the trigger |
| `-logFile <path>` | **one per instance**, or they overwrite each other's `Player.log` |

Floats are parsed invariant-culture, so `2.0` is always two — not twenty on a comma-decimal machine.

### 15.4 Editor setup — do this BEFORE Play

Select [Assets/NetworkConfig.asset](Assets/NetworkConfig.asset) in the Project window. It currently holds the
**production defaults**, which are deliberately too slow to demo, so change them:

| Field | Set to | Default | Why |
|---|---|---|---|
| `rttProbeInterval` | **1000** | 3000 | warm-up and EWMA convergence 3× faster |
| `proactiveCheckInterval` | **10000** | 30000 | 30 s is a long time to stare at four windows |
| `proactiveSustainedChecks` | **1** | 2 | otherwise two full intervals before anything happens |
| `proactiveThresholdFactor` | **2.0** | 3.0 | 3.0 is unreachable in the pinned case — see 15.6 |
| `proactiveCooldown` | **15000** | 60000 | lets you retry without waiting a minute |
| `rttSimulatedExtraMs` | **0** | 0 | ← leave at 0. Making the host bad *live* is the demo |
| `proactiveMigrationEnabled` | ✅ on | on | master switch, [ConditionHolds()](Assets/Scripts/Networking/HostQualityMonitor.cs#L431) returns false without it |

These are the same values the `.bat` passes to the builds via `TUNE`, so all four peers agree.

### 15.5 Run order

1. Edit `GAME` in [demo-4-instances.bat](claude/demo-4-instances.bat) if the build isn't at `%USERPROFILE%\Desktop\game-ds\`.
2. Confirm the build is fresh — check `…/2D Zombie p2p_Data/Managed/Assembly-CSharp.dll`, **not** the `.exe` timestamp. A stale build looks exactly like a networking bug.
3. **Editor: enter Play, host on port 7777.** Dock the Console where it's readable.
4. Run the `.bat`. Three clients launch 2 s apart and auto-join.
5. Wait out the warm-up floor: `rttMinSamples × rttProbeInterval` = **5 × 1000 = 5 s**. Nothing can be elected on RTT grounds before that, by design ([IsUsable()](Assets/Scripts/Networking/RttStats.cs#L98)).

### 15.6 Forcing the proactive migration — the one move

> **In the Inspector, while still in Play mode, set `rttSimulatedExtraMs` from 0 → 400 on [NetworkConfig](Assets/NetworkConfig.asset).**

That is the whole trigger. Say: *"I am now simulating that the host is far from everyone."*

Costs then settle at (mean of everyone's link cost **toward** X — a column, per [TryAggregateCost()](Assets/Scripts/Networking/HostQualityMonitor.cs#L291)):

| peer | extra | cost(X) |
|---|---|---|
| Editor host | 400 | (400+500+600)/3 = **500** |
| A `Near` | 0 | (400+100+200)/3 = **233** |
| B `Mid` | 100 | (500+100+300)/3 = **300** |
| C `Far` | 200 | (600+200+300)/3 = **367** |

Ratio 500/233 = **2.14** (clears the 2.0 factor), absolute gap **267 ms** (clears the 100 ms `proactiveMinCostGap` floor). Both conditions in [ConditionHolds()](Assets/Scripts/Networking/HostQualityMonitor.cs#L431) are met → **A wins on merit**, not on a GUID tiebreak.

**The trap this table exists to avoid:** if the host carried the *only* non-zero offset, the ratio is pinned at exactly `n − 1` = 3.0 for four players (`cost(host) = E`, `cost(other) = E/(n−1)`). The check is a strict `>`, so a 3.0 threshold against a 3.0 ratio never fires. Hence the clients are differentiated 0/100/200 **and** the factor is lowered to 2.0.

Because it is a ScriptableObject, editing the field at runtime **persists into the project** — set it back to 0 afterwards.

### 15.7 What to watch in the console

Printed every check by [EvaluateProactiveOnSchedule()](Assets/Scripts/Networking/HostQualityMonitor.cs#L384):

```
[HostQuality] links   Near: est=… dev=… TO=… n=…        <- DebugSummary(),  per link
[HostQuality] cost(X) Near=233ms/3of3  Editor(host)=500ms/3of3   <- DebugCostTable(), the column means
[HostQuality] Proactive condition holds (1/1), challenger <id>   <- on EVERY peer, not just the challenger
[HostMigration] Vote for <id>: yes (round=1, proactive=True)     <- including from the current host
[HostMigration] Standing down for <id> (proactive=True)          <- old host, stays in the game as a client
```

`DevRTT` spikes before `EstimatedRTT` moves — the samples jumped, so the deviation term reacts first ([AddSample()](Assets/Scripts/Networking/RttStats.cs#L59)).

On a **build** (no Unity console) press **F1** for the in-game log console — [DebugConsole](Assets/Scripts/DebugConsole.cs#L21) — and right-click the `HostQuality` tag to solo it.

### 15.8 Forcing the other two paths

- **Failure migration:** just close the host window (or Stop Play). Survivors at 0/100/200 give costs 150/200/250 → the 0 ms peer wins. Check the ids in the log to prove it wasn't simply the lowest GUID.
- **Split proactive vote (safe no-op):** set one client's offset so no majority forms. Expect `[Proactive] No majority, keeping the current host` from [RunProactiveElection()](Assets/Scripts/Networking/NetworkManager.cs#L901) → [AbortProactiveElection()](Assets/Scripts/Networking/NetworkManager.cs#L926), and nothing else changes.
- **Late join:** close client C's window, relaunch it from its `.bat` line. It rejoins mid-game through the same code path as a first join ([SyncToRoster()](Assets/Scripts/Networking/PlayerSpawner.cs#L129)).

### 15.9 If nothing happens

| Symptom | Cause |
|---|---|
| No migration after the Inspector bump | Warm-up floor — wait one more check |
| Log says *"…x worse but only Nms of link cost — below the … floor"* | `proactiveMinCostGap`, not the ratio, is blocking ([HostQualityMonitor.cs:476](Assets/Scripts/Networking/HostQualityMonitor.cs#L476)) |
| It migrated during the *gameplay* section | The asset wasn't on `rttSimulatedExtraMs = 0`. Reset and restart |
| A client shows no players | Old build — check the `.dll` timestamp, not the `.exe` |
| `[LanDiscovery] Could not bind 47777` on 2nd–4th instance | Expected: one process owns the port, the rest fall back to searcher mode ([Rebind()](Assets/Scripts/Networking/LanDiscovery.cs#L80)) |
| Two hosts / session splits | Should self-heal — ordinal id tiebreak in [HandleHostClaim()](Assets/Scripts/Networking/NetworkManager.cs#L780) |

> Note: [HOST-ELECTION.md §13](claude/HOST-ELECTION.md#L460) lists a startup line `[NetworkManager] Simulating +400ms of link latency…` — that log does not exist. What actually prints, and only on the **builds**, is `[NetworkManager] Override -rttExtraMs = 400`. The Editor takes the value from the asset and logs nothing.
