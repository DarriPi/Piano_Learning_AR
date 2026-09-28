using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PianoLearningCore
{
    /// <summary>
    /// Builds the <see cref="HelpSlideshow"/> into the Help panel: a 16:9 picture/video area with
    /// Previous / Next arrows either side, a caption under it and a clickable segmented progress
    /// bar, all sized for the 960 x 620 SongSelectionCanvas.
    ///
    /// Tools &gt; Piano Learning &gt; Add Help Slideshow upgrades the Help panel already in the open
    /// scene in place (it replaces the old text Body, and rebuilds the slideshow if run again),
    /// and relabels the song list's "?" button "Align".
    /// <see cref="SongSelectionMenuBuilder"/> calls <see cref="BuildInto"/> so a fresh menu gets it
    /// too.
    ///
    /// The slide list (media + captions) lives in <see cref="Slides"/>; the media sits in
    /// <see cref="MediaFolder"/>.
    /// </summary>
    public static class HelpSlideshowBuilder
    {
        public const string MediaFolder = "Assets/PianoLearningCore/Tutorial";

        private const string Teal = "#33FFD9";   // KeyboardAlignmentGuide hit line
        private const string Pink = "#FF4D99";   // KeyboardAlignmentGuide Middle-C post

        // (media file in MediaFolder, or null for a text-only slide; caption)
        private static readonly (string media, string caption)[] Slides =
        {
            ("1_LocatePinkPost.png",
             "<b>1. Find Middle C</b>\n" +
             $"The tall <color={Pink}>pink</color> post is <b>Middle C</b>. " +
             "Put it on Middle C of your real piano."),
            ("2_Move.mp4",
             "<b>2. Move</b>\n" +
             "<b>Right stick</b> slides the keyboard. Hold the <b>right trigger</b> to move faster."),
            ("3_RaiseLower.mp4",
             "<b>3. Raise / lower</b>\n" +
             "<b>Left stick</b> up / down raises and lowers it. Left / right rotates it."),
            ("4_NarrowWider.mp4",
             "<b>4. Change width</b>\n" +
             "Hold <b>X</b> for narrower, <b>Y</b> for wider, until the keys match your piano."),
            ("5_LineUp.png",
             "<b>5. Line up, then press A</b>\n" +
             $"The <color={Teal}>teal</color> line sits on the front edge of your keys. " +
             "Press <b>A</b> when done (<b>B</b> resets)."),
            ("6_Practice.mp4",
             "<b>6. Practice mode</b>\n" +
             "Play each note as it reaches the line. The song waits for the right note — play a " +
             "wrong key and the note you needed flashes <color=#FF4D4D>red</color>."),
            ("7_Assessment.mp4",
             "<b>7. Assessment mode</b>\n" +
             "The song doesn't wait — you are scored as you play. " +
             "<color=#33CC66>Green</color> = correct · <color=#AAAAAA>Grey</color> = missed " +
             "(no red hints here)."),
        };

        // Layout on the 960 x 620 canvas (units, measured from the panel's top edge unless noted).
        private const float TitleHeight = 52f;
        private const float HintTop = 60f;
        private const float HintHeight = 26f;
        private const float MediaTop = 92f;
        private const float MediaHeight = 306f;                      // 16:9 -> 544 wide
        private const float MediaWidth = MediaHeight * 16f / 9f;
        private const float CaptionTop = MediaTop + MediaHeight + 8f;
        private const float CaptionHeight = 70f;
        private const float ProgressTop = CaptionTop + CaptionHeight + 4f;
        private const float ProgressHitHeight = 36f;                 // laser target; the bar is thinner
        private const float ProgressBarHeight = 10f;
        private const float ProgressWidth = 480f;
        private const float ArrowSize = 88f;
        private const float ArrowGap = 24f;

        // Shown on the Help panel for its whole life: the calibrator only runs while Help is open
        // (HelpPanel hands it the thumbstick), which is not obvious once the panel is closed.
        private const string AlignHint =
            "You can only move the keyboard while this window is open. " +
            "Reopen it any time with <b>Align</b>.";

        public const string AlignButtonLabel = "Align";

        private static readonly Color ArrowColor = new Color(0.18f, 0.40f, 0.62f, 1f); // matches Close

        [MenuItem("Tools/Piano Learning/Add Help Slideshow")]
        public static void AddToScene()
        {
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            var helpPanel = Object.FindObjectOfType<HelpPanel>();
            var allHelpPanels = Object.FindObjectsOfType<HelpPanel>();
#pragma warning restore CS0618
            // Two menus stack in the same spot and one laser click advances both, so a stale copy
            // shows through the updated one. Only one gets upgraded here; say which.
            if (allHelpPanels.Length > 1)
                Debug.LogWarning($"[HelpSlideshowBuilder] {allHelpPanels.Length} HelpPanels in the scene " +
                                 $"({string.Join(", ", System.Array.ConvertAll(allHelpPanels, p => ScenePath(p.transform)))}). " +
                                 $"Only '{ScenePath(helpPanel.transform)}' is being updated — delete the " +
                                 "extra menu canvas, or it will draw over this one.", helpPanel);
            if (helpPanel == null || helpPanel.helpPanelRoot == null)
            {
                EditorUtility.DisplayDialog("Add Help Slideshow",
                    "No HelpPanel with a 'Help Panel Root' in the open scene. Run Tools > Piano " +
                    "Learning > Create Song Selection Menu first.", "OK");
                return;
            }

            var help = (RectTransform)helpPanel.helpPanelRoot.transform;
            Undo.RegisterFullObjectHierarchyUndo(help.gameObject, "Add Help Slideshow");

            // The old text instructions now live in the slides.
            var oldBody = help.Find("Body");
            if (oldBody != null) Undo.DestroyObjectImmediate(oldBody.gameObject);

            var slideshow = BuildInto(help, helpPanel.closeButton);
            if (helpPanel.helpButton != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(helpPanel.helpButton.gameObject, "Add Help Slideshow");
                StyleAlignButton(helpPanel.helpButton);
            }
            Undo.RegisterCreatedObjectUndo(slideshow.gameObject, "Add Help Slideshow");

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(help.gameObject.scene);
            Selection.activeGameObject = slideshow.gameObject;
            Debug.Log($"[HelpSlideshowBuilder] Added a {slideshow.slides.Length}-slide slideshow to " +
                      $"'{help.name}' and relabelled the menu's help button '{AlignButtonLabel}'. Media and captions: {MediaFolder} + HelpSlideshowBuilder.Slides.");
        }

        /// <summary>
        /// Builds (or rebuilds) the slideshow under <paramref name="help"/> and tightens the panel's
        /// Title and Close button so everything fits the canvas.
        /// </summary>
        public static HelpSlideshow BuildInto(RectTransform help, Button closeButton)
        {
            var existing = help.Find("Slideshow");
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            // Title a little shorter, Close a little lower, to make room for the media.
            var title = help.Find("Title") as RectTransform;
            if (title != null)
            {
                title.sizeDelta = new Vector2(title.sizeDelta.x, TitleHeight);
                title.anchoredPosition = new Vector2(0f, -8f);
                var titleText = title.GetComponent<TextMeshProUGUI>();
                if (titleText != null) titleText.fontSize = 40f;
            }
            if (closeButton != null)
            {
                var closeRt = (RectTransform)closeButton.transform;
                closeRt.sizeDelta = new Vector2(220f, 56f);
                closeRt.anchoredPosition = new Vector2(0f, 16f);
            }

            var root = CreateChild("Slideshow", help);
            Stretch(root);
            var show = root.gameObject.AddComponent<HelpSlideshow>();

            var hintRt = CreateChild("AlignHint", root);
            AnchorTopCentre(hintRt, 840f, HintHeight, HintTop);
            var hint = hintRt.gameObject.AddComponent<TextMeshProUGUI>();
            hint.text = AlignHint;
            hint.fontSize = 20f;
            hint.fontStyle = FontStyles.Italic;
            hint.alignment = TextAlignmentOptions.Center;
            hint.color = new Color(1f, 0.85f, 0.4f, 1f); // warm, so it reads as a note, not a step
            hint.raycastTarget = false;

            // Media area: black backing so a video's first frame never flashes the panel through.
            var media = CreateChild("Media", root);
            AnchorTopCentre(media, MediaWidth, MediaHeight, MediaTop);
            media.gameObject.AddComponent<Image>().color = Color.black;

            var displayRt = CreateChild("Display", media);
            Stretch(displayRt);
            show.display = displayRt.gameObject.AddComponent<RawImage>();
            show.display.raycastTarget = false;

            show.player = root.gameObject.AddComponent<VideoPlayer>();
            show.player.playOnAwake = false;
            show.player.isLooping = true;
            show.player.renderMode = VideoRenderMode.RenderTexture;
            show.player.audioOutputMode = VideoAudioOutputMode.None; // the mic must not hear it

            var textPageRt = CreateChild("TextPage", media);
            Stretch(textPageRt);
            textPageRt.offsetMin = new Vector2(28f, 20f);
            textPageRt.offsetMax = new Vector2(-28f, -20f);
            show.textPage = textPageRt.gameObject.AddComponent<TextMeshProUGUI>();
            show.textPage.fontSize = 24f;
            show.textPage.alignment = TextAlignmentOptions.MidlineLeft;
            show.textPage.color = Color.white;
            show.textPage.raycastTarget = false;

            var captionRt = CreateChild("Caption", root);
            AnchorTopCentre(captionRt, 840f, CaptionHeight, CaptionTop);
            show.caption = captionRt.gameObject.AddComponent<TextMeshProUGUI>();
            show.caption.fontSize = 24f;
            show.caption.enableAutoSizing = true;       // two lines always fit
            show.caption.fontSizeMin = 16f;
            show.caption.fontSizeMax = 24f;
            show.caption.alignment = TextAlignmentOptions.Top;
            show.caption.color = Color.white;
            show.caption.raycastTarget = false;

            // Arrows either side of the media, vertically centred on it.
            float arrowX = MediaWidth / 2f + ArrowGap + ArrowSize / 2f;
            float arrowY = -(MediaTop + MediaHeight / 2f);
            show.previousButton = CreateArrow("PreviousButton", "<", root, -arrowX, arrowY);
            show.nextButton = CreateArrow("NextButton", ">", root, arrowX, arrowY);

            // Progress bar: one clickable segment per slide.
            var progress = CreateChild("Progress", root);
            AnchorTopCentre(progress, ProgressWidth, ProgressHitHeight, ProgressTop);
            var hlg = progress.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            var slides = new List<HelpSlideshow.Slide>();
            var segments = new List<Button>();
            for (int i = 0; i < Slides.Length; i++)
            {
                slides.Add(LoadSlide(Slides[i].media, Slides[i].caption));
                segments.Add(CreateSegment($"Segment{i + 1}", progress));
            }
            show.slides = slides.ToArray();
            show.progressSegments = segments.ToArray();

            EditorUtility.SetDirty(show);
            return show;
        }

        /// <summary>
        /// The song list's button that reopens Help. Labelled "Align" rather than "?" because
        /// reopening Help is the only way to move the keyboard again.
        /// </summary>
        public static void StyleAlignButton(Button button)
        {
            var rt = (RectTransform)button.transform;
            rt.sizeDelta = new Vector2(120f, 64f); // top-right corner; anchored by the menu builder
            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null)
            {
                label.text = AlignButtonLabel;
                label.fontSize = 24f;
            }
            EditorUtility.SetDirty(button.gameObject);
        }

        private static HelpSlideshow.Slide LoadSlide(string media, string caption)
        {
            var slide = new HelpSlideshow.Slide { caption = caption };
            if (media == null) return slide;

            string path = MediaFolder + "/" + media;
            if (path.EndsWith(".mp4"))
            {
                slide.video = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
            }
            else
            {
                // A picture imported before TutorialMediaImporter existed keeps the default import
                // settings until it is imported again.
                if (AssetImporter.GetAtPath(path) is TextureImporter ti &&
                    ti.textureType != TextureImporterType.Sprite)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                slide.image = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }

            if (slide.video == null && slide.image == null)
                Debug.LogWarning($"[HelpSlideshowBuilder] Missing tutorial media '{path}' — that " +
                                 "slide will show its caption as text only.");
            return slide;
        }

        private static Button CreateArrow(string name, string glyph, RectTransform parent, float x, float y)
        {
            var rt = CreateChild(name, parent);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(ArrowSize, ArrowSize);
            rt.anchoredPosition = new Vector2(x, y);

            var img = rt.gameObject.AddComponent<Image>();
            img.color = ArrowColor;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var labelRt = CreateChild("Label", rt);
            Stretch(labelRt);
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = glyph;
            tmp.fontSize = 52f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return btn;
        }

        // The Button's rect is the tall laser target; the visible bar is a thin child that the
        // slideshow recolours (it is the Button's targetGraphic).
        private static Button CreateSegment(string name, RectTransform parent)
        {
            var rt = CreateChild(name, parent);
            var btn = rt.gameObject.AddComponent<Button>();

            var barRt = CreateChild("Bar", rt);
            barRt.anchorMin = new Vector2(0f, 0.5f);
            barRt.anchorMax = new Vector2(1f, 0.5f);
            barRt.sizeDelta = new Vector2(0f, ProgressBarHeight);
            barRt.anchoredPosition = Vector2.zero;
            var bar = barRt.gameObject.AddComponent<Image>();
            bar.color = new Color(1f, 1f, 1f, 0.18f);
            btn.targetGraphic = bar;
            return btn;
        }

        private static string ScenePath(Transform t) =>
            t.parent == null ? t.name : ScenePath(t.parent) + "/" + t.name;

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

        // Fixed-size rect, horizontally centred, its top edge `top` units below the parent's top.
        private static void AnchorTopCentre(RectTransform rt, float width, float height, float top)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = new Vector2(0f, -top);
        }
    }
}
