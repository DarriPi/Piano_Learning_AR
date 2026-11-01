using System.Collections;
using TMPro;
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
        private float _length; // size along Z (constant, = speed * duration, minus the note gap)
        private float _width;  // size along X
        private float _thickness; // size along Y

        private MeshRenderer _renderer;
        private Color        _originalColor;
        private Coroutine    _feedbackCoroutine;

        private TextMeshPro _label;  // the note's letter; null when labels are off
        private float _labelY;       // label height: just clear of the block's top face
        private float _labelOffsetZ; // label centre, measured back from the block's front edge

        // Labels are built at 1/100 scale, so their sizes are in centimetres. At full scale a 2 cm
        // letter needs a font size near 0.2, and TMP's auto-size steps by at least 0.05 — too coarse.
        private const float LabelScale = 0.01f;

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

            // Trim the END of the block, never its front edge (which carries the timing), so a note
            // followed straight away by the same key reads as two blocks instead of one long bar.
            float fullLength = controller.Speed * n.duration;
            _length = Mathf.Max(fullLength * 0.5f, fullLength - controller.noteGap);

            // Set scale once - it doesn't change over time.
            transform.localScale = new Vector3(_width, _thickness, _length);

            _renderer = GetComponent<MeshRenderer>();
            if (_renderer != null)
                _originalColor = GetMaterialColor(_renderer.material);

            if (controller.showNoteLabels) CreateLabel(controller.noteLabelColor);

            UpdatePosition();
        }

        private void Update()
        {
            UpdatePosition();
        }

        private void OnDestroy()
        {
            // The label isn't our child (see CreateLabel), so it doesn't go with us by itself.
            if (_label != null) Destroy(_label.gameObject);
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

            if (_label != null)
            {
                float frontZ = centerZ - _length * 0.5f;
                _label.transform.localPosition = new Vector3(_xCenter, _labelY, frontZ + _labelOffsetZ);
            }
        }

        /// <summary>
        /// Print the note's letter flat on the block's top face, near its front edge, sized to the
        /// block's width. The label is the block's sibling in keyboard space rather than its child,
        /// because the block's non-uniform scale (width x thickness x length) would stretch the text.
        /// </summary>
        private void CreateLabel(Color color)
        {
            var go = new GameObject("Label_" + NoteUtils.GetNoteName(note.midiNumber));
            go.transform.SetParent(transform.parent, false);
            // Lying flat, readable from above, with the top of the letter pointing away from the player.
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one * LabelScale;

            _label = go.AddComponent<TextMeshPro>();
            _label.text = NoteUtils.GetPitchName(note.midiNumber);
            _label.color = color;
            _label.alignment = TextAlignmentOptions.Center;
            _label.textWrappingMode = TextWrappingModes.NoWrap;

            // A box inside the block's width and a little taller than wide; auto-size fits the
            // letter to it, so "E" on a white-key block and "C#" on a narrow black-key one both fit.
            float boxWidth = _width * 0.9f;
            float boxHeight = Mathf.Min(boxWidth * 1.4f, _length * 0.8f);
            _label.rectTransform.sizeDelta = new Vector2(boxWidth, boxHeight) / LabelScale;
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 1f;
            _label.fontSizeMax = 40f;

            _labelY = _yCenter + _thickness * 0.5f + 0.0005f; // just clear of the top face, no z-fight
            _labelOffsetZ = 0.004f + boxHeight * 0.5f;       // 4 mm in from the front edge
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
