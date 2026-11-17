using UnityModManagerNet;

namespace DVRAT
{
    /// <summary>Unity Mod Manager settings, for what needs a keyboard. Tablet settings live in <see cref="TabletSession"/>.</summary>
    public class RATSettings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Music folder for the Media Player (empty: your Windows Music folder)")]
        public string MusicFolder = "";

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void OnChange() { }
    }
}
