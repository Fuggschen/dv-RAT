using System.Collections.Generic;
using DV.Customization.Gadgets;
using HarmonyLib;
using UnityEngine;

namespace DVRAT.Patches
{
    /// <summary>
    /// Lines up the tablet when placed with a VR hand. VR keeps the held orientation turned by the item's placing axes
    /// (default front -Z, top +Y), but the gadget stands on its feet (+Z) with its back along +Y.
    /// So the held bottom (-Y) goes to the gadget's +Z and the held back (+Z) to its +Y.
    /// The class-level attribute is required, PatchAll skips classes without one.
    /// </summary>
    [HarmonyPatch]
    public static class PlacementPatches
    {
        // The game turns the placement by LookRotation(-placingAxis, placingUp).
        private static readonly Vector3 PlacingAxis = Vector3.up;
        private static readonly Vector3 PlacingUp = Vector3.forward;

        private static readonly AccessTools.FieldRef<GadgetItem, GadgetBase> GadgetPrefab =
            AccessTools.FieldRefAccess<GadgetItem, GadgetBase>("gadgetPrefab");

        // Runs every frame while a gadget is held, so cache the tablet check per prefab.
        private static readonly Dictionary<GadgetBase, bool> IsTablet = new Dictionary<GadgetBase, bool>();

        [HarmonyPatch(typeof(GadgetItem), "AttemptProximityVRPlace")]
        [HarmonyPrefix]
        public static void AlignTablet(object c)
        {
            var item = Traverse.Create(c).Field("item").GetValue<GadgetItem>();
            var prefab = item != null ? GadgetPrefab(item) : null;
            if (prefab == null) return;
            if (!IsTablet.TryGetValue(prefab, out bool tablet))
                IsTablet[prefab] = tablet = prefab.GetComponentInChildren<RATTabletController>(true) != null;
            if (!tablet) return;
            Traverse.Create(c).Field("placingAxis").SetValue(PlacingAxis);
            Traverse.Create(c).Field("placingUp").SetValue(PlacingUp);
        }
    }
}
