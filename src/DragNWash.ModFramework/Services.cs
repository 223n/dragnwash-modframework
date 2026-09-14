using System;
using System.Collections.Generic;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Lets a library offer functionality to other mods without them referencing
    /// its internals. A library registers an implementation of an interface it
    /// publishes; mods ask for the interface.
    /// </summary>
    /// <remarks>
    /// BepInEx loads a mod after the libraries it depends on, so a mod with a
    /// <c>BepInDependency</c> on the library can call <see cref="Get{T}"/> in Awake.
    /// Without a dependency, use <see cref="WhenAvailable{T}"/>.
    /// </remarks>
    public static class Services
    {
        private sealed class Registration
        {
            public object Implementation;
            public Version Version;
            public string OwnerGuid;
        }

        private static readonly Dictionary<Type, Registration> Registered = new Dictionary<Type, Registration>();
        private static readonly Dictionary<Type, List<Action<object>>> Waiting = new Dictionary<Type, List<Action<object>>>();

        /// <summary>
        /// Registers <paramref name="implementation"/> as the provider of
        /// <typeparamref name="T"/>. A second registration of the same type is
        /// refused and logged, so two libraries cannot silently replace each other.
        /// </summary>
        /// <param name="implementation">The object mods receive.</param>
        /// <param name="version">Version of the service's contract, for mods that need a minimum.</param>
        /// <param name="ownerGuid">BepInEx GUID of the library, shown in logs.</param>
        /// <returns>True when registered.</returns>
        public static bool Register<T>(T implementation, Version version = null, string ownerGuid = null) where T : class
        {
            if (implementation == null)
            {
                throw new ArgumentNullException(nameof(implementation));
            }
            List<Action<object>> callbacks;
            lock (Registered)
            {
                if (Registered.TryGetValue(typeof(T), out Registration existing))
                {
                    ModFramework.Log.LogWarning($"{ownerGuid ?? "A mod"} tried to register {typeof(T).FullName}, already provided by {existing.OwnerGuid ?? "another mod"}.");
                    return false;
                }
                Registered[typeof(T)] = new Registration { Implementation = implementation, Version = version, OwnerGuid = ownerGuid };
                Waiting.TryGetValue(typeof(T), out callbacks);
                Waiting.Remove(typeof(T));
            }
            ModFramework.Log.LogInfo($"Service {typeof(T).FullName} {version} provided by {ownerGuid ?? "a mod"}.");
            if (callbacks != null)
            {
                foreach (Action<object> callback in callbacks)
                {
                    Invoke(callback, implementation);
                }
            }
            return true;
        }

        /// <summary>Returns the provider of <typeparamref name="T"/>, or null when none is installed.</summary>
        public static T Get<T>() where T : class
        {
            lock (Registered)
            {
                return Registered.TryGetValue(typeof(T), out Registration r) ? (T)r.Implementation : null;
            }
        }

        /// <summary>
        /// Returns the provider of <typeparamref name="T"/> if one is installed with at
        /// least <paramref name="minimumVersion"/>.
        /// </summary>
        public static bool TryGet<T>(out T service, Version minimumVersion = null) where T : class
        {
            lock (Registered)
            {
                if (Registered.TryGetValue(typeof(T), out Registration r) &&
                    (minimumVersion == null || (r.Version != null && r.Version >= minimumVersion)))
                {
                    service = (T)r.Implementation;
                    return true;
                }
            }
            service = null;
            return false;
        }

        /// <summary>Version the provider of <typeparamref name="T"/> registered, or null.</summary>
        public static Version GetVersion<T>() where T : class
        {
            lock (Registered)
            {
                return Registered.TryGetValue(typeof(T), out Registration r) ? r.Version : null;
            }
        }

        /// <summary>
        /// Calls <paramref name="callback"/> with the provider of <typeparamref name="T"/>
        /// now if it is registered, or as soon as it is.
        /// </summary>
        public static void WhenAvailable<T>(Action<T> callback) where T : class
        {
            if (callback == null)
            {
                return;
            }
            T now;
            lock (Registered)
            {
                now = Registered.TryGetValue(typeof(T), out Registration r) ? (T)r.Implementation : null;
                if (now == null)
                {
                    if (!Waiting.TryGetValue(typeof(T), out List<Action<object>> list))
                    {
                        Waiting[typeof(T)] = list = new List<Action<object>>();
                    }
                    list.Add(o => callback((T)o));
                    return;
                }
            }
            Invoke(o => callback((T)o), now);
        }

        private static void Invoke(Action<object> callback, object service)
        {
            try
            {
                callback(service);
            }
            catch (Exception ex)
            {
                ModFramework.Log.LogError($"A service callback threw: {ex}");
            }
        }
    }
}
