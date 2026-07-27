using System;
using UnityEngine;

/// <summary>
/// A single falling asteroid in Kebab Karnage mode. Falls straight down (via kinematic
/// Rigidbody2D so trigger overlap works), takes damage via TakeHit() (called by
/// KebabKarnageManager's centralized tap detection, not its own input handling - see that
/// class for why), and reports back to the manager if it's broken or if it reaches the bottom.
///
/// Requires a non-trigger Collider2D (used both for tap hit-testing and for
/// OnTriggerEnter2D against the kill zone) and a Rigidbody2D (auto-added, forced kinematic).
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class FallingAsteroid : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float rotationSpeed = 30f;

    [Tooltip("Failsafe: if the asteroid somehow falls below this world Y without hitting an AsteroidKillZone " +
             "(e.g. the kill zone collider doesn't cover this asteroid's X, or a layer collision matrix issue), " +
             "it resolves as 'reached bottom' anyway instead of falling forever. Set well below your kill zone.")]
    [SerializeField] private float failsafeBottomY = -12f;

    [Header("Health")]
    [Tooltip("Hit points. Each tap removes the player's current Kebab tap damage (base 1 + PlayerRunStats.KebabTapDamageBonus).")]
    [SerializeField] private int maxHealth = 1;

    [Header("Damage / Reward")]
    [Tooltip("Player health lost (via PlayerHealth.TakeDamage) if this asteroid reaches the kill zone unbroken")]
    [SerializeField] private int damageOnImpact = 1;
    [Tooltip("Counted toward KebabKarnageManager.AsteroidsBroken when broken by the player")]
    [SerializeField] private int rewardOnBreak = 10;

    [Header("Visuals")]
    [SerializeField] private AsteroidHealthBar healthBar;
    [SerializeField] private GameObject breakVfxPrefab;
    [SerializeField] private GameObject impactVfxPrefab;

    private Rigidbody2D _rb;
    private float _fallSpeed;
    private int _currentHealth;
    private bool _resolved; // prevents double-trigger (broken + hit kill zone / failsafe same frame)

    public event Action<FallingAsteroid> OnBroken;
    public event Action<FallingAsteroid> OnReachedBottom;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.bodyType = RigidbodyType2D.Kinematic;
        _rb.gravityScale = 0f;
    }

    /// <param name="fallSpeed">World units/sec falling speed for this asteroid.</param>
    /// <param name="maxHealthOverride">Pass > 0 to override the inspector-configured max health (e.g. bigger asteroid variants).</param>
    public void Init(float fallSpeed, int maxHealthOverride = -1)
    {
        _fallSpeed = fallSpeed;
        if (maxHealthOverride > 0) maxHealth = maxHealthOverride;

        _currentHealth = maxHealth;
        _resolved = false;
        healthBar?.SetFraction(1f);

        if (healthBar == null && maxHealth > 1)
            Debug.LogWarning($"[FallingAsteroid] '{name}' has Max Health {maxHealth} but no Health Bar assigned - " +
                              "damage still works, but the player won't see any visual feedback before it breaks.", this);
    }

    private void FixedUpdate()
    {
        if (_resolved) return;
        _rb.MovePosition(_rb.position + Vector2.down * _fallSpeed * Time.fixedDeltaTime);
    }

    private void Update()
    {
        if (_resolved) return;

        transform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);

        // Belt-and-suspenders: don't rely solely on the trigger overlap with AsteroidKillZone.
        if (transform.position.y <= failsafeBottomY)
            ReachBottom();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_resolved) return;
        if (other.GetComponent<AsteroidKillZone>() != null)
            ReachBottom();
    }

    public void TakeHit(int amount)
    {
        if (_resolved || amount <= 0) return;

        _currentHealth -= amount;
        healthBar?.SetFraction(maxHealth > 0 ? (float)_currentHealth / maxHealth : 0f);

        if (_currentHealth <= 0)
            Break();
    }

    private void Break()
    {
        if (_resolved) return;
        _resolved = true;

        if (breakVfxPrefab != null)
            Instantiate(breakVfxPrefab, transform.position, Quaternion.identity);

        OnBroken?.Invoke(this);
        Destroy(gameObject);
    }

    private void ReachBottom()
    {
        if (_resolved) return;
        _resolved = true;

        if (impactVfxPrefab != null)
            Instantiate(impactVfxPrefab, transform.position, Quaternion.identity);

        OnReachedBottom?.Invoke(this);
        Destroy(gameObject);
    }

    public int Damage => damageOnImpact;
    public int Reward => rewardOnBreak;
}
