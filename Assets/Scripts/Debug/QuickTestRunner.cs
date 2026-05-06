using UnityEngine;

public class QuickTestRunner : MonoBehaviour
{
    void Update()
    {
        // ── PC Keyboard Bindings ──────────────────────────
        if (Input.GetKeyDown(KeyCode.Space))
            NoteSpawner.Instance?.StartSong();

        if (Input.GetKeyDown(KeyCode.Escape))
            NoteSpawner.Instance?.StopSong();

        // if (Input.GetKeyDown(KeyCode.C))
        //     CalibrationManager.Instance?.StartCalibration();

        // if (Input.GetKeyDown(KeyCode.V))
        //     CalibrationManager.Instance?.ConfirmCalibration();

        // // ── Quest Controller Bindings ─────────────────────
        // // Left Menu button (hamburger button on left controller): Start calibration
        // if (OVRInput.GetDown(OVRInput.Button.Start))
        //     CalibrationManager.Instance?.StartCalibration();

        // Left thumbstick click: Start song
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick))
            NoteSpawner.Instance?.StartSong();

        // Right thumbstick click: Stop song
        if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick))
            NoteSpawner.Instance?.StopSong();
    }
}