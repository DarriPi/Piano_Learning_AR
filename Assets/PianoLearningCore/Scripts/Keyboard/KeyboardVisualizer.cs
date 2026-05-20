using System.Collections.Generic;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Procedurally builds a visual keyboard underneath the GameObject this is attached to.
    /// Useful in the standalone 3D scene to see alignment. In AR with a real physical keyboard,
    /// you would disable this component (the user's real keys are visible through passthrough).
    ///
    /// Requires a KeyboardLayout sibling component.
    /// </summary>
    [RequireComponent(typeof(KeyboardLayout))]
    public class KeyboardVisualizer : MonoBehaviour
    {
        [Header("Materials (optional - we generate defaults if null)")]
        public Material whiteKeyMaterial;
        public Material blackKeyMaterial;

        [Header("Behaviour")]
        [Tooltip("Rebuild the visualization automatically when the layout's config changes in the Inspector.")]
        public bool rebuildOnValidate = true;

        [Tooltip("Highlight color when a 'key down' is signalled (test mode).")]
        public Color highlightColor = new Color(0.3f, 0.9f, 0.5f, 1f);

        private KeyboardLayout _layout;
        private readonly Dictionary<int, GameObject> _keyObjects = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Material> _keyMaterials = new Dictionary<int, Material>();

        private void OnEnable()
        {
            _layout = GetComponent<KeyboardLayout>();
            Rebuild();
        }

        private void OnDisable()
        {
            ClearChildren();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (rebuildOnValidate && isActiveAndEnabled)
            {
                // Defer to next editor frame; OnValidate can't destroy objects directly.
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null && isActiveAndEnabled) Rebuild();
                };
            }
        }
#endif

        /// <summary>Destroy and re-create all key cubes.</summary>
        public void Rebuild()
        {
            if (_layout == null) _layout = GetComponent<KeyboardLayout>();
            if (_layout == null || _layout.config == null) return;

            ClearChildren();
            EnsureDefaultMaterials();

            var cfg = _layout.config;
            for (int midi = cfg.startMidiNote; midi <= cfg.endMidiNote; midi++)
            {
                BuildKey(midi);
            }
        }

        private void ClearChildren()
        {
            // Iterate backwards: destroying children mutates the list.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform c = transform.GetChild(i);
                if (c != null && c.name.StartsWith("Key_"))
                {
                    if (Application.isPlaying) Destroy(c.gameObject);
                    else DestroyImmediate(c.gameObject);
                }
            }
            _keyObjects.Clear();
            _keyMaterials.Clear();
        }

        private void BuildKey(int midi)
        {
            if (_layout == null || _layout.config == null) return;
            var cfg = _layout.config;

            bool black = NoteUtils.IsBlackKey(midi);
            float width  = black ? cfg.blackKeyWidth  : cfg.whiteKeyWidth;
            float length = black ? cfg.blackKeyLength : cfg.whiteKeyLength;
            float height = black ? 0.018f : 0.012f;
            float yCenter = black ? cfg.blackKeyHeightAboveWhite + height * 0.5f
                                  : -height * 0.5f;

            float centerX = _layout.GetKeyCenterX(midi);
            // The key body extends from the front edge (Z=0) BACK into +Z, so its center is at length/2.
            float centerZ = length * 0.5f;

            GameObject keyGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            keyGO.name = $"Key_{midi}_{NoteUtils.GetNoteName(midi)}";
            keyGO.transform.SetParent(transform, worldPositionStays: false);
            keyGO.transform.localPosition = new Vector3(centerX, yCenter, centerZ);
            keyGO.transform.localRotation = Quaternion.identity;
            keyGO.transform.localScale = new Vector3(width * 0.95f, height, length);

            // Remove the collider — we don't need physics on the visual.
            var col = keyGO.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }

            // Give it a per-key material instance so we can flash it individually later.
            var rend = keyGO.GetComponent<MeshRenderer>();
            Material src = black ? blackKeyMaterial : whiteKeyMaterial;
            Material inst = new Material(src);
            rend.sharedMaterial = inst;

            _keyObjects[midi] = keyGO;
            _keyMaterials[midi] = inst;
        }

        private void EnsureDefaultMaterials()
        {
            // Use the URP/Lit shader if available (Unity 6 default), otherwise fall back to Standard.
            if (whiteKeyMaterial == null)
            {
                whiteKeyMaterial = MakeDefaultMaterial(new Color(0.95f, 0.95f, 0.95f));
            }
            if (blackKeyMaterial == null)
            {
                blackKeyMaterial = MakeDefaultMaterial(new Color(0.07f, 0.07f, 0.07f));
            }
        }

        private static Material MakeDefaultMaterial(Color c)
        {
            // Try the URP shader first; fall back to Standard/Diffuse if not available.
            Shader sh = Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("Standard")
                        ?? Shader.Find("Legacy Shaders/Diffuse");
            var mat = new Material(sh);
            // The URP/Lit base color property is _BaseColor; Standard uses _Color.
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            return mat;
        }

        /// <summary>Briefly flash a key (for feedback testing). Optional, no-op if visualizer is disabled.</summary>
        public void FlashKey(int midi, float seconds = 0.15f)
        {
            if (!_keyMaterials.TryGetValue(midi, out var mat)) return;
            StartCoroutine(FlashRoutine(mat, midi, seconds));
        }

        private System.Collections.IEnumerator FlashRoutine(Material mat, int midi, float seconds)
        {
            Color orig = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                       : mat.HasProperty("_Color")     ? mat.GetColor("_Color")
                       : Color.white;
            SetMatColor(mat, highlightColor);
            yield return new WaitForSeconds(seconds);
            SetMatColor(mat, orig);
        }

        private static void SetMatColor(Material mat, Color c)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
        }
    }
}
