using TMPro;
using UnityEngine;

public class ZombieHealth : MonoBehaviour
{
    [Header("Health")]
    public int maxHealth = 10;
    public int currentHealth;

    [Header("UI")]
    [SerializeField] private TMPro.TMP_Text healthText;

    private void Awake()
    {
        currentHealth = maxHealth;
        UpdateHealthText();
    }

    // Host only. Clients receive health in the zombie state instead of computing it, so a
    // client cannot kill a zombie that is still alive on the host.
    public void TakeDamage(int amount)
    {
        if (NetworkManager.Instance != null && !NetworkManager.Instance.IsHost)
            return;

        currentHealth -= amount;
        currentHealth = Mathf.Max(currentHealth, 0);

        UpdateHealthText();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void SetHealthFromNetwork(int value)
    {
        currentHealth = value;
        UpdateHealthText();
    }

    private void UpdateHealthText()
    {
        if (healthText != null)
            healthText.text = $"{currentHealth}/{maxHealth}";
    }

    private void Die()
    {
        Destroy(gameObject);
    }
}
