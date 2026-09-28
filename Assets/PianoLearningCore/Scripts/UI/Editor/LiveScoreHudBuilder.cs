using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One-click builder for the in-play <see cref="LiveScoreHud"/> in the current scene. Creates a
    /// compact, semi-transparent world-space strip showing live Accuracy / Streak / Score, wires its
    /// references to the scene's <see cref="NoteEvaluator"/> + <see cref="FallingNotesController"/>,
    /// and parks it ABOVE the play area so it stays clear of the player's view of the keys and the
    /// falling notes (both of which live on a low, near-horizontal plane at keyboard height).
    ///
    /// Three deliberate "don't block the player's vision" choices vs. the score-board builder:
    ///   1. Thin STRIP, not a panel — minimal screen real-estate.
    ///   2. Low-alpha backing — MR passthrough and any notes behind it stay visible.
    ///   3. Positioned high/forward — above the key-plane sightline; tune on-device from there.
    /// It is also display-only, so it adds NO ControllerUIPointer and needs NO EventSystem.
    /// Uses only standard Unity UI APIs (no Meta Interaction types).
    ///
    /// Menu: Tools &gt; Piano Learning &gt; Create Live Score HUD
    /// </summary>
    public static class LiveScoreHudBuilder
    {
        // Low alpha so MR passthrough and the notes behind stay visible through the strip.
        private static readonly Color StripColor   = new Color(0.05f, 0.06f, 0.10f, 0.50f);
        private static readonly Color CaptionColor = new Color(0.78f, 0.80f, 0.86f, 1f);
        private static readonly Color GreenValue   = new Color(0.25f, 0.90f, 0.45f, 1f);

        [MenuItem("Tools/Piano Learning/Create Live Score HUD")]
        public static void Create()
        {
            const string undoLabel = "Create Live Score HUD";

#pragma warning disable CS0618 // FindObjectOfType is fine here and works on every Unity version
            var evaluator  = Object.FindObjectOfType<NoteEvaluator>();
            var controller = Object.FindObjectOfType<FallingNotesController>();
#pragma warning restore CS0618

            // World-space canvas — compact and short so it reads as a HUD strip, not a panel.
            // No EventSystem is created: the HUD takes no input, it only displays.
            var canvasGo = new GameObject("LiveScoreHudCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, undoLabel);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(520f, 150f);
            canvasGo.transform.localScale = Vector3.one * 0.0007f;       // ~0.36m x 0.11m strip
            // Parked high and slightly forward, ABOVE the low key-plane / note highway so it does not
            // sit in the player's downward sightline to the keys or the falling notes. TUNE ON-DEVICE:
            // raise Y or slide X aside if it ever overlaps the play area in the headset.
            canvasGo.transform.position = new Vector3(0f, 1.5f, 0.7f);
            // Same facing rule as the score board: identity faces the player; a 180° Y turn mirrors uGUI.
            canvasGo.transform.rotation = Quaternion.identity;

            // Panel (this is what LiveScoreHud shows/hides).
            var panel = CreateChild("Panel", canvasRt);
            Stretch(panel);
            panel.gameObject.AddComponent<Image>().color = StripColor;

            // Content — one horizontal row of three stat cells.
            var content = CreateChild("Content", panel);
            Stretch(content);
            content.offsetMin = new Vector2(18f, 12f);
            content.offsetMax = new Vector2(-18f, -12f);
            var hlg = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            // Accuracy is the headline (green); streak + score are secondary.
            var accuracyVal = CreateStatCell("Accuracy", content, "100%", "ACCURACY", GreenValue);
            var streakVal   = CreateStatCell("Streak",   content, "x0",   "STREAK",   Color.white);
            var scoreVal    = CreateStatCell("Score",    content, "0",    "SCORE",    Color.white);

            // Controller on the Canvas root (so hiding the Panel keeps it alive), fully wired.
            var hud = canvasGo.AddComponent<LiveScoreHud>();
            hud.evaluator = evaluator;
            hud.controller = controller;
            hud.panelRoot = panel.gameObject;
            hud.accuracyText = accuracyVal;
            hud.streakText = streakVal;
            hud.scoreText = scoreVal;

            EditorUtility.SetDirty(hud);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            Debug.Log("[LiveScoreHudBuilder] Created 'LiveScoreHudCanvas'. " +
                      (evaluator == null
                          ? "No NoteEvaluator found in the scene — drag one onto the LiveScoreHud's " +
                            "'Evaluator' field so it can read the live score."
                          : $"Linked to NoteEvaluator '{evaluator.name}'") +
                      (controller == null
                          ? ". No FallingNotesController found — assign one for the show-while-playing check."
                          : $" and FallingNotesController '{controller.name}'.") +
                      " It is visible now for layout; at runtime it shows only while a song is playing " +
                      "and hides when the song ends so the score board can take over. It is parked above " +
                      "the play area — nudge its Transform in-headset so it never covers the keys or notes.");
        }

        // ---------------- Builder helpers ----------------

        // A stat cell: big VALUE over a small CAPTION. Returns the VALUE text so the builder can wire it.
        private static TMP_Text CreateStatCell(string name, RectTransform parent,
                                               string value, string caption, Color valueColor)
        {
            var cellRt = CreateChild(name, parent);
            var vlg = cellRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 0f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var valueTmp = CreateText("Value", cellRt, value, 56f, valueColor,
                TextAlignmentOptions.Center, FontStyles.Bold, 66f);
            CreateText("Caption", cellRt, caption, 20f, CaptionColor,
                TextAlignmentOptions.Center, FontStyles.Normal, 24f);
            return valueTmp;
        }

        private static TMP_Text CreateText(string name, RectTransform parent, string text,
                                           float fontSize, Color color, TextAlignmentOptions align,
                                           FontStyles style, float preferredHeight)
        {
            var rt = CreateChild(name, parent);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = color;
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
