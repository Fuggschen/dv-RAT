using UnityEngine;

// Stub: SerializeField fields exist for Unity Inspector serialization only.
#pragma warning disable 0169, 0414, 0649

namespace DVRAT
{
    public enum SpeakerKind { Speaker, Subwoofer }

    /// <summary>Goes next to the GadgetBase. The LED is every material named "LED" on the model.</summary>
    public class RATSpeaker : MonoBehaviour
    {
        [SerializeField] private SpeakerKind _kind = SpeakerKind.Speaker;
        [Tooltip("Color of the LED while a tablet is connected.")]
        [SerializeField] private Color _ledColor = new Color(0.1f, 1f, 0.25f);
        [Tooltip("Brightness of the lit LED; raise it if the LED looks dark in daylight.")]
        [SerializeField] private float _ledIntensity = 8f;
    }
}
