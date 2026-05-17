// ============================================================
//  EventBus.cs  —  Out of Bullet
//  Decoupled publish/subscribe system.
//  Add new GameEvent types in GameEvents.cs — never here.
// ============================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace OutOfBullet.Core
{
    public static class EventBus
    {
        // Key = event Type, Value = list of subscriber delegates
        private static readonly Dictionary<Type, List<Delegate>> _subscribers
            = new Dictionary<Type, List<Delegate>>();

        // ── Subscribe ────────────────────────────────────────────
        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            var key = typeof(T);
            if (!_subscribers.ContainsKey(key))
                _subscribers[key] = new List<Delegate>();

            _subscribers[key].Add(handler);
        }

        // ── Unsubscribe ──────────────────────────────────────────
        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var key = typeof(T);
            if (_subscribers.TryGetValue(key, out var list))
                list.Remove(handler);
        }

        // ── Publish ──────────────────────────────────────────────
        public static void Publish<T>(T evt) where T : struct
        {
            var key = typeof(T);
            if (!_subscribers.TryGetValue(key, out var list)) return;

            // Iterate a copy — handlers may unsubscribe during dispatch
            var snapshot = list.ToArray();
            foreach (var d in snapshot)
            {
                try { ((Action<T>)d)(evt); }
                catch (Exception ex)
                {
                    Debug.LogError($"[EventBus] Exception in handler for {key.Name}: {ex}");
                }
            }
        }

        // ── Debug Utility ────────────────────────────────────────
        public static void LogAllSubscribers()
        {
            foreach (var kvp in _subscribers)
                Debug.Log($"[EventBus] {kvp.Key.Name} → {kvp.Value.Count} subscriber(s)");
        }

        // ── Teardown (call on scene unload) ──────────────────────
        public static void ClearAll() => _subscribers.Clear();
    }
}
