using UnityEngine;

// Stub: SerializeField fields exist for Unity Inspector serialization only.
#pragma warning disable 0169, 0414, 0649

namespace DVRAT
{
    /// <summary>Pages are built at runtime, so only the canvas needs wiring.</summary>
    public class RATUIManager : MonoBehaviour
    {
        [SerializeField] private Canvas _rootCanvas;
        [Tooltip("Optional. Falls back to Unity's built-in Arial.")]
        [SerializeField] private Font _font;
        [Tooltip("Delay between elements appearing when a page builds up, to feel like old hardware.")]
        [SerializeField] private float _buildStepDelay = 0.03f;
        [SerializeField] private float _pageSwitchDelay = 0.15f;
    }
}
