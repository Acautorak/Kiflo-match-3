using System;
using UnityEngine;

/// <summary>
/// Minimal companion popup for LuckyScratchTicketManager - same role as your existing
/// KebabKarnageIntroPopup (show an announcement banner, then invoke onDismissed once the player
/// taps through). This stub already satisfies the Show(Action) contract
/// LuckyScratchTicketManager expects, so the feature compiles and works end-to-end immediately;
/// replace the body with your real animation/tap-to-continue whenever you're ready, or just
/// delete this file and repoint the manager's Intro Popup field at your existing
/// KebabKarnageIntroPopup if you'd rather share one class between features.
/// </summary>
public class LuckyScratchIntroPopup : MonoBehaviour
{
    [SerializeField] private GameObject visualRoot;

    public void Show(Action onDismissed)
    {
        if (visualRoot != null) visualRoot.SetActive(true);
        // TODO: wire this up to your real "ticket incoming!" banner / tap-to-continue button.
        // For now it dismisses immediately so the feature isn't blocked while this is unstyled -
        // call Dismiss(onDismissed) from a button's onClick instead, once you have one.
        onDismissed?.Invoke();
    }

    public void Dismiss(Action onDismissed)
    {
        if (visualRoot != null) visualRoot.SetActive(false);
        onDismissed?.Invoke();
    }
}
