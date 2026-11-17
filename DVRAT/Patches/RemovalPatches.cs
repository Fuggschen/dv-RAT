using DV.Customization.Gadgets;
using HarmonyLib;

namespace DVRAT.Patches
{
    /// <summary>
    /// A running tablet can only be picked up once switched off.
    /// The class-level attribute is required, PatchAll skips classes without one.
    /// </summary>
    [HarmonyPatch]
    public static class RemovalPatches
    {
        /// <summary>The game allows <c>(removal method | FromCode) &amp; mask</c>, so a FromCode mask blocks hand and remover but not game code.</summary>
        [HarmonyPatch(typeof(GadgetBase), "GetValidRemovalMethodsMask")]
        [HarmonyPostfix]
        public static void KeepRunningTablet(GadgetBase __instance, ref GadgetBase.GadgetRemovalMethod __result)
        {
            var controller = __instance != null ? __instance.GetComponentInChildren<RATTabletController>(true) : null;
            if (controller != null && controller.CurrentState != RATTabletController.TabletState.Off)
                __result &= GadgetBase.GadgetRemovalMethod.FromCode;
        }
    }
}
