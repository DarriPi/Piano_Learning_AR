package com.darripi.arpiano;

import android.content.Context;
import android.media.midi.MidiDevice;
import android.media.midi.MidiDeviceInfo;
import android.media.midi.MidiManager;
import android.media.midi.MidiOutputPort;
import android.media.midi.MidiReceiver;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;

import java.io.Closeable;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;

/**
 * Live note-ons from a USB MIDI keyboard, for MidiNoteInput.cs — the Detection strip's MIDI mode.
 *
 * The keyboard reaches the headset through a class-compliant USB-MIDI interface (a Roland UM-ONE
 * mk2 with its switch on TAB). Horizon OS registers that with Android's MIDI service by itself
 * (`adb shell dumpsys midi` lists it), so this uses the public android.media.midi API: no
 * permission, and — unlike raw UsbManager access — no "allow access?" dialog. On Quest that
 * dialog would be a 2D panel, and a panel is exactly what took VR input focus away from the app
 * in the Add Song picker (see MidiPickerActivity).
 *
 * Why Java at all: the API hands bytes to a MidiReceiver, which is an abstract CLASS, and C#'s
 * AndroidJavaProxy can only implement interfaces.
 *
 * Threading: bytes arrive on each output port's own thread and are queued; device open/close
 * runs on the main looper; Unity collects the queue with drain() once per frame. Nothing here
 * calls into Unity, so the file needs no UnityPlayer and compiles against android.jar alone.
 */
public final class MidiInputBridge {

    private static final String TAG = "MidiInputBridge";

    /**
     * A note-on still queued this long after it was played is dropped rather than handed to
     * Unity. That only happens when Unity has not run for a while (app paused, a long hitch),
     * and judging those notes now would score them against the wrong moment of the song.
     */
    private static final long MAX_AGE_NANOS = 250_000_000L;

    private static final int QUEUE_SIZE = 256;

    /** First onSend payloads per port logged as hex: proof on the first test that bytes arrive. */
    private static final int RAW_LOG_COUNT = 8;

    private final MidiManager manager;
    private final Handler handler = new Handler(Looper.getMainLooper());

    // Pending note-ons, written by the port threads and read by Unity's thread in drain().
    private final Object queueLock = new Object();
    private final int[] queuedNotes = new int[QUEUE_SIZE];
    private final long[] queuedTimes = new long[QUEUE_SIZE];
    private int queueHead;
    private int queueCount;

    // Main looper only.
    private final List<Connection> connections = new ArrayList<>();
    private boolean running;

    private final MidiManager.DeviceCallback deviceCallback = new MidiManager.DeviceCallback() {
        @Override
        public void onDeviceAdded(MidiDeviceInfo info) {
            open(info);
        }

        @Override
        public void onDeviceRemoved(MidiDeviceInfo info) {
            close(info);
        }
    };

    public MidiInputBridge(Context context) {
        manager = (MidiManager) context.getApplicationContext().getSystemService(Context.MIDI_SERVICE);
        if (manager == null) Log.w(TAG, "This device has no MIDI service");
    }

    /** False when the headset has no Android MIDI service at all. */
    public boolean isSupported() {
        return manager != null;
    }

    /**
     * The MIDI device(s) plugged in right now that can send notes, e.g. "Roland UM-ONE", or ""
     * when there are none — whether or not this bridge is listening. The menu polls this so the
     * MIDI button is only clickable while a keyboard is actually connected. Safe from any thread.
     */
    @SuppressWarnings("deprecation") // getDevices(): see startOnLooper
    public String getPresentDeviceName() {
        if (manager == null) return "";
        StringBuilder names = new StringBuilder();
        for (MidiDeviceInfo info : manager.getDevices()) {
            if (info.getOutputPortCount() == 0) continue;
            if (names.length() > 0) names.append(", ");
            names.append(nameOf(info));
        }
        return names.toString();
    }

    /** Start listening: connects every MIDI device present now, and any plugged in later. */
    public void start() {
        if (manager == null) return;
        handler.post(new Runnable() {
            @Override
            public void run() {
                startOnLooper();
            }
        });
    }

    /** Stop listening and let go of every device. */
    public void stop() {
        if (manager == null) return;
        handler.post(new Runnable() {
            @Override
            public void run() {
                stopOnLooper();
            }
        });
    }

    /**
     * Unity's thread, once per frame: moves the waiting note-ons (MIDI note numbers, oldest first)
     * into out and returns how many. Anything that does not fit stays queued for the next call.
     */
    public int drain(int[] out) {
        long now = System.nanoTime();
        int n = 0;
        synchronized (queueLock) {
            while (queueCount > 0 && n < out.length) {
                int note = queuedNotes[queueHead];
                long time = queuedTimes[queueHead];
                queueHead = (queueHead + 1) % QUEUE_SIZE;
                queueCount--;
                if (now - time <= MAX_AGE_NANOS) out[n++] = note;
            }
        }
        return n;
    }

    private void push(int note) {
        long now = System.nanoTime();
        synchronized (queueLock) {
            if (queueCount == QUEUE_SIZE) {
                // Full: Unity hasn't drained for a long time. Lose the oldest, which is stale anyway.
                queueHead = (queueHead + 1) % QUEUE_SIZE;
                queueCount--;
            }
            int tail = (queueHead + queueCount) % QUEUE_SIZE;
            queuedNotes[tail] = note;
            queuedTimes[tail] = now;
            queueCount++;
        }
    }

    // ------------------------------------------------------------------ main looper

    // The replacements for getDevices() and registerDeviceCallback(callback, handler) are API 33,
    // and the app's minSdk is 32. The old calls still return every MIDI 1.0 (byte-stream) device.
    @SuppressWarnings("deprecation")
    private void startOnLooper() {
        if (running) return;
        running = true;
        // Register before listing, so a device plugged in between the two is not missed. One that
        // shows up in both is opened once: open() skips ids it already has.
        manager.registerDeviceCallback(deviceCallback, handler);
        MidiDeviceInfo[] present = manager.getDevices();
        Log.i(TAG, "Listening; " + present.length + " MIDI device(s) present");
        for (MidiDeviceInfo info : present) open(info);
    }

    private void stopOnLooper() {
        if (!running) return;
        running = false;
        manager.unregisterDeviceCallback(deviceCallback);
        for (Connection c : connections) c.close();
        connections.clear();
        synchronized (queueLock) {
            queueCount = 0;
        }
        Log.i(TAG, "Stopped listening");
    }

    private void open(final MidiDeviceInfo info) {
        if (!running || find(info.getId()) != null) return;
        if (info.getOutputPortCount() == 0) {
            Log.i(TAG, "Ignoring " + nameOf(info) + ": it sends nothing (no output ports)");
            return;
        }

        final Connection c = new Connection(info.getId(), nameOf(info));
        connections.add(c);
        Log.i(TAG, "Opening " + c.name);

        manager.openDevice(info, new MidiManager.OnDeviceOpenedListener() {
            @Override
            public void onDeviceOpened(MidiDevice device) {
                if (device == null) {
                    Log.w(TAG, "Could not open " + c.name);
                    connections.remove(c);
                    return;
                }
                if (!connections.contains(c)) {
                    // Stopped, or unplugged, while it was opening.
                    closeQuietly(device);
                    return;
                }
                c.device = device;
                for (int p = 0; p < info.getOutputPortCount(); p++) {
                    MidiOutputPort port = device.openOutputPort(p);
                    if (port == null) {
                        Log.w(TAG, "Could not open output port " + p + " of " + c.name);
                        continue;
                    }
                    port.connect(new NoteParser(c.name + " port " + p));
                    c.ports.add(port);
                }
                Log.i(TAG, "Connected " + c.name + " (" + c.ports.size() + " output port(s))");
            }
        }, handler);
    }

    private void close(MidiDeviceInfo info) {
        Connection c = find(info.getId());
        if (c == null) return;
        connections.remove(c);
        c.close();
        Log.i(TAG, "Disconnected " + c.name);
    }

    private Connection find(int id) {
        for (Connection c : connections) if (c.id == id) return c;
        return null;
    }

    private static String nameOf(MidiDeviceInfo info) {
        Bundle props = info.getProperties();
        String name = props.getString(MidiDeviceInfo.PROPERTY_NAME);
        if (name == null || name.isEmpty()) name = props.getString(MidiDeviceInfo.PROPERTY_PRODUCT);
        return name == null || name.isEmpty() ? "MIDI device #" + info.getId() : name;
    }

    private static void closeQuietly(Closeable closeable) {
        try {
            closeable.close();
        } catch (IOException e) {
            Log.w(TAG, "Close failed: " + e.getMessage());
        }
    }

    /** One opened device and the output ports we listen on. */
    private static final class Connection {
        final int id;
        final String name;
        final List<MidiOutputPort> ports = new ArrayList<>();
        MidiDevice device; // null until openDevice() answers

        Connection(int id, String name) {
            this.id = id;
            this.name = name;
        }

        void close() {
            for (MidiOutputPort port : ports) closeQuietly(port);
            ports.clear();
            if (device != null) closeQuietly(device);
            device = null;
        }
    }

    // ------------------------------------------------------------------ byte stream -> note-ons

    /**
     * Turns one port's byte stream into note-ons. MIDI does not arrive one message per onSend: a
     * message can be split across calls or several packed into one, keyboards use running status
     * (a run of notes sends the 0x9n status byte only once), and real-time bytes such as active
     * sensing (0xFE, every 300 ms from many keyboards) may turn up anywhere, even mid-message.
     */
    private final class NoteParser extends MidiReceiver {
        private final String source;
        private int status;          // running status: last channel status byte, 0 = none
        private int firstData = -1;  // first data byte of a two-byte message, -1 = not yet seen
        private int rawLogsLeft = RAW_LOG_COUNT;

        NoteParser(String source) {
            this.source = source;
        }

        @Override
        public void onSend(byte[] msg, int offset, int count, long timestamp) {
            if (rawLogsLeft > 0) {
                rawLogsLeft--;
                Log.d(TAG, "Bytes from " + source + ": " + hex(msg, offset, count));
            }

            for (int i = offset; i < offset + count; i++) {
                int b = msg[i] & 0xFF;
                if (b >= 0xF8) continue;            // real-time: no note, and interrupts nothing
                if (b >= 0x80) {
                    // A channel status (0x80-0xEF) becomes the running status. System common and
                    // SysEx (0xF0-0xF7) cancel it, which also skips their data bytes below.
                    status = b < 0xF0 ? b : 0;
                    firstData = -1;
                    continue;
                }
                if (status == 0) continue;          // data byte of SysEx / system common
                int type = status & 0xF0;
                if (type == 0xC0 || type == 0xD0) continue; // one data byte, nothing we use
                if (firstData < 0) {
                    firstData = b;
                    continue;
                }
                // Second data byte: one complete message. A note-on with velocity 0 is a note-off.
                if (type == 0x90 && b > 0) {
                    push(firstData);
                    Log.d(TAG, "Note on " + firstData + " vel " + b);
                }
                firstData = -1; // status stays, for running status
            }
        }
    }

    private static String hex(byte[] msg, int offset, int count) {
        StringBuilder sb = new StringBuilder();
        for (int i = offset; i < offset + count; i++) {
            if (sb.length() > 0) sb.append(' ');
            sb.append(String.format("%02X", msg[i] & 0xFF));
        }
        return sb.toString();
    }
}
