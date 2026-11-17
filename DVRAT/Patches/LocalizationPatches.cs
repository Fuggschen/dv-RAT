using DV.Localization;
using HarmonyLib;

namespace DVRAT.Patches
{
    /// <summary>
    /// Drops cached translations on a language change.
    /// The class-level attribute is required, PatchAll skips classes without one.
    /// </summary>
    [HarmonyPatch]
    public static class LocalizationPatches
    {
        [HarmonyPatch(typeof(LocalizationAPI), nameof(LocalizationAPI.SetLanguage))]
        [HarmonyPostfix]
        public static void OnLanguageChanged() => GameContent.ClearTranslations();
    }
}
