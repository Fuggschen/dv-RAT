using UnityEngine;

namespace DVRAT
{
    /// <summary>Gives this object's AudioSource a loudspeaker's sound, see <see cref="ToneSettings"/>. Change <see cref="Settings"/> from the main thread.</summary>
    public class ToneFilter : MonoBehaviour
    {
        private int _sampleRate;
        private ToneChain _chain;

        public ToneSettings Settings { get; set; } = new ToneSettings();

        private void Awake() => _sampleRate = AudioSettings.outputSampleRate;

        // Audio thread.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            var settings = Settings;
            if (settings == null || _sampleRate <= 0) return;
            if (_chain == null || _chain.Settings != settings || _chain.Channels != channels)
                _chain = new ToneChain(_sampleRate, channels, settings);
            _chain.Process(data);
        }

        /// <summary>Applies the <see cref="Falloff"/> curve. Distances are in metres.</summary>
        public static void UseFalloff(AudioSource source, float near, float far)
        {
            // More keys close to the source, where 1/distance bends most.
            const int count = 64;
            var keys = new Keyframe[count];
            for (int i = 0; i < count; i++)
            {
                float share = (float)i / (count - 1);
                share *= share;
                keys[i] = new Keyframe(share, Falloff.Gain(share * far, near, far));
            }
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < count; i++)
                curve.SmoothTangents(i, 0f);

            source.rolloffMode = AudioRolloffMode.Custom;
            source.minDistance = near;
            source.maxDistance = far;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, curve);
        }
    }
}
