using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField, Min(1)] private int startingMaxHealth = 5;
    [SerializeField] private int maxHealth;
    [SerializeField] private int currentHealth;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    /// <summary>True while a Grace Move (see GraceMoveController) is being resolved - TakeDamage
    /// no-ops entirely while this is set, covering every damage source that routes through it
    /// (match-based color damage, the no-match swap penalty, Madness ignite effects, etc.)
    /// without needing to touch any of those systems individually. Heal is deliberately NOT
    /// gated by this - a Grace Move blocks damage, not healing.</summary>
    public bool IsDamageImmune { get; private set; }

    /// <summary>Toggles damage immunity - Board wraps this around a Grace Move's resolution.</summary>
    public void SetDamageImmune(bool immune) => IsDamageImmune = immune;

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

        if (IsDamageImmune)
        {
            Debug.Log($"[PlayerHealth] TakeDamage({amount}) suppressed - a Grace Move is active.");
            return;
        }

        currentHealth = Mathf.Max(0, currentHealth - amount);
        PublishState();

        if (currentHealth <= 0)
            EventBus.Publish(new GameOverEvent(0));
    }

    /// <summary>Restores HP without changing maxHealth. Clamped to maxHealth.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || currentHealth <= 0) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        PublishState();
    }

    /// <summary>Raises the health ceiling for the rest of this run (a powerup effect). Also heals by the same amount unless healToFull is set.</summary>
    public void IncreaseMaxHealth(int amount, bool healToFull = false)
    {
        if (amount <= 0) return;
        maxHealth += amount;
        currentHealth = healToFull ? maxHealth : Mathf.Min(maxHealth, currentHealth + amount);
        PublishState();
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
