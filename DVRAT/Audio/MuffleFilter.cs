using UnityEngine;

namespace DVRAT
{
    /// <summary>
    /// Muffles this object's AudioSource behind a closed cab wall, see <see cref="CabZones"/>. The sources play outside
    /// the game's mixer. Add it after any other filter: Unity runs filters in component order.
    /// </summary>
    public class MuffleFilter : MonoBehaviour
    {
        private int _sampleRate;
        private MuffleChain _chain;
        // Main thread writes, audio thread reads.
        private volatile bool _walled;

        /// <summary>False for a bass-only source. Set right after adding the component.</summary>
        public bool LowPass { get; set; } = true;

        private void Awake() => _sampleRate = AudioSettings.outputSampleRate;

        private void Update() => _walled = CabZones.Walled(transform);

        // Audio thread.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (_sampleRate <= 0) return;
            if (_chain == null || _chain.Channels != channels)
                _chain = new MuffleChain(_sampleRate, channels, LowPass);
            _chain.Process(data, _walled);
        }
    }
}
