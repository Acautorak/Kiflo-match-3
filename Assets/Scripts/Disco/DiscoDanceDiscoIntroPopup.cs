using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal shown right as Disco Dance Disco starts: pauses the game (Time.timeScale = 0) and
/// announces the win, then resumes and calls back into DiscoDanceDiscoManager once dismissed -
/// either by the player tapping Continue, or automatically after autoDismissAfter real-time
/// seconds (whichever comes first). Runs entirely on unscaled time so it isn't affected by the
/// pause it itself causes. Identical shape to FreeSpinsIntroPopup/TileCollectorIntroPopup - kept
/// as its own class so each feature/event owns its own popup the same way it owns its own manager.
///
/// Unlike the other features, Disco Dance Disco never disables normal input (see
/// DiscoDanceDiscoManager - it's a buff layered on top of regular play, not its own mini-game),
/// so this popup's pause is the ONLY thing stopping the player from swapping tiles before the
/// duration/multiplier window has actually started - same reasoning as
/// TileCollectorIntroPopup's note about not wasting session time behind an undismissed popup.
///
/// Setup: put your announcement text/art ("DISCO DANCE DISCO!" banner, duration/multiplier
/// callout, whatever) under panelRoot (inactive by default in the prefab/scene). continueButton
/// is optional - leave it unassigned if you only want the auto-dismiss timer.
/// </summary>
public class DiscoDanceDiscoIntroPopup : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button continueButton;

    [Tooltip("Auto-dismiss after this many real-time seconds even if the player doesn't tap Continue. Set to 0 to wait indefinitely for the button instead.")]
    [SerializeField] private float autoDismissAfter = 2.5f;

    private Action _onDismissed;
    private float _shownAtRealtime;
    private bool _isShowing;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (continueButton != null) continueButton.onClick.AddListener(Dismiss);
    }

    public void Show(Action onDismissed)
    {
        _onDismissed = onDismissed;
        _isShowing = true;
        _shownAtRealtime = Time.unscaledTime;

        if (panelRoot != null) panelRoot.SetActive(true);
        Time.timeScale = 0f;
    }

    private void Update()
    {
        if (!_isShowing) return;
        if (autoDismissAfter > 0f && Time.unscaledTime - _shownAtRealtime >= autoDismissAfter)
            Dismiss();
    }

    private void Dismiss()
    {
        if (!_isShowing) return;
        _isShowing = false;

        if (panelRoot != null) panelRoot.SetActive(false);
        Time.timeScale = 1f;

        var callback = _onDismissed;
        _onDismissed = null;
        callback?.Invoke();
    }
}
