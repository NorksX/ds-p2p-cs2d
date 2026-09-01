/// <summary>
/// Connection state of the local peer. Gameplay runs in InLobby - there is no separate
/// "in game" state, because spawning is roster-driven rather than gated on a start step.
/// </summary>
public enum ConnectionState
{
    Disconnected,
    ConnectingToLobby,
    InLobby,
    HostMigration
}
