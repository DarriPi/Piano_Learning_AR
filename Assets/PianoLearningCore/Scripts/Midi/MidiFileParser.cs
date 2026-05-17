using System;
using System.Collections.Generic;
using System.IO;

namespace PianoLearningCore
{
    /// <summary>
    /// Self-contained Standard MIDI File (.mid) parser.
    /// Supports SMF Format 0 (single track) and Format 1 (multi-track synchronous).
    /// Format 2 (independent tracks) is not supported.
    ///
    /// We only extract the note events and tempo changes — enough to build a Song.
    /// No third-party dependencies.
    /// </summary>
    public static class MidiFileParser
    {
        /// <summary>One raw MIDI note event with its absolute time in seconds.</summary>
        public struct RawNote
        {
            public int midiNumber;
            public int channel;
            public float startSeconds;
            public float durationSeconds;
            public byte velocity;
        }

        public static List<RawNote> Parse(string filePath, out float ticksPerQuarterNote)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            return Parse(bytes, out ticksPerQuarterNote);
        }

        public static List<RawNote> Parse(byte[] bytes, out float ticksPerQuarterNote)
        {
            int pos = 0;

            // ----- Header chunk "MThd" -----
            if (!MatchAscii(bytes, ref pos, "MThd"))
                throw new InvalidDataException("Not a MIDI file: missing MThd header.");

            int headerLength = ReadInt32(bytes, ref pos);
            int headerEnd = pos + headerLength;

            int format = ReadInt16(bytes, ref pos);
            int trackCount = ReadInt16(bytes, ref pos);
            int division = ReadInt16(bytes, ref pos);

            if (format == 2)
                throw new NotSupportedException("MIDI Format 2 (independent tracks) is not supported.");

            if ((division & 0x8000) != 0)
                throw new NotSupportedException("SMPTE timing in MIDI files is not supported. Only ticks-per-quarter-note timing is supported.");

            ticksPerQuarterNote = division;
            pos = headerEnd; // skip any extra header bytes

            // ----- Parse each track -----
            // For each track we record raw events with absolute tick times,
            // then merge into a single timeline and convert ticks -> seconds
            // using a global tempo map.
            var allEvents = new List<TrackEvent>();
            for (int t = 0; t < trackCount; t++)
            {
                if (!MatchAscii(bytes, ref pos, "MTrk"))
                    throw new InvalidDataException($"Expected MTrk at byte {pos}.");

                int trackLength = ReadInt32(bytes, ref pos);
                int trackEnd = pos + trackLength;

                long absTick = 0;
                byte runningStatus = 0;

                while (pos < trackEnd)
                {
                    int delta = ReadVarLength(bytes, ref pos);
                    absTick += delta;

                    byte statusByte = bytes[pos];
                    if (statusByte < 0x80)
                    {
                        // Running status: reuse the previous status byte.
                        statusByte = runningStatus;
                    }
                    else
                    {
                        pos++;
                        runningStatus = statusByte;
                    }

                    if (statusByte == 0xFF)
                    {
                        // Meta event
                        byte metaType = bytes[pos++];
                        int metaLen = ReadVarLength(bytes, ref pos);

                        if (metaType == 0x51 && metaLen == 3)
                        {
                            // Set Tempo: 24-bit microseconds per quarter note
                            int mpq = (bytes[pos] << 16) | (bytes[pos + 1] << 8) | bytes[pos + 2];
                            allEvents.Add(new TrackEvent
                            {
                                kind = EventKind.Tempo,
                                absTick = absTick,
                                tempoMicrosPerQn = mpq
                            });
                        }
                        // 0x2F (end of track), 0x58 (time sig), etc. -> skip
                        pos += metaLen;
                    }
                    else if (statusByte == 0xF0 || statusByte == 0xF7)
                    {
                        // SysEx event: skip
                        int sysexLen = ReadVarLength(bytes, ref pos);
                        pos += sysexLen;
                    }
                    else
                    {
                        // Channel voice message
                        byte msgType = (byte)(statusByte & 0xF0);
                        byte channel = (byte)(statusByte & 0x0F);

                        switch (msgType)
                        {
                            case 0x80: // Note Off
                            {
                                byte note = bytes[pos++];
                                byte vel  = bytes[pos++];
                                allEvents.Add(new TrackEvent
                                {
                                    kind = EventKind.NoteOff,
                                    absTick = absTick,
                                    channel = channel,
                                    noteNumber = note,
                                    velocity = vel
                                });
                                break;
                            }
                            case 0x90: // Note On (vel 0 = Note Off)
                            {
                                byte note = bytes[pos++];
                                byte vel  = bytes[pos++];
                                allEvents.Add(new TrackEvent
                                {
                                    kind = vel == 0 ? EventKind.NoteOff : EventKind.NoteOn,
                                    absTick = absTick,
                                    channel = channel,
                                    noteNumber = note,
                                    velocity = vel
                                });
                                break;
                            }
                            case 0xA0: // poly aftertouch
                            case 0xB0: // control change
                            case 0xE0: // pitch bend
                                pos += 2; // two data bytes, ignore
                                break;
                            case 0xC0: // program change
                            case 0xD0: // channel aftertouch
                                pos += 1; // one data byte, ignore
                                break;
                            default:
                                // Unknown status — try to recover by skipping one byte.
                                pos++;
                                break;
                        }
                    }
                }

                pos = trackEnd; // ensure we're cleanly past this track
            }

            // ----- Sort events by absolute tick (stable) -----
            // We use a stable sort by adding an event index as a secondary key.
            for (int i = 0; i < allEvents.Count; i++)
            {
                var e = allEvents[i];
                e.order = i;
                allEvents[i] = e;
            }
            allEvents.Sort((a, b) =>
            {
                int cmp = a.absTick.CompareTo(b.absTick);
                if (cmp != 0) return cmp;
                return a.order.CompareTo(b.order);
            });

            // ----- Walk events, tracking running seconds via tempo map -----
            int currentTempo = 500000; // default 120 BPM
            long prevTick = 0;
            double currentSeconds = 0.0;

            // For each unique (channel, note), remember the last NoteOn so we can compute duration on NoteOff.
            var pending = new Dictionary<int, NoteOnInfo>(); // key = (channel<<8)|note
            var result = new List<RawNote>();

            for (int i = 0; i < allEvents.Count; i++)
            {
                var ev = allEvents[i];

                if (ev.absTick > prevTick)
                {
                    long deltaTicks = ev.absTick - prevTick;
                    double secondsPerTick = currentTempo / 1_000_000.0 / ticksPerQuarterNote;
                    currentSeconds += deltaTicks * secondsPerTick;
                    prevTick = ev.absTick;
                }

                switch (ev.kind)
                {
                    case EventKind.Tempo:
                        currentTempo = ev.tempoMicrosPerQn;
                        break;

                    case EventKind.NoteOn:
                    {
                        // Most MIDI authoring tools never overlap the same pitch on the same channel,
                        // but if a NoteOn arrives while one is still pending, we end the previous one first.
                        int key = (ev.channel << 8) | ev.noteNumber;
                        if (pending.TryGetValue(key, out var existing))
                        {
                            result.Add(new RawNote
                            {
                                midiNumber = ev.noteNumber,
                                channel = ev.channel,
                                startSeconds = (float)existing.startSeconds,
                                durationSeconds = (float)(currentSeconds - existing.startSeconds),
                                velocity = existing.velocity
                            });
                        }
                        pending[key] = new NoteOnInfo
                        {
                            startSeconds = currentSeconds,
                            velocity = ev.velocity
                        };
                        break;
                    }

                    case EventKind.NoteOff:
                    {
                        int key = (ev.channel << 8) | ev.noteNumber;
                        if (pending.TryGetValue(key, out var info))
                        {
                            result.Add(new RawNote
                            {
                                midiNumber = ev.noteNumber,
                                channel = ev.channel,
                                startSeconds = (float)info.startSeconds,
                                durationSeconds = (float)(currentSeconds - info.startSeconds),
                                velocity = info.velocity
                            });
                            pending.Remove(key);
                        }
                        break;
                    }
                }
            }

            // Close out any notes that never received a NoteOff (give them a small default duration).
            foreach (var kv in pending)
            {
                int noteNumber = kv.Key & 0xFF;
                int channel = (kv.Key >> 8) & 0xFF;
                result.Add(new RawNote
                {
                    midiNumber = noteNumber,
                    channel = channel,
                    startSeconds = (float)kv.Value.startSeconds,
                    durationSeconds = 0.25f,
                    velocity = kv.Value.velocity
                });
            }

            result.Sort((a, b) => a.startSeconds.CompareTo(b.startSeconds));
            return result;
        }

        // ----------------------------------------------------------------
        // Internal types
        // ----------------------------------------------------------------

        private enum EventKind { NoteOn, NoteOff, Tempo }

        private struct TrackEvent
        {
            public EventKind kind;
            public long absTick;
            public int order;            // stable-sort tiebreaker
            public byte channel;
            public byte noteNumber;
            public byte velocity;
            public int tempoMicrosPerQn;
        }

        private struct NoteOnInfo
        {
            public double startSeconds;
            public byte velocity;
        }

        // ----------------------------------------------------------------
        // Byte readers
        // ----------------------------------------------------------------

        private static bool MatchAscii(byte[] b, ref int pos, string s)
        {
            if (pos + s.Length > b.Length) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (b[pos + i] != (byte)s[i]) return false;
            }
            pos += s.Length;
            return true;
        }

        private static int ReadInt32(byte[] b, ref int pos)
        {
            int v = (b[pos] << 24) | (b[pos + 1] << 16) | (b[pos + 2] << 8) | b[pos + 3];
            pos += 4;
            return v;
        }

        private static int ReadInt16(byte[] b, ref int pos)
        {
            int v = (b[pos] << 8) | b[pos + 1];
            pos += 2;
            return v;
        }

        /// <summary>Read a MIDI variable-length quantity (1-4 bytes).</summary>
        private static int ReadVarLength(byte[] b, ref int pos)
        {
            int value = 0;
            for (int i = 0; i < 4; i++)
            {
                byte by = b[pos++];
                value = (value << 7) | (by & 0x7F);
                if ((by & 0x80) == 0) return value;
            }
            return value;
        }
    }
}
