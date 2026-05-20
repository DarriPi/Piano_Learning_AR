using System.Collections.Generic;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Lightweight visual aid for aligning the virtual keyboard with a physical one in AR.
    /// Use this INSTEAD of <see cref="KeyboardVisualizer"/> when the user can see their real piano
    /// through passthrough — instead of drawing every key as a cube, this just draws thin
    /// reference lines (front edge, side edges, optional white-key ticks, optional C-key posts).
    ///
    /// Visuals are unlit so they remain readable through passthrough and against varied lighting.
    /// </summary>
    [RequireComponent(typeof(KeyboardLayout))]
    public class KeyboardAlignmentGuide : MonoBehaviour
    {
        [Header("What to show")]
        [Tooltip("Bright line along the front edge of the keyboard, where notes land. " +
                 "Most useful single guide — align this with the front edge of your real piano keys.")]
        public bool showHitLine = true;

        [Tooltip("Faint line along the BACK edge of the keys (visualises depth/orientation).")]
        public bool showBackLine = false;

        [Tooltip("Lines at the left and right ENDS of the keyboard.")]
        public bool showSideLines = true;

        [Tooltip("Short vertical posts at every C key (octave landmarks).")]
        public bool showCKeyPosts = true;

        [Tooltip("Highlight middle C (C4) with a taller / differently coloured post.")]
        public bool highlightMiddleC = true;

        [Tooltip("Tiny ticks at every white key (denser guide, can be visually noisy on a 76-key board).")]
        public bool showAllWhiteKeyTicks = false;

        [Header("Style")]
        [Tooltip("Thickness of all reference lines, in meters.")]
        public float lineThickness = 0.0025f;

        [Tooltip("Height of the C-key posts above the hit line.")]
        public float cPostHeight = 0.04f;

        [Tooltip("Height of the white-key ticks above the hit line (used when showAllWhiteKeyTicks is on).")]
        public float whiteTickHeight = 0.012f;

        [Tooltip("Main reference colour (hit line, side lines, back line, ticks).")]
        public Color primaryColor = new Color(0.20f, 1f, 0.85f, 1f);

        [Tooltip("Colour for C-key posts (so they stand out from the main reference lines).")]
        public Color cPostColor = new Color(1f, 0.85f, 0.20f, 1f);

        [Tooltip("Colour for the middle-C marker.")]
        public Color middleCColor = new Color(1f, 0.30f, 0.60f, 1f);

        [Header("Behaviour")]
        [Tooltip("Rebuild automatically when the layout config changes in the Inspector.")]
        public bool rebuildOnValidate = true;

        // Internal
        private KeyboardLayout _layout;
        private readonly List<GameObject> _guideObjects = new List<GameObject>();
        private Material _primaryMat;
        private Material _cPostMat;
        private Material _middleCMat;

        private void OnEnable()
        {
            _layout = GetComponent<KeyboardLayout>();
            Rebuild();
        }

        private void OnDisable()
        {
            ClearGuides();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (rebuildOnValidate && isActiveAndEnabled)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && isActiveAndEnabled) Rebuild();
                };
            }
        }
#endif

        // --------------------------------------------------------------
        // Public API
        // --------------------------------------------------------------

        public void Rebuild()
        {
            if (_layout == null) _layout = GetComponent<KeyboardLayout>();
            if (_layout == null || _layout.config == null) return;

            ClearGuides();
            EnsureMaterials();

            var cfg = _layout.config;
            float width = cfg.TotalWidth;
            float depth = cfg.whiteKeyLength;

            // Hit line (front edge of keyboard, Z = 0).
            if (showHitLine)
            {
                MakeLine(
                    centerX: width * 0.5f,
                    centerY: lineThickness * 0.5f,
                    centerZ: 0f,
                    sizeX: width,
                    sizeY: lineThickness,
                    sizeZ: lineThickness,
                    mat: _primaryMat,
                    name: "Guide_HitLine");
            }

            // Back line (back edge of the keys).
            if (showBackLine)
            {
                MakeLine(
                    centerX: width * 0.5f,
                    centerY: lineThickness * 0.5f,
                    centerZ: depth,
                    sizeX: width,
                    sizeY: lineThickness,
                    sizeZ: lineThickness,
                    mat: _primaryMat,
                    name: "Guide_BackLine");
            }

            // Side lines (left + right edges).
            if (showSideLines)
            {
                MakeLine(0f,       lineThickness * 0.5f, depth * 0.5f,
                         lineThickness, lineThickness, depth, _primaryMat, "Guide_LeftSide");
                MakeLine(width,    lineThickness * 0.5f, depth * 0.5f,
                         lineThickness, lineThickness, depth, _primaryMat, "Guide_RightSide");
            }

            // C-key vertical posts.
            if (showCKeyPosts || showAllWhiteKeyTicks)
            {
                for (int midi = cfg.startMidiNote; midi <= cfg.endMidiNote; midi++)
                {
                    if (NoteUtils.IsBlackKey(midi)) continue;
                    int pc = ((midi % 12) + 12) % 12;
                    bool isC = pc == 0;
                    bool isMiddleC = isC && midi == NoteUtils.MIDDLE_C;

                    float x = _layout.GetKeyCenterX(midi);

                    if (showCKeyPosts && isC)
                    {
                        float h = isMiddleC && highlightMiddleC ? cPostHeight * 1.6f : cPostHeight;
                        Material mat = isMiddleC && highlightMiddleC ? _middleCMat : _cPostMat;
                        MakeLine(
                            centerX: x,
                            centerY: h * 0.5f,
                            centerZ: 0f,
                            sizeX: lineThickness,
                            sizeY: h,
                            sizeZ: lineThickness,
                            mat: mat,
                            name: $"Guide_CPost_{NoteUtils.GetNoteName(midi)}");
                    }
                    else if (showAllWhiteKeyTicks && !isC)
                    {
                        MakeLine(
                            centerX: x,
                            centerY: whiteTickHeight * 0.5f,
                            centerZ: 0f,
                            sizeX: lineThickness,
                            sizeY: whiteTickHeight,
                            sizeZ: lineThickness,
                            mat: _primaryMat,
                            name: $"Guide_Tick_{NoteUtils.GetNoteName(midi)}");
                    }
                }
            }
        }

        public void ClearGuides()
        {
            for (int i = _guideObjects.Count - 1; i >= 0; i--)
            {
                if (_guideObjects[i] != null)
                {
                    if (Application.isPlaying) Destroy(_guideObjects[i]);
                    else DestroyImmediate(_guideObjects[i]);
                }
            }
            _guideObjects.Clear();

            // Belt-and-braces sweep in case OnDisable ran while _guideObjects had cleared.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c != null && c.name.StartsWith("Guide_"))
                {
                    if (Application.isPlaying) Destroy(c.gameObject);
                    else DestroyImmediate(c.gameObject);
                }
            }
        }

        // --------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------

        private void MakeLine(float centerX, float centerY, float centerZ,
                              float sizeX, float sizeY, float sizeZ,
                              Material mat, string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = new Vector3(centerX, centerY, centerZ);
            go.transform.localScale = new Vector3(sizeX, sizeY, sizeZ);

            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            var rend = go.GetComponent<MeshRenderer>();
            rend.sharedMaterial = mat;
            // Reflection probes shouldn't blur a 2mm-thick line; disable for perf.
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            _guideObjects.Add(go);
        }

        private void EnsureMaterials()
        {
            // Unlit so the lines stay readable through passthrough (no shading wash-out).
            _primaryMat  = MakeUnlitMaterial(primaryColor);
            _cPostMat    = MakeUnlitMaterial(cPostColor);
            _middleCMat  = MakeUnlitMaterial(middleCColor);
        }

        private static Material MakeUnlitMaterial(Color c)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit")
                        ?? Shader.Find("Unlit/Color")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }
    }
}
