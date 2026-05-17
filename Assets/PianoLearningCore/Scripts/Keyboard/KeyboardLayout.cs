using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Converts MIDI note numbers into local-space positions on a virtual keyboard.
    ///
    /// Coordinate system (in the keyboard's LOCAL space):
    ///   - X: along the keyboard. The leftmost white key's LEFT edge is at X = 0;
    ///        higher notes go in the +X direction.
    ///   - Y: vertical. White-key top surface is at Y = 0. Black keys sit above this.
    ///   - Z: depth. The FRONT edge of the keys (closest to the player) is at Z = 0.
    ///        Notes fall in from +Z (behind the keyboard) toward Z = 0.
    ///
    /// Attach this to an empty GameObject that represents the keyboard's origin.
    /// To align with a physical keyboard, move/rotate THIS GameObject in world space.
    /// </summary>
    [DisallowMultipleComponent]
    public class KeyboardLayout : MonoBehaviour
    {
        [Tooltip("The KeyboardConfig ScriptableObject describing this keyboard's dimensions.")]
        public KeyboardConfig config;

        // Cache so we don't recompute white-key indices on every call during runtime.
        private int[] _whiteIndexCache;
        private int _cachedStart = int.MinValue;
        private int _cachedEnd = int.MinValue;

        private void OnValidate()
        {
            _cachedStart = int.MinValue; // force rebuild after Inspector edits
        }

        private void EnsureCache()
        {
            if (config == null) return;
            if (_cachedStart == config.startMidiNote && _cachedEnd == config.endMidiNote) return;

            _cachedStart = config.startMidiNote;
            _cachedEnd = config.endMidiNote;

            int range = config.endMidiNote - config.startMidiNote + 1;
            _whiteIndexCache = new int[range];

            int whiteCount = 0;
            for (int i = 0; i < range; i++)
            {
                int midi = config.startMidiNote + i;
                if (NoteUtils.IsWhiteKey(midi))
                {
                    _whiteIndexCache[i] = whiteCount;
                    whiteCount++;
                }
                else
                {
                    _whiteIndexCache[i] = -1;
                }
            }
        }

        // ---------------- Public API ----------------

        /// <summary>
        /// Local-space CENTER position of the TOP of the given key, at the FRONT edge of the keyboard.
        /// This is the point a falling note should arrive at when it is "hit".
        /// Returns Vector3.zero if the note is outside the configured range.
        /// </summary>
        public Vector3 GetKeyLocalPosition(int midiNumber)
        {
            if (config == null || !config.ContainsNote(midiNumber))
                return Vector3.zero;

            EnsureCache();
            float x = GetKeyCenterX(midiNumber);
            float y = NoteUtils.IsBlackKey(midiNumber) ? config.blackKeyHeightAboveWhite : 0f;
            float z = config.hitLineZ;
            return new Vector3(x, y, z);
        }

        /// <summary>World-space version of GetKeyLocalPosition.</summary>
        public Vector3 GetKeyWorldPosition(int midiNumber)
        {
            return transform.TransformPoint(GetKeyLocalPosition(midiNumber));
        }

        /// <summary>Width (X dimension) of the falling-note rectangle for a given key.</summary>
        public float GetKeyVisualWidth(int midiNumber)
        {
            if (config == null) return 0.02f;
            return NoteUtils.IsBlackKey(midiNumber) ? config.blackKeyWidth : config.whiteKeyWidth;
        }

        /// <summary>
        /// Computes the local X coordinate of the CENTER of a key.
        /// White keys are laid out one after another. Black keys are centered between their
        /// two adjacent white keys.
        /// </summary>
        public float GetKeyCenterX(int midiNumber)
        {
            if (config == null || !config.ContainsNote(midiNumber)) return 0f;

            EnsureCache();
            int idx = midiNumber - config.startMidiNote;

            if (NoteUtils.IsWhiteKey(midiNumber))
            {
                int whiteIndex = _whiteIndexCache[idx];
                // Center of a white key = leftEdge + width/2 = whiteIndex*width + width/2.
                return whiteIndex * config.whiteKeyWidth + config.whiteKeyWidth * 0.5f;
            }
            else
            {
                // Black key: centered between the white key directly to its left
                // and the white key directly to its right.
                int leftWhiteMidi = midiNumber - 1; // black keys always have a white to their left
                while (leftWhiteMidi >= config.startMidiNote && NoteUtils.IsBlackKey(leftWhiteMidi))
                    leftWhiteMidi--;

                int rightWhiteMidi = midiNumber + 1;
                while (rightWhiteMidi <= config.endMidiNote && NoteUtils.IsBlackKey(rightWhiteMidi))
                    rightWhiteMidi++;

                bool hasLeft = leftWhiteMidi >= config.startMidiNote;
                bool hasRight = rightWhiteMidi <= config.endMidiNote;

                if (hasLeft && hasRight)
                {
                    float lx = GetKeyCenterX(leftWhiteMidi);
                    float rx = GetKeyCenterX(rightWhiteMidi);
                    return (lx + rx) * 0.5f;
                }
                if (hasLeft) return GetKeyCenterX(leftWhiteMidi) + config.whiteKeyWidth * 0.5f;
                if (hasRight) return GetKeyCenterX(rightWhiteMidi) - config.whiteKeyWidth * 0.5f;
                return 0f;
            }
        }

        /// <summary>
        /// Returns true if the given MIDI note is one this keyboard can play.
        /// </summary>
        public bool ContainsNote(int midiNumber) => config != null && config.ContainsNote(midiNumber);

#if UNITY_EDITOR
        // Draw the keyboard outline in the Scene view so it's visible even with no
        // KeyboardVisualizer attached.
        private void OnDrawGizmosSelected()
        {
            if (config == null) return;
            float width = config.TotalWidth;
            float depth = config.whiteKeyLength;
            Vector3 a = transform.TransformPoint(new Vector3(0f,     0f, 0f));
            Vector3 b = transform.TransformPoint(new Vector3(width, 0f, 0f));
            Vector3 c = transform.TransformPoint(new Vector3(width, 0f, depth));
            Vector3 d = transform.TransformPoint(new Vector3(0f,     0f, depth));
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
#endif
    }
}
