using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace DVRAT.Patches
{
    /// <summary>
    /// Saves tablet state and speaker settings with their gadget, and the active-job marks with the savegame.
    /// The class-level attribute is required, PatchAll skips classes without one.
    /// </summary>
    [HarmonyPatch]
    public static class SavePatches
    {
        /// <summary>Key in gadget save data and in the savegame.</summary>
        private const string SaveKey = "dvrat";

        [HarmonyPatch(typeof(DV.Customization.Gadgets.GadgetBase), nameof(DV.Customization.Gadgets.GadgetBase.SaveDataRequested))]
        [HarmonyPostfix]
        public static void OnGadgetSaving(DV.Customization.Gadgets.GadgetBase __instance, JObject dst)
        {
            var speaker = __instance != null ? __instance.GetComponentInChildren<RATSpeaker>(true) : null;
            if (speaker != null && dst != null)
                dst[SaveKey] = speaker.SaveState();

            var controller = __instance != null ? __instance.GetComponentInChildren<RATTabletController>(true) : null;
            if (controller == null || dst == null) return;
            dst[SaveKey] = controller.SaveState();
            RATMain.Log($"Tablet state saved: {(controller.Session.PoweredOn ? "on" : "off")}");
        }

        [HarmonyPatch(typeof(DV.Customization.Gadgets.GadgetBase), nameof(DV.Customization.Gadgets.GadgetBase.AfterSaveDataLoaded))]
        [HarmonyPostfix]
        public static void OnGadgetLoaded(DV.Customization.Gadgets.GadgetBase __instance, JObject src)
        {
            var speaker = __instance != null ? __instance.GetComponentInChildren<RATSpeaker>(true) : null;
            if (speaker != null)
                speaker.LoadState(src?[SaveKey]);

            var controller = __instance != null ? __instance.GetComponentInChildren<RATTabletController>(true) : null;
            if (controller == null) return;
            if (src?[SaveKey] == null)
                RATMain.Log("Tablet gadget loaded without saved tablet state");
            controller.LoadState(src?[SaveKey]);
        }

        /// <summary>Job marks belong to the player, not a tablet.</summary>
        [HarmonyPatch(typeof(SaveGameManager), "DoSaveIO")]
        [HarmonyPrefix]
        public static void OnGameSaving(SaveGameData data)
        {
            if (data == null) return;
            var mod = data.GetJObject(SaveKey) ?? new JObject();
            ActiveJobStore.WriteTo(mod);
            data.SetJObject(SaveKey, mod);
        }

        [HarmonyPatch(typeof(SaveGameManager), nameof(SaveGameManager.FindStartGameData))]
        [HarmonyPostfix]
        public static void OnGameLoading(SaveGameManager __instance)
        {
            ActiveJobStore.ReadFrom(__instance?.data?.GetJObject(SaveKey));
        }
    }
}
