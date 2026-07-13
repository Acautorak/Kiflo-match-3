using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField, Min(1)] private int startingMaxHealth = 5;
    [SerializeField] private int maxHealth;
    [SerializeField] private int currentHealth;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;

    private void Awake()
    {
        InitializeHealth();
    }

    public void ResetForNewRun()
    {
        maxHealth = Mathf.Max(1, startingMaxHealth);
        currentHealth = maxHealth;
        PublishState();
    }

    public void ResetToFullHealth()
    {
        currentHealth = Mathf.Max(1, startingMaxHealth);
        maxHealth = currentHealth;
        PublishState();
    }

    public void SetHealth(int current, int max)
    {
        maxHealth = Mathf.Max(1, max);
        currentHealth = Mathf.Clamp(current, 0, maxHealth);
        PublishState();
    }

    public void TakeDamage(int amount = 1)
    {
        if (amount <= 0 || currentHealth <= 0) return;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        PublishState();

        if (currentHealth <= 0)
            EventBus.Publish(new GameOverEvent(0));
    }

    private void InitializeHealth()
    {
        if (maxHealth <= 0)
            maxHealth = startingMaxHealth;

        if (currentHealth <= 0 || currentHealth > maxHealth)
            currentHealth = maxHealth;

        PublishState();
    }

    private void PublishState()
    {
        EventBus.Publish(new HealthChangedEvent(currentHealth, maxHealth));
    }
}
