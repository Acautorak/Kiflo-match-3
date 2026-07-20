using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Global, static, type-based pub/sub event bus.
/// Any system can Subscribe&lt;T&gt; / Publish&lt;T&gt; without referencing each other directly.
/// T is typically a small readonly struct (see GameEvents.cs).
/// </summary>
public static class EventBus
{
    private static readonly Dictionary<Type, Delegate> handlers = new Dictionary<Type, Delegate>();

    public static void Subscribe<T>(Action<T> listener)
    {
        var type = typeof(T);
        handlers[type] = handlers.TryGetValue(type, out var existing)
            ? Delegate.Combine(existing, listener)
            : listener;
    }

    public static void Unsubscribe<T>(Action<T> listener)
    {
        var type = typeof(T);
        if (!handlers.TryGetValue(type, out var existing)) return;

        var result = Delegate.Remove(existing, listener);
        if (result == null) handlers.Remove(type);
        else handlers[type] = result;
    }

    public static void Publish<T>(T evt)
    {
        if (!handlers.TryGetValue(typeof(T), out var existing)) return;
        if (existing is not Action<T> action) return;
 
        foreach (var del in action.GetInvocationList())
        {
            try
            {
                ((Action<T>)del).Invoke(evt);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    /// <summary>Call on scene/domain reload if you need a clean slate (e.g. from a bootstrap script).</summary>
    public static void ClearAll() => handlers.Clear();
}
