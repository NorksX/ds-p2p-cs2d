using UnityEngine;

/// <summary>
/// Swaps the lobby UI for the gameplay UI once the local player exists.
/// Spawning itself is roster-driven (see PlayerSpawner.SyncToRoster) so that joining
/// mid-game works - there is no longer a "start" step gating it.
/// </summary>
public class SimpleGameStarter : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject lobbyUI;
    [SerializeField] private GameObject gameplayPanel;
    [SerializeField] private GameObject startGameButton;

    private bool inGame;

    private void Update()
    {
        bool localPlayerExists = LocalPlayerExists();

        if (localPlayerExists != inGame)
        {
            inGame = localPlayerExists;
            ApplyUIState();
        }

        // Hidden until Phase 5 gives it a purpose (starting zombie waves).
        if (startGameButton != null && startGameButton.activeSelf)
            startGameButton.SetActive(false);
    }

    private bool LocalPlayerExists()
    {
        if (NetworkManager.Instance == null || PlayerSpawner.Instance == null)
            return false;

        return PlayerSpawner.Instance.GetPlayer(NetworkManager.Instance.LocalPlayerId) != null;
    }

    private void ApplyUIState()
    {
        if (lobbyUI != null)
            lobbyUI.SetActive(!inGame);

        if (gameplayPanel != null)
            gameplayPanel.SetActive(inGame);

        Debug.Log(inGame ? "[SimpleGameStarter] Entered game" : "[SimpleGameStarter] Returned to lobby");
    }

    // Kept so the scene's button wiring stays intact. Phase 5 will start waves here.
    public void StartGame()
    {
    }
}
