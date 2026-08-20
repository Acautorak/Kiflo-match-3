using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Forwards a uGUI pointer-down on the cookie Image straight to CookieSmashManager - kept as its
/// own tiny component (rather than tap logic living directly on the Image's GameObject inline)
/// so the cookie prefab/hierarchy stays simple: one Image + this + a Collider-free UI click.
/// Requires an EventSystem in the scene (standard for any uGUI project) and the cookie's Image to
/// have Raycast Target enabled.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class CookieTapTarget : MonoBehaviour, IPointerDownHandler
{
    public void OnPointerDown(PointerEventData eventData)
    {
        if (CookieSmashManager.Instance != null) CookieSmashManager.Instance.RegisterTap();
    }
}
