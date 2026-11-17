using System;
using System.Collections.Generic;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Runs callbacks from worker threads on the Unity main thread.</summary>
    public static class UnityMainThreadDispatcher
    {
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static bool _initialized = false;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            var go = new GameObject("RAT_MainThreadDispatcher");
            GameObject.DontDestroyOnLoad(go);
            go.AddComponent<MainThreadDispatcherBehaviour>();
        }

        public static void Enqueue(Action action)
        {
            lock (_queue)
            {
                _queue.Enqueue(action);
            }
        }

        public static void Update()
        {
            Action[] pending;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                pending = _queue.ToArray();
                _queue.Clear();
            }

            // Outside the lock, so a callback can enqueue more work.
            foreach (var action in pending)
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    RATMain.LogError($"MainThreadDispatcher error: {ex.Message}");
                }
            }
        }
    }

    public class MainThreadDispatcherBehaviour : MonoBehaviour
    {
        private void Update()
        {
            UnityMainThreadDispatcher.Update();
        }
    }
}
