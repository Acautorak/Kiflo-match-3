using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Draws a [SortingLayer] string field as a popup of the project's actual sorting layers (Edit >
/// Project Settings > Tags and Layers > Sorting Layers), matching how SpriteRenderer/LineRenderer
/// show their own "Sorting Layer" field - instead of a free-text box that silently falls back to
/// Default on a typo. Must live in a folder named "Editor" anywhere under Assets so Unity excludes
/// it from player builds (UnityEditor/UnityEditorInternal aren't available at runtime).
/// </summary>
[CustomPropertyDrawer(typeof(SortingLayerAttribute))]
public class SortingLayerAttributeDrawer : PropertyDrawer
{
    /// <summary>
    /// InternalEditorUtility.sortingLayerNames is declared `internal`, not `public` - it's not
    /// actually visible outside Unity's own assemblies despite the class name suggesting it's a
    /// public utility, so referencing it directly (InternalEditorUtility.sortingLayerNames) fails
    /// to compile with CS0117. Reflection is the standard workaround (same trick every editor
    /// sorting-layer-dropdown implementation out there uses) since Unity has never shipped a
    /// public API for "give me every sorting layer name".
    /// </summary>
    private static string[] GetSortingLayerNames()
    {
        var utilityType = typeof(InternalEditorUtility);
        var property = utilityType.GetProperty("sortingLayerNames", BindingFlags.Static | BindingFlags.NonPublic);
        return (string[])property.GetValue(null, null);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        if (property.propertyType != SerializedPropertyType.String)
        {
            EditorGUI.HelpBox(position, $"{property.displayName}: [SortingLayer] only works on a string field.", MessageType.Error);
            return;
        }

        var layerNames = GetSortingLayerNames();
        int currentIndex = System.Array.IndexOf(layerNames, property.stringValue);
        if (currentIndex < 0) currentIndex = 0; // unknown/empty value (e.g. a renamed or deleted layer) - default to the first entry

        EditorGUI.BeginProperty(position, label, property);
        int newIndex = EditorGUI.Popup(position, label.text, currentIndex, layerNames);
        if (newIndex != currentIndex || currentIndex != System.Array.IndexOf(layerNames, property.stringValue))
            property.stringValue = layerNames[newIndex];
        EditorGUI.EndProperty();
    }
}
