using System;
using UnityEngine;

/// <summary>
/// Designer-facing asset: per lock behavior, an overlay sprite for each remaining layer
/// count (index 0 = 1 layer left, index 1 = 2 layers left, etc). If a tile has more layers
/// than sprites provided, the last sprite in the array is reused. Create via
/// Assets > Create > Match3 > Lock Visual Config.
/// </summary>
[CreateAssetMenu(fileName = "LockVisualConfig", menuName = "Match3/Lock Visual Config")]
public class LockVisualConfig : ScriptableObject
{
    [Serializable]
    public class LockVisual
    {
        public LockBehavior behavior;
        [Tooltip("Index 0 = overlay shown with 1 layer remaining, index 1 = 2 layers, etc.")]
        public Sprite[] overlayByLayerCount;
    }

    public LockVisual[] visuals;

    public Sprite GetOverlaySprite(LockBehavior behavior, int layersRemaining)
    {
        if (layersRemaining <= 0) return null;

        foreach (var v in visuals)
        {
            if (v.behavior != behavior) continue;
            if (v.overlayByLayerCount == null || v.overlayByLayerCount.Length == 0) return null;

            int index = Mathf.Clamp(layersRemaining - 1, 0, v.overlayByLayerCount.Length - 1);
            return v.overlayByLayerCount[index];
        }

        return null;
    }
}
