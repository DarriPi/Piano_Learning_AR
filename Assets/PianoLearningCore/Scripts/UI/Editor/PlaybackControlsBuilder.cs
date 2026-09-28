using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One-click builder for the in-world <see cref="PlaybackControls"/> bar (step 2). Creates a
    /// compact world-space row — Pause/Resume, Restart, and tempo −/readout/+ — wires it to the
    /// scene's <see cref="FallingNotesController"/> (+ <see cref="NoteEvaluator"/> for the Restart
    /// score reset), and adds a <see cref="ControllerUIPointer"/> pinned to THIS canvas so it stays
    /// clickable on Quest without grabbing another canvas's buttons.
    ///
    /// Interactive, so — like the score-board builder, and unlike the display-only HUD — it ensures an
    /// EventSystem exists. Uses only standard Unity UI APIs (no Meta Interaction types). Parked off to
    /// the side and semi-transparent so it keeps clear of the keys and the falling notes; tune the
    /// Transform on-device.
    ///
    /// Menu: Tools &gt; Piano Learning &gt; Create Playback Controls
    /// </summary>
    public static class PlaybackControlsBuilder
    {
        // Semi-transparent so MR passthrough shows through the bar's backing (the buttons stay opaque).
        private static readonly Color BarColor   = new Color(0.05f, 0.06f, 0.10f, 0.62f);
        private static readonly Color TempoColor = new Color(0.85f, 0.87f, 0.92f, 1f);
        private static readonly Color ButtonColor = new Color(0.30f, 0.32f, 0.38f, 1f);
        // Red-ish so Stop reads as the "leave this song" action and is hard to confuse with the rest.
        private static readonly Color StopColor    = new Color(0.62f, 0.24f, 0.22f, 1f);

        [MenuItem("Tools/Piano Learning/Create Playback Controls")]
        public static void Create()
        {
            const string undoLabel = "Create Playback Controls";

            // EventSystem — required for ANY uGUI clicking (mouse in editor, ray in VR).
#pragma warning disable CS0618 // FindObjectOfType is fine here and works on every Unity version
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, undoLabel);
            }
            var controller = Object.FindObjectOfType<FallingNotesController>();
            var evaluator  = Object.FindObjectOfType<NoteEvaluator>();
            var songMenu   = Object.FindObjectOfType<SongSelectionMenu>();
#pragma warning restore CS0618

            var canvasGo = new GameObject("PlaybackControlsCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, undoLabel);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main; // lets the mouse hit it in the Game view

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(900f, 150f); // wider to fit the Stop button
            canvasGo.transform.localScale = Vector3.one * 0.0008f;       // ~0.58m x 0.12m bar
            // Parked off to the player's side so it is reachable by the controller ray without sitting
            // over the keys or the falling notes (which live low and centred). TUNE ON-DEVICE.
            canvasGo.transform.position = new Vector3(0.55f, 1.2f, 0.5f);
            // Same facing rule as the score board: identity faces the player; a 180° Y turn mirrors uGUI.
            canvasGo.transform.rotation = Quaternion.identity;

            // Panel (this is what PlaybackControls shows/hides).
            var panel = CreateChild("Panel", canvasRt);
            Stretch(panel);
            panel.gameObject.AddComponent<Image>().color = BarColor;

            // Content — one horizontal row.
            var content = CreateChild("Content", panel);
            Stretch(content);
            content.offsetMin = new Vector2(16f, 16f);
            content.offsetMax = new Vector2(-16f, -16f);
            var hlg = content.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 14f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            // Pause/Resume | Restart | Stop | − | 1.0x | +
            var pauseBtn   = CreateButton("PauseResumeButton", "Pause", content,
                new Color(0.18f, 0.40f, 0.60f, 1f), 180f, out TMP_Text pauseLabel);
            var restartBtn = CreateButton("RestartButton", "Restart", content, ButtonColor, 160f, out _);
            var stopBtn    = CreateButton("StopButton", "Stop", content, StopColor, 150f, out _);
            var downBtn    = CreateButton("TempoDownButton", "-", content, ButtonColor, 76f, out _);
            var tempoLabel = CreateText("Tempo", content, "1.0x", 34f, TempoColor,
                TextAlignmentOptions.Center, FontStyles.Bold, preferredHeight: 0f, preferredWidth: 100f);
            var upBtn      = CreateButton("TempoUpButton", "+", content, ButtonColor, 76f, out _);

            // Controller on the Canvas root (so hiding the Panel keeps it alive), fully wired.
            var controls = canvasGo.AddComponent<PlaybackControls>();
            controls.controller = controller;
            controls.evaluator = evaluator;
            controls.panelRoot = panel.gameObject;
            controls.pauseResumeButton = pauseBtn;
            controls.restartButton = restartBtn;
            controls.stopButton = stopBtn;
            controls.songMenu = songMenu;
            controls.tempoDownButton = downBtn;
            controls.tempoUpButton = upBtn;
            controls.pauseResumeLabel = pauseLabel;
            controls.tempoLabel = tempoLabel;

            // VR laser, pinned to THIS canvas so it doesn't collect another canvas's buttons.
            var pointer = canvasGo.AddComponent<ControllerUIPointer>();
            pointer.targetCanvas = canvas;

            EditorUtility.SetDirty(controls);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            Debug.Log("[PlaybackControlsBuilder] Created 'PlaybackControlsCanvas'. " +
                      (controller == null
                          ? "No FallingNotesController found — assign one to the PlaybackControls 'Controller' field."
                          : $"Linked to FallingNotesController '{controller.name}'") +
                      (evaluator == null
                          ? "."
                          : $" and NoteEvaluator '{evaluator.name}' (Restart resets the score).") +
                      " Visible now for layout; at runtime it shows during a song (stays up while paused) " +
                      "and hides when the song ends. A ControllerUIPointer was added for VR clicking — " +
                      "nudge the Transform in-headset so it stays clear of the keys and notes.");
        }

        // ---------------- Builder helpers ----------------

        // Image+Button with a centred TMP label. Returns the Button and outputs its label TMP_Text.
        private static Button CreateButton(string name, string text, RectTransform parent,
                                           Color color, float preferredWidth, out TMP_Text label)
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
            label = tmp;
            return btn;
        }

        private static TMP_Text CreateText(string name, RectTransform parent, string text,
                                           float fontSize, Color color, TextAlignmentOptions align,
                                           FontStyles style, float preferredHeight, float preferredWidth = 0f)
        {
            var rt = CreateChild(name, parent);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            if (preferredHeight > 0f) le.preferredHeight = preferredHeight;
            if (preferredWidth > 0f) le.preferredWidth = preferredWidth;
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
