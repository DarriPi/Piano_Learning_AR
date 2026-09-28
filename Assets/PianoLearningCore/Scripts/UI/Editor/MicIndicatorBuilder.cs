using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One-click builder for the <see cref="MicActiveIndicator"/> privacy badge (proposal §6.2) in the
    /// current scene: a small pill with a red dot and "MIC ON", wired to the scene's
    /// <see cref="PianoAudioDetector"/> and headset camera.
    ///
    /// The canvas is left at the top level of the scene rather than parented under the camera rig, so
    /// the OVRCameraRig stays untouched; <see cref="MicActiveIndicator"/> attaches itself to the head at
    /// runtime. It is placed at the same head-relative spot here purely so it can be previewed.
    /// Display-only, so no GraphicRaycaster and no EventSystem.
    ///
    /// Menu: Tools &gt; Piano Learning &gt; Create Mic Indicator
    /// </summary>
    public static class MicIndicatorBuilder
    {
        private static readonly Color PillColor = new Color(0.05f, 0.06f, 0.10f, 0.60f);
        private static readonly Color DotColor  = new Color(0.95f, 0.20f, 0.20f, 1f);

        [MenuItem("Tools/Piano Learning/Create Mic Indicator")]
        public static void Create()
        {
            const string undoLabel = "Create Mic Indicator";

#pragma warning disable CS0618 // FindObjectOfType is fine here and works on every Unity version
            var existing = Object.FindObjectOfType<MicActiveIndicator>();
            var detector = Object.FindObjectOfType<PianoAudioDetector>();
#pragma warning restore CS0618

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog(undoLabel,
                    $"The scene already has a mic indicator ('{existing.name}'). Delete it first to " +
                    "rebuild it.", "OK");
                return;
            }

            var eye = GameObject.Find("CenterEyeAnchor");
            Transform head = eye != null ? eye.transform
                           : Camera.main != null ? Camera.main.transform : null;

            // World-space canvas, no GraphicRaycaster: the badge takes no input and the laser must
            // never land on it.
            var canvasGo = new GameObject("MicIndicatorCanvas", typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(canvasGo, undoLabel);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(220f, 70f);
            canvasGo.transform.localScale = Vector3.one * 0.0005f;       // ~11cm x 3.5cm pill

            // Controller on the Canvas root (so hiding the Panel keeps it alive), fully wired.
            var indicator = canvasGo.AddComponent<MicActiveIndicator>();
            indicator.detector = detector;
            indicator.head = head;

            // Preview placement only — the component re-attaches to the head at runtime.
            if (head != null)
            {
                canvasGo.transform.position = head.TransformPoint(indicator.headOffset);
                canvasGo.transform.rotation = head.rotation;
            }

            // Panel (this is what MicActiveIndicator shows/hides): rounded dark pill.
            var panel = CreateChild("Panel", canvasRt);
            Stretch(panel);
            var pill = panel.gameObject.AddComponent<Image>();
            pill.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            pill.type = Image.Type.Sliced;
            pill.color = PillColor;
            pill.raycastTarget = false;

            var content = CreateChild("Content", panel);
            Stretch(content);
            content.offsetMin = new Vector2(18f, 10f);
            content.offsetMax = new Vector2(-18f, -10f);
            var hlg = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;

            // Red "recording" dot.
            var dotRt = CreateChild("Dot", content);
            var dotLayout = dotRt.gameObject.AddComponent<LayoutElement>();
            dotLayout.preferredWidth = 30f;
            dotLayout.preferredHeight = 30f;
            var dot = dotRt.gameObject.AddComponent<Image>();
            dot.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            dot.color = DotColor;
            dot.raycastTarget = false;

            // Label, so the dot can't be mistaken for anything else.
            var labelRt = CreateChild("Label", content);
            var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = "MIC ON";
            label.fontSize = 34f;
            label.fontStyle = FontStyles.Bold;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.color = Color.white;
            label.raycastTarget = false;

            indicator.panelRoot = panel.gameObject;

            EditorUtility.SetDirty(indicator);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            Debug.Log("[MicIndicatorBuilder] Created 'MicIndicatorCanvas'. " +
                      (detector == null
                          ? "No PianoAudioDetector found in the scene — drag one onto the indicator's " +
                            "'Detector' field or it will never show."
                          : $"Linked to PianoAudioDetector '{detector.name}'") +
                      (head == null
                          ? ". No CenterEyeAnchor / Main Camera found — assign 'Head' so it follows the view."
                          : $" and head '{head.name}'.") +
                      " At runtime it follows the view and shows only while the mic is capturing. " +
                      "Tune 'Head Offset' in-headset if it sits too far out or covers anything.");
        }

        // ---------------- Builder helpers ----------------

        private static RectTransform CreateChild(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
