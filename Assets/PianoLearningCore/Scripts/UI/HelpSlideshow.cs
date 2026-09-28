using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace PianoLearningCore
{
    /// <summary>
    /// The step-by-step "How to Use" slideshow inside the Help panel: one picture or looping video
    /// per step, a caption under it, Previous / Next arrows either side, and a segmented progress
    /// bar whose segments jump straight to that step.
    ///
    /// Every control is a plain uGUI <see cref="Button"/>, so the canvas's existing
    /// <see cref="ControllerUIPointer"/> laser drives it with no extra input code — the thumbstick
    /// stays with the keyboard calibrator while Help is open (see <see cref="HelpPanel"/>).
    ///
    /// Videos play through one <see cref="VideoPlayer"/> into a RenderTexture shown by the same
    /// <see cref="RawImage"/> that shows the pictures. They are muted: the mic is listening for
    /// piano notes and must not hear the tutorial. A slide with neither picture nor video shows its
    /// caption as a full text page instead.
    ///
    /// Reopening Help (the "Align" button) starts again from step 1.
    ///
    /// Built automatically by Tools &gt; Piano Learning &gt; Add Help Slideshow.
    /// </summary>
    public class HelpSlideshow : MonoBehaviour
    {
        [Serializable]
        public class Slide
        {
            [Tooltip("Picture for this step. Ignored if a video is set.")]
            public Texture image;

            [Tooltip("Looping, muted video for this step.")]
            public VideoClip video;

            [Tooltip("Text under the picture (TMP rich text). With no picture or video it fills the " +
                     "media area instead.")]
            [TextArea(2, 6)]
            public string caption;
        }

        public Slide[] slides = new Slide[0];

        [Header("References")]
        public RawImage display;
        public VideoPlayer player;
        public TextMeshProUGUI caption;

        [Tooltip("Shown in place of the picture for a text-only slide.")]
        public TextMeshProUGUI textPage;

        public Button previousButton;
        public Button nextButton;

        [Tooltip("One per slide, left to right. Clicking one jumps to that slide.")]
        public Button[] progressSegments = new Button[0];

        [Header("Progress bar colours")]
        public Color currentColor = new Color(0.20f, 1.00f, 0.85f, 1f);  // the teal of the hit line
        public Color doneColor = new Color(0.20f, 1.00f, 0.85f, 0.45f);
        public Color todoColor = new Color(1f, 1f, 1f, 0.18f);

        private int _index;
        private RenderTexture _videoTexture;
        private bool _wired;

        public int Index => _index;

        private void OnEnable()
        {
            Wire();
            Show(0);
        }

        private void OnDisable()
        {
            if (player != null) player.Stop();
        }

        private void OnDestroy()
        {
            if (_videoTexture != null)
            {
                _videoTexture.Release();
                Destroy(_videoTexture);
            }
        }

        public void Next() => Show(_index + 1);
        public void Previous() => Show(_index - 1);

        public void Show(int index)
        {
            if (slides == null || slides.Length == 0) return;
            _index = Mathf.Clamp(index, 0, slides.Length - 1);
            Slide s = slides[_index];

            bool hasVideo = s.video != null && player != null;
            bool hasImage = !hasVideo && s.image != null;
            bool textOnly = !hasVideo && !hasImage;

            if (player != null) player.Stop();

            if (display != null)
            {
                display.enabled = !textOnly;
                if (hasImage) display.texture = s.image;
                if (hasVideo) display.texture = PrepareVideoTexture();
            }

            if (hasVideo)
            {
                player.clip = s.video;
                player.Play();
            }

            if (textPage != null)
            {
                textPage.gameObject.SetActive(textOnly);
                textPage.text = textOnly ? s.caption : string.Empty;
            }
            if (caption != null) caption.text = textOnly ? string.Empty : s.caption;

            if (previousButton != null) previousButton.interactable = _index > 0;
            if (nextButton != null) nextButton.interactable = _index < slides.Length - 1;

            for (int i = 0; i < progressSegments.Length; i++)
            {
                Button seg = progressSegments[i];
                if (seg == null) continue;
                seg.gameObject.SetActive(i < slides.Length);
                // The visible bar is the segment's child graphic; the Button's own rect is taller so
                // the laser can hit it easily.
                Graphic bar = seg.targetGraphic;
                if (bar != null)
                    bar.color = i == _index ? currentColor : i < _index ? doneColor : todoColor;
            }
        }

        // One RenderTexture for every video, cleared to black on each switch so the first frames of
        // a new clip never show the last frame of the previous one.
        private RenderTexture PrepareVideoTexture()
        {
            if (_videoTexture == null)
            {
                _videoTexture = new RenderTexture(1280, 720, 0) { name = "HelpSlideshowVideo" };
                _videoTexture.Create();
            }

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _videoTexture;
            GL.Clear(false, true, Color.black);
            RenderTexture.active = previous;

            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = _videoTexture;
            return _videoTexture;
        }

        private void Wire()
        {
            if (_wired) return;
            _wired = true;

            if (player != null)
            {
                player.playOnAwake = false;
                player.isLooping = true;
                player.source = VideoSource.VideoClip;
                player.audioOutputMode = VideoAudioOutputMode.None;
            }

            if (previousButton != null) previousButton.onClick.AddListener(Previous);
            if (nextButton != null) nextButton.onClick.AddListener(Next);
            for (int i = 0; i < progressSegments.Length; i++)
            {
                if (progressSegments[i] == null) continue;
                int target = i;
                progressSegments[i].onClick.AddListener(() => Show(target));
            }
        }
    }
}
