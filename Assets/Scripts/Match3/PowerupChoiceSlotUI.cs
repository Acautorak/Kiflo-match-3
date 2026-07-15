using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One tappable slot in the powerup picker. Bind() populates its visuals from a PowerupDefinition
/// and wires the button to report the pick back to whoever bound it (see PowerupSelectionUI).
/// </summary>
public class PowerupChoiceSlotUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Button selectButton;

    private PowerupDefinition assigned;
    private System.Action<PowerupDefinition> onSelected;

    private void Awake()
    {
        if (selectButton != null)
            selectButton.onClick.AddListener(HandleClicked);
    }

    public void Bind(PowerupDefinition powerup, System.Action<PowerupDefinition> onSelectedCallback)
    {
        assigned = powerup;
        onSelected = onSelectedCallback;

        if (titleText != null) titleText.text = powerup != null ? powerup.title : string.Empty;
        if (descriptionText != null) descriptionText.text = powerup != null ? powerup.description : string.Empty;
        if (iconImage != null)
        {
            iconImage.sprite = powerup != null ? powerup.icon : null;
            iconImage.enabled = powerup != null && powerup.icon != null;
        }

        gameObject.SetActive(powerup != null);
    }

    private void HandleClicked() => onSelected?.Invoke(assigned);
}
