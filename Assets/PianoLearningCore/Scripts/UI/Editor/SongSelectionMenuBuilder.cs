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
            hlg.childForceExpandHeight = false;

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

            // Controller laser pointer so the menu is clickable on Quest without any Meta
            // Interaction SDK wiring. Auto-finds the controller anchor + this canvas at runtime.
            canvasGo.AddComponent<ControllerUIPointer>();

            EditorUtility.SetDirty(menu);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            Debug.Log("[SongSelectionMenuBuilder] Created 'SongSelectionCanvas' with per-song " +
                      "Practice/Assessment buttons. " +
                      (launcher == null
                          ? "No SongLauncher found in the scene — add one (Play On Start = false) and " +
                            "drag it onto the SongSelectionMenu's 'Launcher' field."
                          : $"Linked to SongLauncher '{launcher.name}'.") +
                      " A ControllerUIPointer was added for VR clicking (auto-finds the rig's " +
                      "controller anchor). Position the canvas in front of the player and you're set.");
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
