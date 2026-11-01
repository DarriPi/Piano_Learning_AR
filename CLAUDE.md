# Piano Learning AR — working notes

AR piano-learning app for Quest 3 (Unity, **OpenXR loader** — no Oculus XR plugin).
Package `com.darripi.arpiano`, main activity `com.unity3d.player.UnityPlayerGameActivity`.
Build output: `Apks/MicTest.apk` (built from Unity, then `adb install -r`).

Scope rule: this is an honours-proposal project — **build only what the proposal specifies**, no
extras. Detailed history lives in the per-topic memory files; this file is the current handoff.

---

## IN PROGRESS — "Add your own MIDI" breaks the controllers

Branch: `feature/native-mic-capture`. Feature files: `Assets/PianoLearningCore/Scripts/Midi/MidiImporter.cs`,
`UserSongLibrary.cs`, `Assets/PianoLearningCore/Scripts/UI/AddSongButton.cs`,
`Assets/Plugins/Android/com/darripi/arpiano/MidiPickerActivity.java`.

### Status as of 2026-08-06 (evening)

- **The picker itself works.** Horizon OS does expose a document picker; the file copies in and
  the new song row appears. That risk is closed and has stayed closed.
- **Step 1 (the 2026-08-04 finish-order + watchdog fix) was device-tested and FAILED.** User
  report: "the app disappears completely", then it is unusable when you get back to it.
- **Step 2 (separate process + separate task + file-based reply) was device-tested and WORKS.**
  The controllers come back every time: five imports in one capture, all logging
  `VR input focus back after 0.4s and 0 nudge(s)`. The unclickable-app bug is CLOSED.
- **Step 3 — the flicker — is fixed in the tree but NOT yet device-tested.** Step 2 left one
  visible defect: the menu vanished and came back four times per import (user-confirmed count).
  See below. Rebuild the APK and confirm one vanish per import, not four.

### The flicker, and why re-fronts are now conditional (2026-08-06)

`MidiPickerActivity` fires `REFRONT_DELAYS_MS = {250, 1200, 2500, 4000}` — four re-launches of
the immersive app, posted on a Handler with nothing able to cancel them. **Attempt 1 already
does the whole job**, and attempts 2-4 then each throw a healthy app out and back in:

```
20:53:50.622  Re-fronted the VR app (attempt 1)
20:53:50.914  [OVRManager] InputFocusAcquired          <- back, working
20:53:51.153  [MidiImporter] VR input focus back after 0.4s and 0 nudge(s)
20:53:51.569  Re-fronted the VR app (attempt 2)
20:53:51.600  [OVRManager] InputFocusLost              <- and there goes the menu
20:53:52.914  ... attempt 3 -> InputFocusLost
20:53:54.409  ... attempt 4 -> InputFocusLost
```

⇒ **An unnecessary re-front is NOT free.** The old comment in the Java file claimed the extra
transitions were invisible; on-device each one costs a full
`InputFocusLost → pause → resume → InputFocusAcquired` cycle that the user sees.

The retries can't simply be deleted — they are the only thing that can run when the app never
comes back at all, because Unity (and every coroutine in it) is frozen in exactly that state.
So they are now **gated**: `MidiImporter` writes `.vr-refocused` next to `.import-result` the
moment `HasInputFocus` goes true, and `returnToVrApp` skips **attempts 2+** when that file
exists. Attempt 1 is deliberately unconditional — it does the real work, and it lands while the
picker panel is still on screen, so its transition is not one the user can pick out. Absence of
the marker fails safe: the full ladder runs, i.e. exactly the old behaviour.

### Root cause (settled over live adb — do not re-derive)

It is **VR input focus**, never a UI or raycast bug:

- Controller *poses* come from `OVRPlugin.GetNodePose` and keep flowing while the app is merely
  VISIBLE → **the laser still moves**.
- Controller *buttons* come from `OVRPlugin.GetControllerState`, which the runtime leaves EMPTY
  unless the app is FOCUSED → **clicks die**.
- ⇒ **Triage rule for any future Quest input report: "laser moves but nothing clicks" = no input
  focus. A *frozen* laser means the opposite (app actually paused).**
  (Since 2026-09-28 `ControllerUIPointer` hides the laser when the controller is put down, using
  the runtime's in-hand flag + touch sensors (NOT movement: a resting controller jitters 2-4°).
  That check is skipped while `!OVRManager.hasInputFocus`, because touch data comes from the same
  empty `GetControllerState`, so this rule still holds. **Don't remove that bypass**: without it,
  lost focus would show up as "no laser" instead.)

Three further facts nailed down on 2026-08-06, each from the log line quoted:

1. **It is never a crash.** The process survives the whole episode (same pid logging before and
   after); `/data/anr` had nothing newer than April.
2. **The app is PAUSED, not merely unfocused** — that is the "disappears completely" half.
   `ActivityTaskManager: Add Task{#2738 arpiano} to hidden list because adding Task{#2742
   arpiano}` — Horizon hides the immersive task the moment a second task with the **same
   `taskAffinity`** appears. ⇒ **The C# focus watchdog could never have worked: it is a Unity
   coroutine, and Unity is frozen in exactly the state the watchdog exists to repair.** Proof:
   `_expectPickerReturn` was still `true` 2.5 minutes later, so the 20 s watchdog never finished
   a single pass; the "focus back after 0.4s" it eventually logged was after the user had
   already rescued the app by hand.
3. **One re-front is not too weak, it is too EARLY.** It works and is then undone, in 118 ms:

   ```
   48.269  START .../UnityPlayerGameActivity   (returnToVrApp)
   48.276  [SEO] PanelAppHost: Exiting panel application (panelId:36) ... System Defined Closure
   48.287  START ... result code=3             (task-to-front — the transition we want)
   48.304  ClientMgrFocus: Updating Focus State: 1 for com.darripi.arpiano   <- we won
   48.318  START com.oculus.vrshell/.FocusPlaceholderActivity
   48.387  ClientMgrFocus: Updating Focus State: 0 for com.darripi.arpiano   <- shell took it back
   48.418  Displayed com.oculus.vrshell/.FocusPlaceholderActivity: +51ms
   ```

   The re-front lands **inside the shell's own panel teardown**, whose tail end then puts
   `FocusPlaceholderActivity` on top. That race is the "intermittent" behaviour noted before —
   50 ms one run, 4 minutes the next. **The attempt that counts is the one after the placeholder
   is up**, which is why the retries are now on a delay and live in the picker process.

**Going to Home and back does NOT fix it** (a task switch issues no activity `START`, so the
shell never re-evaluates focus). Manual recovery that IS proven to unstick a headset:

```sh
adb shell am start -a android.intent.action.MAIN -c android.intent.category.LAUNCHER \
  -c com.oculus.intent.category.VR -n com.darripi.arpiano/com.unity3d.player.UnityPlayerGameActivity
```

### The fix currently in the tree (1-2 device-tested and WORKING; 3's gate pending test)

1. **`AndroidManifest.xml`: `android:process=":midipicker"` AND
   `android:taskAffinity="com.darripi.arpiano.picker"` on `MidiPickerActivity`.** Both levers,
   because they are different mechanisms — affinity is what triggers the "hidden list" line
   above, process is what the shell registers a panel app and its focus by. `MidiImporter` also
   adds `FLAG_ACTIVITY_NEW_TASK` explicitly, because Android only consults an affinity when the
   activity starts a task (the shell was adding the flag itself; don't depend on that).
2. **Reply is a file, not `UnitySendMessage`** — it cannot cross a process boundary.
   `MidiPickerActivity` writes `STATUS|detail` to `.import-result` in the songs folder
   (write-then-rename, so a half-written file is never read) and `MidiImporter.WaitForPickerResult`
   polls for it. Polling beats a resume callback twice over: the coroutine simply freezes with the
   app and continues on resume, and it also covers the case where the app is never paused at all.
   ⚠️ **Never reintroduce a `UnityPlayer` reference into that Java file** — in the picker process
   the class has no player behind it and touching it raises an `Error`, which `catch (Exception)`
   does not stop. It now compiles with **no `unity-classes.jar` on the classpath**; keep it that way.
3. **The re-front retries from the picker process**, at 250/1200/2500/4000 ms after `finish()`,
   posted on a Handler — the only code that can run while Unity is frozen. Intent shape unchanged:
   **`FLAG_ACTIVITY_NEW_TASK` ONLY**, plus category `com.oculus.intent.category.VR`.
   ⚠️ **Never add `CLEAR_TOP` / `SINGLE_TOP` / `REORDER_TO_FRONT` to a re-front intent.** With
   those, Android short-circuits to `START_DELIVERED_TO_TOP` — `onNewIntent` runs but there is no
   task transition, and **it is the transition that makes the shell re-evaluate focus.**
   Attempts 2+ are now gated on the `.vr-refocused` marker — see the flicker section above.
   ⚠️ **Never make attempt 1 conditional too**: the marker is written by the app that is paused
   for most of this, so at 250 ms "no marker" does not yet mean "not back".
4. `MidiImporter.RestoreInputFocus` is **kept as the backstop**, not the primary fix — it can only
   act once Unity is running again. After ~20 s `AddSongButton` tells the user to **close the app
   and open it again** (the old "press the Meta button" advice was wrong — see Home-and-back above).

### Next session — test and read the logs

Rebuild the APK in Unity, `adb install -r Apks/MicTest.apk`. **Capture to a FILE, don't trust
`logcat -d`** — `PianoAudioDetector` logs once a second and rolled the main buffer in under 3
minutes on 2026-08-06, taking every Unity line with it while the quieter *system* buffer still
held `ActivityTaskManager` lines from 6 minutes back. That asymmetry is what makes a `-d` dump
look like "the app never logged anything":

```sh
adb logcat -G 32M          # survives until reboot; do this once and stop losing evidence
adb logcat -c && adb logcat -v time > repro.log &
```

```sh
grep -E "\[MidiImporter\]|MidiPickerActivity|START u0.*arpiano|InputFocus" repro.log
```

What each line means:

- `[OVRManager] InputFocusLost` / `InputFocusAcquired` — the cleanest per-import signal there is,
  and **one Lost/Acquired pair per import is the pass mark.** Four pairs is the old flicker.
- `Updating Focus State: 0` = app gets no controller buttons; `1` = it does. Watch whether a `1`
  **stays** `1` — winning it briefly was never the problem.
- `Add Task ... to hidden list` **should be absent** — if it reappears, the taskAffinity split
  didn't take (check the merged manifest in the build, not just the source one).
- `Re-fronted the VR app (attempt N)` vs `VR app already has input focus; skipping re-front
  attempt N` — after the fix, expect attempt 1 to fire and 2-4 to skip. If 2-4 still *fire*, the
  `.vr-refocused` marker isn't landing in time: check for the `Couldn't write the refocused
  marker` warning, and note the margin is only ~400 ms (marker at attempt1+530 ms, attempt 2 at
  attempt1+950 ms), so a slower `RestoreInputFocus` poll would eat it.
- `[MidiImporter] VR input focus back after 0.4s and 0 nudge(s)` — the recovery worked and the
  marker has just been written. `0 nudge(s)` means the picker-side attempt 1 did the job.
- `[MidiImporter] Picker result:` — the file-based reply arrived.

### The fallback — no longer needed, kept for the record

Step 2 worked, so **do not reach for this**. It was the plan if the picker could never hand focus
back. The option that avoids Horizon's 2D-panel path **entirely** is to drop the system picker
and list `.mid` files already sitting in the app's external files folder
(`/sdcard/Android/data/com.darripi.arpiano/files/Songs`, reachable from a PC over USB and already
scanned by `UserSongLibrary`). No permission, no second activity, never leaves immersive mode — at
the cost of the in-headset browse. Worth raising with the user before building it, since it changes
the feature's UX rather than fixing it.

---

## Reusable gotchas

- **Song folders:** in this build `Application.persistentDataPath` resolves to
  `/storage/emulated/0/Android/data/com.darripi.arpiano/files` (Write Permission = External), so
  the import root and the `adb push` drop folder are the SAME folder.
  `UserSongLibrary.Enumerate` dedupes via a `seen` HashSet.
- **JNI null-argument trap:** `AndroidJavaObject.Call` builds the signature from each argument's
  RUNTIME type, so **any null argument is typed `Ljava/lang/Object;`** and can never match
  `(Ljava/lang/String;)…` — even with a cast. Use raw `AndroidJNI.GetMethodID` + `CallObjectMethod`
  with the signature written out (see `UserSongLibrary.ResolveExternalDropRoot`).
- **Why there is Java here at all:** Unity's `UnityPlayerGameActivity` does not override
  `onActivityResult`, and C# can't subclass a Java class (`AndroidJavaProxy` does interfaces only),
  so a pure-C# SAF picker would fire and never hear back.
- **Compile-checking without opening Unity:**
  - C#: the default `csc` pass defines BOTH `UNITY_ANDROID` and `UNITY_EDITOR`, so it never
    compiles `#if UNITY_ANDROID && !UNITY_EDITOR` blocks. Run a **second pass with the
    `UNITY_EDITOR*` defines stripped**, and prove the branch is live by injecting a deliberate
    syntax error into it first. Editor-assembly passes must reference the **freshly built**
    `Assembly-CSharp.dll`, not the stale one in `Library/ScriptAssemblies`.
  - Java: `<Unity>/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/javac.exe -Xlint:all -cp
    "<SDK>/platforms/android-34/android.jar;Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/libs/unity-classes.jar"`.
    **Classpath must use Windows paths** — the POSIX `/c/...` form silently becomes "bad path
    element" and produces dozens of bogus errors.
- **Build fails with `JSON parse error: The document is empty` at `BuildPlayerDataExtractor`:**
  the files in `Library/BuildPlayerData/Player/` are the right SIZE but all NUL bytes, left that
  way when a session dies mid-build at `[853/854] BuildPlayerDataGenerator` (2026-08-06 — the
  size reaches the MFT, the data never gets flushed). It repeats forever because their mtime is
  newer than their inputs, so Bee thinks the node is up to date. **Delete both files and
  rebuild**; nothing else needs touching. `find Library -size 0` will NOT find them — test with
  `tr -d '\000' < f | wc -c`.
- **PowerShell tool has hung on this project before — prefer the Bash tool.**
