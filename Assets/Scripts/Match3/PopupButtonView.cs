using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One button in a PopupView - just wires a label and a click callback. PopupView
/// instantiates one of these per PopupButtonOption.</summary>
public class PopupButtonView : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;

    public void Setup(string text, Action onClick)
    {
        if (label != null) label.text = text;

        if (button == null)
        {
            Debug.LogWarning($"[PopupButtonView] No Button assigned on '{gameObject.name}'.");
            return;
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(() => onClick?.Invoke());
    }
}
