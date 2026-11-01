# Note Detection Accuracy — Results Log

Microphone-based (audio-only) note detection on Meta Quest 3, measured against the proposal
criteria: **≥85% detection accuracy** and **≤1% false-positive rate** in controlled conditions.

---

## 1. Usability test — 85% accuracy MET

During the usability test, detection accuracy reached the **≥85% criterion** on the
**Happy Birthday** song.

> The detailed figures from this test (date, participant count, mode, tempo, hit/miss
> counts) are recorded with the usability-test materials, not in this project folder.

---

## 2. Internal testing — LAST RECORDED internal session (2026-09-24)

This is the **last recorded detection session done internally** by the developer. The
detector code has not changed since (commit `8ac235b`, 2026-09-24), so these figures
describe the current build's detector.

**Setup:** Quest 3, native `UNPROCESSED` microphone capture at 44.1 kHz, electronic piano
played through its own speakers, one room (measured ~28 dB ambient).

### 2.1 Accuracy (recall)

| Song | Runs | Hit | Missed | Recall | Criterion (≥85%) |
|---|---|---|---|---|---|
| Twinkle Twinkle (melody) | 7 | 207 | 117 | **64%** | Not met |
| Ode to Joy (melody) | 1 | — | — | **81.5%** | Not met (single run) |

### 2.2 False-positive rate across detector builds

Three builds measured on the same day, in the same room, on the same piano:

| Build | Hit | Wrong | False-positive rate |
|---|---|---|---|
| Original peak-picking | 122 | 194 | 61% |
| + adaptive noise floor & harmonic-sum scoring | 163 | 68 | 29% |
| + harmonic guard fix (current) | 267 | 72 | **21%** |

Criterion is ≤1%, so it is **not met**. The rate is about 20× the target.

### 2.3 Recall by pitch (current build)

| Note | Recall |
|---|---|
| F#4 | 95% |
| F4 | 84% |
| D4 | 77% |
| G4 | 70% |
| E4 | 60% |
| C4 | 54% |
| A4 | 25% |

**Likely cause of the spread (hypothesis, not proven):** the room has steady tonal hum at
roughly 200 / 221 / 350 / 370 Hz.

- 221 Hz is A3, and A4 (440 Hz) is its 2nd harmonic. The detector can credit A3 with the
  room's energy plus the played A4, and then remove the real A4 as A3's overtone.
- F4 (~350 Hz) and F#4 (~370 Hz) sit on the two strongest room tones, so part of their high
  recall may be room hum being credited as correct notes.

---

## 3. Caveats

- Figures come from the app's own evaluator, so they mix player errors with detector errors.
- Internal runs were played at mixed tempos.
- Phantom notes detected while no note was due are not counted, so the true false-positive
  rate is probably higher than reported.
- All internal testing used an electronic keyboard's speakers in one room. No acoustic piano
  and no other rooms were tested.
- The room met the proposal's <40 dB quiet-room condition (~28 dB), yet false positives stayed
  high. Most of the damage comes from steady low-frequency tonal hum, which a dB meter barely
  registers. A quiet room by dB is not necessarily quiet for pitch detection.
- Practice-mode runs are not accuracy measurements: the song waits on each note until it is
  detected, so a run can end on a perfect score however many attempts each note took.
- Assessment mode at 1.0× tempo (test T3 in the proposal) has no internal detection numbers.
