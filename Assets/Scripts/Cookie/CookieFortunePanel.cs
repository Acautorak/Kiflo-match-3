using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "you crack open the cookie" panel: fortune flavor text plus whatever powerup Cookie Smash
/// just rolled, with a single dismiss button. Same optional-reference/callback shape as
/// KebabKarnageIntroPopup (Show(...) takes a callback, fires it on dismiss) so CookieSmashManager
/// can wait on it the same way KebabKarnageManager waits on its intro popup.
///
/// Assumes TextMeshPro - swap the TMP_Text fields for UnityEngine.UI.Text if this project isn't
/// using TMP.
/// </summary>
public class CookieFortunePanel : MonoBehaviour
{
    [SerializeField] private GameObject root; // the panel's own root GameObject, toggled active/inactive
    [SerializeField] private TMP_Text fortuneText;
    [SerializeField] private TMP_Text powerupTitleText;
    [SerializeField] private TMP_Text powerupDescriptionText;
    [SerializeField] private Image powerupIcon;
    [SerializeField] private GameObject powerupSection; // parent of the three fields above - hidden if no powerup was granted
    [SerializeField] private Button dismissButton;

    private System.Action _onDismissed;

    private void Awake()
    {
        if (dismissButton != null) dismissButton.onClick.AddListener(HandleDismissClicked);
        if (root != null) root.SetActive(false);
    }

    /// <summary>Shows the panel populated with the given fortune line and (optionally) the
    /// granted powerup's presentation fields. Pass a null powerup to hide that section entirely -
    /// keeps this panel reusable even if Cookie Smash is ever configured to grant nothing.</summary>
    public void Show(string fortune, PowerupDefinition grantedPowerup, System.Action onDismissed)
    {
        _onDismissed = onDismissed;

        if (fortuneText != null) fortuneText.text = fortune;

        bool hasPowerup = grantedPowerup != null;
        if (powerupSection != null) powerupSection.SetActive(hasPowerup);
        if (hasPowerup)
        {
            if (powerupTitleText != null) powerupTitleText.text = grantedPowerup.title;
            if (powerupDescriptionText != null) powerupDescriptionText.text = grantedPowerup.description;
            if (powerupIcon != null)
            {
                powerupIcon.sprite = grantedPowerup.icon;
                powerupIcon.enabled = grantedPowerup.icon != null;
            }
        }

        if (root != null) root.SetActive(true);
    }

    private void HandleDismissClicked()
    {
        if (root != null) root.SetActive(false);

        var callback = _onDismissed;
        _onDismissed = null;
        callback?.Invoke();
    }
}
