using System;

/// <summary>
/// Plain data describing a single popup to show - tutorial step, confirmation dialog, error
/// message, etc. Queue these up via PopupManager.Show(request); PopupManager and PopupView
/// handle everything about actually displaying and dismissing it.
///
/// Popups have exactly one button (e.g. "Continue"/"OK") - this isn't meant for multi-choice UI
/// like the powerup picker, which has its own dedicated PowerupSelectionUI outside this system.
/// </summary>
public class PopupRequest
{
    public string Title;
    public string Body;

    /// <summary>Leave null for a default "OK" button that just closes the popup.</summary>
    public PopupButtonOption Button;

    /// <summary>
    /// Whether showing this popup should call GameManager.EnterPopup() (blocking board input and
    /// pausing gameplay flow). Almost always true - set false only for a non-blocking toast-style
    /// popup that shouldn't interrupt anything.
    /// </summary>
    public bool PausesGame = true;

    /// <summary>Invoked once the popup has fully closed (after the button's own OnClick).</summary>
    public Action OnClosed;
}

public class PopupButtonOption
{
    public string Label = "OK";

    /// <summary>Invoked when the button is pressed, before the popup closes.</summary>
    public Action OnClick;

    /// <summary>Set false for a button that should trigger OnClick but leave the popup open.</summary>
    public bool ClosesPopup = true;
}
