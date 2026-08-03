using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Dumb display piece for a single popup - knows nothing about queues or GameManager, just how
/// to show a PopupRequest's content and report back when it's closed. PopupManager is the only
/// thing that should call Show().
///
/// Wires a single static button already placed in the scene (e.g. under ButtonParent) rather
/// than instantiating one from a prefab - popups only ever have one button.
/// </summary>
public class PopupView : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private PopupButtonView button;

    private Action onClosedCallback;

    private void Awake()
    {
        if (root != null) root.SetActive(false);
    }

    public void Show(PopupRequest request, Action onClosed)
    {
        onClosedCallback = onClosed;

        if (titleText != null) titleText.text = request.Title ?? string.Empty;
        if (bodyText != null) bodyText.text = request.Body ?? string.Empty;

        var option = request.Button ?? new PopupButtonOption();

        if (button == null)
            Debug.LogWarning("[PopupView] No button assigned - can't wire up the popup's button.");
        else
            button.Setup(option.Label, () => HandleButtonClicked(option));

        if (root != null) root.SetActive(true);
    }

    private void HandleButtonClicked(PopupButtonOption option)
    {
        // Close BEFORE invoking OnClick: OnClick often triggers further game-flow logic that may
        // itself need to call GameManager.SetState, which gets silently blocked while GameManager
        // still thinks this popup is open. Closing first means we're fully out of Popup state
        // before OnClick runs.
        if (option.ClosesPopup)
        {
            Close();
            option.OnClick?.Invoke();
        }
        else
        {
            option.OnClick?.Invoke();
        }
    }

    private void Close()
    {
        if (root != null) root.SetActive(false);
        var callback = onClosedCallback;
        onClosedCallback = null;
        callback?.Invoke();
    }
}
