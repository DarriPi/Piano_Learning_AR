using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// The Detection strip at the top of the song menu — how the app hears the player:
    /// <b>Microphone</b>, <b>MIDI</b> (a USB MIDI keyboard) or <b>Free Play</b> (nothing listens).
    /// Choosing one applies it at once through <see cref="NoteEvaluator.SetDetection"/>, so picking
    /// MIDI or Free Play switches the mic off right there in the menu (the MIC ON badge goes).
    /// Practice and Assessment both still play in Free Play — nothing is judged and neither gives a
    /// score.
    ///
    /// Microphone is selected at every launch and the choice is never saved: the study headset is
    /// shared, and the next participant must not inherit a mode the last one left on.
    ///
    /// The MIDI button is only clickable while a MIDI device is plugged in: its second line names the
    /// device, or reads "not connected" (greyed out, so the controller laser skips it). Pull the
    /// cable while MIDI is chosen and the strip goes back to Microphone, so a mode that can't hear
    /// anything is never left selected. Where MIDI can't work at all (the Editor, or a build without
    /// the Java half) it reads "not available".
    ///
    /// Same lifecycle as <see cref="AddSongButton"/>: this component lives on the canvas ROOT while
    /// the strip is a child of the menu PANEL, so it hides with the list and the one-laser rule is
    /// untouched. <see cref="EnsureFor"/> builds the strip at runtime, so the shipping scene needs
    /// no edit and no builder re-run.
    /// </summary>
    public class DetectionModeSelector : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [Tooltip("Applies the choice (and switches the mic). Auto-found.")]
        public NoteEvaluator evaluator;

        [Header("Buttons")]
        public Button microphoneButton;
        [Tooltip("USB MIDI keyboard input. Greyed out until a MIDI device is plugged in (always in the Editor).")]
        public Button midiButton;
        public Button freePlayButton;

        /// <summary>The detection mode currently selected on the strip.</summary>
        public DetectionMode Current { get; private set; } = DetectionMode.Microphone;

        // Selected = the menu's button blue; the others sit back in a dim slate.
        private static readonly Color SelectedColor = new Color(0.18f, 0.40f, 0.62f, 1f);
        private static readonly Color UnselectedColor = new Color(0.14f, 0.16f, 0.22f, 1f);
        private static readonly Color SelectedTextColor = Color.white;
        private static readonly Color UnselectedTextColor = new Color(0.70f, 0.72f, 0.78f, 1f);
        private static readonly Color PlaceholderTextColor = new Color(0.50f, 0.52f, 0.58f, 1f);

        private const float StripHeight = 56f;
        private const float StripGap = 12f; // between the strip and the list below it

        private const int MidiStatusMaxChars = 18; // what fits on the button's second line
        private string _midiStatusShown;

        private MidiNoteInput MidiInput => evaluator != null ? evaluator.midiInput : null;

        // MIDI can work here AND a device is plugged in.
        private bool MidiAvailable => MidiInput != null && MidiInput.IsSupported && MidiInput.HasDevice;

        private void Start()
        {
            AutoFind();
            Wire(microphoneButton, SelectMicrophone);
            Wire(midiButton, SelectMidi);
            Wire(freePlayButton, SelectFreePlay);

            // Every launch starts listening — see the class notes.
            Select(DetectionMode.Microphone);
            RefreshMidiButton();
        }

        private void Update()
        {
            // The cable came out while MIDI was chosen: nothing can be heard now, so go back to the
            // microphone rather than leave selected a mode the strip no longer offers. Checked during
            // songs too, while the menu is hidden — the MIC ON badge then shows the mic took over.
            if (Current == DetectionMode.Midi && !MidiAvailable)
            {
                Debug.Log("[DetectionModeSelector] MIDI device gone — back to Microphone.");
                Select(DetectionMode.Microphone);
            }

            // Plugging in or unplugging has no event here, so poll while the menu is up (it is hidden
            // during a song); RefreshMidiButton only touches the UI on a change.
            if (midiButton != null && midiButton.gameObject.activeInHierarchy) RefreshMidiButton();
        }

        public void SelectMicrophone() => Select(DetectionMode.Microphone);
        public void SelectMidi()       => Select(DetectionMode.Midi);
        public void SelectFreePlay()   => Select(DetectionMode.FreePlay);

        /// <summary>Apply a detection mode: switch the mic and light the chosen button.</summary>
        public void Select(DetectionMode mode)
        {
            Current = mode;
            if (evaluator != null) evaluator.SetDetection(mode);

            Paint(microphoneButton, mode == DetectionMode.Microphone);
            Paint(midiButton, mode == DetectionMode.Midi);
            Paint(freePlayButton, mode == DetectionMode.FreePlay);

            Debug.Log($"[DetectionModeSelector] Detection: {mode}");
        }

        /// <summary>
        /// Keep the MIDI button in step with the cable: clickable only while a MIDI device is plugged
        /// in, with the device's name on its second line — "not connected" otherwise, and "not
        /// available" where MIDI can't work at all. Only a change touches the UI.
        /// </summary>
        private void RefreshMidiButton()
        {
            if (midiButton == null) return;

            bool available = MidiAvailable;
            if (midiButton.interactable != available)
            {
                midiButton.interactable = available;
                Paint(midiButton, Current == DetectionMode.Midi); // greyed vs normal label colour
            }

            var midi = MidiInput;
            string status = midi == null || !midi.IsSupported ? "not available"
                : !available ? "not connected"
                : midi.AvailableDevice.Length <= MidiStatusMaxChars ? midi.AvailableDevice
                : midi.AvailableDevice.Substring(0, MidiStatusMaxChars - 3) + "...";
            SetMidiStatus(status);
        }

        private void SetMidiStatus(string status)
        {
            if (midiButton == null || status == _midiStatusShown) return;
            _midiStatusShown = status;
            var label = midiButton.GetComponentInChildren<TMP_Text>();
            // noparse: a device name is outside text and must not be read as rich-text tags.
            if (label != null) label.text = $"MIDI\n<size=70%><noparse>{status}</noparse></size>";
        }

        private static void Paint(Button button, bool selected)
        {
            if (button == null) return;
            if (button.targetGraphic != null)
                button.targetGraphic.color = selected ? SelectedColor : UnselectedColor;

            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.color = selected ? SelectedTextColor
                    : button.interactable ? UnselectedTextColor
                    : PlaceholderTextColor;
                label.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private void AutoFind()
        {
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (evaluator == null) evaluator = FindObjectOfType<NoteEvaluator>();
#pragma warning restore CS0618
        }

        private static void Wire(Button b, UnityAction action)
        {
            if (b == null) return;
            b.onClick.RemoveListener(action); // idempotent across a re-Start
            b.onClick.AddListener(action);
        }

        // ------------------------------------------------------------------ runtime self-heal

        /// <summary>
        /// Make sure the menu has the Detection strip, building it if it's missing. Safe to call
        /// from every <see cref="SongSelectionMenu.Build"/>: once the strip exists it just returns
        /// the component. Needs the scrollable list (<see cref="SongListScroller.EnsureFor"/> runs
        /// first in Build), because the strip takes the top of the list area: the list and its Up
        /// button move down to make room. That move happens only while the strip is being created,
        /// so nothing ever shifts twice. Returns null if the menu has no panel or no list area.
        /// </summary>
        public static DetectionModeSelector EnsureFor(SongSelectionMenu menu)
        {
            if (menu == null) return null;

            var panel = menu.panelRoot != null
                ? menu.panelRoot.GetComponent<RectTransform>()
                : null;
            // panelRoot defaults to the canvas root before Start runs, and the canvas root is
            // not a panel we should be parenting buttons to.
            if (panel == null || menu.panelRoot == menu.gameObject) return null;

            var existing = menu.GetComponent<DetectionModeSelector>();
            if (existing != null && existing.microphoneButton != null) return existing;

            var scroller = menu.GetComponent<SongListScroller>();
            var scrollView = scroller != null && scroller.scrollRect != null
                ? scroller.scrollRect.transform as RectTransform
                : null;
            if (scrollView == null) return null;

            var selector = existing != null ? existing : menu.gameObject.AddComponent<DetectionModeSelector>();

            // The list area is stretched to the panel, so offsetMax.y is its top edge measured from
            // the panel's top (negative = below it) — both in builder-made scenes and in scenes
            // SongListScroller wrapped at runtime. The strip goes exactly there.
            float listTop = scrollView.offsetMax.y;

            var strip = NewUIChild("DetectionStrip", panel);
            strip.anchorMin = new Vector2(0f, 1f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.pivot = new Vector2(0.5f, 1f);
            strip.offsetMin = new Vector2(40f, listTop - StripHeight);
            strip.offsetMax = new Vector2(-40f, listTop);

            var hlg = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 12f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            // Full strip height for every child: a button with no height has no area for the
            // controller laser's ray to hit (the bug the song rows once had).
            hlg.childForceExpandHeight = true;

            var title = NewUIChild("Label", strip);
            title.gameObject.AddComponent<LayoutElement>().preferredWidth = 150f;
            var titleTmp = title.gameObject.AddComponent<TextMeshProUGUI>();
            titleTmp.text = "Detection:";
            titleTmp.fontSize = 24f;
            titleTmp.alignment = TextAlignmentOptions.MidlineRight;
            titleTmp.color = Color.white;
            titleTmp.raycastTarget = false;

            selector.microphoneButton = MakeStripButton("MicrophoneButton", "Microphone", strip);
            // Plain ASCII and a rich-text size step — the default TMP atlas lacks symbols like '·'.
            // Greyed out until a MIDI device is plugged in; Start and Update keep it in step.
            selector.midiButton = MakeStripButton("MidiButton", "MIDI\n<size=70%>not connected</size>", strip);
            selector.midiButton.interactable = false;
            selector.midiButton.GetComponentInChildren<TMP_Text>().color = PlaceholderTextColor;
            selector.freePlayButton = MakeStripButton("FreePlayButton", "Free Play", strip);

            // Make room: push the list's top edge, and the Up button that sits level with it, down
            // by the strip plus a gap.
            float shift = StripHeight + StripGap;
            scrollView.offsetMax = new Vector2(scrollView.offsetMax.x, scrollView.offsetMax.y - shift);

            var up = scroller.scrollUpButton != null
                ? scroller.scrollUpButton.transform as RectTransform
                : panel.Find("ScrollUpButton") as RectTransform;
            if (up != null) up.anchoredPosition -= new Vector2(0f, shift);

            return selector;
        }

        // Image+Button with a centred TMP label, sized by the strip's layout group.
        private static Button MakeStripButton(string name, string label, RectTransform strip)
        {
            var rt = NewUIChild(name, strip);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = UnselectedColor;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img; // AddComponent doesn't auto-assign it, so the tint works
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 200f;
            le.flexibleWidth = 0f;

            var labelRt = NewUIChild("Label", rt);
            Stretch(labelRt);
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 22f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = UnselectedTextColor;
            return btn;
        }

        private static RectTransform NewUIChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
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
