using UnityEngine;

/// <summary>
/// Put on a `string` field to make it show as a sorting-layer dropdown in the Inspector, the same
/// way Unity's own SpriteRenderer/LineRenderer "Sorting Layer" field works, instead of a plain
/// text box you have to type a name into by hand. The field still just stores the layer's name as
/// a string underneath - see SortingLayerAttributeDrawer for the actual dropdown rendering.
/// Runtime-safe (no UnityEditor reference here), so this lives outside any Editor folder.
/// </summary>
public class SortingLayerAttribute : PropertyAttribute
{
}
