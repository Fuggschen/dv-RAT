using DV.Customization;
using DV.Customization.Gadgets;
using HarmonyLib;
using UnityEngine;

namespace DVRAT.Patches
{
    /// <summary>
    /// Reports mounting and removal to <see cref="MultiplayerSync"/>. TryPlace is the player's placing only, a loading save uses Place.
    /// The class-level attribute is required, PatchAll skips classes without one.
    /// </summary>
    [HarmonyPatch]
    public static class MountPatches
    {
        [HarmonyPatch(typeof(GadgetItem), nameof(GadgetItem.TryPlace))]
        [HarmonyPostfix]
        public static void OnPlaced(bool __result, GadgetBase gadget)
        {
            if (!__result || GadgetMounts.ApplyingRemote) return;
            MultiplayerSync.OnLocalMount(gadget);
        }

        [HarmonyPatch(typeof(GadgetBase), nameof(GadgetBase.Remove))]
        [HarmonyPostfix]
        public static void OnRemoved(GadgetBase __instance, GadgetItem __result)
        {
            if (__result == null || GadgetMounts.ApplyingRemote) return;
            MultiplayerSync.OnLocalUnmount(__instance);
        }
    }
}
