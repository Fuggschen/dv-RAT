using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRTK;

namespace DVRAT
{
    /// <summary>Lets VR hands use the screen through a game cab button over its whole face. The button only reports a use, so the touch point is the screen point nearest the hand.</summary>
    public class VrTouch
    {
        /// <summary>Touch area thickness, mostly in front of the screen so a hand reaches it before the case.</summary>
        private const float Depth = 0.03f;
        private const float InFront = 0.01f;

        private GameObject _area;
        private BoxCollider _collider;
        private Action<Vector3> _pressed;

        /// <summary><paramref name="pressed"/> receives the world point on the screen.</summary>
        public bool Attach(RectTransform screen, Action<Vector3> pressed)
        {
            if (_area != null || screen == null) return _area != null;
            _pressed = pressed;
            try
            {
                _area = new GameObject("VrTouchArea");
                _area.SetActive(false);
                _area.transform.SetParent(screen, false);
                _area.transform.localPosition = screen.rect.center;
                _area.layer = LayerMask.NameToLayer("Interactable");

                _collider = _area.AddComponent<BoxCollider>();
                _collider.isTrigger = true;
                float scale = Mathf.Abs(screen.lossyScale.z) > 1e-6f ? Mathf.Abs(screen.lossyScale.z) : Mathf.Abs(screen.lossyScale.x);
                _collider.size = new Vector3(screen.rect.width, screen.rect.height, Depth / scale);
                // A world-space canvas faces local -z.
                _collider.center = new Vector3(0f, 0f, -InFront / scale);

                var spec = _area.AddComponent<DV.CabControls.Spec.Button>();
                spec.createRigidbody = false;
                spec.useJoints = false;
                spec.pushLocalOffset = Vector3.zero;
                spec.colliderGameObjects = new[] { _area };
                // VRTK throws for a runtime button without poses.
                spec.handPosesOverride = new DV.Interaction.InteractionHandPoses();
                // A poke counts as a use, like the trigger.
                spec.disableTouchUse = false;
                _area.SetActive(true);

                var control = _area.GetComponent<DV.CabControls.ButtonBase>();
                if (control == null)
                {
                    RATMain.LogWarning("The screen could not be made a game control; it cannot be used in VR");
                    return false;
                }
                control.Used += OnUsed;
                RATMain.Log("Screen set up for VR hands");
                return true;
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"The screen could not be made a game control, it cannot be used in VR: {ex.Message}");
                return false;
            }
        }

        private void OnUsed()
        {
            try
            {
                Press();
            }
            catch (Exception ex)
            {
                // An exception in the game's input handling would break the hand for good.
                RATMain.LogWarning($"Using the screen in VR failed: {ex.Message}");
            }
        }

        private void Press()
        {
            var hand = UsingHand();
            if (!hand.HasValue)
            {
                RATMain.LogWarning("Screen used in VR, but no hand was found at it");
                return;
            }
            // Nearest point of the touch area, projected onto the screen plane.
            var point = _collider.ClosestPoint(hand.Value);
            var screen = _area.transform.parent;
            point -= screen.forward * Vector3.Dot(point - screen.position, screen.forward);
            _pressed?.Invoke(point);
        }

        /// <summary>The nearer hand when both touch.</summary>
        private Vector3? UsingHand()
        {
            var interactable = _area.GetComponentInParent<VRTK_InteractableObject>() ?? _area.GetComponentInChildren<VRTK_InteractableObject>();
            var hands = new List<GameObject>();
            // Not GetUsingObject: the game reports the use before VRTK stores the hand, so it throws.
            if (interactable != null)
                hands.AddRange(interactable.GetTouchingObjects() ?? new List<GameObject>());
            if (hands.Count == 0)
                hands.AddRange(new[] { VRTK_DeviceFinder.GetControllerLeftHand(false), VRTK_DeviceFinder.GetControllerRightHand(false) });

            var center = _collider.bounds.center;
            return hands.Where(h => h != null && h.activeInHierarchy)
                .Select(TouchPoint)
                .OrderBy(p => (_collider.ClosestPoint(p) - p).sqrMagnitude)
                .Select(p => (Vector3?)p)
                .FirstOrDefault();
        }

        private readonly Dictionary<GameObject, Transform> _fingertips = new Dictionary<GameObject, Transform>();

        /// <summary>The index fingertip, else the middle of VRTK's touch colliders, else the hand.</summary>
        private Vector3 TouchPoint(GameObject hand)
        {
            var palm = PalmPoint(hand);
            if (!_fingertips.TryGetValue(hand, out var tip) || tip == null)
            {
                tip = Fingertip(hand, palm);
                _fingertips[hand] = tip;
                RATMain.Log(tip != null ? $"VR touch uses the bone \"{tip.name}\" as fingertip" : "VR touch found no index finger bone, it uses the palm");
            }
            return tip != null ? tip.position : palm;
        }

        private static Vector3 PalmPoint(GameObject hand)
        {
            var touch = hand.GetComponentInChildren<VRTK_InteractTouch>() ?? hand.GetComponentInParent<VRTK_InteractTouch>();
            var colliders = touch != null ? touch.ControllerColliders() : null;
            if (colliders != null)
                foreach (var collider in colliders)
                    if (collider != null && collider.enabled)
                        return collider.bounds.center;
            return hand.transform.position;
        }

        /// <summary>The index bone farthest from the palm, searched where VRTK keeps the hand model.</summary>
        private static Transform Fingertip(GameObject hand, Vector3 palm)
        {
            var roots = new[] { hand, VRTK_DeviceFinder.GetModelAliasController(hand), VRTK_DeviceFinder.GetActualController(hand) };
            return roots.Where(r => r != null)
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .Where(t => t.name.IndexOf("index", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(t => (t.position - palm).sqrMagnitude)
                .FirstOrDefault();
        }
    }
}
