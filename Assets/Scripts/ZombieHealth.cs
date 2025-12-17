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

    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        currentHealth = Mathf.Max(currentHealth, 0);

        UpdateHealthText();

        if (currentHealth <= 0)
        {
            Die();
        }
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
