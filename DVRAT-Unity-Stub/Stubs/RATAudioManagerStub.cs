using UnityEngine;

// Stub: SerializeField fields exist for Unity Inspector serialization only.
#pragma warning disable 0169, 0414, 0649

namespace DVRAT
{
    public class RATAudioManager : MonoBehaviour
    {
        [Header("Audio Sources")]
        [SerializeField] private AudioSource _mainSource;
        [SerializeField] private AudioSource _loopSource;
        [SerializeField] private AudioSource _busySource;

        [Header("Audio Clips")]
        [SerializeField] private AudioClip _hddStartupClip;
        [SerializeField] private AudioClip _hddHumClip;
        [SerializeField] private AudioClip _hddClickClip;
        [SerializeField] private AudioClip _hddBusyClip;
        [SerializeField] private AudioClip _shutdownClip;

        [Header("Volume Settings")]
        [SerializeField] [Range(0f, 1f)] private float _masterVolume = 0.7f;
        [SerializeField] [Range(0f, 1f)] private float _humVolume = 0.3f;
        [SerializeField] [Range(0f, 1f)] private float _clickVolume = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float _startupVolume = 0.8f;

        [Header("3D Audio")]
        [SerializeField] [Range(0f, 1f)] private float _spatialBlend = 1.0f;
    }
}