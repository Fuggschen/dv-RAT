using System.Linq;
using HarmonyLib;
using System.Reflection;
using UnityModManagerNet;
using UnityEngine;

namespace DVRAT
{
    // No [EnableReloading]: the custom item framework registers the item once, so a reloaded assembly would not match it.
    public static class RATMain
    {
        public static UnityModManager.ModEntry ModEntry { get; private set; }
        public static RATSettings Settings { get; private set; }
        public static bool IsTabletActive { get; private set; } = false;
        private static Harmony HarmonyInstance { get; set; }

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            ModEntry = modEntry;

            modEntry.OnToggle = OnToggle;
            Settings = UnityModManager.ModSettings.Load<RATSettings>(modEntry);
            modEntry.OnGUI = entry => Settings.Draw(entry);
            modEntry.OnSaveGUI = entry => Settings.Save(entry);

            HarmonyInstance = new Harmony(modEntry.Info.Id);
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            modEntry.Logger.Log("Patched: " + string.Join(", ",
                HarmonyInstance.GetPatchedMethods().Select(m => m.DeclaringType?.Name + "." + m.Name)));

            modEntry.Logger.Log("R.A.T. mod loaded successfully.");
            return true;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool active)
        {
            if (active)
            {
                RATGadgetManager.Initialize();
                MultiplayerSync.Initialize();
                Odometer.Initialize();
                WorldStreamingInit.LoadingFinished += GameContent.OnWorldLoaded;
                WorldStreamingInit.LoadingFinished += CatenaryLayout.OnWorldLoaded;
                WorldStreamingInit.LoadingFinished += TrackSigns.OnWorldLoaded;
                modEntry.Logger.Log("R.A.T. activated.");
            }
            else
            {
                RATGadgetManager.Shutdown();
                MultiplayerSync.Shutdown();
                Odometer.Shutdown();
                WorldStreamingInit.LoadingFinished -= GameContent.OnWorldLoaded;
                WorldStreamingInit.LoadingFinished -= CatenaryLayout.OnWorldLoaded;
                WorldStreamingInit.LoadingFinished -= TrackSigns.OnWorldLoaded;
                modEntry.Logger.Log("R.A.T. deactivated.");
            }

            return true;
        }

        public static void Log(string message)
        {
            ModEntry?.Logger.Log(message);
        }

        public static void LogWarning(string message)
        {
            ModEntry?.Logger.Warning(message);
        }

        public static void LogError(string message)
        {
            ModEntry?.Logger.Error(message);
        }
    }
}