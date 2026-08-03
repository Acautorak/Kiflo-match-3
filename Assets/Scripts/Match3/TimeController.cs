using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small reusable, stack-based Time.timeScale controller. Any system can Push a slow-mo/pause
/// request and Pop it later without wiping out another system's still-active request - e.g. a
/// popup's slow-mo and a future ability's bullet-time effect can overlap safely, since
/// Time.timeScale is always driven by the most restrictive (lowest) currently active request.
/// </summary>
public static class TimeController
{
    private static readonly Dictionary<int, float> requests = new Dictionary<int, float>();
    private static int nextHandle = 1;

    /// <summary>Requests a time scale. Returns a handle - keep it and pass it to Pop when this
    /// request should end. Time.timeScale becomes the minimum of every currently active request.</summary>
    public static int Push(float scale)
    {
        int handle = nextHandle++;
        requests[handle] = Mathf.Clamp01(scale);
        Apply();
        return handle;
    }

    /// <summary>Ends one specific request (the handle returned by its matching Push call). Safe
    /// to call with an invalid/already-popped handle - it's just a no-op.</summary>
    public static void Pop(int handle)
    {
        if (requests.Remove(handle)) Apply();
    }

    private static void Apply()
    {
        Time.timeScale = requests.Count == 0 ? 1f : Mathf.Min(new List<float>(requests.Values).ToArray());
    }

    /// <summary>Emergency reset - clears every pending request and forces Time.timeScale back to
    /// 1. Useful after a scene reload if anything might have left a stale request behind.</summary>
    public static void ResetAll()
    {
        requests.Clear();
        Time.timeScale = 1f;
    }
}
