using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Microphone privacy indicator (proposal §6.2): a small red "MIC ON" badge that stays in the
    /// user's view for as long as the microphone is capturing, and disappears the moment it stops.
    ///
    /// Driven by <see cref="PianoAudioDetector.IsRunning"/>, which only goes true once capture has
    /// ACTUALLY started (after the RECORD_AUDIO grant and a successful open) and false again in
    /// StopDetection — so the badge reports the real mic state rather than being a static icon. If
    /// the user denies the permission, the mic never opens and the badge never shows.
    ///
    /// Head-locked on purpose: the mic runs from scene start, through Help, the menu and every song,
    /// so a badge fixed in the room could be behind the user exactly when it matters. At runtime it
    /// re-parents itself under the headset camera at <see cref="headOffset"/>: up and to the left,
    /// above the downward sightline to the keys and falling notes. It is display-only — no
    /// GraphicRaycaster, raycastTarget off — so the controller laser passes straight through it.
    ///
    /// Put this on the Canvas ROOT and point <see cref="panelRoot"/> at the visual child, so the
    /// component keeps running while the badge is hidden and can re-show it.
    ///
    /// Build one with Tools &gt; Piano Learning &gt; Create Mic Indicator, which wires every reference.
    /// </summary>
    public class MicActiveIndicator : MonoBehaviour
    {
        [Header("References (auto-found if left empty)")]
        [Tooltip("The detector whose IsRunning drives the badge.")]
        public PianoAudioDetector detector;

        [Tooltip("The headset camera the badge follows. Auto-found: 'CenterEyeAnchor', else Camera.main.")]
        public Transform head;

        [Header("Panel")]
        [Tooltip("The visual child shown while the mic is on and hidden otherwise. Point this at the " +
                 "PANEL child, NOT the Canvas root, so this controller keeps running. " +
                 "Defaults to this GameObject if left empty.")]
        public GameObject panelRoot;

        [Header("Placement")]
        [Tooltip("Position relative to the head, in metres (x right, y up, z forward). The default sits " +
                 "about 16° left and 12° up: in view, but clear of the keys and notes below the sightline.")]
        public Vector3 headOffset = new Vector3(-0.17f, 0.13f, 0.6f);

        private bool _visible;

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        private void Awake()
        {
            if (panelRoot == null) panelRoot = gameObject;
            AutoFindReferences();
        }

        private void Start()
        {
            // Late safety net, as in LiveScoreHud: Start runs after every Awake.
            AutoFindReferences();
            AttachToHead();
            SetVisible(IsMicOn());
        }

        private void Update()
        {
            // IsRunning has no event, so poll it — but only touch the panel on a CHANGE.
            bool on = IsMicOn();
            if (on != _visible) SetVisible(on);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private bool IsMicOn() => detector != null && detector.IsRunning;

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (panelRoot != null) panelRoot.SetActive(visible);
        }

        private void AttachToHead()
        {
            if (head == null)
            {
                Debug.LogWarning("[MicActiveIndicator] No headset camera found — the badge stays where " +
                                 "it was placed instead of following the view. Assign 'Head'.", this);
                return;
            }
            // worldPositionStays=false keeps the canvas's tiny localScale. Identity rotation faces the
            // viewer: a uGUI canvas is read from its -Z side, which is where the camera sits.
            transform.SetParent(head, false);
            transform.localPosition = headOffset;
            transform.localRotation = Quaternion.identity;
        }

        private void AutoFindReferences()
        {
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (detector == null) detector = FindObjectOfType<PianoAudioDetector>();
#pragma warning restore CS0618
            if (head == null)
            {
                // Not Camera.main first: this scene also tags LeftEyeAnchor as MainCamera.
                var eye = GameObject.Find("CenterEyeAnchor");
                if (eye != null) head = eye.transform;
                else if (Camera.main != null) head = Camera.main.transform;
            }
        }
    }
}
