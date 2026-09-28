using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// Controller-driven menu input that REPLACES the laser <see cref="ControllerUIPointer"/> with
    /// something that can't miss: instead of aiming a ray at a button, the player moves a highlight
    /// along the list with the thumbstick and presses the trigger to activate it. It reuses the same
    /// OVRInput path that already works on-device for keyboard calibration
    /// (<see cref="XRControllerCalibrator"/>), so there is no raycasting, canvas-facing, or
    /// controller-anchor dependency to go wrong.
    ///
    /// Because only ONE interactive panel is visible at a time (Help / song list / playback bar /
    /// score board), a single navigator just operates whichever panel's buttons are currently active.
    /// <see cref="HelpPanel"/> toggles <see cref="isActive"/>: OFF while Help is open (the thumbstick
    /// belongs to the calibrator then), ON once Help closes.
    ///
    /// Add one via Tools &gt; Piano Learning &gt; Create Menu Navigator.
    /// </summary>
    public class ControllerMenuNavigator : MonoBehaviour
    {
        [Header("Active")]
        [Tooltip("When false the navigator ignores input and clears the highlight. HelpPanel turns it " +
                 "OFF during positioning (Help open) so the thumbstick drives the keyboard calibrator, " +
                 "and ON for the menu / playback.")]
        public bool isActive = true;

        [Header("Input")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;
        [Tooltip("Button that activates the highlighted menu item.")]
        public OVRInput.Button confirmButton = OVRInput.Button.PrimaryIndexTrigger;
        [Tooltip("Thumbstick deflection needed to step the highlight.")]
        [Range(0.1f, 0.9f)] public float stepThreshold = 0.5f;
        [Tooltip("Seconds between steps while the stick is held.")]
        public float repeatDelay = 0.25f;

        [Header("Highlight")]
        [Tooltip("The highlighted button is scaled by this so it's obvious in VR.")]
        public float highlightScale = 1.12f;

        private readonly List<Button> _buttons = new List<Button>(16);
        private readonly List<Button> _scratch = new List<Button>(16);
        private Canvas[] _canvases;
        private int _index;
        private float _nextStepTime;

        private Button _highlighted;
        private Vector3 _highlightedBaseScale = Vector3.one;

        private void Start()
        {
            // Scope to the interactive menu canvases — the ones the builders put a ControllerUIPointer
            // on (song menu / playback bar / score board). That naturally excludes the display-only HUD
            // and any unrelated package canvases. Fall back to all canvases if none are marked.
            var set = new HashSet<Canvas>();
#pragma warning disable CS0618 // FindObjectsOfType works across every Unity version
            foreach (var p in FindObjectsOfType<ControllerUIPointer>(true))
            {
                var c = p.GetComponent<Canvas>();
                if (c == null) c = p.GetComponentInParent<Canvas>();
                if (c != null) set.Add(c);
            }
            _canvases = set.Count > 0 ? new Canvas[set.Count] : FindObjectsOfType<Canvas>(true);
#pragma warning restore CS0618
            if (set.Count > 0) set.CopyTo(_canvases);
        }

        private void Update()
        {
            if (!isActive)
            {
                ClearHighlight();
                return;
            }

            CollectButtons();
            if (_buttons.Count == 0)
            {
                ClearHighlight();
                return;
            }

            if (_index >= _buttons.Count) _index = _buttons.Count - 1;
            if (_index < 0) _index = 0;

            HandleNavigation();
            ApplyHighlight();   // after navigation, so the scale tracks the current item
            HandleConfirm();
        }

        private void OnDisable() => ClearHighlight();

        /// <summary>Enable/disable menu input. Resets to the top of the list when enabled.</summary>
        public void SetActive(bool active)
        {
            isActive = active;
            if (active) { _index = 0; _nextStepTime = 0f; }
            else ClearHighlight();
        }

        // ---------------- collection ----------------

        private void CollectButtons()
        {
            _buttons.Clear();
            if (_canvases == null) return;
            foreach (var c in _canvases)
            {
                if (c == null) continue;
                c.GetComponentsInChildren(false, _scratch); // active-only
                for (int i = 0; i < _scratch.Count; i++)
                {
                    var b = _scratch[i];
                    if (b != null && b.interactable) _buttons.Add(b);
                }
            }
            // Top-to-bottom, then left-to-right, so up/down on the stick feels natural.
            _buttons.Sort(CompareByScreenPosition);
        }

        private static int CompareByScreenPosition(Button a, Button b)
        {
            Vector3 pa = a.transform.position;
            Vector3 pb = b.transform.position;
            const float yEps = 0.02f; // treat near-equal heights as one row
            if (Mathf.Abs(pa.y - pb.y) > yEps) return pb.y.CompareTo(pa.y); // higher Y first
            return pa.x.CompareTo(pb.x);                                    // then left-to-right
        }

        // ---------------- navigation ----------------

        private void HandleNavigation()
        {
            Vector2 stick = ReadStick();
            int dir = 0;
            if (Mathf.Abs(stick.y) >= Mathf.Abs(stick.x))
            {
                if (stick.y >= stepThreshold) dir = -1;        // up = previous
                else if (stick.y <= -stepThreshold) dir = +1;  // down = next
            }
            else
            {
                if (stick.x <= -stepThreshold) dir = -1;       // left = previous
                else if (stick.x >= stepThreshold) dir = +1;   // right = next
            }

            if (dir == 0)
            {
                _nextStepTime = 0f; // released → the next push steps immediately
                return;
            }
            if (Time.unscaledTime < _nextStepTime) return;

            int n = _buttons.Count;
            _index = (_index + dir + n) % n; // wrap around
            _nextStepTime = Time.unscaledTime + repeatDelay;
        }

        private Vector2 ReadStick()
        {
            Vector2 r = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
            Vector2 l = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
            return r.sqrMagnitude >= l.sqrMagnitude ? r : l; // accept either hand
        }

        private void HandleConfirm()
        {
            if (!OVRInput.GetDown(confirmButton, controller)) return;
            if (_index < 0 || _index >= _buttons.Count) return;
            var b = _buttons[_index];
            if (b != null && b.interactable) b.onClick.Invoke();
        }

        // ---------------- highlight ----------------

        private void ApplyHighlight()
        {
            Button target = _buttons[_index];
            if (target == _highlighted) return;
            ClearHighlight();
            _highlighted = target;
            _highlightedBaseScale = target.transform.localScale;
            target.transform.localScale = _highlightedBaseScale * highlightScale;
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(target.gameObject);
        }

        private void ClearHighlight()
        {
            if (_highlighted != null)
                _highlighted.transform.localScale = _highlightedBaseScale;
            _highlighted = null;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }
}
