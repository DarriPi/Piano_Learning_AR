using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PianoLearningCore
{
    /// <summary>
    /// A minimal controller laser pointer for world-space uGUI menus on Meta Quest, built WITHOUT
    /// the Meta Interaction SDK ray stack (no PointableCanvas / RayInteractor / PointableCanvasModule).
    ///
    /// Each frame it shoots a ray from a controller anchor, ray-tests it against the
    /// <see cref="Button"/>s under <see cref="targetCanvas"/> (using their RectTransform world
    /// quads, so it is stereo-proof and camera-independent), draws a <see cref="LineRenderer"/>
    /// laser to the nearest one, highlights it, and "clicks" it when the trigger is pressed.
    ///
    /// Why it exists: the Song Selection menu is plain uGUI, which the Quest controller ray cannot
    /// reach on its own. This bridges the controller straight to those Buttons, so it works with the
    /// existing OVRCameraRig with zero extra scene wiring.
    ///
    /// Setup: add it to any GameObject in the scene (e.g. the SongSelectionCanvas). Leave the fields
    /// empty to auto-find the controller anchor and the menu canvas, or assign them explicitly.
    /// </summary>
    public class ControllerUIPointer : MonoBehaviour
    {
        [Header("Source (auto-found if left empty)")]
        [Tooltip("Transform the laser is cast from — normally a controller anchor on the OVRCameraRig. " +
                 "If empty, the rig's right/left controller anchor is used based on 'Controller'.")]
        public Transform pointerOrigin;

        [Tooltip("Canvas whose Buttons can be clicked. If empty, the SongSelectionMenu's canvas is used.")]
        public Canvas targetCanvas;

        [Header("Input")]
        [Tooltip("Which hand drives the pointer when 'Pointer Origin' is auto-found.")]
        public OVRInput.Controller controller = OVRInput.Controller.RTouch;

        [Tooltip("Button that selects the hovered menu item.")]
        public OVRInput.Button clickButton = OVRInput.Button.PrimaryIndexTrigger;

        [Header("Laser")]
        [Tooltip("Length of the laser when it isn't hitting a button, in metres.")]
        public float maxDistance = 5f;
        public float laserWidth = 0.004f;
        public Color idleColor = new Color(0.40f, 0.70f, 1f, 0.85f);
        public Color hoverColor = new Color(0.30f, 1f, 0.55f, 1f);
        [Tooltip("Optional material for the laser. If empty, a Sprites/Default material is created " +
                 "(works under URP and supports the per-end colours).")]
        public Material laserMaterial;

        private LineRenderer _line;
        private GameObject _laserObject;
        private GameObject _currentHover;
        private PointerEventData _pointerData;
        private readonly List<Button> _buttons = new List<Button>(16);
        private readonly Vector3[] _corners = new Vector3[4];

        private void Awake()
        {
            // The laser lives on its own root object: a LineRenderer's width scales with its
            // transform's lossyScale, and this component usually sits on the tiny (~0.0009) canvas.
            _laserObject = new GameObject("ControllerUIPointer_Laser");
            _line = _laserObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.startWidth = laserWidth;
            _line.endWidth = laserWidth;
            _line.numCapVertices = 4;
            _line.textureMode = LineTextureMode.Stretch;
            _line.material = laserMaterial != null
                ? laserMaterial
                : new Material(Shader.Find("Sprites/Default"));
            _line.enabled = false;
        }

        private void Start()
        {
            // A ControllerMenuNavigator (thumbstick + trigger) replaces this laser when present,
            // so stand down to avoid two competing input paths / a stray laser.
#pragma warning disable CS0618 // FindObjectOfType works across every Unity version
            if (FindObjectOfType<ControllerMenuNavigator>() != null)
#pragma warning restore CS0618
            {
                SetLaser(false);
                enabled = false;
                return;
            }

            if (EventSystem.current != null)
                _pointerData = new PointerEventData(EventSystem.current);

            if (targetCanvas == null)
            {
                // Prefer the canvas this pointer lives on — every builder puts the pointer on the
                // canvas ROOT, so this is the correct target and never grabs another canvas by
                // mistake. (This is why a serialized-null targetCanvas still resolves correctly.)
                targetCanvas = GetComponent<Canvas>();
                if (targetCanvas == null) targetCanvas = GetComponentInParent<Canvas>();
#pragma warning disable CS0618 // FindObjectOfType is fine and works across Unity versions
                if (targetCanvas == null)
                {
                    var menu = FindObjectOfType<SongSelectionMenu>();
                    if (menu != null) targetCanvas = menu.GetComponentInChildren<Canvas>(true);
                }
                if (targetCanvas == null) targetCanvas = FindObjectOfType<Canvas>();
            }

            if (pointerOrigin == null)
            {
                var rig = FindObjectOfType<OVRCameraRig>();
#pragma warning restore CS0618
                if (rig != null)
                    pointerOrigin = controller == OVRInput.Controller.LTouch
                        ? rig.leftControllerAnchor
                        : rig.rightControllerAnchor;
            }

            if (pointerOrigin == null)
                Debug.LogWarning("[ControllerUIPointer] No pointer origin found — assign a controller " +
                                 "anchor (e.g. RightControllerAnchor) to 'Pointer Origin' in the Inspector.", this);
        }

        private void Update()
        {
            if (pointerOrigin == null || targetCanvas == null)
            {
                SetLaser(false);
                return;
            }

            CollectButtons();
            if (_buttons.Count == 0) // menu hidden / nothing to point at
            {
                ClearHover();
                SetLaser(false);
                return;
            }

            Vector3 origin = pointerOrigin.position;
            Vector3 dir = pointerOrigin.forward;
            Button hit = RaycastButtons(origin, dir, out Vector3 hitPoint);

            // Laser visual.
            SetLaser(true);
            _line.SetPosition(0, origin);
            _line.SetPosition(1, hit != null ? hitPoint : origin + dir * maxDistance);
            Color c = hit != null ? hoverColor : idleColor;
            _line.startColor = c;
            _line.endColor = c;

            // Hover enter/exit so the Button shows its highlighted state.
            GameObject hoverGo = hit != null ? hit.gameObject : null;
            if (hoverGo != _currentHover)
            {
                if (_currentHover != null && _pointerData != null)
                    ExecuteEvents.Execute(_currentHover, _pointerData, ExecuteEvents.pointerExitHandler);
                _currentHover = hoverGo;
                if (_currentHover != null && _pointerData != null)
                    ExecuteEvents.Execute(_currentHover, _pointerData, ExecuteEvents.pointerEnterHandler);
            }

            // Click.
            if (hit != null && OVRInput.GetDown(clickButton, controller))
            {
                if (_pointerData != null)
                {
                    _pointerData.button = PointerEventData.InputButton.Left;
                    ExecuteEvents.Execute(hit.gameObject, _pointerData, ExecuteEvents.pointerClickHandler);
                }
                else
                {
                    hit.onClick.Invoke();
                }
            }
        }

        private void CollectButtons()
        {
            _buttons.Clear();
            // active-only: a hidden menu panel contributes no buttons, so the laser turns off.
            targetCanvas.GetComponentsInChildren(false, _buttons);
        }

        // Nearest interactable Button whose RectTransform quad the ray pierces (within maxDistance).
        private Button RaycastButtons(Vector3 origin, Vector3 dir, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;
            float nearest = maxDistance;
            Button best = null;

            foreach (var b in _buttons)
            {
                if (b == null || !b.interactable) continue;
                if (!(b.transform is RectTransform rt)) continue;
                rt.GetWorldCorners(_corners); // 0=BL, 1=TL, 2=TR, 3=BR

                float d;
                bool pierced =
                    RayTriangle(origin, dir, _corners[0], _corners[1], _corners[2], out d) ||
                    RayTriangle(origin, dir, _corners[0], _corners[2], _corners[3], out d);

                if (pierced && d < nearest)
                {
                    Vector3 p = origin + dir * d;
                    // Rows scrolled out of the song list's viewport are CLIPPED by a RectMask2D
                    // but stay active, so the quad test above still hits them. Only accept hits
                    // that land inside every ancestor mask — i.e. on the visible part.
                    if (!VisibleThroughMasks(p, b.transform)) continue;
                    nearest = d;
                    hitPoint = p;
                    best = b;
                }
            }
            return best;
        }

        // True when the world-space point is inside every RectMask2D above the transform, so a
        // masked-away (invisible) button can't be hovered or clicked. Buttons with no mask
        // ancestor are unaffected.
        private static bool VisibleThroughMasks(Vector3 worldPoint, Transform t)
        {
            for (var rt = t as RectTransform; rt != null; rt = rt.parent as RectTransform)
            {
                if (rt.TryGetComponent(out RectMask2D _))
                {
                    Vector2 local = rt.InverseTransformPoint(worldPoint);
                    if (!rt.rect.Contains(local)) return false;
                }
            }
            return true;
        }

        // Double-sided Möller–Trumbore ray/triangle test. Always assigns 'dist'.
        private static bool RayTriangle(Vector3 o, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2,
                                        out float dist)
        {
            dist = 0f;
            const float eps = 1e-7f;
            Vector3 e1 = v1 - v0;
            Vector3 e2 = v2 - v0;
            Vector3 p = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, p);
            if (det > -eps && det < eps) return false; // ray parallel to triangle
            float inv = 1f / det;
            Vector3 t = o - v0;
            float u = Vector3.Dot(t, p) * inv;
            if (u < 0f || u > 1f) return false;
            Vector3 q = Vector3.Cross(t, e1);
            float v = Vector3.Dot(dir, q) * inv;
            if (v < 0f || u + v > 1f) return false;
            float d = Vector3.Dot(e2, q) * inv;
            if (d <= 0f) return false; // behind the controller
            dist = d;
            return true;
        }

        private void ClearHover()
        {
            if (_currentHover != null && _pointerData != null)
                ExecuteEvents.Execute(_currentHover, _pointerData, ExecuteEvents.pointerExitHandler);
            _currentHover = null;
        }

        private void SetLaser(bool on)
        {
            if (_line != null && _line.enabled != on) _line.enabled = on;
        }

        private void OnDestroy()
        {
            if (_laserObject != null) Destroy(_laserObject);
        }
    }
}
