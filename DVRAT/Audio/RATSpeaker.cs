using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DVRAT
{
    /// <summary>A speaker or subwoofer gadget. A tablet on the same train plays through <see cref="Source"/>. The LED lights while one is connected.</summary>
    public class RATSpeaker : MonoBehaviour
    {
        public const string LedMaterialName = "LED";

        [SerializeField] private SpeakerKind _kind = SpeakerKind.Speaker;
        [Tooltip("Color of the LED while a tablet is connected.")]
        [SerializeField] private Color _ledColor = new Color(0.1f, 1f, 0.25f);
        [Tooltip("Brightness of the lit LED; raise it if the LED looks dark in daylight.")]
        [SerializeField] private float _ledIntensity = 8f;

        private static readonly List<RATSpeaker> Active = new List<RATSpeaker>();

        // Instanced LED materials with their original emission.
        private readonly List<(Material material, Color off)> _leds = new List<(Material, Color)>();
        private bool _connected;
        private ToneSettings _tone;

        // Full volume up to near, silent at range.
        private const float SpeakerNear = 2f;
        private const float SpeakerRange = 60f;
        private const float SubwooferNear = 3f;
        private const float SubwooferRange = 100f;

        public AudioSource Source { get; private set; }

        /// <summary>Null while free.</summary>
        public MediaPlayer Owner { get; set; }

        /// <summary>The owning tablet's volume, 0 to 1. The device's own volume comes on top.</summary>
        public float MasterVolume { get; set; } = 1f;

        /// <summary>Enabled devices only.</summary>
        public static IReadOnlyList<RATSpeaker> All => Active;

        public SpeakerKind Kind => _kind;

        public SpeakerSettings Settings { get; private set; } = new SpeakerSettings();

        /// <summary>Null while not mounted.</summary>
        public TrainCar TrainCar
        {
            get
            {
                var gadget = GetComponentInParent<DV.Customization.Gadgets.GadgetBase>();
                return gadget != null ? gadget.TrainCar : null;
            }
        }

        public bool Connected
        {
            get => _connected;
            set
            {
                if (_connected == value) return;
                _connected = value;
                ShowLed();
            }
        }

        private void Awake()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                Material[] instances = null;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null || !shared[i].name.StartsWith(LedMaterialName)) continue;
                    // Copy materials only on renderers with an LED.
                    instances = instances ?? renderer.materials;
                    _leds.Add((instances[i], Emission(instances[i])));
                }
            }
            if (_leds.Count == 0)
                RATMain.LogWarning($"{name}: no material named \"{LedMaterialName}\" found, the {_kind} has no LED");
            ShowLed();

            // Own object at the model's middle: the gadget origin is its bottom front, and the filter must stay off the game's own sources.
            var holder = new GameObject("RAT speaker sound");
            holder.transform.SetParent(transform, false);
            holder.transform.position = ModelCenter();
            Source = holder.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.priority = MediaPlayer.MusicPriority;
            Source.loop = false;
            Source.spatialBlend = 1f;
            Source.dopplerLevel = 0f;
            if (_kind == SpeakerKind.Subwoofer)
                ToneFilter.UseFalloff(Source, SubwooferNear, SubwooferRange);
            else
                ToneFilter.UseFalloff(Source, SpeakerNear, SpeakerRange);
            _tone = SpeakerSettings.BaseTone(_kind);
            holder.AddComponent<ToneFilter>().Settings = _tone;
            if (GetComponent<SpeakerSync>() == null) gameObject.AddComponent<SpeakerSync>();
            holder.AddComponent<MuffleFilter>().LowPass = _kind != SpeakerKind.Subwoofer;
        }

        private Vector3 ModelCenter()
        {
            Bounds? bounds = null;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (bounds == null)
                    bounds = renderer.bounds;
                else
                {
                    var all = bounds.Value;
                    all.Encapsulate(renderer.bounds);
                    bounds = all;
                }
            }
            return bounds?.center ?? transform.position;
        }

        // Settings change at any time, and the filter reads them on the audio thread.
        private void Update()
        {
            Settings.ApplyTo(_tone, _kind);
            float volume = MasterVolume * Settings.Volume / 100f;
            if (!Mathf.Approximately(Source.volume, volume))
                Source.volume = volume;
        }

        private void OnEnable() => Active.Add(this);

        // The owning tablet notices on its next device scan.
        private void OnDisable()
        {
            Active.Remove(this);
            Connected = false;
            Owner = null;
        }

        public JObject SaveState() => Settings.ToJson();

        public void LoadState(JToken state) => Settings = SpeakerSettings.FromJson(state);

        private void ShowLed()
        {
            foreach (var (material, off) in _leds)
                SetEmission(material, _connected ? _ledColor * _ledIntensity : off);
        }

        // HDRP's Lit shader reads _EmissiveColor; the built-in shaders read _EmissionColor.
        private static Color Emission(Material material) =>
            material.HasProperty("_EmissiveColor") ? material.GetColor("_EmissiveColor")
            : material.HasProperty("_EmissionColor") ? material.GetColor("_EmissionColor")
            : Color.black;

        private static void SetEmission(Material material, Color color)
        {
            if (material.HasProperty("_EmissiveColor"))
                material.SetColor("_EmissiveColor", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color);
            }
        }
    }
}
