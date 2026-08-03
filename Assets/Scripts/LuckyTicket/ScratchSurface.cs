using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drag-to-reveal foil for one scratch panel. Generates its own Texture2D at Awake (foilColor,
/// alpha = fully opaque), then clears alpha in a soft circular stamp wherever the pointer drags
/// across it, via Unity UI's IPointerDownHandler/IDragHandler (not Physics2D.OverlapPointAll like
/// KebabKarnageManager's tap detection - that approach exists there because falling asteroids are
/// world-space colliders that can overlap each other; this is a plain UI element, so the standard
/// EventSystem drag interfaces are simpler and already handle canvas scaling/camera correctly).
///
/// Percentage-scratched is tracked on a separate, much coarser boolean grid (revealGridSize x
/// revealGridSize, e.g. 12x12) rather than by counting actual texture pixels every frame - a
/// finger stroke only needs to trip a handful of coarse cells to count as "this area is done",
/// and checking 144 bools is far cheaper than scanning a 128x128 texture on every drag event.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ScratchSurface : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("Foil")]
    [Tooltip("The RawImage showing the scratchable foil texture - assign one on this GameObject or a child. Auto-found if left empty.")]
    [SerializeField] private RawImage foilImage;
    [Tooltip("Foil color - alpha is what gets scratched away, RGB is just the foil's look (e.g. silver/gold).")]
    [SerializeField] private Color foilColor = new Color(0.75f, 0.75f, 0.78f, 1f);
    [Tooltip("Resolution of the generated scratch texture - higher = smoother stamps but more per-drag work. 96-160 is plenty for a phone-sized ticket panel.")]
    [Range(32, 256)]
    [SerializeField] private int textureResolution = 128;

    [Header("Brush")]
    [Tooltip("Radius of each scratch stamp, in texture pixels.")]
    [SerializeField] private int brushRadius = 10;

    [Header("Reveal")]
    [Tooltip("Fraction of the coarse reveal grid that must be scratched before the panel auto-completes and locks in.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float revealThreshold = 0.55f;
    [Tooltip("Side length of the coarse boolean grid used to track percentage scratched - independent of textureResolution, kept low for performance.")]
    [Range(4, 32)]
    [SerializeField] private int revealGridSize = 12;

    /// <summary>Fired exactly once - either the moment enough of the surface has been scratched,
    /// or when RevealFully() is called directly (skip button / mode timeout).</summary>
    public event Action OnFullyRevealed;

    public bool IsRevealed { get; private set; }
    /// <summary>0-1 fraction of the coarse reveal grid scratched so far.</summary>
    public float RevealedFraction { get; private set; }

    private Texture2D scratchTexture;
    private Color32[] pixels;
    private bool[] revealGrid;
    private int revealedCellCount;
    private RectTransform rectTransform;
    private bool inputLocked;

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        if (foilImage == null) foilImage = GetComponentInChildren<RawImage>();

        // Unity UI renders children in sibling order - later siblings draw on top. If the
        // reward icon/text happen to sit later in the hierarchy than the foil, they'd render
        // over it and the foil would never be visible even though its texture is scratching
        // correctly underneath. Forcing this to the last sibling makes the foil always render
        // on top regardless of how a given panel prefab's hierarchy was authored.
        if (foilImage != null) foilImage.transform.SetAsLastSibling();

        BuildTexture();
    }

    private void BuildTexture()
    {
        scratchTexture = new Texture2D(textureResolution, textureResolution, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        pixels = new Color32[textureResolution * textureResolution];
        Color32 opaqueFoil = foilColor;
        for (int i = 0; i < pixels.Length; i++) pixels[i] = opaqueFoil;
        scratchTexture.SetPixels32(pixels);
        scratchTexture.Apply(false);

        revealGrid = new bool[revealGridSize * revealGridSize];
        revealedCellCount = 0;
        IsRevealed = false;
        inputLocked = false;
        RevealedFraction = 0f;

        if (foilImage != null) foilImage.texture = scratchTexture;
    }

    /// <summary>Call before reusing a pooled/instantiated panel for a new ticket - resets the
    /// foil back to fully opaque and unlocks input.</summary>
    public void ResetSurface() => BuildTexture();

    public void OnPointerDown(PointerEventData eventData) => TryScratchAt(eventData);
    public void OnDrag(PointerEventData eventData) => TryScratchAt(eventData);
    public void OnPointerUp(PointerEventData eventData) { }

    private void TryScratchAt(PointerEventData eventData)
    {
        if (inputLocked || IsRevealed) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out var localPoint))
            return;

        Rect rect = rectTransform.rect;
        float u = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float v = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);
        if (u < 0f || u > 1f || v < 0f || v > 1f) return; // drag moved outside this panel's bounds

        int px = Mathf.Clamp(Mathf.RoundToInt(u * (textureResolution - 1)), 0, textureResolution - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(v * (textureResolution - 1)), 0, textureResolution - 1);
        StampBrush(px, py);
        MarkRevealGridCell(u, v);

        RevealedFraction = revealedCellCount / (float)revealGrid.Length;
        if (RevealedFraction >= revealThreshold) RevealFully();
    }

    /// <summary>Clears alpha to 0 in a square bounding box around (cx, cy) and re-uploads only
    /// that box - a typical finger stroke only touches a small area per frame, so there's no
    /// need to touch the whole texture on every drag event.</summary>
    private void StampBrush(int cx, int cy)
    {
        int minX = Mathf.Clamp(cx - brushRadius, 0, textureResolution - 1);
        int maxX = Mathf.Clamp(cx + brushRadius, 0, textureResolution - 1);
        int minY = Mathf.Clamp(cy - brushRadius, 0, textureResolution - 1);
        int maxY = Mathf.Clamp(cy + brushRadius, 0, textureResolution - 1);
        int radiusSqr = brushRadius * brushRadius;

        for (int y = minY; y <= maxY; y++)
        {
            int rowOffset = y * textureResolution;
            int dy = y - cy;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - cx;
                if (dx * dx + dy * dy > radiusSqr) continue;
                Color32 c = pixels[rowOffset + x];
                c.a = 0;
                pixels[rowOffset + x] = c;
            }
        }

        scratchTexture.SetPixels32(pixels);
        scratchTexture.Apply(false);
    }

    private void MarkRevealGridCell(float u, float v)
    {
        int gx = Mathf.Clamp(Mathf.FloorToInt(u * revealGridSize), 0, revealGridSize - 1);
        int gy = Mathf.Clamp(Mathf.FloorToInt(v * revealGridSize), 0, revealGridSize - 1);
        int index = gy * revealGridSize + gx;
        if (revealGrid[index]) return;
        revealGrid[index] = true;
        revealedCellCount++;
    }

    /// <summary>Instantly clears the whole foil and locks the panel - used internally once the
    /// threshold is crossed, and externally by a "reveal all" / skip button or a mode timeout
    /// (see LuckyScratchTicketManager.RevealAllRemaining).</summary>
    public void RevealFully()
    {
        if (IsRevealed) return;
        IsRevealed = true;
        inputLocked = true;
        RevealedFraction = 1f;

        for (int i = 0; i < pixels.Length; i++)
        {
            Color32 c = pixels[i];
            c.a = 0;
            pixels[i] = c;
        }
        scratchTexture.SetPixels32(pixels);
        scratchTexture.Apply(false);

        OnFullyRevealed?.Invoke();
    }

    private void OnDestroy()
    {
        if (scratchTexture != null) Destroy(scratchTexture);
    }
}
