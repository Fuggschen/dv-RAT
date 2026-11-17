using System;

namespace DVRAT
{
    /// <summary>
    /// Item registration is automatic: custom_item_mod scans the mod's subdirectories for item_info.json,
    /// and the gadget system converts custom_item_components.GadgetItem + GadgetBase to the real GadgetItem at load.
    /// </summary>
    public static class RATGadgetManager
    {
        public static bool IsInitialized { get; private set; }

        /// <summary>Called from RATMain.OnToggle(active: true).</summary>
        public static void Initialize()
        {
            if (IsInitialized) return;

            try
            {
                UnityMainThreadDispatcher.Initialize();

                IsInitialized = true;
                RATMain.Log("RATGadgetManager initialized.");
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Failed to initialize RATGadgetManager: {ex}");
            }
        }

        /// <summary>Called from RATMain.OnToggle(active: false).</summary>
        public static void Shutdown()
        {
            if (!IsInitialized) return;

            IsInitialized = false;
            RATMain.Log("RATGadgetManager shut down.");
        }
    }
}
