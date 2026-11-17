using System;
using UnityEngine;

namespace DVRAT
{
    /// <summary>Radio reception computed like the game's Boombox, from raycasts into a cone above the device.</summary>
    public class TabletRadioSignal : MonoBehaviour
    {
        // Same values as the Boombox (VolumeAndInterferenceController).
        private const float SamplerHeight = 0.4f;
        private const float ConeAngle = 85f;
        private const float MaxDistance = 10f;
        private const int SampleRateFrames = 3;
        private const int SampleBufferSize = 30;
        private const float SmoothTime = 0.3f;
        private const float BlockTerrain = 0.8f;
        private const float BlockTunnel = 0.9f;
        private const float BlockSignalBlocker = 1f;
        private const float BlockOther = 0.45f;

        /// <summary>Fixed built-in antenna, between the Boombox's retracted (0.67) and extended (1.33).</summary>
        private const float AntennaBoost = 1f;

        /// <summary>The game's interference sets in below 0.7.</summary>
        private static readonly float[] BarThresholds = { SignalLoad.OneBar, SignalLoad.TwoBars, SignalLoad.ThreeBars };
        public const int BarCount = 3;

        private const float PreferenceRefreshSeconds = 1f;

        private ConeSampler _sampler;
        private RATTabletController _tablet;
        private int _terrainLayer;
        private bool _signalLoss = true;
        private float _nextPreferenceRead;
        private float _smoothVelocity;

        /// <summary>0 (none) to 1 (full).</summary>
        public float Strength { get; private set; }

        public int Bars
        {
            get
            {
                int bars = 0;
                while (bars < BarCount && Strength >= BarThresholds[bars])
                    bars++;
                return bars;
            }
        }

        private void Awake()
        {
            _tablet = GetComponent<RATTabletController>();
            _terrainLayer = LayerMask.NameToLayer("Terrain");

            var samplerObject = new GameObject("tablet signal sampler");
            samplerObject.transform.SetParent(transform, false);
            samplerObject.transform.localPosition = Vector3.up * SamplerHeight;
            // Always looks at the sky.
            samplerObject.transform.LookAt(samplerObject.transform.position + Vector3.up);

            _sampler = samplerObject.AddComponent<ConeSampler>();
            _sampler.useCustomHitWeightFunction = true;
            _sampler.weightFunction = BlockingFactor;
            _sampler.coneAngle = ConeAngle;
            _sampler.maxDistance = MaxDistance;
            _sampler.sampleLayers = LayerMask.GetMask("Terrain", "Default");
            _sampler.timingMode = ConeSampler.TimingMode.OneSampleEveryNFrames;
            _sampler.timingRate = SampleRateFrames;
            _sampler.sampleBufferSize = SampleBufferSize;
            // Off while the tablet is dark, to save raycasts.
            _sampler.enabled = false;
        }

        private float BlockingFactor(RaycastHit hit)
        {
            if (hit.collider.gameObject.layer == _terrainLayer) return BlockTerrain;
            string name = hit.collider.name;
            if (name.EndsWith("tunnel", StringComparison.Ordinal)) return BlockTunnel;
            if (name.ToLowerInvariant().Contains("signal blocker")) return BlockSignalBlocker;
            return BlockOther;
        }

        private void Update()
        {
            // On while loading too: the boot page may wait for signal.
            bool on = _tablet != null
                && (_tablet.CurrentState == RATTabletController.TabletState.Ready
                    || _tablet.CurrentState == RATTabletController.TabletState.Loading);
            if (_sampler.enabled != on)
                _sampler.enabled = on;
            if (!on)
            {
                Strength = 0f;
                _smoothVelocity = 0f;
                return;
            }

            if (Time.unscaledTime >= _nextPreferenceRead)
            {
                _nextPreferenceRead = Time.unscaledTime + PreferenceRefreshSeconds;
                _signalLoss = GamePreferences.Get<bool>(Preferences.RadioSignalLoss);
            }

            float target = _signalLoss
                ? Mathf.Clamp01(Mathf.Max(0f, 1f - _sampler.average) * AntennaBoost)
                : 1f;
            if (Mathf.Abs(target - Strength) > 0.001f)
                Strength = Mathf.SmoothDamp(Strength, target, ref _smoothVelocity, SmoothTime);
        }
    }
}
