# Falling-Notes Setup Guide

This is a beginner-friendly, step-by-step walkthrough for getting the falling-note interface working inside your Unity 6 project. By the end you will have a scene that plays a PianoVision-style falling-note animation on top of a virtual 76-key keyboard. Everything in this module lives under `Assets/PianoLearningCore/` and is completely isolated from your existing code.

---

## 1. What you should have

A Unity 6 (6000.x LTS) project open. Your project already has the Meta XR SDK installed, which is fine — we are not touching it yet. We are first building this in a regular 3D scene; we'll migrate it into AR later.

In your **Project window** (lower-left of the Unity editor) you should now see a new folder:

```
Assets/
  PianoLearningCore/
    SETUP_GUIDE.md          (this file)
    Scenes/                 (empty for now — you will save your test scene here)
    Scripts/
      Core/
        PianoNote.cs        single-note data
        Song.cs             song data + built-in C-major demo
        NoteUtils.cs        MIDI helpers (black-key detection, naming, ranges)
      Keyboard/
        KeyboardConfig.cs   ScriptableObject with keyboard dimensions
        KeyboardLayout.cs   converts MIDI numbers to local positions
        KeyboardVisualizer.cs  procedurally builds a visible keyboard
        KeyboardCalibrator.cs  WASD/arrow-key alignment controls
      FallingNotes/
        FallingNote.cs            one note block, position recomputed every frame
        FallingNotesController.cs the brain (clock, spawning, despawning)
      Midi/
        MidiFileParser.cs   pure C# Standard MIDI File parser (no dependencies)
        MidiSongLoader.cs   turns a .mid file into a Song
      Demo/
        FallingNotesDemo.cs one-script bootstrapper for the test scene
```

If you only see the folders but not the files inside them, **right-click in the Project window → Refresh** (or press `Ctrl+R`). Unity needs to import them.

After the import you should see Unity compile briefly (a spinner in the bottom-right). If you get red errors in the Console (`Window → General → Console`), don't continue — fix those first. Compilation errors here would almost always be a typo from a copy-paste; let me know what the message says.

---

## 2. Make the test scene

1. In the **Project window**, double-click `Assets/PianoLearningCore/Scenes/` to open the folder.
2. Right-click inside the folder → **Create → Scene**. Name it `FallingNotesTest`.
3. Double-click `FallingNotesTest` to open it. The Hierarchy on the left should now show just `Main Camera` and `Directional Light` (Unity adds those by default — we'll let our demo script handle them).
4. *(Optional)* Delete `Main Camera` and `Directional Light`. The bootstrap script creates its own camera and light when none are present. Leaving them in is also fine.

### Add the bootstrap GameObject

1. In the Hierarchy, right-click in the empty space → **Create Empty**. A `GameObject` appears.
2. With the new GameObject selected, look at the **Inspector** on the right. Rename it to `FallingNotesDemo` (click the name field at the top of the Inspector).
3. Click the **Add Component** button at the bottom of the Inspector, type `FallingNotesDemo`, and hit Enter. The script we wrote attaches.

You should see a list of fields in the Inspector for the `FallingNotesDemo` component. Defaults work for the first run.

### Press Play

Hit the ▶ Play button at the top of the editor. You should see:

- A flat 76-key virtual keyboard sitting in front of the camera.
- Blue rectangles falling toward the keyboard from the back, in time with a C major scale (ascending, descending, then a held C-major chord).
- Each rectangle lines up with the matching key on the virtual keyboard.

If you don't see anything, scroll to **Section 7: Troubleshooting** below.

When you're done watching, press the Play button again to stop. Save the scene now with `Ctrl+S` (`Cmd+S` on Mac).

---

## 3. Understanding what just happened

Three GameObjects exist in the Hierarchy while Play is running:

- **FallingNotesDemo** — the one you created. Its `Awake()` builds everything else.
- **Keyboard** — built by the demo. Has `KeyboardLayout` (compute key positions), `KeyboardVisualizer` (drew the cubes), and `KeyboardCalibrator` (lets you nudge it).
- **FallingNotesController** — owns the song clock and spawns/despawns the falling cubes. Cubes are parented to **Keyboard** so they move with the keyboard.

The whole system is in **keyboard-local space**. That means when you later align the keyboard to a real, physical piano (in AR), the notes automatically come along. You move ONE GameObject; the notes follow.

Coordinate system on the keyboard:

```
        +Y (up)
         |          notes spawn here (Z = spawnDistance)
         |   ____________________
         |  |                    |
         |  |                    |
         |  |   falling notes    |
         |  |        ↓           |
         |  |________________----| <-- "hit line" at Z = 0
         |   ▢ ▢ ▢ ▢ ▢ ▢ ▢ ▢   <-- the keyboard, +X is higher notes
         +-------------------> +X
        (player is somewhere on -Z, looking toward +Z)
```

---

## 4. Calibration controls (already built in)

While Play is running, click on the **Game** view so it has keyboard focus, then:

| Keys | Action |
| --- | --- |
| Arrow keys / WASD | Move the keyboard along X and Z |
| Q / E | Move down / up |
| Z / C | Rotate around Y (yaw) |
| `[` / `]` | Scale X (stretch / shrink the keyboard width) |
| Shift (hold) | 10× faster |
| R | Reset to original pose |

Watch the falling notes — they move with the keyboard automatically.

---

## 5. Playing a real MIDI file

The built-in C major demo is just a sanity check. To play a real song:

### Option A — StreamingAssets (recommended)

1. In `Assets/`, create a new folder named **exactly** `StreamingAssets` (capitalization matters; Unity treats this folder specially).
2. Inside it create a `Songs/` folder.
3. Drop your `.mid` file in there, e.g. `Assets/StreamingAssets/Songs/MaryHadALittleLamb.mid`.
4. In the Hierarchy, select your **FallingNotesDemo** GameObject.
5. In the Inspector, change **Song Source** to `StreamingAssetsFile`.
6. Set **Streaming Assets Relative Path** to `Songs/MaryHadALittleLamb.mid`.
7. Press Play.

### Option B — Absolute path (quick test)

1. Set **Song Source** to `AbsoluteFile`.
2. Paste a full Windows path into **Absolute File Path**, e.g. `C:/Users/Student/Downloads/song.mid`.
3. Press Play.

> **Why not a TextAsset (drag-and-drop)?** Unity refuses to import `.mid` files as TextAssets by default. You would have to rename `.mid` → `.bytes` for that path. StreamingAssets is easier.

### Where do MIDI files come from for testing?

Free public-domain MIDIs of children's songs (Twinkle Twinkle, Mary Had a Little Lamb) and classical pieces are widely available — `bitmidi.com`, `midiworld.com`, etc. Stick to small, simple files first (≤ 100 notes); huge piano sonatas will work, but they make debugging harder.

---

## 6. Inspector fields, explained

Click your **FallingNotesDemo** GameObject in the Hierarchy and look at the Inspector. Useful knobs:

- **Lead Time** (default 3s) — how many seconds of look-ahead the user sees. More = easier to read, but more notes on screen at once.
- **Spawn Distance** (default 1.5 m) — how far back from the keyboard the notes start. Speed = `Spawn Distance / Lead Time`, so 1.5 m / 3 s = 0.5 m/s.
- **Start Delay** (default 0.5 s) — pre-roll pause so the first notes have time to fall into view.
- **Keyboard Position** — where the keyboard root sits in world space. Default is roughly table-height, 40 cm in front of the camera.
- **Show Virtual Keyboard** — uncheck this when you move into AR, so you can see your real piano through passthrough instead of the procedural cubes.

Inside the runtime `Keyboard` GameObject, the **KeyboardLayout** component has a `Config` slot pointing to a runtime-created `KeyboardConfig`. To use your own, see Section 8.

---

## 7. Troubleshooting

**"I don't see any falling notes / I just see a black Game view."**
- Open `Window → General → Console`. Any red errors? Fix those first.
- Did you delete the camera AND the demo created one? Look in the Hierarchy while Play is running — there should be a `Demo Camera`. If not, the demo only creates a camera when `Camera.main == null`. Try unchecking `Create Demo Camera` and adding a regular Main Camera manually pointing at world position `(-0.55, 1.2, -0.15)` looking toward `(+0.55, 0.7, 0.4)`.
- Make sure your **Game** view is the active tab, not the Scene view.

**"I see the keyboard but no notes are falling."**
- Check the Console for `[FallingNotesDemo] Loaded '...' with N notes`. If `N` is 0, your MIDI file produced zero notes — try the built-in demo (`Song Source = BuiltInDemo`) to confirm the rest of the pipeline works.

**"Notes are tiny / wrong scale."**
- The keyboard is sized for 23 mm white keys, ~1.07 m total width. The default camera distance assumes 1 unit = 1 meter (Unity's default). Don't change the Transform `Scale` on the keyboard root; instead change `whiteKeyWidth` on the `KeyboardConfig` if your physical keyboard has different-size keys.

**"Notes are coming from the wrong direction."**
- Make sure the camera is on the `-Z` side of the keyboard. The demo sets this up automatically — if you used your own camera, the keyboard's `+Z` faces away from the player by convention.

**"My Input System throws an InvalidOperationException."**
- Unity 6 ships with the **Input System** package and uses it by default. The calibrator and demo handle both old and new systems via `#if ENABLE_INPUT_SYSTEM`. If you see input-related errors, go to `Edit → Project Settings → Player → Other Settings → Active Input Handling` and set it to **Both** (then restart Unity when it prompts).

---

## 8. Going beyond the bootstrap

The `FallingNotesDemo` script is a convenience for the standalone scene. For your real app, you'll want each piece as its own GameObject set up in the Editor (not at runtime). To do that:

1. **Create a KeyboardConfig asset.** Right-click in the Project window inside `Assets/PianoLearningCore/` → **Create → Piano Learning → Keyboard Config**. Name it `Default76KeyConfig`. Leave defaults (E1..G7, 23 mm white keys).
2. **Create the Keyboard GameObject.** In an empty scene, right-click in the Hierarchy → **Create Empty**, name it `Keyboard`. Add components in this order: `KeyboardLayout`, `KeyboardVisualizer`, `KeyboardCalibrator`.
3. On `KeyboardLayout`, drag your `Default76KeyConfig` into the **Config** field.
4. **Create the FallingNotesController GameObject.** Another empty GameObject named `FallingNotesController`. Add the `FallingNotesController` component. Drag the `Keyboard` GameObject into its **Keyboard** field.
5. To kick off playback in code, write a small `MonoBehaviour` that, in `Start()`, calls:

   ```csharp
   var song = MidiSongLoader.LoadFromStreamingAssets("Songs/MyFile.mid");
   GetComponent<FallingNotesController>().LoadSong(song);
   ```

6. Press Play.

---

## 9. What's next (not part of this milestone)

This module deliberately covers ONLY the falling-note interface, as requested. Future pieces (each a separate task) will be:

- **Audio detection** (microphone → pitch detection → "was the right key played at the right time?") — this is the proposal's big innovation.
- **Hit / miss feedback** — when a note's leading edge crosses Z=0, check if audio detection registered the right pitch within a tolerance window. Green = hit, red = miss. The `FlashKey(int midi)` hook on `KeyboardVisualizer` is already there for this.
- **Learning vs. Assessment mode** — same falling notes, different scoring/feedback rules.
- **Meta XR integration** — replace `Demo Camera` with the OVR/Meta XR camera rig; switch `Show Virtual Keyboard` off so the real piano is visible through passthrough; use the calibrator to align once.

All of those plug into the existing `FallingNotesController` without changes — that was the point of keeping song data, keyboard layout, and visuals separate.

---

If anything in this guide doesn't match what you're seeing, paste the Console output and I'll walk you through it.
