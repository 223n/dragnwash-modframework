using System;
using System.Collections.Generic;
using System.Linq;

namespace DragNWash.ModFramework
{
    /// <summary>
    /// Something that happens in the game, by name, with plain values: a scene
    /// loaded, a dialogue line shown, a save written. The library that hooks it
    /// registers it (<see cref="Operations.RegisterEvent"/>) and raises it
    /// (<see cref="Operations.Raise"/>); what listens — graphs today — never
    /// needs to know which library that is. Since 1.4.0.
    /// </summary>
    public sealed class OperationEvent
    {
        /// <summary><c>library.noun.verb</c>, e.g. <c>dialogue.line.showing</c>.</summary>
        public string Name { get; internal set; }
        /// <summary>One line on when it happens.</summary>
        public string Description { get; internal set; }
        /// <summary>GUID of the mod that registered it.</summary>
        public string Owner { get; internal set; }
        /// <summary>The values it hands over, named and typed as an operation's parameters are.</summary>
        public IReadOnlyList<OperationParameter> Values { get; internal set; }
    }

    public static partial class Operations
    {
        private static readonly Dictionary<string, OperationEvent> Events = new Dictionary<string, OperationEvent>(StringComparer.Ordinal);

        /// <summary>
        /// Registers an event, so anything that builds on the registry can answer
        /// it by name. The name is <c>library.noun.verb</c>, lower case; a second
        /// registration of a name is refused and logged. The event goes when its
        /// owner is reloaded or unloaded. Since 1.4.0.
        /// </summary>
        /// <param name="ownerGuid">BepInEx GUID of the mod registering it.</param>
        /// <param name="name">e.g. <c>scene.loaded</c>.</param>
        /// <param name="description">One line on when it happens.</param>
        /// <param name="values">What it hands over, made with <see cref="Parameter"/>.</param>
        /// <returns>The event, or null when the name is taken.</returns>
        public static OperationEvent RegisterEvent(string ownerGuid, string name, string description, params OperationParameter[] values)
        {
            if (string.IsNullOrEmpty(ownerGuid) || string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("An event needs its owner's GUID and a name.");
            }
            if (name.Any(c => char.IsWhiteSpace(c) || char.IsUpper(c)))
            {
                throw new ArgumentException($"Event names are lower case with no spaces: \"{name}\".", nameof(name));
            }
            var e = new OperationEvent
            {
                Name = name,
                Description = description ?? "",
                Owner = ownerGuid,
                Values = (values ?? new OperationParameter[0]).Where(v => v != null).ToList(),
            };
            lock (Events)
            {
                if (Events.TryGetValue(name, out OperationEvent existing))
                {
                    ModFramework.Log.LogWarning($"[op] {ownerGuid} tried to register the event {name}, already registered by {existing.Owner}.");
                    return null;
                }
                Events[name] = e;
            }
            return e;
        }

        /// <summary>Every event registered, by name. Since 1.4.0.</summary>
        public static IReadOnlyList<OperationEvent> AllEvents
        {
            get
            {
                lock (Events)
                {
                    return Events.Values.OrderBy(e => e.Name, StringComparer.Ordinal).ToList();
                }
            }
        }

        /// <summary>The event of that name, or null. Since 1.4.0.</summary>
        public static OperationEvent FindEvent(string name)
        {
            lock (Events)
            {
                return name != null && Events.TryGetValue(name, out OperationEvent e) ? e : null;
            }
        }

        /// <summary>
        /// Raised by the library that owns the event, on the main thread, with
        /// the values it hands over (names as registered; null for none). What
        /// listens must be quick and must not throw: a listener that throws is
        /// logged and the others still hear it. Nothing happens when the event is
        /// not registered. Since 1.4.0.
        /// </summary>
        public static void Raise(string name, IDictionary<string, object> values = null)
        {
            OperationEvent e = FindEvent(name);
            Action<OperationEvent, IDictionary<string, object>> listeners = Happened;
            if (e == null || listeners == null)
            {
                return;
            }
            foreach (Action<OperationEvent, IDictionary<string, object>> one in listeners.GetInvocationList())
            {
                try
                {
                    one(e, values ?? new Dictionary<string, object>(StringComparer.Ordinal));
                }
                catch (Exception ex)
                {
                    ModFramework.Log.LogWarning($"[op] A listener of {name} threw: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Every registered event as it is raised, for what answers events by
        /// name (the Graphs library). A run never starts inside the event: what
        /// listens copies the values and acts at its next frame. Since 1.4.0.
        /// </summary>
        public static event Action<OperationEvent, IDictionary<string, object>> Happened;

        internal static void RemoveOwnerEvents(string guid)
        {
            lock (Events)
            {
                foreach (string name in Events.Values.Where(e => e.Owner == guid).Select(e => e.Name).ToList())
                {
                    Events.Remove(name);
                }
            }
        }
    }
}
