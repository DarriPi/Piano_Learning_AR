using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One-click builder for the <see cref="StartCountdownDisplay"/> count-in in the current scene: a
    /// translucent dark panel with a small "Starting in" line over a big number, wired to the scene's
    /// <see cref="FallingNotesController"/>.
    ///
    /// The canvas is left at the top level of the scene: <see cref="StartCountdownDisplay"/> moves itself
    /// over the keyboard every time it appears, so its place in the hierarchy doesn't matter. It is put
    /// at that spot here too, purely so it can be previewed. Display-only, so no GraphicRaycaster and no
    /// EventSystem.
    ///
    /// Menu: Tools &gt; Piano Learning &gt; Create Start Countdown
    /// </summary>
    public static class StartCountdownBuilder
    {
        // Low alpha so passthrough still shows through; dark enough for the number to read in a bright room.
        private static readonly Color PanelColor   = new Color(0.05f, 0.06f, 0.10f, 0.55f);
        private static readonly Color CaptionColor = new Color(0.78f, 0.80f, 0.86f, 1f);

        [MenuItem("Tools/Piano Learning/Create Start Countdown")]
        public static void Create()
        {
            const string undoLabel = "Create Start Countdown";

#pragma warning disable CS0618 // FindObjectOfType is fine here and works on every Unity version
            var existing   = Object.FindObjectOfType<StartCountdownDisplay>();
            var controller = Object.FindObjectOfType<FallingNotesController>();
#pragma warning restore CS0618

            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorUtility.DisplayDialog(undoLabel,
                    $"The scene already has a start countdown ('{existing.name}'). Delete it first to " +
                    "rebuild it.", "OK");
                return;
            }

            // World-space canvas, no GraphicRaycaster: the count-in takes no input and the laser must
            // never land on it.
            var canvasGo = new GameObject("StartCountdownCanvas", typeof(Canvas), typeof(CanvasScaler));
            Undo.RegisterCreatedObjectUndo(canvasGo, undoLabel);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(360f, 260f);
            canvasGo.transform.localScale = Vector3.one * 0.001f;        // 1 unit = 1 mm: a 36cm x 26cm panel

            // Panel (this is what StartCountdownDisplay shows/hides): rounded translucent backing.
            var panel = CreateChild("Panel", canvasRt);
            Stretch(panel);
            var backing = panel.gameObject.AddComponent<Image>();
            backing.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            backing.type = Image.Type.Sliced;
            backing.color = PanelColor;
            backing.raycastTarget = false;

            // Content — the caption over the number. When GO! hides the caption, the layout re-centres
            // the number in the panel.
            var content = CreateChild("Content", panel);
            Stretch(content);
            content.offsetMin = new Vector2(16f, 16f);
            content.offsetMax = new Vector2(-16f, -16f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 0f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var caption = CreateText("Caption", content, "Starting in", 36f, CaptionColor,
                FontStyles.Normal, 48f);
            var number = CreateText("Number", content, "3", 160f, Color.white,
                FontStyles.Bold, 180f);

            // Controller on the Canvas root (so hiding the Panel keeps it alive), fully wired.
            var countdown = canvasGo.AddComponent<StartCountdownDisplay>();
            countdown.controller = controller;
            countdown.panelRoot = panel.gameObject;
            countdown.numberText = number;
            countdown.captionText = caption;

            // Preview placement only — at runtime it re-places itself over the calibrated keyboard.
            countdown.PlaceOverKeyboard();

            EditorUtility.SetDirty(countdown);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            string link;
            if (controller == null)
                link = "No FallingNotesController found in the scene — drag one onto the countdown's " +
                       "'Controller' field or it will never show.";
            else if (controller.startDelay <= 0f)
                link = $"Linked to FallingNotesController '{controller.name}', but its Start Delay is 0 — " +
                       "set one (the scene uses 5) or there is nothing to count down.";
            else
                link = $"Linked to FallingNotesController '{controller.name}' (Start Delay {controller.startDelay:0.#} s).";

            Debug.Log("[StartCountdownBuilder] Created 'StartCountdownCanvas'. " + link +
                      " At runtime it appears over the keyboard for the Start Delay before each song, then " +
                      "shows GO! as the first notes reach the keys. Tune 'Keyboard Offset' in-headset if it " +
                      "covers anything.");
        }

        // ---------------- Builder helpers ----------------

        private static TMP_Text CreateText(string name, RectTransform parent, string text, float fontSize,
                                           Color color, FontStyles style, float preferredHeight)
        {
            var rt = CreateChild(name, parent);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            tmp.textWrappingMode = TextWrappingModes.NoWrap; // "GO!" must never break across two lines
            tmp.raycastTarget = false;
            return tmp;
        }

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
