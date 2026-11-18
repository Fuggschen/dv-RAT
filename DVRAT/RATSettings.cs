using UnityModManagerNet;

namespace DVRAT
{
    /// <summary>Unity Mod Manager settings, for what needs a keyboard. Tablet settings live in <see cref="TabletSession"/>.</summary>
    public class RATSettings : UnityModManager.ModSettings, IDrawable
    {
        [Draw("Music folder for the Media Player (empty: your Windows Music folder)")]
        public string MusicFolder = "";

        [Draw("PC Audio boost in dB (raise it if PC Audio plays too quietly, lower it if it sounds distorted)",
            DrawType.Slider, Min = CaptureBoost.MinDb, Max = CaptureBoost.MaxDb)]
        public int PcAudioBoostDb = CaptureBoost.DefaultDb;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void OnChange() { }
    }
}
