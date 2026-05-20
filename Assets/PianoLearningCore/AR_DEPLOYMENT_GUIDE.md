# AR Deployment Guide — Meta Quest 3

This guide picks up after you've finished **Section 8 ("Going beyond the bootstrap")** of `SETUP_GUIDE.md`, meaning you have:

- A scene with `Keyboard` and `FallingNotesController` GameObjects set up manually in the Editor.
- A `SongLauncher` (or `FallingNotesDemo` with `Create Demo Camera = false`) that loads and plays a MIDI file.
- Falling notes working in Play mode in the regular Game view.

Now we'll get that same scene running on a Quest 3 in AR (with the real world visible behind the falling notes via passthrough).

> **Quest 3 vs Quest Pro.** Your proposal targets Quest Pro, but you asked about Quest 3 — they are essentially identical from a Unity dev perspective. Both use the Meta XR SDK, both run Android-based Horizon OS, both support color passthrough. Every step here applies to either. The biggest practical difference is that the Quest Pro has higher-res sensors and eye tracking; Quest 3 has slightly better passthrough quality and pancake lenses. For testing the falling-note interface, use whichever you have — the code doesn't change.

---

## 1. Pre-flight checklist

Before you start, confirm:

- **Unity Hub** has the **Android Build Support** module installed for your Unity 6 version.
  - Open Unity Hub → Installs → click the gear icon on your Unity 6 entry → Add Modules → tick "Android Build Support" (and its sub-options "OpenJDK" and "Android SDK & NDK Tools") → Install.
- Your project already has the **Meta XR SDK** installed — I saw `Meta.XR.BuildingBlocks` in the project, so you're set. If you ever need to re-check: Window → Package Manager → "In Project" → look for "Meta XR Core SDK" and "Meta XR All-in-One SDK".
- You have a **USB-C cable** that can do data transfer (not just charging) for connecting the Quest to your PC.

---

## 2. Put your Quest 3 into developer mode

Do this on the headset, **not** Unity.

1. Install the **Meta Horizon** app on your phone (Android or iOS) and sign in with the same Meta account as your headset.
2. In the phone app: **Menu → Devices** → tap your headset to connect.
3. Tap **Headset settings → Developer mode → ON**. If the toggle isn't there, you need to first register as a developer at <https://developer.oculus.com/manage/organizations/create/> — create a free "organization" (any name), then the toggle appears.
4. Put on the headset, plug it into your PC via USB-C.
5. Inside the headset, a popup appears: **"Allow USB debugging?"** — check "Always allow" and tap Allow.

To verify the headset is recognized, in Unity later you should see it appear in **File → Build Profiles → Run Device** dropdown.

---

## 3. Switch the project to Android

In Unity:

1. **File → Build Profiles** (Unity 6 renamed "Build Settings" to "Build Profiles").
2. In the left list, select **Android**.
3. Click **Switch Platform** at the bottom. Unity will re-import all assets for Android — this takes 5–15 minutes the first time. Have a coffee.

Once it finishes, the Unity icon next to "Android" in the Build Profiles window should be highlighted/active.

### Build Profile settings (in the same window)

- **Texture Compression**: ASTC (best for Quest's mobile GPU).
- **Run Device**: select your Quest 3 from the dropdown (refresh if it's not there).
- **Development Build**: ☑ tick this while you're iterating — it gives you stack traces in logcat.

---

## 4. Player Settings (one-time configuration)

**Edit → Project Settings → Player → Android tab** (the little Android icon).

| Section | Setting | Value |
| --- | --- | --- |
| Identification | Package Name | `com.yourname.piano_learning_ar` (must be unique across all Android apps; lowercase, no dashes, dots-separated) |
| Identification | Minimum API Level | **Android 12 (API 32)** — Quest 3 requirement |
| Identification | Target API Level | Automatic (highest installed) |
| Configuration | Scripting Backend | **IL2CPP** |
| Configuration | Target Architectures | ☑ ARM64 only (untick ARMv7) |
| Resolution and Presentation | Default Orientation | **Landscape Left** (irrelevant on Quest but XR plugins expect this) |

These are one-time. After this you don't need to revisit Player Settings.

---

## 5. XR Plug-in Management

**Edit → Project Settings → XR Plug-in Management** (left side of the Project Settings window).

1. If you see "Install XR Plug-in Management" — click it. It's almost certainly already installed since the Meta XR SDK depends on it.
2. Make sure you're on the **Android tab** (Android icon).
3. **Plug-in Providers**: tick **Oculus** (Meta hasn't renamed this checkbox yet in the Unity package).
4. Below, an **Oculus** section appears in the left tree. Click it.
5. Configure:
   - **Stereo Rendering Mode**: Multiview (best perf on Quest).
   - **Target Devices**: ☑ Quest 3 (and tick Quest Pro, Quest 2, etc. if you want to support them).

You'll likely see Meta's "Project Setup Tool" pop up after this — it scans for misconfigurations. Click "Fix All" and "Apply All" when prompted. Trust it; it knows the Quest specifics better than we do.

---

## 6. Make your scene AR-ready

Open your `FallingNotesTest` scene (or whatever you named it).

### 6a. Remove the desktop camera

Your scene currently has either:
- The auto-created `Demo Camera` from `FallingNotesDemo`, or
- A `Main Camera` you added yourself.

Either way: **delete it**. The Quest needs its own camera rig.

If you're still using the bootstrap `FallingNotesDemo`, uncheck **Create Demo Camera** in its Inspector — but for AR I recommend switching to the manual setup from SETUP_GUIDE.md Section 8 plus the `SongLauncher` script (cleaner separation, fewer surprises).

### 6b. Drop in a Meta XR Camera Rig

The fastest path uses **Building Blocks**:

1. Top menu: **Meta → Tools → Building Blocks**.
2. A window opens showing draggable cards. Find **Camera Rig** and drag it into the Hierarchy.
3. While still in Building Blocks, find **Passthrough** and drag it into the Hierarchy. This is what makes the real world visible behind your falling notes.
4. *(Optional but recommended for later)*: drag **Hand Tracking** in too. We won't use hands in this milestone but it costs nothing to have.

What you should now see in the Hierarchy:
- `[BuildingBlock] Camera Rig`
- `[BuildingBlock] Passthrough`
- `Keyboard` (the one you built)
- `FallingNotesController`
- `SongLauncher` (or `FallingNotesDemo` with `Create Demo Camera = false`)

### 6c. Hide the virtual keyboard in AR

On your `Keyboard` GameObject, find the **KeyboardVisualizer** component in the Inspector and either:
- **Uncheck the box** next to its name (disables the component), or
- Click the ⋮ menu on the component → **Remove Component**.

You want the user to see their *real* piano through passthrough, not procedural cubes. The `KeyboardLayout` and `KeyboardCalibrator` components stay — those are what position the falling notes.

### 6d. Set the camera to transparent (so passthrough shows through)

Select the `CenterEyeAnchor` (a child of `[BuildingBlock] Camera Rig → TrackingSpace`). In its `Camera` component:

- **Clear Flags**: Solid Color
- **Background**: RGBA = `(0, 0, 0, 0)` — the alpha channel is what matters. Click the color swatch and drag the **A** slider all the way to 0.

This makes Unity render nothing where you'd normally see the skybox, so the Quest's passthrough video comes through.

### 6e. Position the keyboard at hand height

A real 76-key keyboard usually sits on a stand at ~70 cm. With your Quest's tracking origin on the floor:

- Select the `Keyboard` GameObject.
- Set its Transform position to roughly `(0, 0.7, 0.5)` — 70 cm up, 50 cm in front of the play-area center.

You'll tune this precisely in AR using the calibrator (Section 8).

---

## 7. Build and Run

1. **File → Build Profiles → Build And Run** (with the Quest plugged in and unlocked).
2. Unity will compile, package as APK, push it to the Quest, and auto-launch it. First build takes a few minutes; subsequent builds are faster.
3. The Quest will switch to "Unknown Sources" automatically and launch your APK.

Look around inside the headset — you should see:
- Your real room via passthrough.
- Floating falling notes coming from behind a virtual hit-line, moving toward you.
- No virtual keyboard (because you disabled the visualizer).

If nothing appears, check the bottom-left of the headset display for any "App crashed" notification. To get stack traces:

```bash
adb logcat -s Unity
```

(`adb` is at `<Unity install>/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe`. Run it in a Windows terminal with the Quest plugged in.)

---

## 8. In-AR calibration (no keyboard available!)

Your `KeyboardCalibrator` listens for keyboard input — useless inside a Quest. You have three options, easiest first:

### Option A — Pair a Bluetooth keyboard to the Quest (zero code)

1. Inside the headset: Settings → Devices → Bluetooth → Pair → put your BT keyboard in pairing mode.
2. Once paired, the existing `KeyboardCalibrator` works as-is. Arrow keys nudge the virtual keyboard, R resets, etc.
3. Run the app, look at the virtual hit-line, and nudge until it sits exactly along the front edge of your physical piano.

This is what I recommend for your first AR test — it's instant and lets you focus on whether falling notes themselves are working before adding controller logic.

### Option B — Use the Quest controllers (small script)

Add a new script `XRControllerCalibrator.cs` alongside `KeyboardCalibrator.cs` that maps controller thumbsticks/buttons to the same actions. A minimal version using `OVRInput` (already installed via Meta XR SDK):

```csharp
using UnityEngine;

namespace PianoLearningCore
{
    /// Attach to the same GameObject as KeyboardLayout. Quest controllers nudge the keyboard.
    public class XRControllerCalibrator : MonoBehaviour
    {
        public float positionSpeed = 0.05f;
        public float rotationSpeed = 15f;

        void Update()
        {
            float dt = Time.deltaTime;

            // Right thumbstick = X / Z position
            Vector2 right = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
            transform.position += new Vector3(right.x, 0f, right.y) * positionSpeed * dt;

            // Left thumbstick Y = up/down
            float leftY = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).y;
            transform.position += Vector3.up * leftY * positionSpeed * dt;

            // Left thumbstick X = yaw rotation
            float leftX = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick).x;
            transform.Rotate(0f, leftX * rotationSpeed * dt, 0f, Space.World);

            // Right grip = reset to initial pose (you'd cache it in Awake like KeyboardCalibrator does)
        }
    }
}
```

This will only compile when the Meta XR SDK is present, which is true in your project. If you ever remove that SDK, delete this file.

### Option C — Pinch-to-place (most polished, future work)

In the proposal you noted that auto-alignment is out of scope, and that's fine. The future-feature path is: enable hand tracking, detect when the user pinches at two points along their piano's front edge, and use those two world-space points to compute the keyboard's position, rotation, and scale in one gesture. The `KeyboardLayout.transform` is exactly the thing this would set.

---

## 9. What success looks like

After build & run + alignment:

- Looking down at your physical keyboard, you see colored blocks (your falling notes) approaching the front edge of the white keys, from ~1.5 m behind the keyboard.
- When a block's leading edge reaches the front edge of a key, that's the moment you should press that key on your real piano.
- You can move your head around freely; the notes stay anchored to the keyboard's position in your room (because the camera rig is what tracks your head, not the keyboard).

If it looks misaligned: the keyboard's X scale or position is slightly off. Re-run calibration. If the X scale needs to differ from 1.0 by a lot, your real keyboard probably has non-standard white-key widths — measure one white key with a ruler and update `whiteKeyWidth` in your `KeyboardConfig` (default 23.5 mm).

---

## 10. Common pitfalls

**"Build And Run" greys out the Run Device dropdown.**
The Quest isn't being detected. Unplug, re-plug, accept the USB debugging prompt inside the headset again, and refresh the dropdown.

**The app launches but the screen is fully black, no passthrough.**
You didn't set the camera background alpha to 0 (Section 6d). Re-check the CenterEyeAnchor's Camera component.

**Notes appear but you see no real-world passthrough.**
Either the Passthrough Building Block isn't in the scene, OR it is but the `OVRManager` on the camera rig doesn't have "Insight Passthrough → Enable Passthrough" ticked. Select the camera rig's root, find OVRManager, scroll down to that section.

**Falling notes appear inside the keyboard / under the floor.**
Your `Keyboard` GameObject's Y position is wrong relative to where the Quest thinks the floor is. Adjust position.y, or use the calibrator's Q/E (or controller equivalents) to move it down/up.

**Performance is bad (frame rate drops below 90).**
You're probably spawning too many notes at once. Lower `Lead Time` on the FallingNotesController to limit how many are visible simultaneously. Also confirm Player Settings → Configuration → Scripting Backend = IL2CPP, not Mono.

**"Failed to install APK — INSTALL_FAILED_UPDATE_INCOMPATIBLE".**
You changed the package name or signing key between builds. Either uninstall the old app from inside the Quest (Apps → Unknown Sources → your app → ⋮ → Uninstall) or revert to the original package name.

---

## 11. Where this fits in the broader project

You've now closed the loop on the falling-note interface in AR. The next pieces from your proposal can be added without touching this code:

- **Audio detection module** — a separate `MicrophoneListener` + pitch-detection algorithm. When it identifies a pitch at time T with frequency F, it converts F → MIDI number and asks `FallingNotesController` "was a note with this MIDI number's leading edge near time T?". The controller already exposes `CurrentTime`, so this is just a lookup.
- **Hit/miss feedback** — call `KeyboardVisualizer.FlashKey(midi)` (already implemented) when the detector confirms a hit; flash red on the screen when it misses. The visualizer is disabled in AR, so you'd need a thin "hit feedback" component that uses the same flash method on transient overlay quads positioned at the key.
- **Learning vs assessment mode** — same falling-note system; just two different scoring components subscribing to detector events.
- **Song picker UI** — a simple Canvas with buttons calling `SongLauncher.LoadAndPlay()` after swapping `streamingAssetsRelativePath` at runtime. You already wired the launcher's methods to be UI-button-friendly.

The Meta Quest Pro target from your proposal is identical in code; the only changes when you switch hardware would be in the **XR Plug-in Management → Oculus → Target Devices** checkboxes.
