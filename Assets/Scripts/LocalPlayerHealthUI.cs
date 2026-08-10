using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the scene health bar from whichever player is local.
///
/// PlayerHealth carried these references itself, but it lives on a prefab and a prefab cannot
/// reference scene objects - so they were always null and the bar never moved. Polling from the
/// scene side is the way round that, and it survives respawns and host migration.
/// </summary>
public class LocalPlayerHealthUI : MonoBehaviour
{
    [SerializeField] private Image healthFill;
    [SerializeField] private TMP_Text healthText;

    private PlayerHealth tracked;

    private void Update()
    {
        if (tracked == null)
            tracked = FindLocalPlayerHealth();

        if (tracked == null)
        {
            if (healthText != null)
                healthText.text = "";

            if (healthFill != null)
                healthFill.fillAmount = 0f;

            return;
        }

        float fraction = tracked.maxHealth > 0
            ? (float)tracked.currentHealth / tracked.maxHealth
            : 0f;

        if (healthFill != null)
            healthFill.fillAmount = Mathf.Clamp01(fraction);

        if (healthText != null)
            healthText.text = tracked.IsDead
                ? "DEAD"
                : $"{tracked.currentHealth}/{tracked.maxHealth}";
    }

    private PlayerHealth FindLocalPlayerHealth()
    {
        if (NetworkManager.Instance == null || PlayerSpawner.Instance == null)
            return null;

        NetworkedPlayer local = PlayerSpawner.Instance.GetPlayer(NetworkManager.Instance.LocalPlayerId);
        return local != null ? local.GetComponent<PlayerHealth>() : null;
    }
}
