using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires a SPIN Button's OnClick to FreeSpinsManager.SpinButtonPressed and keeps it in sync with
/// the mode's state - disabled while inactive, disabled again mid-spin (so mashing it can't queue
/// a second spin on top of one still animating), re-enabled once the spin lands and spins remain.
/// Also drives an optional "Spins Left: N" label.
///
/// Drop this on the same button (or its parent panel) that's already listed in
/// FreeSpinsManager.showWhileActive/hideWhileActive - see the ordering note on OnModeStarted
/// below for why that's safe. No Inspector OnClick wiring needed; this does it in code.
/// </summary>
[DisallowMultipleComponent]
public class FreeSpinsUI : MonoBehaviour
{
    [Header("References (auto-found in Awake if left empty)")]
    [SerializeField] private FreeSpinsManager freeSpinsManager;
    [Tooltip("The SPIN button itself. Required - OnClick is wired to FreeSpinsManager.SpinButtonPressed automatically.")]
    [SerializeField] private Button spinButton;
    [Tooltip("Optional - shows spins-left text. Leave empty to skip.")]
    [SerializeField] private TMP_Text spinsRemainingLabel;
    [Tooltip("Format string passed to string.Format with the remaining spin count as {0}.")]
    [SerializeField] private string spinsRemainingFormat = "Spins Left: {0}";

    private void Awake()
    {
        if (freeSpinsManager == null) freeSpinsManager = FindAnyObjectByType<FreeSpinsManager>();
        if (spinButton == null) spinButton = GetComponent<Button>();

        if (freeSpinsManager == null)
            Debug.LogError("[FreeSpinsUI] No FreeSpinsManager found in scene - the SPIN button won't do anything.");
        if (spinButton == null)
            Debug.LogError("[FreeSpinsUI] No Button assigned/found - nothing to wire up.");

        // Safe default before any FreeSpinsManager event has fired yet.
        if (spinButton != null) spinButton.interactable = false;
    }

    private void OnEnable()
    {
        // Ordering note: FreeSpinsManager.StartFeatureMode() activates showWhileActive BEFORE
        // invoking OnModeStarted, so if this script lives on one of those objects, OnEnable here
        // has already run (and the listener below is already subscribed) by the time
        // OnModeStarted actually fires - no missed first event.
        if (spinButton != null)
        {
            spinButton.onClick.RemoveListener(HandleSpinButtonClicked);
            spinButton.onClick.AddListener(HandleSpinButtonClicked);
        }

        if (freeSpinsManager != null)
        {
            freeSpinsManager.OnModeStarted.AddListener(HandleSpinsRemainingChanged);
            freeSpinsManager.OnSpinsRemainingChanged.AddListener(HandleSpinsRemainingChanged);
            freeSpinsManager.OnSpinStarted.AddListener(HandleSpinStateChanged);
            freeSpinsManager.OnSpinCompleted.AddListener(HandleSpinStateChanged);
            freeSpinsManager.OnModeEnded.AddListener(HandleSpinStateChanged);
        }
    }

    private void OnDisable()
    {
        if (spinButton != null)
            spinButton.onClick.RemoveListener(HandleSpinButtonClicked);

        if (freeSpinsManager != null)
        {
            freeSpinsManager.OnModeStarted.RemoveListener(HandleSpinsRemainingChanged);
            freeSpinsManager.OnSpinsRemainingChanged.RemoveListener(HandleSpinsRemainingChanged);
            freeSpinsManager.OnSpinStarted.RemoveListener(HandleSpinStateChanged);
            freeSpinsManager.OnSpinCompleted.RemoveListener(HandleSpinStateChanged);
            freeSpinsManager.OnModeEnded.RemoveListener(HandleSpinStateChanged);
        }
    }

    private void HandleSpinButtonClicked() => freeSpinsManager?.SpinButtonPressed();

    // OnModeStarted and OnSpinsRemainingChanged are both UnityEvent<int> carrying the same
    // "this many spins left" payload, so one handler covers both.
    private void HandleSpinsRemainingChanged(int spinsRemaining)
    {
        if (spinsRemainingLabel != null)
            spinsRemainingLabel.text = string.Format(spinsRemainingFormat, spinsRemaining);

        UpdateInteractable();
    }

    private void HandleSpinStateChanged() => UpdateInteractable();

    private void UpdateInteractable()
    {
        if (spinButton == null || freeSpinsManager == null) return;
        spinButton.interactable = freeSpinsManager.IsActive
            && !freeSpinsManager.IsSpinning
            && freeSpinsManager.SpinsRemaining > 0;
    }
}
