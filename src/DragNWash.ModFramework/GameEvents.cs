using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// The game's events, received once by the framework and handed to each mod
    /// on its own: a handler that throws is logged and shown on the Mods screen
    /// under its mod, and the other mods' handlers still run. Prefer these to
    /// subscribing to <c>SceneManager</c> or <c>Application</c> directly, where
    /// one mod's exception stops every later subscriber of that event. Every
    /// handler is registered with the GUID of the mod it belongs to, so that
    /// mod can be named when it fails, and can be taken out with
    /// <see cref="Remove"/>. There is no per-frame event on purpose: a
    /// MonoBehaviour's Update is the right tool for that.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>True once the title screen has been shown for the first time.</summary>
        public static bool IsGameStarted { get; private set; }

        /// <summary>Called after every scene load, with the scene and how it was loaded.</summary>
        public static void OnSceneLoaded(string ownerGuid, Action<Scene, LoadSceneMode> handler)
        {
            Add(_sceneLoaded, "SceneLoaded", ownerGuid, handler);
            HookScenes();
        }

        /// <summary>Called after every scene unload.</summary>
        public static void OnSceneUnloaded(string ownerGuid, Action<Scene> handler)
        {
            Add(_sceneUnloaded, "SceneUnloaded", ownerGuid, handler);
            HookScenes();
        }

        /// <summary>
        /// Called once, when the title screen is first shown: the game is up and
        /// its systems exist. A handler added after that point is called at once.
        /// </summary>
        public static void OnGameStarted(string ownerGuid, Action handler)
        {
            if (handler == null)
            {
                return;
            }
            if (IsGameStarted)
            {
                Invoke(new Handler("GameStarted", ownerGuid, handler), () => handler());
                return;
            }
            Add(_gameStarted, "GameStarted", ownerGuid, handler);
        }

        /// <summary>Called when the game is quitting.</summary>
        public static void OnQuitting(string ownerGuid, Action handler)
        {
            Add(_quitting, "Quitting", ownerGuid, handler);
            if (!_quitHooked)
            {
                _quitHooked = true;
                Application.quitting += () => Raise(_quitting, h => ((Action)h.Delegate)());
            }
        }

        /// <summary>Takes out every handler <paramref name="ownerGuid"/> registered.</summary>
        public static void Remove(string ownerGuid)
        {
            lock (Lock)
            {
                _sceneLoaded.RemoveAll(h => h.Owner == ownerGuid);
                _sceneUnloaded.RemoveAll(h => h.Owner == ownerGuid);
                _gameStarted.RemoveAll(h => h.Owner == ownerGuid);
                _quitting.RemoveAll(h => h.Owner == ownerGuid);
            }
        }

        // A handler is dropped for good after this many failures in a row: an
        // exception on every scene would otherwise fill the log for the session.
        private const int FailuresBeforeDrop = 3;
        // Slower than this is worth a line in the log (visible in the Console tab).
        private const long SlowMilliseconds = 100;

        private sealed class Handler
        {
            public readonly string Event;
            public readonly string Owner;
            public readonly Delegate Delegate;
            public int Failures;

            public Handler(string evt, string owner, Delegate d)
            {
                Event = evt;
                Owner = owner;
                Delegate = d;
            }
        }

        private static readonly object Lock = new object();
        private static readonly List<Handler> _sceneLoaded = new List<Handler>();
        private static readonly List<Handler> _sceneUnloaded = new List<Handler>();
        private static readonly List<Handler> _gameStarted = new List<Handler>();
        private static readonly List<Handler> _quitting = new List<Handler>();
        private static bool _scenesHooked, _quitHooked;

        private static void Add(List<Handler> list, string evt, string ownerGuid, Delegate handler)
        {
            if (handler == null)
            {
                return;
            }
            if (string.IsNullOrEmpty(ownerGuid))
            {
                throw new ArgumentException("GameEvents handlers are registered with the GUID of the mod they belong to.", nameof(ownerGuid));
            }
            lock (Lock)
            {
                list.Add(new Handler(evt, ownerGuid, handler));
            }
        }

        private static void HookScenes()
        {
            if (_scenesHooked)
            {
                return;
            }
            _scenesHooked = true;
            SceneManager.sceneLoaded += (scene, mode) => Raise(_sceneLoaded, h => ((Action<Scene, LoadSceneMode>)h.Delegate)(scene, mode));
            SceneManager.sceneUnloaded += scene => Raise(_sceneUnloaded, h => ((Action<Scene>)h.Delegate)(scene));
        }

        // From the title screen hook, the first time it runs.
        internal static void RaiseGameStarted()
        {
            if (IsGameStarted)
            {
                return;
            }
            IsGameStarted = true;
            Raise(_gameStarted, h => ((Action)h.Delegate)());
            lock (Lock)
            {
                _gameStarted.Clear();
            }
        }

        private static void Raise(List<Handler> list, Action<Handler> call)
        {
            Handler[] handlers;
            lock (Lock)
            {
                handlers = list.ToArray();
            }
            foreach (Handler handler in handlers)
            {
                if (!Invoke(handler, () => call(handler)))
                {
                    lock (Lock)
                    {
                        list.Remove(handler);
                    }
                }
            }
        }

        // Runs one handler on its own. False when it has failed often enough to be dropped.
        private static bool Invoke(Handler handler, Action call)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                call();
                handler.Failures = 0;
            }
            catch (Exception ex)
            {
                handler.Failures++;
                string what = $"{handler.Event} handler of {handler.Owner}";
                ModFramework.Log.LogError($"{what} threw: {ex}");
                GameHooks.Unavailable(handler.Owner, handler.Event + " handler", ex.GetType().Name + ": " + ex.Message);
                if (handler.Failures >= FailuresBeforeDrop)
                {
                    ModFramework.Log.LogError($"{what} failed {handler.Failures} times in a row and is switched off for this session.");
                    return false;
                }
            }
            finally
            {
                watch.Stop();
                if (watch.ElapsedMilliseconds >= SlowMilliseconds)
                {
                    ModFramework.Log.LogDebug($"{handler.Event} handler of {handler.Owner} took {watch.ElapsedMilliseconds} ms.");
                }
            }
            return true;
        }
    }
}
