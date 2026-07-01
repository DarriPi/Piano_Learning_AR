using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// One-click builder for a world-space Song Selection menu in the current scene.
    /// Creates Canvas > Panel > (Title + Content[VerticalLayout] + SongRowTemplate), where the
    /// row template carries a label plus a Practice and an Assessment button. Adds a
    /// <see cref="SongSelectionMenu"/> and auto-assigns its references plus the two bundled
    /// demo songs.
    ///
    /// This deliberately uses ONLY standard Unity UI APIs (no Meta Interaction types) so it
    /// is safe across SDK versions and can't block compilation. After running it, add the
    /// Meta Interaction ray-canvas wiring by hand for on-device VR clicking.
    ///
    /// Menu: Tools > Piano Learning > Create Song Selection Menu
    /// </summary>
    public static class SongSelectionMenuBuilder
    {
        // Help text (TMP rich text). Colours match the in-scene KeyboardAlignmentGuide markers
        // (pink Middle-C post, teal hit line) and the NoteEvaluator flash colours (green/red/grey).
        // Bullets/dashes are literal UTF-8 and render with the default TMP (LiberationSans) font.
        private const string PositioningHelp =
            "<b>Position your keyboard</b>\n" +
            "• Find the tall <color=#FF4D99>pink</color> post — that's <b>Middle C</b>. " +
            "Slide the keyboard so it sits on Middle C of your real piano. " +
            "(The shorter gold posts mark the other C's.)\n" +
            "• Line up the bright <color=#33FFD9>teal</color> front line with the front edge of your keys.\n" +
            "• Fine-tune with the controllers:  right stick = move · left stick = rotate / raise-lower · " +
            "hold right trigger = faster · X / Y = narrower / wider · B = reset · A = done.";

        private const string HowToPlayHelp =
            "<b>How to play</b>\n" +
            "• Notes fall onto your keys — play the matching key on your real piano as each note reaches the line.\n" +
            "• <color=#33CC66>Green</color> = correct · <color=#FF4D4D>Red</color> = wrong note · " +
            "<color=#AAAAAA>Grey</color> = missed.\n" +
            "• Practice waits for the right note and reveals the answer in red; Assessment scores you in real time.";

        [MenuItem("Tools/Piano Learning/Create Song Selection Menu")]
        public static void Create()
        {
            const string undoLabel = "Create Song Selection Menu";

            // 1. EventSystem — required for ANY uGUI clicking (mouse in editor, ray in VR).
#pragma warning disable CS0618 // FindObjectOfType is fine here and works on every Unity version
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, undoLabel);
            }
            var launcher = Object.FindObjectOfType<SongLauncher>();
#pragma warning restore CS0618

            // 2. World-space Canvas.
            var canvasGo = new GameObject("SongSelectionCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, undoLabel);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main; // lets the mouse hit it in the Game view

            var canvasRt = canvasGo.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(960f, 620f);
            canvasGo.transform.localScale = Vector3.one * 0.0009f;        // ~0.86m x 0.56m
            canvasGo.transform.position = new Vector3(0f, 1.2f, 0.6f);
            // A world-space Canvas is readable from the side its +Z (forward) points AWAY from.
            // The user spawns near the origin facing +Z, so the canvas must also face +Z (identity).
            // A 180° Y rotation turns its back to the user and the UI renders MIRRORED — do not add it.
            canvasGo.transform.rotation = Quaternion.identity;

            // 3. Panel (this is what we hide when a song starts).
            var panel = CreateChild("Panel", canvasRt);
            Stretch(panel);
            panel.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.10f, 0.85f);

            // 4. Title.
            var title = CreateChild("Title", panel);
            AnchorTop(title, 80f);
            var titleTmp = title.gameObject.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "Select a Song";
            titleTmp.fontSize = 44f;
            titleTmp.alignment = TextAlignmentOptions.Center;
            titleTmp.color = Color.white;

            // 5. Content (the vertical list the rows go into).
            var content = CreateChild("Content", panel);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(40f, 40f);
            content.offsetMax = new Vector2(-40f, -100f); // leave room for the title
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 14f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // 6. Row template: [ Label .......... | Practice | Assessment ], kept hidden.
            var rowRt = CreateChild("SongRowTemplate", content);
            rowRt.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.04f);
            rowRt.gameObject.AddComponent<LayoutElement>().preferredHeight = 84f;
            var hlg = rowRt.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.padding = new RectOffset(16, 16, 8, 8);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            // Expand children to the row's full height so the mode buttons get a real hit
            // rectangle. With this false they'd collapse to ~zero height (no preferred height set)
            // and the controller-laser ray could never intersect their quad. See SongSelectionMenu.Build.
            hlg.childForceExpandHeight = true;

            // 6a. Title label (takes the remaining width).
            var rowLabel = CreateChild("Label", rowRt);
            rowLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            var rowLabelTmp = rowLabel.gameObject.AddComponent<TextMeshProUGUI>();
            rowLabelTmp.text = "Song";
            rowLabelTmp.fontSize = 30f;
            rowLabelTmp.alignment = TextAlignmentOptions.Left;
            rowLabelTmp.color = Color.white;

            // 6b. Mode buttons.
            var practiceBtn = CreateModeButton("PracticeButton", "Practice", rowRt,
                new Color(0.18f, 0.52f, 0.30f, 1f), 170f);
            var assessmentBtn = CreateModeButton("AssessmentButton", "Assessment", rowRt,
                new Color(0.72f, 0.45f, 0.12f, 1f), 210f);

            // 6c. The SongRow component that ties it together.
            var songRow = rowRt.gameObject.AddComponent<SongRow>();
            songRow.label = rowLabelTmp;
            songRow.practiceButton = practiceBtn;
            songRow.assessmentButton = assessmentBtn;
            rowRt.gameObject.SetActive(false);

            // 7. Controller on the Canvas root (so hiding the Panel keeps it alive), wired up.
            var menu = canvasGo.AddComponent<SongSelectionMenu>();
            menu.launcher = launcher;
            menu.contentParent = content;
            menu.rowTemplate = songRow;
            menu.panelRoot = panel.gameObject;
            menu.songs = new List<SongEntry>
            {
                new SongEntry
                {
                    displayName = "Twinkle Twinkle Little Star",
                    streamingAssetsPath = "Songs/twinkle-twinkle-little-star.mid"
                },
                new SongEntry
                {
                    displayName = "Example",
                    streamingAssetsPath = "Songs/example.mid"
                },
            };

            // 8. "?" help button in the TOP-RIGHT corner of the menu Panel. Lives on the Panel so
            //    it goes inactive (unclickable) whenever the list is hidden.
            var helpButton = CreateButton("HelpButton", "?", panel,
                new Color(0.18f, 0.40f, 0.62f, 1f), 36f);
            var helpButtonRt = (RectTransform)helpButton.transform;
            helpButtonRt.anchorMin = new Vector2(1f, 1f);
            helpButtonRt.anchorMax = new Vector2(1f, 1f);
            helpButtonRt.pivot = new Vector2(1f, 1f);
            helpButtonRt.sizeDelta = new Vector2(64f, 64f);
            helpButtonRt.anchoredPosition = new Vector2(-16f, -16f);

            // 9. Help panel — a sibling of the menu Panel under the SAME canvas, shown at startup
            //    and via "?". Because the existing ControllerUIPointer only sees ACTIVE buttons and
            //    only one panel is ever active, it drives this panel's Close button too with no
            //    extra pointer (one laser, always).
            var help = CreateChild("HelpPanel", canvasRt);
            Stretch(help);
            help.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.10f, 0.92f);

            var helpTitle = CreateText("Title", "How to Use", help, 44f, TextAlignmentOptions.Center);
            AnchorTop(helpTitle.rectTransform, 80f);

            var helpBody = CreateChild("Body", help);
            helpBody.anchorMin = Vector2.zero;
            helpBody.anchorMax = Vector2.one;
            helpBody.offsetMin = new Vector2(60f, 120f);   // leave room for the Close button
            helpBody.offsetMax = new Vector2(-60f, -100f);  // leave room for the title
            var helpVlg = helpBody.gameObject.AddComponent<VerticalLayoutGroup>();
            helpVlg.spacing = 20f;
            helpVlg.childAlignment = TextAnchor.UpperLeft;
            helpVlg.childControlWidth = true;
            helpVlg.childControlHeight = true;
            helpVlg.childForceExpandWidth = true;
            helpVlg.childForceExpandHeight = false;

            CreateText("Positioning", PositioningHelp, helpBody, 26f, TextAlignmentOptions.TopLeft);
            CreateText("HowToPlay", HowToPlayHelp, helpBody, 26f, TextAlignmentOptions.TopLeft);

            var closeButton = CreateButton("CloseButton", "Close", help,
                new Color(0.18f, 0.40f, 0.62f, 1f), 28f);
            var closeRt = (RectTransform)closeButton.transform;
            closeRt.anchorMin = new Vector2(0.5f, 0f);
            closeRt.anchorMax = new Vector2(0.5f, 0f);
            closeRt.pivot = new Vector2(0.5f, 0f);
            closeRt.sizeDelta = new Vector2(220f, 64f);
            closeRt.anchoredPosition = new Vector2(0f, 30f);

            // 10. Help controller on the canvas root, wired to both panels + buttons. It shows Help
            //     and hides the list on Start, so the app opens on the instructions, not a song.
            var helpPanel = canvasGo.AddComponent<HelpPanel>();
            helpPanel.helpPanelRoot = help.gameObject;
            helpPanel.menu = menu;
            helpPanel.closeButton = closeButton;
            helpPanel.helpButton = helpButton;

            // Controller laser pointer so the menu is clickable on Quest without any Meta
            // Interaction SDK wiring. Pin it to THIS canvas (like the score/playback builders) so it
            // never grabs another canvas; it still auto-finds the controller anchor at runtime.
            var pointer = canvasGo.AddComponent<ControllerUIPointer>();
            pointer.targetCanvas = canvas;

            EditorUtility.SetDirty(menu);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            Debug.Log("[SongSelectionMenuBuilder] Created 'SongSelectionCanvas' with a startup Help " +
                      "panel (keyboard positioning + how to play), a top-right '?' button, and per-song " +
                      "Practice/Assessment buttons. " +
                      (launcher == null
                          ? "No SongLauncher found in the scene — add one and drag it onto the " +
                            "SongSelectionMenu's 'Launcher' field."
                          : $"Linked to SongLauncher '{launcher.name}'.") +
                      " IMPORTANT: set the SongLauncher's 'Play On Start' to FALSE so the app opens on " +
                      "the Help panel instead of launching straight into a song. A ControllerUIPointer " +
                      "was added for VR clicking (auto-finds the rig's controller anchor). Position the " +
                      "canvas in front of the player and you're set.");
        }

        // Creates an Image+Button with a centred TMP label; returns the Button.
        private static Button CreateModeButton(string name, string text, RectTransform parent,
                                               Color color, float preferredWidth)
        {
            var rt = CreateChild(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img; // so highlight/pressed tint works (AddComponent doesn't auto-assign it)
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.flexibleWidth = 0f;

            var labelRt = CreateChild("Label", rt);
            Stretch(labelRt);
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 26f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return btn;
        }

        // Image+Button with a centred TMP label, positioned by the caller via its RectTransform
        // (unlike CreateModeButton, this one isn't sized by a layout group).
        private static Button CreateButton(string name, string text, RectTransform parent,
                                           Color color, float fontSize)
        {
            var rt = CreateChild(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img; // AddComponent doesn't auto-assign it, so the tint works

            var labelRt = CreateChild("Label", rt);
            Stretch(labelRt);
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return btn;
        }

        // A TMP text block (rich text enabled by default), returned so the caller can anchor it.
        private static TextMeshProUGUI CreateText(string name, string text, RectTransform parent,
                                                  float fontSize, TextAlignmentOptions align)
        {
            var rt = CreateChild(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.color = Color.white;
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

        private static void AnchorTop(RectTransform rt, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, -10f);
        }
    }
}
