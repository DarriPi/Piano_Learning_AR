// using UnityEngine;

// namespace PianoLearningCore
// {
//     /// <summary>
//     /// Forces Floor-Level tracking on startup so virtual content positioned in world space
//     /// (e.g. the keyboard at Y = 0.7) sits at the correct real-world height.
//     ///
//     /// Without this, Meta XR Camera Rigs default to Eye-Level tracking, which puts world
//     /// Y = 0 at the user's eye height (~1.6 m above the floor). That makes any object
//     /// placed at Y greater than 0 appear in the air.
//     ///
//     /// Attach this to ANY active GameObject in the scene (it doesn't have to live on the
//     /// camera rig). It runs once in Awake and then does nothing.
//     ///
//     /// REQUIRES the Meta XR SDK (uses OVRManager). If you ever uninstall the Meta XR SDK,
//     /// delete this file.
//     /// </summary>
//     [DisallowMultipleComponent]
//     public class ForceFloorTrackingOrigin : MonoBehaviour
//     {
//         [Tooltip("Log a message to the Console when the tracking origin is set. Useful for confirming the script ran on device.")]
//         public bool logOnApply = true;

//         private void Awake()
//         {
//             // OVRManager.SetTrackingOrigin updates both the runtime and the OVRManager
//             // serialized state, so it survives subsequent SDK re-inits.
//             OVRManager.SetTrackingOrigin(OVRManager.TrackingOrigin.FloorLevel);

//             if (logOnApply)
//             {
//                 Debug.Log("[ForceFloorTrackingOrigin] Tracking origin set to FloorLevel. " +
//                           "World Y = 0 is now the real floor.");
//             }
//         }
//     }
// }
