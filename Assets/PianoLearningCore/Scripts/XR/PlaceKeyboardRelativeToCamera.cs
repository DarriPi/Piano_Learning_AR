using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// On Start, snaps this GameObject (the keyboard root) to a fixed offset relative
    /// to the user's head — guaranteeing it appears in a sensible spot regardless of
    /// what tracking origin or floor calibration the Quest reports.
    ///
    /// Use this as a workaround when the keyboard otherwise floats in the wrong place.
    /// Attach to the Keyboard GameObject. By default it appears 50 cm in front of you
    /// and 85 cm below eye level (i.e. table height for a standing user, comfortable to
    /// look down at). Tune the offsets in the Inspector.
    ///
    /// The rotation is set so the keyboard's +Z axis (back) points away from the user.
    /// That means falling notes will travel toward you exactly as in the desktop test.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlaceKeyboardRelativeToCamera : MonoBehaviour
    {
        [Header("Head reference")]
        [Tooltip("Leave blank to auto-find the main camera (Quest CenterEyeAnchor).")]
        public Transform headAnchor;

        [Header("Offset from the head (meters)")]
        [Tooltip("Sideways offset. 0 = directly in front of you.")]
        public float right = 0f;

        [Tooltip("Vertical offset. Negative = below eye level. -0.85 ~= table height when standing.")]
        public float up = -0.85f;

        [Tooltip("Forward offset. Positive = in front of you. 0.5 = 50 cm away.")]
        public float forward = 0.5f;

        [Header("Behaviour")]
        [Tooltip("Run the placement again whenever this component is enabled (useful if you toggle the script via UI).")]
        public bool placeOnEnable = false;

        [Tooltip("Run the placement on Start. Disable if you want to place manually via PlaceNow().")]
        public bool placeOnStart = true;

        [Tooltip("Log the final placement to the Console so you can read it via adb logcat.")]
        public bool logPlacement = true;

        private void Start()
        {
            if (placeOnStart) PlaceNow();
        }

        private void OnEnable()
        {
            if (placeOnEnable && Application.isPlaying) PlaceNow();
        }

        /// <summary>Snap the keyboard to its offset relative to the head, right now.</summary>
        public void PlaceNow()
        {
            Transform head = ResolveHead();
            if (head == null)
            {
                Debug.LogError("[PlaceKeyboardRelativeToCamera] No head reference found. " +
                               "Assign 'Head Anchor' or make sure a Camera tagged MainCamera exists.", this);
                return;
            }

            // Project the head's forward and right onto the horizontal plane so the keyboard
            // ends up level even if the user is looking up / down when the app starts.
            Vector3 forwardHoriz = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forwardHoriz.sqrMagnitude < 0.0001f) forwardHoriz = Vector3.forward;
            forwardHoriz.Normalize();

            Vector3 rightHoriz = Vector3.Cross(Vector3.up, forwardHoriz); // right of forward, in world frame

            Vector3 pos = head.position
                          + rightHoriz   * right
                          + Vector3.up   * up
                          + forwardHoriz * forward;

            transform.position = pos;
            // Face the user: keyboard's +Z (back of the keys) points AWAY from the user.
            transform.rotation = Quaternion.LookRotation(forwardHoriz, Vector3.up);

            if (logPlacement)
            {
                Debug.Log($"[PlaceKeyboardRelativeToCamera] Head at {head.position} " +
                          $"-> keyboard at {pos}. Forward axis = {forwardHoriz}.");
            }
        }

        private Transform ResolveHead()
        {
            if (headAnchor != null) return headAnchor;
            if (Camera.main != null) return Camera.main.transform;
            return null;
        }
    }
}
