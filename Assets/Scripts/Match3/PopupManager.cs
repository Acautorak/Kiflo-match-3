using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns a FIFO queue of popup requests and drives GameManager into/out of its Popup state while
/// any are showing. Anything that wants to show a popup - tutorial steps, StageManager's powerup
/// choice, confirmation dialogs, error messages - calls PopupManager.Show(request) and doesn't
/// need to know or care about GameManager at all.
/// </summary>
public class PopupManager : MonoBehaviour
{
    public static PopupManager Instance { get; private set; }

    [SerializeField] private GameManager gameManager;
    [SerializeField] private PopupView popupView;

    private readonly Queue<PopupRequest> queue = new Queue<PopupRequest>();
    private PopupRequest current;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[PopupManager] Duplicate PopupManager on '{gameObject.name}' - destroying it. " +
                              "Only one should exist in the scene.");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    /// <summary>Enqueues a popup. If nothing is currently showing, displays it immediately;
    /// otherwise it waits its turn behind whatever's already queued.</summary>
    public void Show(PopupRequest request)
    {
        if (request == null)
        {
            Debug.LogWarning("[PopupManager] Ignored a null PopupRequest.");
            return;
        }

        queue.Enqueue(request);
        if (current == null) ShowNext();
    }

    /// <summary>True if a popup is currently displayed or queued.</summary>
    public bool HasActivePopup => current != null;

    private void ShowNext()
    {
        if (queue.Count == 0)
        {
            current = null;
            if (gameManager != null && gameManager.IsPopupActive) gameManager.ExitPopup();
            return;
        }

        current = queue.Dequeue();

        if (current.PausesGame && gameManager != null) gameManager.EnterPopup();

        if (popupView == null)
        {
            Debug.LogWarning("[PopupManager] No PopupView assigned - skipping display but still " +
                              "running the request's OnClosed callback so callers aren't stuck waiting.");
            HandleClosed();
            return;
        }

        popupView.Show(current, HandleClosed);
    }

    private void HandleClosed()
    {
        var closed = current;
        current = null;
        closed?.OnClosed?.Invoke();
        ShowNext();
    }
}
