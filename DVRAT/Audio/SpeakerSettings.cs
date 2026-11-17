using System;
using Newtonsoft.Json.Linq;

namespace DVRAT
{
    public enum SpeakerKind { Speaker, Subwoofer }

    /// <summary>Per-device volume and EQ from the Devices page, saved with the gadget.</summary>
    public class SpeakerSettings
    {
        public const int DefaultVolume = 80;

        /// <summary>Names this device in multiplayer, like <see cref="TabletSession.Id"/>.</summary>
        public string Id { get; set; } = TabletSession.NewId();

        private int _volume = DefaultVolume;
        private float _lowsDb, _midsDb, _highsDb;

        /// <summary>Percent, 0 to 100.</summary>
        public int Volume { get => _volume; set => _volume = Math.Max(0, Math.Min(100, value)); }

        public float LowsDb { get => _lowsDb; set => _lowsDb = ClampDb(value); }
        public float MidsDb { get => _midsDb; set => _midsDb = ClampDb(value); }
        public float HighsDb { get => _highsDb; set => _highsDb = ClampDb(value); }

        /// <summary>The device's own range before the player's EQ.</summary>
        public static ToneSettings BaseTone(SpeakerKind kind) => kind == SpeakerKind.Subwoofer
            ? new ToneSettings { LowCutHz = 25, HighCutHz = 120 }
            : new ToneSettings { LowCutHz = 60 };

        /// <summary>A subwoofer stops at 120 Hz, below the 1 kHz and 4 kHz bands.</summary>
        public static bool HasMidsAndHighs(SpeakerKind kind) => kind != SpeakerKind.Subwoofer;

        /// <summary>Copies the EQ into <paramref name="tone"/> and keeps its <see cref="BaseTone"/> range.</summary>
        public void ApplyTo(ToneSettings tone, SpeakerKind kind)
        {
            tone.LowsDb = LowsDb;
            // Hidden bands stay flat even if a value was set before.
            bool all = HasMidsAndHighs(kind);
            tone.MidsDb = all ? MidsDb : 0f;
            tone.HighsDb = all ? HighsDb : 0f;
        }

        /// <summary>Keeps this object and its ID, since the Devices page holds a reference to it.</summary>
        public void CopyFrom(SpeakerSettings other)
        {
            Volume = other.Volume;
            LowsDb = other.LowsDb;
            MidsDb = other.MidsDb;
            HighsDb = other.HighsDb;
        }

        public JObject ToJson() => new JObject
        {
            ["id"] = Id,
            ["volume"] = Volume,
            ["lows"] = LowsDb,
            ["mids"] = MidsDb,
            ["highs"] = HighsDb,
        };

        public static SpeakerSettings FromJson(JToken json)
        {
            var settings = new SpeakerSettings();
            if (!(json is JObject saved)) return settings;
            string id = saved.Value<string>("id");
            if (!string.IsNullOrEmpty(id)) settings.Id = id;
            settings.Volume = saved.Value<int?>("volume") ?? DefaultVolume;
            settings.LowsDb = saved.Value<float?>("lows") ?? 0f;
            settings.MidsDb = saved.Value<float?>("mids") ?? 0f;
            settings.HighsDb = saved.Value<float?>("highs") ?? 0f;
            return settings;
        }

        private static float ClampDb(float db) => Math.Max(-ToneSettings.MaxEqDb, Math.Min(ToneSettings.MaxEqDb, db));
    }
}
