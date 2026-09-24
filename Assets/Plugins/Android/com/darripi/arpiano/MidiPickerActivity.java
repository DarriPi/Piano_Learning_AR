package com.darripi.arpiano;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.OpenableColumns;
import android.util.Log;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/**
 * Transparent, one-shot Activity that lets the user pick a MIDI file with Android's system
 * document picker, copies it into a folder Unity chose, and leaves the answer in a file.
 *
 * Why this exists at all: the Storage Access Framework is the only way to read a file out of
 * /sdcard/Download without a storage permission under scoped storage, but it delivers its
 * answer through onActivityResult — and Unity's UnityPlayerGameActivity does not override
 * that method, so a pure-C# picker would fire and then never hear back. This Activity owns
 * the request/result pair.
 *
 * The copy is done here rather than in C# so the content:// URI never has to cross JNI; by
 * the time Unity reads anything, there is a real file at a real path.
 *
 * RUNS IN ITS OWN PROCESS AND ITS OWN TASK (android:process / android:taskAffinity in the
 * manifest). Sharing either with the VR app is what made the app vanish and come back dead:
 * Horizon hides the immersive task when another task with the SAME affinity appears, and the
 * shell tracks panel apps — and which process owns VR input focus — by process name. The
 * price is that UnityPlayer.UnitySendMessage cannot cross a process boundary, so the reply is
 * written to RESULT_FILE_NAME inside destDir and MidiImporter.cs polls for it. Do not
 * reintroduce a UnityPlayer reference here: in this process the class has no player behind it
 * and touching it raises an Error, which a `catch (Exception)` would not stop.
 *
 * Launched from MidiImporter.cs with one string extra:
 *   destDir - folder to copy the chosen file into (created if missing)
 *
 * The reply file holds a single line, "STATUS|detail", split on the first '|':
 *   OK|/path/to/copied.mid
 *   CANCEL|
 *   ERROR|human readable reason
 */
public class MidiPickerActivity extends Activity {

    private static final String TAG = "MidiPickerActivity";

    public static final String EXTRA_DEST_DIR = "destDir";

    /** Reply file, written into destDir. Dot-prefixed and not a .mid, so no song list shows it. */
    public static final String RESULT_FILE_NAME = ".import-result";

    /**
     * Marker MidiImporter.cs drops in destDir the moment the VR app has input focus again. Its
     * presence stands the remaining re-front attempts down — see returnToVrApp. Must match
     * RefocusedFileName on the C# side.
     */
    public static final String REFOCUSED_FILE_NAME = ".vr-refocused";

    private static final int REQUEST_PICK_MIDI = 4711;

    /** Category Horizon OS uses to mark an immersive (VR) entry point, as in our manifest. */
    private static final String VR_INTENT_CATEGORY = "com.oculus.intent.category.VR";

    /**
     * When to re-front the VR app, in milliseconds after this activity finishes.
     *
     * Measured on-device, not guessed. A single immediate re-front DOES work — focus was
     * watched going 0 -> 1 — and is then taken straight back, because it lands in the middle
     * of the shell's own panel teardown:
     *
     *   +0ms   we ask for the VR app        +7ms   shell: "Exiting panel application"
     *   +35ms  focus 0 -> 1 (we won)        +49ms  shell starts FocusPlaceholderActivity
     *   +118ms focus 1 -> 0 (we lost)       +149ms FocusPlaceholderActivity displayed
     *
     * So the attempt that matters is the one AFTER the placeholder is up, and retrying is the
     * only thing that can run at all in this situation: the VR app is PAUSED by then, which
     * freezes every coroutine on the Unity side, including the watchdog written to handle this.
     *
     * A retry is NOT free, though. This comment used to say an unnecessary transition was
     * invisible; on-device that is simply wrong. Every extra attempt costs a full
     * InputFocusLost -> pause -> resume -> InputFocusAcquired cycle, which the user sees as the
     * menu disappearing and coming back. Watched over five imports, attempt 1 won every single
     * time ("focus back after 0.4s and 0 nudge(s)") and attempts 2-4 each threw a working app
     * out and back in for nothing — four vanishes per import. They are kept because nothing here
     * knows in advance that attempt 1 will win, but they now fire only while still needed: see
     * the REFOCUSED_FILE_NAME check in returnToVrApp.
     */
    private static final long[] REFRONT_DELAYS_MS = { 250L, 1200L, 2500L, 4000L };

    /** Guards against someone picking a huge file; real MIDI songs are a few KB. */
    private static final long MAX_BYTES = 8L * 1024L * 1024L;

    private static final int COPY_BUFFER = 8 * 1024;

    /** Same allowed set as UserSongLibrary.SanitizeFileName on the C# side. */
    private static final String ALLOWED_PUNCTUATION = " ._()-";
    private static final int MAX_NAME_LENGTH = 80;

    private String destDir;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        destDir = getIntent().getStringExtra(EXTRA_DEST_DIR);

        // Recreated (rotation, low memory) while the picker is still up: the pending result
        // is still coming, so don't launch a second picker on top of it.
        if (savedInstanceState != null) return;

        if (destDir == null || destDir.length() == 0) {
            finishWith("ERROR|No destination folder was given.");
            return;
        }

        try {
            startActivityForResult(openDocumentIntent(), REQUEST_PICK_MIDI);
        } catch (ActivityNotFoundException openDocumentMissing) {
            // Horizon OS is a heavily modified AOSP and may not ship the Storage Access
            // Framework's DocumentsUI. A file/media app that ignores ACTION_OPEN_DOCUMENT
            // will often still answer the older ACTION_GET_CONTENT, and both come back
            // through onActivityResult identically, so it costs nothing to try.
            Log.w(TAG, "No activity handles ACTION_OPEN_DOCUMENT; trying ACTION_GET_CONTENT",
                    openDocumentMissing);
            try {
                startActivityForResult(getContentIntent(), REQUEST_PICK_MIDI);
            } catch (ActivityNotFoundException getContentMissing) {
                Log.w(TAG, "No activity handles ACTION_GET_CONTENT either", getContentMissing);
                finishWith("ERROR|This headset has no file browser to pick with.");
            }
        } catch (Exception e) {
            Log.e(TAG, "Couldn't start the file picker", e);
            finishWith("ERROR|Couldn't open the file browser.");
        }
    }

    private Intent openDocumentIntent() {
        Intent pick = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        pick.addCategory(Intent.CATEGORY_OPENABLE);
        // Deliberately unfiltered. MIDI files downloaded on a headset are routinely reported
        // as application/octet-stream (or as no type at all), and a MIME filter would grey out
        // the very file the user came here for, with no explanation. Unity parses the file
        // afterwards and rejects it if it isn't a MIDI, which is a far better failure.
        pick.setType("*/*");
        // Local files only — no point offering cloud providers for something we copy anyway.
        pick.putExtra(Intent.EXTRA_LOCAL_ONLY, true);
        return pick;
    }

    private Intent getContentIntent() {
        Intent pick = new Intent(Intent.ACTION_GET_CONTENT);
        pick.addCategory(Intent.CATEGORY_OPENABLE);
        pick.setType("*/*");
        pick.putExtra(Intent.EXTRA_LOCAL_ONLY, true);
        return Intent.createChooser(pick, "Choose a MIDI file");
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);

        if (requestCode != REQUEST_PICK_MIDI) {
            finishWith("ERROR|Unexpected result from the file browser.");
            return;
        }

        Uri uri = (data == null) ? null : data.getData();
        if (resultCode != RESULT_OK || uri == null) {
            finishWith("CANCEL|");
            return;
        }

        try {
            finishWith("OK|" + copyToDestination(uri));
        } catch (Exception e) {
            Log.e(TAG, "Copying the picked file failed", e);
            String reason = e.getMessage();
            finishWith("ERROR|" + ((reason == null || reason.length() == 0)
                    ? "Couldn't copy that file."
                    : reason));
        }
    }

    /** Stream the picked document into destDir under a safe, unique name. Returns the path. */
    private String copyToDestination(Uri uri) throws IOException {
        File folder = new File(destDir);
        if (!folder.exists() && !folder.mkdirs()) {
            throw new IOException("Couldn't create the songs folder.");
        }

        File target = uniqueFile(folder, midiFileName(displayNameOf(uri)));

        InputStream in = null;
        OutputStream out = null;
        try {
            in = getContentResolver().openInputStream(uri);
            if (in == null) throw new IOException("Couldn't open that file.");

            out = new FileOutputStream(target);
            byte[] buffer = new byte[COPY_BUFFER];
            long total = 0;
            int read;
            while ((read = in.read(buffer)) > 0) {
                total += read;
                if (total > MAX_BYTES) {
                    throw new IOException("That file is too big to be a song.");
                }
                out.write(buffer, 0, read);
            }
            out.flush();
        } catch (IOException e) {
            // Never leave a half-written file behind for Unity to find.
            if (target.exists() && !target.delete()) {
                Log.w(TAG, "Couldn't clean up the partial copy at " + target);
            }
            throw e;
        } finally {
            closeQuietly(in);
            closeQuietly(out);
        }

        return target.getAbsolutePath();
    }

    /** The file's name as the source app reports it, falling back to the URI's last segment. */
    private String displayNameOf(Uri uri) {
        Cursor cursor = null;
        try {
            cursor = getContentResolver().query(
                    uri, new String[]{OpenableColumns.DISPLAY_NAME}, null, null, null);
            if (cursor != null && cursor.moveToFirst()) {
                int column = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                if (column >= 0) {
                    String name = cursor.getString(column);
                    if (name != null && name.length() > 0) return name;
                }
            }
        } catch (Exception e) {
            Log.w(TAG, "Couldn't read the document's display name", e);
        } finally {
            if (cursor != null) cursor.close();
        }

        String segment = uri.getLastPathSegment();
        return (segment == null || segment.length() == 0) ? "song.mid" : segment;
    }

    /**
     * Sanitise the name and make sure it ends in .mid / .midi. Mirrors
     * UserSongLibrary.SanitizeFileName so both import paths name files the same way.
     */
    private static String midiFileName(String rawName) {
        StringBuilder sb = new StringBuilder(rawName.length());
        for (int i = 0; i < rawName.length(); i++) {
            char c = rawName.charAt(i);
            boolean ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9') || ALLOWED_PUNCTUATION.indexOf(c) >= 0;
            sb.append(ok ? c : '_');
        }

        String cleaned = trimDotsAndSpaces(sb.toString());
        if (cleaned.length() == 0) cleaned = "song";

        String lower = cleaned.toLowerCase();
        if (lower.endsWith(".mid") || lower.endsWith(".midi")) {
            cleaned = cleaned.substring(0, cleaned.lastIndexOf('.'));
        }
        if (cleaned.length() > MAX_NAME_LENGTH) cleaned = cleaned.substring(0, MAX_NAME_LENGTH);
        if (cleaned.length() == 0) cleaned = "song";

        return cleaned + ".mid";
    }

    private static String trimDotsAndSpaces(String value) {
        int start = 0;
        int end = value.length();
        while (start < end && (value.charAt(start) == ' ' || value.charAt(start) == '.')) start++;
        while (end > start && (value.charAt(end - 1) == ' ' || value.charAt(end - 1) == '.')) end--;
        return value.substring(start, end);
    }

    /** "name.mid", then "name (2).mid", "name (3).mid"... so an import never overwrites. */
    private static File uniqueFile(File folder, String fileName) throws IOException {
        int dot = fileName.lastIndexOf('.');
        String stem = (dot > 0) ? fileName.substring(0, dot) : fileName;
        String extension = (dot > 0) ? fileName.substring(dot) : ".mid";

        File candidate = new File(folder, stem + extension);
        for (int i = 2; candidate.exists() && i < 1000; i++) {
            candidate = new File(folder, stem + " (" + i + ")" + extension);
        }
        if (candidate.exists()) throw new IOException("Too many songs with that name.");
        return candidate;
    }

    private static void closeQuietly(java.io.Closeable stream) {
        if (stream == null) return;
        try {
            stream.close();
        } catch (IOException ignored) {
            // Nothing useful to do — the caller already has the real error.
        }
    }

    /**
     * Leave the answer where Unity will find it and get out of the way.
     *
     * The reply file is written BEFORE the app is re-fronted, so it is already on disk by the
     * time Unity resumes and starts looking. Nothing here waits for Unity: this process and
     * the VR process are independent, and the VR one is paused for most of this.
     */
    private void finishWith(String payload) {
        writeResult(payload);

        // Take ourselves off the stack FIRST: while any window of ours is still up the shell
        // can go on treating a 2D panel as the thing that owns VR input focus. Then re-front
        // the VR app on a delay, because finishing is what STARTS the shell's teardown and the
        // re-front only sticks once that has finished — see REFRONT_DELAYS_MS.
        finish();
        scheduleReturnToVrApp();
    }

    /** Write "STATUS|detail" into destDir for MidiImporter.cs to pick up. */
    private void writeResult(String payload) {
        if (destDir == null || destDir.length() == 0) {
            Log.w(TAG, "No destination folder; dropping result: " + payload);
            return;
        }

        File folder = new File(destDir);
        if (!folder.exists() && !folder.mkdirs()) {
            Log.w(TAG, "Couldn't create " + destDir + "; dropping result: " + payload);
            return;
        }

        // Write-then-rename: Unity polls this path, and a rename is the only way to be sure it
        // never reads a file that is still being written.
        File temp = new File(folder, RESULT_FILE_NAME + ".tmp");
        File target = new File(folder, RESULT_FILE_NAME);
        OutputStream out = null;
        try {
            out = new FileOutputStream(temp);
            out.write(payload.getBytes("UTF-8"));
            out.flush();
            closeQuietly(out);
            out = null;

            if (target.exists() && !target.delete()) {
                Log.w(TAG, "Couldn't clear the previous result file at " + target);
            }
            if (!temp.renameTo(target)) {
                Log.w(TAG, "Couldn't put the result file in place at " + target);
            }
        } catch (IOException e) {
            Log.e(TAG, "Couldn't write the result file for: " + payload, e);
        } finally {
            closeQuietly(out);
            if (temp.exists() && !temp.delete()) {
                Log.w(TAG, "Couldn't clean up " + temp);
            }
        }
    }

    /**
     * Hand the headset back to the VR app, repeatedly, until this process is gone.
     *
     * Finishing this activity is NOT enough on Horizon OS. Showing a 2D panel takes VR *input
     * focus* away from the immersive app, and losing focus does not stop the app rendering:
     * the controller anchors keep moving because OVRCameraRig drives them from node poses.
     * What stops is OVRInput — the runtime hands back an empty controller state — so every
     * trigger press and thumbstick nudge silently does nothing. The symptom is a menu you can
     * point at but cannot click, and it is why this is worth several attempts.
     *
     * These have to be posted, not looped: each attempt is a task transition and the shell
     * needs to settle in between. The delayed messages keep running after finish() because the
     * process outlives its last activity by a few seconds, which is all this needs.
     */
    private void scheduleReturnToVrApp() {
        Handler handler = new Handler(Looper.getMainLooper());
        for (int i = 0; i < REFRONT_DELAYS_MS.length; i++) {
            final int attempt = i + 1;
            handler.postDelayed(new Runnable() {
                @Override
                public void run() {
                    returnToVrApp(attempt);
                }
            }, REFRONT_DELAYS_MS[i]);
        }
    }

    /**
     * Re-launch our own main activity, which is what puts the immersive app back in front and
     * makes the shell re-evaluate who owns VR input focus. The VR category asks the shell to
     * resume it immersively rather than as another 2D panel; getLaunchIntentForPackage has
     * already pinned the component, so an extra category cannot change what is resolved.
     *
     * This process has no way to read the runtime's focus state itself, so it asks the one that
     * can: MidiImporter.RestoreInputFocus drops REFOCUSED_FILE_NAME as soon as focus is back,
     * and every attempt after the first stands down when it sees it. Without that check the
     * fallback attempts kick a perfectly healthy app out of the foreground, once each.
     */
    private void returnToVrApp(int attempt) {
        // Attempt 1 always fires. It is the one that does the real work, and it lands while the
        // picker panel is still on screen, so its transition is not one the user can tell apart
        // from the panel closing. Everything after it exists only for an app that never came
        // back, and an app that DID come back must not be disturbed by it.
        if (attempt > 1 && vrAppHasFocusAgain()) {
            Log.i(TAG, "VR app already has input focus; skipping re-front attempt " + attempt + ".");
            return;
        }

        try {
            Intent back = getPackageManager().getLaunchIntentForPackage(getPackageName());
            if (back == null) {
                Log.w(TAG, "No launch intent for " + getPackageName() + "; can't refocus the VR app");
                return;
            }
            back.addCategory(VR_INTENT_CATEGORY);
            // NEW_TASK (already set by getLaunchIntentForPackage) and NOTHING else. This is the
            // exact intent shape that was observed to move the runtime's focus state from 0 to 1
            // on a headset that was stuck. CLEAR_TOP/SINGLE_TOP/REORDER_TO_FRONT all make
            // Android short-circuit to START_DELIVERED_TO_TOP when the activity is already on
            // top — onNewIntent runs, but there is no task transition, and it is the transition
            // that makes the shell re-evaluate who owns VR input focus.
            startActivity(back);
            Log.i(TAG, "Re-fronted the VR app (attempt " + attempt + ").");
        } catch (Exception e) {
            Log.w(TAG, "Couldn't bring the VR app back to the front (attempt " + attempt + ")", e);
        }
    }

    /**
     * True once MidiImporter.cs has confirmed, from inside the VR app where the focus state is
     * actually readable, that the controllers work again. Absence means "don't know" — that is
     * also what a failed write looks like, and it fails safe: the ladder simply runs in full,
     * which is exactly the behaviour this check replaced.
     */
    private boolean vrAppHasFocusAgain() {
        if (destDir == null || destDir.length() == 0) return false;
        return new File(destDir, REFOCUSED_FILE_NAME).exists();
    }
}
