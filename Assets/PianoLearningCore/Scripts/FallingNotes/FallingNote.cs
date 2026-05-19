using System.Collections;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// One visible falling-note block. Its position is recomputed every frame from
    /// the controller's current playback time, so timing stays accurate even if
    /// frame rate drops or pauses occur.
    ///
    /// In the keyboard's LOCAL space:
    ///   The note slides along -Z (from +Z toward Z=0).
    ///   When the note's LEADING (front, lower-Z) edge crosses Z=0, that's the "hit moment".
    ///   When the note's TRAILING edge crosses Z=0, the note is fully past the keyboard
    ///   and is despawned.
    /// </summary>
    public class FallingNote : MonoBehaviour
    {
        public PianoNote note;
        private FallingNotesController _controller;
        private KeyboardLayout _layout;
        private float _xCenter;
        private float _yCenter;
        private float _length; // size along Z (constant, = speed * duration)
        private float _width;  // size along X
        private float _thickness; // size along Y

        private MeshRenderer _renderer;
        private Color        _originalColor;
        private Coroutine    _feedbackCoroutine;

        public void Initialize(PianoNote n, FallingNotesController controller, KeyboardLayout layout)
        {
            note = n;
            _controller = controller;
            _layout = layout;

            _xCenter = layout.GetKeyCenterX(n.midiNumber);
            // Sit just above the corresponding key surface.
            bool black = NoteUtils.IsBlackKey(n.midiNumber);
            float baseY = black ? layout.config.blackKeyHeightAboveWhite : 0f;
            _thickness = controller.noteThickness;
            _yCenter = baseY + _thickness * 0.5f + controller.noteHoverHeight;

            _width = layout.GetKeyVisualWidth(n.midiNumber) * 0.9f; // slight padding so adjacent notes don't touch
            _length = controller.Speed * n.duration;

            // Set scale once - it doesn't change over time.
            transform.localScale = new Vector3(_width, _thickness, _length);

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer != null)
                _originalColor = GetMaterialColor(_renderer.material);

            UpdatePosition();
        }

        private void Update()
        {
            UpdatePosition();
        }

        private void UpdatePosition()
        {
            if (_controller == null || _layout == null) return;

            // Center Z of the note at current time, in the layout's local space.
            // At hit time (t == note.startTime), the LEADING edge is at Z=0, so center is at Z = _length/2.
            // Earlier in time the center is further behind (larger Z).
            float t = _controller.CurrentTime;
            float centerZ = _controller.Speed * (note.startTime - t) + _length * 0.5f;

            transform.localPosition = new Vector3(_xCenter, _yCenter, centerZ);
        }

        /// <summary>True once the note's TRAILING edge has passed the hit line (Z=0).</summary>
        public bool IsFullyPast(float currentTime)
        {
            return currentTime > note.startTime + note.duration;
        }

        /// <summary>
        /// Flash the note block with <paramref name="color"/> for <paramref name="duration"/> seconds,
        /// then restore the original material color. Any in-progress flash is cancelled first.
        /// </summary>
        public void SetFeedbackColor(Color color, float duration)
        {
            if (_renderer == null) return;
            if (_feedbackCoroutine != null) StopCoroutine(_feedbackCoroutine);
            _feedbackCoroutine = StartCoroutine(FeedbackColorRoutine(color, duration));
        }

        private IEnumerator FeedbackColorRoutine(Color color, float duration)
        {
            SetMaterialColor(_renderer.material, color);
            yield return new WaitForSeconds(duration);
            SetMaterialColor(_renderer.material, _originalColor);
            _feedbackCoroutine = null;
        }

        private static Color GetMaterialColor(Material m)
        {
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color"))     return m.GetColor("_Color");
            return Color.white;
        }

        private static void SetMaterialColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}
