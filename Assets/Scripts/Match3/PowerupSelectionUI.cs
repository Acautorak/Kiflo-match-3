using UnityEngine;

/// <summary>
/// Shows the powerup picker between stages. Listens for PowerupChoicesOfferedEvent, binds each
/// offered PowerupDefinition to a slot, and forwards whichever one the player taps to
/// PowerupManager.SelectPowerup() - which applies its effect and advances to the next stage.
/// Assign one slot per choice PowerupPoolConfig.choicesOffered can produce; any unused slot
/// (fewer choices offered than slots configured) is hidden automatically.
/// </summary>
public class PowerupSelectionUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private PowerupChoiceSlotUI[] slots;
    [SerializeField] private PowerupManager powerupManager;

    private void OnEnable() => EventBus.Subscribe<PowerupChoicesOfferedEvent>(HandleOffered);
    private void OnDisable() => EventBus.Unsubscribe<PowerupChoicesOfferedEvent>(HandleOffered);

    private void Start()
    {
        if (panelRoot == null) return;

        bool anySlotActive = false;
        if (slots != null)
        {
            foreach (var slot in slots)
            {
                if (slot != null && slot.gameObject.activeSelf)
                {
                    anySlotActive = true;
                    break;
                }
            }
        }

        if (!anySlotActive) panelRoot.SetActive(false);
    }

    private void HandleOffered(PowerupChoicesOfferedEvent evt)
    {
        if (slots == null || slots.Length == 0) return;

        for (int i = 0; i < slots.Length; i++)
        {
            var powerup = (evt.Choices != null && i < evt.Choices.Length) ? evt.Choices[i] : null;
            slots[i].Bind(powerup, HandleSlotSelected);
        }

        bool anyOffered = evt.Choices != null && evt.Choices.Length > 0;
        if (panelRoot != null) panelRoot.SetActive(anyOffered);
    }

    private void HandleSlotSelected(PowerupDefinition powerup)
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (powerupManager != null) powerupManager.SelectPowerup(powerup);
    }
}
