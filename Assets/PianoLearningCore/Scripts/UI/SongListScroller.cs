using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// Scrolls the song list when it holds more songs than fit the panel. Two inputs, both
    /// hands-off the existing click path:
    ///  - the RIGHT thumbstick (free while the list is visible — HelpPanel locks the keyboard
    ///    calibrator whenever the list is up, so nothing else claims the stick), and
    ///  - laser-clickable Up / Down buttons, which double as the visual hint that the list
    ///    scrolls at all (there is no scrollbar the laser could drag).
    ///
    /// Scrolling itself is a plain uGUI <see cref="ScrollRect"/> whose viewport carries a
    /// RectMask2D, so the mouse wheel / drag still work in the editor Game view.
    ///
    /// <see cref="EnsureFor"/> is the runtime self-heal (same pattern as the button-height fix
    /// in <see cref="SongSelectionMenu.Build"/>): a scene built before scrolling existed gets
    /// its flat Content wrapped in ScrollView &gt; Viewport(mask) &gt; Content and the two
    /// buttons created on the fly, so it works without re-running the builder. Scenes made by
    /// the current builder already have the structure and pass through untouched.
    /// </summary>
    public class SongListScroller : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("The ScrollRect around the song list's Content. Wired by the builder, or " +
                 "created at runtime by EnsureFor for scenes that predate scrolling.")]
        public ScrollRect scrollRect;

        [Tooltip("Scrolls one row toward the top of the list.")]
        public Button scrollUpButton;

        [Tooltip("Scrolls one row toward the bottom of the list.")]
        public Button scrollDownButton;

        [Header("Input")]
        [Tooltip("Which hand's thumbstick scrolls the list. Right by default — same hand as " +
                 "the ControllerUIPointer laser.")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        [Tooltip("Stick deflection below this is ignored (prevents drift-scrolling).")]
        [Range(0f, 0.9f)] public float stickDeadzone = 0.25f;

        [Tooltip("Scroll speed at full stick deflection, in canvas units per second. " +
                 "Rows are ~98 units tall, so 600 is about six rows per second.")]
        public float stickScrollSpeed = 600f;

        [Tooltip("Canvas units one Up/Down click moves the list — one row height plus spacing.")]
        public float rowStep = 98f;

        private void Start()
        {
            Wire(scrollUpButton, ScrollUp);
            Wire(scrollDownButton, ScrollDown);
        }

        private void Update()
        {
            // Panel hidden (help open / song playing) → ScrollView inactive → leave the stick alone.
            if (scrollRect == null || scrollRect.content == null ||
                !scrollRect.gameObject.activeInHierarchy) return;

            Vector2 stick = controller == OVRInput.Controller.LTouch
                ? OVRInput.Get(OVRInput.RawAxis2D.LThumbstick)
                : OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);

            if (Mathf.Abs(stick.y) < stickDeadzone) return;

            // Stick up = toward the top of the list (like a mouse wheel).
            ScrollBy(-stick.y * stickScrollSpeed * Time.deltaTime);
        }

        /// <summary>One row toward the top (earlier songs).</summary>
        public void ScrollUp() => ScrollBy(-rowStep);

        /// <summary>One row toward the bottom (later songs).</summary>
        public void ScrollDown() => ScrollBy(rowStep);

        /// <summary>Move the list by canvas units; positive scrolls DOWN the list. Clamped.</summary>
        public void ScrollBy(float deltaUnits)
        {
            if (scrollRect == null || scrollRect.content == null) return;

            // Content pivot is top: anchoredPosition.y == 0 shows the top of the list and grows
            // as it scrolls down. Writing the position directly (instead of the normalized
            // property) stays exact when the content is shorter than the viewport.
            var pos = scrollRect.content.anchoredPosition;
            pos.y = Mathf.Clamp(pos.y + deltaUnits, 0f, ScrollExtent());
            scrollRect.content.anchoredPosition = pos;
            scrollRect.velocity = Vector2.zero; // don't let leftover inertia fight the input
        }

        /// <summary>Jump to the top of the list (used after the rows are rebuilt).</summary>
        public void SnapToTop() => ScrollBy(float.NegativeInfinity);

        private float ScrollExtent()
        {
            RectTransform vp = scrollRect.viewport != null
                ? scrollRect.viewport
                : (RectTransform)scrollRect.transform;
            return Mathf.Max(0f, scrollRect.content.rect.height - vp.rect.height);
        }

        // ------------------------------------------------------------------ runtime self-heal

        /// <summary>
        /// Make the menu's song list scrollable, building whatever is missing. Safe to call on
        /// every <see cref="SongSelectionMenu.Build"/>: once the structure exists it only
        /// returns the scroller. Returns null when the menu has no usable content parent.
        /// </summary>
        public static SongListScroller EnsureFor(SongSelectionMenu menu)
        {
            if (menu == null || !(menu.contentParent is RectTransform content)) return null;

            var scroller = menu.GetComponent<SongListScroller>();

            // Already scrollable (current builder, or a previous EnsureFor run)?
            ScrollRect existing = FindScrollRectAbove(content);
            if (existing != null)
            {
                if (scroller == null)
                {
                    scroller = menu.gameObject.AddComponent<SongListScroller>();
                    scroller.scrollRect = existing;
                }
                else if (scroller.scrollRect == null)
                {
                    scroller.scrollRect = existing;
                }
                return scroller;
            }

            var panel = content.parent as RectTransform;
            if (panel == null) return null;

            // ScrollView takes over the Content's place in the panel, minus a right-hand
            // column (56 units) freed for the Up/Down buttons.
            var scrollView = NewUIChild("ScrollView", panel);
            scrollView.SetSiblingIndex(content.GetSiblingIndex());
            scrollView.anchorMin = content.anchorMin;
            scrollView.anchorMax = content.anchorMax;
            scrollView.pivot = content.pivot;
            scrollView.offsetMin = content.offsetMin;
            scrollView.offsetMax = new Vector2(content.offsetMax.x - 56f, content.offsetMax.y);

            // Near-invisible but NOT alpha 0: a fully transparent mesh gets culled by the
            // CanvasRenderer and would stop receiving the editor mouse wheel/drag raycasts.
            var scrollViewImage = scrollView.gameObject.AddComponent<Image>();
            scrollViewImage.color = new Color(0f, 0f, 0f, 0.01f);
            var scrollRect = scrollView.gameObject.AddComponent<ScrollRect>();

            var viewport = NewUIChild("Viewport", scrollView);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            // Re-home the existing Content (rows AND the hidden template move with it), then
            // re-anchor it to the viewport top so the ContentSizeFitter can grow it downward —
            // that growth past the viewport is exactly what becomes scrollable.
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            if (content.GetComponent<ContentSizeFitter>() == null)
            {
                var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            scrollRect.content = content;
            scrollRect.viewport = viewport;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 35f; // sane mouse-wheel step in the editor

            if (scroller == null) scroller = menu.gameObject.AddComponent<SongListScroller>();
            scroller.scrollRect = scrollRect;

            // Up/Down in the freed right column, aligned with the scroll area's top/bottom.
            // They live on the PANEL (not the masked viewport) so they are never clipped, and
            // they vanish with the panel — the single-laser rule stays intact.
            if (scroller.scrollUpButton == null)
                scroller.scrollUpButton = MakeScrollButton("ScrollUpButton", "Up", panel,
                    new Vector2(1f, 1f), new Vector2(-16f, scrollView.offsetMax.y));
            if (scroller.scrollDownButton == null)
                scroller.scrollDownButton = MakeScrollButton("ScrollDownButton", "Down", panel,
                    new Vector2(1f, 0f), new Vector2(-16f, scrollView.offsetMin.y));

            return scroller;
        }

        // Image+Button with a centred TMP label — runtime twin of the builder's CreateButton.
        private static Button MakeScrollButton(string name, string label, RectTransform parent,
                                               Vector2 anchor, Vector2 anchoredPosition)
        {
            var rt = NewUIChild(name, parent);
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(64f, 64f);
            rt.anchoredPosition = anchoredPosition;

            var img = rt.gameObject.AddComponent<Image>();
            img.color = new Color(0.18f, 0.40f, 0.62f, 1f); // same blue as the 'Align' help button
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;

            var labelRt = NewUIChild("Label", rt);
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            var tmp = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = label; // plain text — '▲'/'▼' are missing from the default TMP font atlas
            tmp.fontSize = 20f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return btn;
        }

        private static RectTransform NewUIChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static ScrollRect FindScrollRectAbove(Transform t)
        {
            for (var p = t.parent; p != null; p = p.parent)
            {
                var sr = p.GetComponent<ScrollRect>();
                if (sr != null) return sr;
            }
            return null;
        }

        private static void Wire(Button b, UnityAction action)
        {
            if (b == null) return;
            b.onClick.RemoveListener(action); // idempotent across a re-Start
            b.onClick.AddListener(action);
        }
    }
}
