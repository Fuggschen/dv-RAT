using UnityEngine;
using UnityEngine.Video;

// Stub: SerializeField fields exist for Unity Inspector serialization only.
#pragma warning disable 0169, 0414, 0649

namespace DVRAT
{
    public class RATTabletController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Canvas _screenCanvas;

        [Header("Boot Settings")]
        [SerializeField] private float _bootDuration = 5.0f;
        [SerializeField] private VideoPlayer _bootVideoPlayer;

        [Header("Performance Simulation")]
        [SerializeField] private float _uiBuildDelay = 0.5f;
        [SerializeField] private float _menuTransitionTime = 1.2f;
        [SerializeField] private float _dataRefreshInterval = 2.0f;

        [Header("Touch Settings")]
        [SerializeField] private float _touchCooldown = 0.3f;
    }
}