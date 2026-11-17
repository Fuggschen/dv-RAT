using System;
using System.Collections.Generic;
using System.Linq;
using UnityModManagerNet;

namespace DVRAT
{
    /// <summary>Whether another mod is installed, switched on and loaded without error. A switched-off mod keeps its assembly loaded.</summary>
    internal sealed class ModWatch
    {
        private static readonly List<ModWatch> Watches = new List<ModWatch>();
        private static bool _subscribed;

        private readonly string[] _modIds;

        /// <param name="modIds">IDs from info.json. Several for a mod with a separate multiplayer build.</param>
        public ModWatch(params string[] modIds)
        {
            _modIds = modIds;
            if (!_subscribed)
            {
                _subscribed = true;
                UnityModManager.toggleModsListen += OnToggle;
            }
            Watches.Add(this);
            Update();
        }

        public bool Active { get; private set; }

        /// <summary>The first of the mod IDs that runs; null while none does.</summary>
        public UnityModManager.ModEntry Running { get; private set; }

        /// <summary>Finds a type in the mod's assembly, then in the assemblies it references (e.g. DVSeasons.Core). Null if not found.</summary>
        public Type FindType(string name)
        {
            var mod = Running?.Assembly;
            if (mod == null) return null;
            var type = mod.GetType(name, false);
            if (type != null) return type;
            var referenced = new HashSet<string>(mod.GetReferencedAssemblies().Select(a => a.Name));
            return AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => referenced.Contains(a.GetName().Name))
                .Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
        }

        private void Update()
        {
            Running = _modIds.Select(UnityModManager.FindMod)
                .FirstOrDefault(m => m != null && m.Active && m.Assembly != null);
            Active = Running != null;
        }

        // Raised when a mod finishes loading and when it is toggled in the mod manager.
        private static void OnToggle(UnityModManager.ModEntry entry, bool active)
        {
            string id = entry?.Info?.Id;
            foreach (var watch in Watches)
                if (watch._modIds.Contains(id)) watch.Update();
        }
    }
}
