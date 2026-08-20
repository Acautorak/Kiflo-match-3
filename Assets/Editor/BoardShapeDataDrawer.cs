using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for BoardShapeData: paints the mask as a clickable/drag-paintable grid of
/// cells instead of hand-counting flat array indices. Applies automatically anywhere
/// BoardShapeData is used as a field - StageDefinition.shape, ShapeTemplate.shape - since
/// PropertyDrawers attach to the TYPE, not a specific field. No per-field wiring needed.
///
/// MUST live in a folder literally named "Editor" (Unity convention for editor-only scripts,
/// e.g. Assets/Editor/BoardShapeDataDrawer.cs) or it will try to compile into player builds and
/// fail - UnityEditor isn't available outside the Editor.
///
/// Grid convention: y=0 is drawn at the BOTTOM row, matching GridModel/GravityController (where
/// gravity pulls toward y=0) - so what you paint here visually matches how it plays in-game.
/// Click a cell to toggle it; click-and-drag paints a run of cells to whatever the first cell in
/// the drag became (standard pixel-art-tool behavior).
/// </summary>
[CustomPropertyDrawer(typeof(BoardShapeData))]
public class BoardShapeDataDrawer : PropertyDrawer
{
    private const float MinCellSize = 10f;
    private const float MaxCellSize = 26f;
    private const float CellSpacing = 2f;

    // PropertyDrawer instances get reused across multiple fields/list elements, so per-field
    // state (foldout expanded? mid-drag?) has to be keyed by propertyPath rather than stored in
    // a plain instance field - otherwise every BoardShapeData field would share one state.
    private static readonly Dictionary<string, bool> ExpandedState = new Dictionary<string, bool>();
    private static readonly Dictionary<string, bool> DragPaintValue = new Dictionary<string, bool>();

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float height = EditorGUIUtility.singleLineHeight + 2f; // foldout line
        if (!IsExpanded(property)) return height;

        var widthProp = property.FindPropertyRelative("width");
        var heightProp = property.FindPropertyRelative("height");
        var maskProp = property.FindPropertyRelative("mask");

        height += EditorGUIUtility.singleLineHeight + 2f; // width/height fields
        height += EditorGUIUtility.singleLineHeight + 4f;  // resize-warning row OR toolbar row

        int w = Mathf.Max(0, widthProp.intValue);
        int h = Mathf.Max(0, heightProp.intValue);
        bool sizeMismatch = maskProp.arraySize != w * h;

        if (!sizeMismatch && w > 0 && h > 0)
            height += h * (GetCellSize(w) + CellSpacing) + 6f;

        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var widthProp = property.FindPropertyRelative("width");
        var heightProp = property.FindPropertyRelative("height");
        var maskProp = property.FindPropertyRelative("mask");

        float lineHeight = EditorGUIUtility.singleLineHeight;
        Rect line = new Rect(position.x, position.y, position.width, lineHeight);

        int activeCount = CountActive(maskProp);
        string summary = maskProp.arraySize == 0
            ? "empty - full rectangle"
            : $"{widthProp.intValue}x{heightProp.intValue}, {activeCount}/{maskProp.arraySize} active";

        bool expanded = EditorGUI.Foldout(line, IsExpanded(property), $"{label.text}  ({summary})", true);
        SetExpanded(property, expanded);
        if (!expanded) return;

        EditorGUI.indentLevel++;
        line.y += lineHeight + 2f;

        // Editing width/height here does NOT resize the mask by itself - resizing is destructive
        // to whatever's already painted, so it's a separate, explicit "Resize Mask" action below
        // rather than something that fires on every keystroke.
        float half = (line.width - 4f) / 2f;
        EditorGUI.PropertyField(new Rect(line.x, line.y, half, lineHeight), widthProp, new GUIContent("Width"));
        EditorGUI.PropertyField(new Rect(line.x + half + 4f, line.y, half, lineHeight), heightProp, new GUIContent("Height"));
        line.y += lineHeight + 2f;

        int w = Mathf.Max(0, widthProp.intValue);
        int h = Mathf.Max(0, heightProp.intValue);
        bool sizeMismatch = maskProp.arraySize != w * h;

        if (sizeMismatch)
        {
            const float buttonWidth = 140f;
            var warnRect = new Rect(line.x, line.y, line.width - buttonWidth - 4f, lineHeight);
            var resizeRect = new Rect(line.x + line.width - buttonWidth, line.y, buttonWidth, lineHeight);

            EditorGUI.LabelField(warnRect, $"Mask doesn't match {w}x{h} yet", EditorStyles.miniLabel);
            if (GUI.Button(resizeRect, "Resize Mask") && w > 0 && h > 0)
                ResizeMask(maskProp, w, h);

            EditorGUI.indentLevel--;
            return; // nothing sensible to paint until the mask actually matches width*height
        }

        // Whole-mask tools.
        float toolWidth = (line.width - 8f) / 3f;
        if (GUI.Button(new Rect(line.x, line.y, toolWidth, lineHeight), "Fill All")) SetAll(maskProp, true);
        if (GUI.Button(new Rect(line.x + toolWidth + 4f, line.y, toolWidth, lineHeight), "Clear All")) SetAll(maskProp, false);
        if (GUI.Button(new Rect(line.x + (toolWidth + 4f) * 2f, line.y, toolWidth, lineHeight), "Invert")) InvertAll(maskProp);
        line.y += lineHeight + 4f;

        if (w > 0 && h > 0)
            DrawGrid(new Rect(line.x, line.y, line.width, position.yMax - line.y), property, maskProp, w, h);

        EditorGUI.indentLevel--;
    }

    private void DrawGrid(Rect area, SerializedProperty property, SerializedProperty maskProp, int width, int height)
    {
        float cellSize = GetCellSize(width);
        string dragKey = property.propertyPath;
        Event evt = Event.current;

        for (int y = 0; y < height; y++)
        {
            float rowY = area.y + (height - 1 - y) * (cellSize + CellSpacing); // y=0 drawn at the bottom

            for (int x = 0; x < width; x++)
            {
                int index = x * height + y; // matches BoardShapeData.ToMask2D's row-major layout
                if (index >= maskProp.arraySize) continue;

                var cellRect = new Rect(area.x + x * (cellSize + CellSpacing), rowY, cellSize, cellSize);
                var elementProp = maskProp.GetArrayElementAtIndex(index);
                bool active = elementProp.boolValue;

                // 1px dark border via a slightly larger backing rect, then the fill inset by 1 -
                // avoids UnityEditor.Handles, which doesn't reliably render inside Inspector GUI.
                EditorGUI.DrawRect(cellRect, new Color(0f, 0f, 0f, 0.6f));
                EditorGUI.DrawRect(new Rect(cellRect.x + 1, cellRect.y + 1, cellRect.width - 2, cellRect.height - 2),
                    active ? new Color(0.35f, 0.75f, 0.4f) : new Color(0.2f, 0.2f, 0.2f));

                if (!cellRect.Contains(evt.mousePosition)) continue;

                if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    bool paintValue = !active;
                    DragPaintValue[dragKey] = paintValue;
                    elementProp.boolValue = paintValue;
                    evt.Use();
                    GUI.changed = true;
                }
                else if (evt.type == EventType.MouseDrag && evt.button == 0 && DragPaintValue.TryGetValue(dragKey, out bool paintVal))
                {
                    elementProp.boolValue = paintVal;
                    evt.Use();
                    GUI.changed = true;
                }
            }
        }

        if (evt.type == EventType.MouseUp) DragPaintValue.Remove(dragKey);
        if (DragPaintValue.ContainsKey(dragKey)) HandleUtility.Repaint(); // live update while dragging, not just on release
    }

    private static float GetCellSize(int width)
    {
        if (width <= 0) return MaxCellSize;
        // Shrinks cells for wide boards so the grid still fits the Inspector; grows them a bit
        // for small boards so painting isn't fiddly.
        float available = EditorGUIUtility.currentViewWidth - 60f;
        return Mathf.Clamp(available / width - CellSpacing, MinCellSize, MaxCellSize);
    }

    private static int CountActive(SerializedProperty maskProp)
    {
        int count = 0;
        for (int i = 0; i < maskProp.arraySize; i++)
            if (maskProp.GetArrayElementAtIndex(i).boolValue) count++;
        return count;
    }

    private static void SetAll(SerializedProperty maskProp, bool value)
    {
        for (int i = 0; i < maskProp.arraySize; i++)
            maskProp.GetArrayElementAtIndex(i).boolValue = value;
    }

    private static void InvertAll(SerializedProperty maskProp)
    {
        for (int i = 0; i < maskProp.arraySize; i++)
        {
            var element = maskProp.GetArrayElementAtIndex(i);
            element.boolValue = !element.boolValue;
        }
    }

    /// <summary>
    /// Resizes the mask array to newWidth * newHeight. Preserves existing values by flat index
    /// up to the overlap and defaults any newly-added cells to active (true) - this is NOT a
    /// spatial/centered resize (unlike ProceduralStageGenerator.FitMaskToSize) since the drawer
    /// has no reliable record of what the OLD width/height were once they've already been
    /// overwritten in the fields above. In practice this only matters if you resize AFTER
    /// painting - resize first, then paint, to avoid the pattern shifting oddly.
    /// </summary>
    private static void ResizeMask(SerializedProperty maskProp, int newWidth, int newHeight)
    {
        int oldSize = maskProp.arraySize;
        var oldValues = new bool[oldSize];
        for (int i = 0; i < oldSize; i++)
            oldValues[i] = maskProp.GetArrayElementAtIndex(i).boolValue;

        maskProp.arraySize = newWidth * newHeight;
        for (int i = 0; i < maskProp.arraySize; i++)
            maskProp.GetArrayElementAtIndex(i).boolValue = i < oldSize ? oldValues[i] : true;
    }

    private static bool IsExpanded(SerializedProperty property) =>
        ExpandedState.TryGetValue(property.propertyPath, out bool v) && v;

    private static void SetExpanded(SerializedProperty property, bool value) =>
        ExpandedState[property.propertyPath] = value;
}
