using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placeholder Chain Lightning visual - no art/shader dependency. Builds a jittered LineRenderer
/// bolt at runtime using Unity's built-in "Sprites/Default" shader, which ships with every Unity
/// project, so this works out of the box with zero asset setup.
///
/// Drop this component on any always-loaded object in the scene (e.g. next to Board) and it just
/// works: it listens for ChainLightningArcEvent over EventBus and draws one bolt per hop. Nothing
/// else in the codebase references this class directly - when real VFX/a shader are ready, delete
/// or disable this component and hook the same event from the new system instead. No changes
/// needed anywhere else.
/// </summary>
public class ChainLightningVisualizer : MonoBehaviour
{
    [Header("Look")]
    [SerializeField] private Color boltColor = new Color(0.65f, 0.85f, 1f, 1f);
    [Min(0f)] [SerializeField] private float boltWidth = 0.06f;
    [Min(0f)] [SerializeField] private float boltLifetime = 0.15f;
    [Tooltip("How many extra jagged midpoints the bolt gets between its two endpoints. 0 = a straight line.")]
    [Min(0)] [SerializeField] private int jaggedSegments = 4;
    [Tooltip("Max random perpendicular offset applied to each jagged midpoint, in world units.")]
    [Min(0f)] [SerializeField] private float jaggedness = 0.15f;

    [Header("Sorting")]
    [Tooltip("Sorting layer the bolt renders on. Set this to whatever layer your symbol sprites " +
             "use (or one above it) so the bolt draws on top of them instead of behind.")]
    [SortingLayer] [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Order-in-layer within the sorting layer above. Higher draws on top of lower - set " +
             "this above your symbols' own order-in-layer if they share a sorting layer.")]
    [SerializeField] private int sortingOrder = 100;

    private Material sharedMaterial;

    private void Awake()
    {
        // Built-in shader - no material/shader asset needs to exist in the project for this to work.
        sharedMaterial = new Material(Shader.Find("Sprites/Default"));
    }

    private void OnEnable() => EventBus.Subscribe<ChainLightningArcEvent>(OnArc);
    private void OnDisable() => EventBus.Unsubscribe<ChainLightningArcEvent>(OnArc);

    private void OnArc(ChainLightningArcEvent evt) => StartCoroutine(DrawBolt(evt.FromWorld, evt.ToWorld));

    private IEnumerator DrawBolt(Vector3 from, Vector3 to)
    {
        var go = new GameObject("ChainLightningBolt (placeholder)");
        go.transform.SetParent(transform, worldPositionStays: true);

        var lr = go.AddComponent<LineRenderer>();
        lr.material = sharedMaterial;
        lr.startColor = lr.endColor = boltColor;
        lr.startWidth = lr.endWidth = boltWidth;
        lr.useWorldSpace = true;
        lr.numCapVertices = 2;
        lr.sortingLayerName = sortingLayerName;
        lr.sortingOrder = sortingOrder;

        var points = new List<Vector3> { from };
        var direction = (to - from);
        var perpendicular = direction.sqrMagnitude > 0.0001f
            ? Vector3.Cross(direction.normalized, Vector3.forward)
            : Vector3.up;

        for (int i = 1; i <= jaggedSegments; i++)
        {
            float t = i / (float)(jaggedSegments + 1);
            var point = Vector3.Lerp(from, to, t) + perpendicular * Random.Range(-jaggedness, jaggedness);
            points.Add(point);
        }
        points.Add(to);

        lr.positionCount = points.Count;
        lr.SetPositions(points.ToArray());

        yield return new WaitForSeconds(boltLifetime);
        Destroy(go);
    }
}
