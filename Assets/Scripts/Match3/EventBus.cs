using System;
using System.Collections.Generic;

namespace Match3.Events
{
    /// <summary>
    /// Marker interface for anything that can travel through the EventBus.
    /// Using structs implementing IEvent keeps events allocation-light.
    /// </summary>
    public interface IEvent { }

    /// <summary>
    /// Static, type-safe publish/subscribe event bus. Any system (UI, audio,
    /// analytics, save system, board logic) can subscribe to an event type
    /// without knowing who publishes it, and publish without knowing who's
    /// listening. This is what keeps match logic, rendering, and persistence
    /// from directly referencing one another.
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> _subscribers = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> handler) where T : IEvent
        {
            var type = typeof(T);
            if (_subscribers.TryGetValue(type, out var existing))
                _subscribers[type] = Delegate.Combine(existing, handler);
            else
                _subscribers[type] = handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : IEvent
        {
            var type = typeof(T);
            if (!_subscribers.TryGetValue(type, out var existing)) return;

            var result = Delegate.Remove(existing, handler);
            if (result == null)
                _subscribers.Remove(type);
            else
                _subscribers[type] = result;
        }

        public static void Publish<T>(T evt) where T : IEvent
        {
            if (_subscribers.TryGetValue(typeof(T), out var existing) && existing is Action<T> action)
                action.Invoke(evt);
        }

        /// <summary>
        /// Call on scene teardown / domain reload boundaries to avoid stale
        /// subscribers from a previous scene firing into a destroyed object.
        /// </summary>
        public static void Clear() => _subscribers.Clear();
    }
}
