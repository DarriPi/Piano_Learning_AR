using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One-click builder for the world-space end-of-song score board in the current scene.
    /// Creates Canvas &gt; Panel &gt; Content[VerticalLayout] with a title, subtitle, big accuracy
    /// readout, letter grade, a Score / Correct / Incorrect / Missed / Best-streak breakdown, and
    /// Play Again + Close buttons. Adds a <see cref="ScoreBoardSummary"/>, auto-wires its text/button
    /// references, and links the scene's <see cref="NoteEvaluator"/> + <see cref="FallingNotesController"/>.
    ///
    /// Like the song-menu builder this uses ONLY standard Unity UI APIs (no Meta Interaction types),
    /// and adds a <see cref="ControllerUIPointer"/> — with its <c>targetCanvas</c> set to THIS canvas —
    /// so it is clickable on Quest without grabbing the song menu's canvas by mistake.
    ///
    /// Menu: Tools &gt; Piano Learning &gt; Create Score Board Summary
    /// </summary>
    public static class ScoreBoardBuilder
    {
        private static readonly Color PanelColor   = new Color(0.05f, 0.06f, 0.10f, 0.92f);
        private static readonly Color LabelColor   = new Color(0.80f, 0.82f, 0.88f, 1f);
        private static readonly Color GreenValue   = new Color(0.25f, 0.90f, 0.45f, 1f);
        private static readonly Color RedValue     = new Color(0.95f, 0.40f, 0.35f, 1f);
        private static readonly Color GreyValue    = new Color(0.65f, 0.65f, 0.68f, 1f);

        [MenuItem("Tools/Piano Learning/Create Score Board Summary")]
        public static void Create()
        {
            const string undoLabel = "Create Score Board Summary";

            // 1. EventSystem — required for ANY uGUI clicking (mouse in editor, ray in VR).
#pragma warning disable CS0618 // FindObjectOfType is fine here and works on every Unity version
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, undoLabel);
            }
            var evaluator  = Object.FindObjectOfType<NoteEvaluator>();
            var controller = Object.FindObjectOfType<FallingNotesController>();
#pragma warning restore CS0618

            // 2. World-space Canvas.
            var canvasGo = new GameObject("ScoreBoardCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, undoLabel);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main; // lets the mouse hit it in the Game view

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(880f, 800f);
            canvasGo.transform.localScale = Vector3.one * 0.0009f;     // ~0.79m x 0.72m
            canvasGo.transform.position = new Vector3(0f, 1.3f, 0.6f);
            // Same facing rule as the song menu: face +Z (identity). A 180° Y turn renders uGUI mirrored.
            canvasGo.transform.rotation = Quaternion.identity;

            // 3. Panel (this is what ScoreBoardSummary shows/hides).
            var panel = CreateChild("Panel", canvasRt);
            Stretch(panel);
            panel.gameObject.AddComponent<Image>().color = PanelColor;

            // 4. Content — one vertical stack drives the whole layout, so sizes stay tidy.
            var content = CreateChild("Content", panel);
            Stretch(content);
            content.offsetMin = new Vector2(36f, 36f);
            content.offsetMax = new Vector2(-36f, -36f);
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 5. Heading block.
            CreateText("Title", content, "Song Complete!", 64f, Color.white,
                TextAlignmentOptions.Center, FontStyles.Bold, 78f);
            var subtitle = CreateText("Subtitle", content, "Song  (Mode)", 30f, LabelColor,
                TextAlignmentOptions.Center, FontStyles.Normal, 38f);
            var accuracy = CreateText("Accuracy", content, "100.0%", 100f, GreenValue,
                TextAlignmentOptions.Center, FontStyles.Bold, 108f);
            var grade = CreateText("Grade", content, "Grade A", 34f, GreenValue,
                TextAlignmentOptions.Center, FontStyles.Bold, 42f);

            CreateSpacer("Gap", content, 8f);

            // 6. Stat breakdown — one [label .......... value] row each.
            var scoreVal     = CreateStatRow("ScoreRow",     content, "Score",       "0",     Color.white);
            var correctVal   = CreateStatRow("CorrectRow",   content, "Correct",     "0 / 0", GreenValue);
            var incorrectVal = CreateStatRow("IncorrectRow", content, "Incorrect",   "0",     RedValue);
            var missedVal    = CreateStatRow("MissedRow",    content, "Missed",      "0",     GreyValue);
            var streakVal    = CreateStatRow("StreakRow",    content, "Best streak", "0",     Color.white);

            CreateSpacer("Gap2", content, 8f);

            // 7. Button row.
            var buttonRow = CreateChild("Buttons", content);
            buttonRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 92f;
            var bhlg = buttonRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            bhlg.spacing = 24f;
            bhlg.childAlignment = TextAnchor.MiddleCenter;
            bhlg.childControlWidth = true;
            bhlg.childControlHeight = true;
            bhlg.childForceExpandWidth = false;
            bhlg.childForceExpandHeight = true;

            var playAgainBtn = CreateButton("PlayAgainButton", "Play Again", buttonRow,
                new Color(0.18f, 0.52f, 0.30f, 1f), 280f);
            var closeBtn = CreateButton("CloseButton", "Close", buttonRow,
                new Color(0.30f, 0.32f, 0.38f, 1f), 220f);

            // 8. Controller on the Canvas root (so hiding the Panel keeps it alive), fully wired.
            var summary = canvasGo.AddComponent<ScoreBoardSummary>();
            summary.evaluator = evaluator;
            summary.controller = controller;
            summary.panelRoot = panel.gameObject;
            summary.subtitleText = subtitle;
            summary.accuracyText = accuracy;
            summary.gradeText = grade;
            summary.scoreText = scoreVal;
            summary.correctText = correctVal;
            summary.incorrectText = incorrectVal;
            summary.missedText = missedVal;
            summary.streakText = streakVal;
            summary.playAgainButton = playAgainBtn;
            summary.closeButton = closeBtn;

            // 9. VR laser, pinned to THIS canvas so it doesn't collect the song menu's buttons.
            var pointer = canvasGo.AddComponent<ControllerUIPointer>();
            pointer.targetCanvas = canvas;

            EditorUtility.SetDirty(summary);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            Debug.Log("[ScoreBoardBuilder] Created 'ScoreBoardCanvas'. " +
                      (evaluator == null
                          ? "No NoteEvaluator found in the scene — drag one onto the ScoreBoardSummary's " +
                            "'Evaluator' field so it can listen for song completion."
                          : $"Linked to NoteEvaluator '{evaluator.name}'") +
                      (controller == null
                          ? ". No FallingNotesController found — assign one for the song title + Play Again."
                          : $" and FallingNotesController '{controller.name}'.") +
                      " The panel is visible now for layout; it hides itself at runtime until a song " +
                      "finishes. A ControllerUIPointer was added for VR clicking.");
        }

        // ---------------- Builder helpers ----------------

        // A [ Label .......... Value ] row. Returns the VALUE text so the builder can wire it.
        private static TMP_Text CreateStatRow(string name, RectTransform parent,
                                              string labelText, string valueText, Color valueColor)
        {
            var rowRt = CreateChild(name, parent);
            rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 50f;
            var hlg = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.padding = new RectOffset(20, 20, 0, 0);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            var labelRt = CreateChild("Label", rowRt);
            labelRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var labelTmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            labelTmp.text = labelText;
            labelTmp.fontSize = 32f;
            labelTmp.alignment = TextAlignmentOptions.Left;
            labelTmp.color = LabelColor;

            var valueRt = CreateChild("Value", rowRt);
            var valueLe = valueRt.gameObject.AddComponent<LayoutElement>();
            valueLe.preferredWidth = 200f;
            valueLe.flexibleWidth = 0f;
            var valueTmp = valueRt.gameObject.AddComponent<TextMeshProUGUI>();
            valueTmp.text = valueText;
            valueTmp.fontSize = 32f;
            valueTmp.fontStyle = FontStyles.Bold;
            valueTmp.alignment = TextAlignmentOptions.Right;
            valueTmp.color = valueColor;
            return valueTmp;
        }

        // Image+Button with a centred TMP label; returns the Button.
        private static Button CreateButton(string name, string text, RectTransform parent,
                                           Color color, float preferredWidth)
        {
            var rt = CreateChild(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img; // AddComponent doesn't auto-assign this, so hover/press tint needs it
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.flexibleWidth = 0f;

            var labelRt = CreateChild("Label", rt);
            Stretch(labelRt);
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 30f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return btn;
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

        private static void CreateSpacer(string name, RectTransform parent, float height)
        {
            var rt = CreateChild(name, parent);
            rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
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
