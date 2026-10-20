#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""
rb3_chart.py - MP3 -> Rock Band 3 chart MIDI (RB3/RBN custom-song format).

Auto-generates PART GUITAR / PART BASS / PART DRUMS charts from song audio
using beat tracking + onset detection + spectral band classification, following
the RBN/C3 authoring spec (docs.c3universe.com):

  Gem lanes (green, red, yellow, blue, orange) = base + 0..4
    Expert base 96, Hard 84, Medium 72, Easy 60
  Drums: kick=orange(100)  snare=red(97)  hi-hat=yellow(98)  crash=green(96)  tom=blue(99)
  Markers: OD phrase 116, solo 103, trill 127, tremolo 126
  Drum mix events: [mix 0..3 drums0] spread over the first four 16ths
  EVENTS track: [music_start] / [music_end] / [end]  ([end] = last item in file)
  BEAT track: note 12 = downbeat, note 13 = other beats

The tempo map follows the tracked beat grid (a tempo change per beat, median
filtered) so the chart stays locked to live recordings that drift in tempo.

Usage:
    python rb3_chart.py "song.mp3" [-o OUTDIR] [--instruments guitar,bass,drums]
"""

import argparse
import os
import sys
import json

import numpy as np

TPQ = 480          # ticks per quarter note
HOP = 512          # STFT hop
NFFT = 2048

# lane indices: green=0 red=1 yellow=2 blue=3 orange=4
G_BASE = {"easy": 60, "medium": 72, "hard": 84, "expert": 96}
D_BASE = {"easy": 60, "medium": 72, "hard": 84, "expert": 96}

OD_NOTE = 116
SOLO_NOTE = 103
GEM_LEN_16TH = TPQ // 4          # 120 ticks: a gem this long shows no sustain tail
MIN_GAP = TPQ // 8               # 60 ticks: min space between end of one gem and next


# ----------------------------------------------------------------------------
# analysis
# ----------------------------------------------------------------------------

def clean_beats(beats, dur):
    """Dedupe/split tracked beats so every interval is 0.18..1.2 s, anchored at 0."""
    beats = [float(b) for b in beats]
    # drop duplicates / too-close beats
    res = []
    for b in beats:
        if not res or (b - res[-1]) >= 0.18:
            res.append(b)
    # split intervals that are too long (missed beats)
    out = []
    for i, b in enumerate(res):
        if i > 0:
            gap = b - res[i - 1]
            if gap > 1.2:
                n = int(round(gap / 0.5))
                for k in range(1, n):
                    out.append(res[i - 1] + gap * k / n)
        out.append(b)
    # anchor the grid at t=0 and extend past the song end
    if not out or out[0] > 0.2:
        out.insert(0, 0.0)
    med = float(np.median(np.diff(out))) if len(out) > 1 else 0.5
    med = min(max(med, 0.2), 1.0)
    if out[-1] < dur - 0.3:
        out.append(out[-1] + med)
    if out[-1] < dur:
        out.append(float(dur))
    return np.asarray(out, dtype=np.float64)


class Timeline:
    """Maps song seconds -> MIDI ticks using the beat grid as quarter notes."""

    def __init__(self, beats, tpq=TPQ):
        self.beats = beats
        self.tpq = tpq
        self.n = len(beats) - 1                      # number of quarter-note segments
        d = np.diff(beats)
        d = np.clip(d, 0.18, 1.6)
        self.sec_per_tick = d / tpq                  # per segment
        self.ticks = np.arange(self.n + 1, dtype=np.float64) * tpq

    def tick(self, t):
        b = self.beats
        if t <= b[0]:
            return self.tpq * (t - b[0]) / max(b[1] - b[0], 1e-6)
        j = int(np.searchsorted(b, t, side="right") - 1)
        j = min(j, self.n - 1)
        frac = (t - b[j]) / max(b[j + 1] - b[j], 1e-6)
        return self.tpq * (j + min(frac, 1.0))

    def bpms(self):
        return 60.0 / (self.sec_per_tick * self.tpq)


def median_filter(x, k=5):
    if k < 3 or len(x) < k:
        return x.copy()
    pad = k // 2
    xp = np.pad(x, (pad, pad), mode="edge")
    return np.median(np.lib.stride_tricks.sliding_window_view(xp, k), axis=1)


def analyze(path):
    import librosa

    print(f"[analyze] loading {os.path.basename(path)} ...")
    y, sr = librosa.load(path, sr=None, mono=True)
    dur = len(y) / sr
    print(f"[analyze] {dur:.1f}s @ {sr} Hz")

    tempo, beat_frames = librosa.beat.beat_track(
        y=y, sr=sr, hop_length=HOP, units="frames")
    beats = librosa.frames_to_time(beat_frames, sr=sr, hop_length=HOP)
    beats = clean_beats(beats, dur)
    med_iv = float(np.median(np.diff(beats)))
    print(f"[analyze] beats: {len(beats)}  median interval {med_iv:.3f}s "
          f"(~{60.0/med_iv:.1f} BPM nominal)")

    print("[analyze] STFT + chroma ...")
    S = librosa.stft(y, n_fft=NFFT, hop_length=HOP)
    mag = np.abs(S).astype(np.float32)
    del S
    freqs = librosa.fft_frequencies(sr=sr, n_fft=NFFT)
    m_low = mag[(freqs >= 30) & (freqs < 150)].sum(0)
    m_mid = mag[(freqs >= 150) & (freqs < 400)].sum(0)
    m_hi = mag[(freqs >= 4000) & (freqs < 12000)].sum(0)
    m_all = mag.sum(0)
    chroma = librosa.feature.chroma_cqt(y=y, sr=sr, hop_length=HOP)

    env = librosa.onset.onset_strength(y=y, sr=sr, hop_length=HOP)
    try:
        onsets = librosa.onset.onset_detect(
            onset_envelope=env, sr=sr, hop_length=HOP, units="time",
            backtrack=True, energy=env)
    except TypeError:
        onsets = librosa.onset.onset_detect(
            onset_envelope=env, sr=sr, hop_length=HOP, units="time")
    onsets = np.asarray(onsets, dtype=np.float64)
    print(f"[analyze] onsets: {len(onsets)}")

    return {
        "y": y, "sr": sr, "dur": dur, "beats": beats,
        "m_low": m_low, "m_mid": m_mid, "m_hi": m_hi, "m_all": m_all,
        "chroma": chroma, "onsets": onsets,
        "frame_t": librosa.frames_to_time(np.arange(mag.shape[1]), sr=sr, hop_length=HOP),
    }


# ----------------------------------------------------------------------------
# quantization helpers
# ----------------------------------------------------------------------------

def q16(tl, t):
    """Quantize a song time (s) to the nearest 16th-note tick on the beat grid."""
    raw = tl.tick(t)
    j = min(int(raw // TPQ), tl.n - 1)
    frac = raw / TPQ - j
    return int(round(TPQ * (j + round(frac * 4) / 4.0)))


def on_grid(tick, grid_quarters):
    g = int(round(grid_quarters * TPQ))
    return tick % g == 0


# ----------------------------------------------------------------------------
# chart builders
# ----------------------------------------------------------------------------

def build_drums(a, tl):
    """Classify onsets into kick/snare/hat/crash/tom via spectral band energies."""
    sr, H = a["sr"], HOP
    ft = a["frame_t"]
    idx = np.searchsorted(ft, a["onsets"])
    idx = np.clip(idx, 0, len(ft) - 1)

    def robust(m):
        s = float(np.percentile(m, 95))
        return s if s > 1e-9 else 1.0

    nlow, nmid, nhi = robust(a["m_low"]), robust(a["m_mid"]), robust(a["m_hi"])

    raw = []   # (t, lane, strength)
    for i in idx:
        sl = slice(i, min(i + 3, len(ft)))
        k = a["m_low"][sl].max() / nlow
        m = a["m_mid"][sl].max() / nmid
        h = a["m_hi"][sl].max() / nhi
        t = float(ft[i])
        pads = []
        if k > 0.5 and k >= m:
            pads.append((100, k))                 # kick -> orange
        if m > 0.45 and m >= 0.8 * k:
            pads.append((97, m))                  # snare -> red
        if h > 0.55:
            # crash on strong beats, hi-hat otherwise
            frac_pos = tl.tick(t) / TPQ
            on_beat = abs(frac_pos - round(frac_pos)) < 1e-6 and h > 0.8
            pads.append((96 if on_beat else 98, h))
        if not pads:
            best = max([("kick", k), ("snare", m), ("hat", h)], key=lambda x: x[1])
            pads.append(({"kick": 100, "snare": 97, "hat": 98}[best[0]], best[1]))
        pads.sort(key=lambda x: -x[1])
        # keep at most kick+one other pad (avoid 3-limb hits)
        keep = pads[:1]
        if len(pads) > 1 and pads[1][1] > 0.55 and pads[0][0] == 100:
            keep.append(pads[1])
        for lane, _s in keep:
            raw.append((t, lane))

    # density caps: hats/kicks at most one per 8th, snares per 16th
    notes = []
    last = {96: -9e9, 98: -9e9, 100: -9e9, 97: -9e9, 99: -9e9}
    min_gap = {96: 2, 98: 2, 100: 2, 97: 1, 99: 1}     # in 16th units
    for t, lane in sorted(raw):
        tick = q16(tl, t)
        g = min_gap.get(lane, 1) * (TPQ // 4)
        if tick - last[lane] < g:
            continue
        last[lane] = tick
        notes.append((tick, (lane - 96,)))             # abs note -> lane index 0..4
    merged = {}
    for tick, lanes in notes:
        merged.setdefault(tick, set()).update(lanes)
    notes = []
    for tick in sorted(merged):
        ls = sorted(merged[tick])
        pads = [l for l in ls if l != 0][:2]    # lane 0 = kick (note 96); max 2 pads + kick
        notes.append((tick, tuple(([0] if 0 in ls else []) + pads)))
    print(f"[drums] {len(notes)} expert gems")
    return notes


def _rank_pitch_classes(counts):
    """Map the 12 pitch classes to lanes 0..4 by popularity (wrapping)."""
    order = sorted(range(12), key=lambda pc: (-counts[pc], pc))
    rank = {pc: r for r, pc in enumerate(order)}
    return {pc: rank[pc] % 5 for pc in range(12)}


def build_guitar(a, tl):
    """Dominant chroma pitch at each onset -> lane (wrapping); power chords when a
    second strong consonant peak exists."""
    counts = np.zeros(12)
    picked = []
    ch = a["chroma"]
    ft = a["frame_t"]
    for t in a["onsets"]:
        i = int(np.searchsorted(ft, t))
        if i >= ch.shape[1]:
            continue
        c = ch[:, i:min(i + 2, ch.shape[1])].mean(1)
        if c.max() <= 0:
            continue
        order = np.argsort(-c)
        p0 = int(order[0])
        picked.append((t, p0, int(order[1]), float(c[order[1]] / max(c[order[0]], 1e-9))))
        counts[p0] += 1
    if not picked:
        return []
    lane_of = _rank_pitch_classes(counts)

    notes = []
    last_tick = -99999
    for t, p0, p1, ratio in picked:
        tick = q16(tl, t)
        if tick <= last_tick:
            continue
        last_tick = tick
        l0 = lane_of[p0]
        chord = [l0]
        if ratio > 0.65:
            d = (p1 - p0) % 12
            if min(d, 12 - d) in (3, 4, 5, 7):
                chord.append(lane_of[p1])
        notes.append((tick, tuple(sorted(set(chord)))))
    notes = _thin_density(notes, max_per_sec=9.0 * DENSITY)
    print(f"[guitar] {len(notes)} expert gems")
    return notes


def build_bass(a, tl):
    """Low-band spectral peaks -> lanes by low-frequency pitch class."""
    sr, H = a["sr"], HOP
    m = a["m_low"]
    if len(m) < 8:
        return []
    thr = float(np.percentile(m, 75)) * 0.8
    peaks = []
    i = 1
    while i < len(m) - 1:
        if m[i] > thr and m[i] >= m[i - 1] and m[i] >= m[i + 1]:
            peaks.append(i)
            i += 4                      # ~90 ms refractory
        else:
            i += 1
    freqs_per_bin = sr / NFFT
    cand = []
    for i in peaks:
        lo = max(i - 2, 0)
        hi = min(i + 3, len(m))
        # dominant low frequency around the peak via chroma-free bin hunt:
        # approximate pitch from the low-band centroid
        seg = a["m_low"][lo:hi]
        t = float(a["frame_t"][i])
        cand.append((t, float(seg.max())))
    if not cand:
        return []
    # pitch class from chroma (low band filtered by using the low octave bins)
    ch = a["chroma"]
    ft = a["frame_t"]
    counts = np.zeros(12)
    picked = []
    for t, _s in cand:
        i = int(np.searchsorted(ft, t))
        if i >= ch.shape[1]:
            continue
        c = ch[:, i:min(i + 2, ch.shape[1])].mean(1)
        if c.max() <= 0:
            continue
        p0 = int(np.argmax(c))
        picked.append((t, p0))
        counts[p0] += 1
    if not picked:
        return []
    lane_of = _rank_pitch_classes(counts)
    notes = []
    last_tick = -99999
    for t, p0 in picked:
        tick = q16(tl, t)
        if tick - last_tick < TPQ // 2:      # min 8th between bass gems
            continue
        last_tick = tick
        notes.append((tick, (lane_of[p0],)))
    notes = _thin_density(notes, max_per_sec=6.0 * DENSITY)
    print(f"[bass] {len(notes)} expert gems")
    return notes


DENSITY = 1.0


def _thin_density(notes, max_per_sec):
    """Cap sustained density: within any 1 s window keep at most max_per_sec notes."""
    if not notes:
        return notes
    if DENSITY < 1.0:
        notes = [n for i, n in enumerate(notes) if int((i + 1) * DENSITY) > int(i * DENSITY)]
        if not notes:
            return notes
    keep = []
    window_ticks = TPQ * 4     # assume ~1s only as rough bound; count-based instead
    # simple approach: track count over a rolling tick window derived from avg spacing
    spacing = []
    for i in range(1, len(notes)):
        spacing.append(notes[i][0] - notes[i - 1][0])
    if not spacing:
        return notes
    med = max(float(np.median(spacing)), 1.0)
    win = int(max(4, round(TPQ * 60.0 / max(60.0 / (med / TPQ), 20.0))))  # ~1 s in ticks
    j0 = 0
    for i, (tick, lanes) in enumerate(notes):
        while notes[i][0] - notes[j0][0] > win:
            j0 += 1
        if (i - j0 + 1) <= max_per_sec:
            keep.append((tick, lanes))
    return keep


# ----------------------------------------------------------------------------
# difficulty reduction (RBN rules: hard=no 3-note or G/O; medium=no 3-note G/O;
# easy=singles only) + coarse-grid thinning
# ----------------------------------------------------------------------------

def group_by_tick(notes):
    g = {}
    for tick, lanes in notes:
        g.setdefault(tick, set()).update(lanes)
    return sorted((t, tuple(sorted(ls))) for t, ls in g.items())


def reduce_diff(notes_expert, grid_quarters, max_chord, allow_go):
    out = []
    for tick, lanes in group_by_tick(notes_expert):
        if not on_grid(tick, grid_quarters):
            continue
        ls = list(lanes)
        if len(ls) > max_chord:
            ls = ls[:max_chord]
        if not allow_go and 0 in ls and 4 in ls:
            ls = [x for x in ls if x != 4] or [ls[0]]
        if len(ls) >= 3 and 0 in ls and 4 in ls:
            ls = [x for x in ls if x != 4]
        if ls:
            out.append((tick, tuple(sorted(set(ls)))))
    return out


def assign_lengths(notes):
    """Gem length 1/16 (no sustain tail), shortened to keep a gap before the next gem."""
    out = []
    for i, (tick, lanes) in enumerate(notes):
        if i + 1 < len(notes):
            gap = notes[i + 1][0] - tick
            ln = min(GEM_LEN_16TH, max(24, int(gap * 0.7)))
        else:
            ln = GEM_LEN_16TH
        out.append((tick, lanes, ln))
    return out


# ----------------------------------------------------------------------------
# MIDI writing
# ----------------------------------------------------------------------------

def _flush(track, abs_msgs):
    prev = 0
    for tick, prio, msg in sorted(abs_msgs, key=lambda x: (max(0, x[0]), x[1])):
        tick = max(0, int(tick))
        msg.time = tick - prev
        prev = tick
        track.append(msg)


def write_midi(path, tl, tracks, events, beat_notes, end_tick, start_tick):
    import mido
    from mido import MidiFile, MidiTrack, MetaMessage, Message

    mid = MidiFile(type=1, ticks_per_beat=TPQ)

    # ---- track 0: tempo / time signature map
    t0 = MidiTrack()
    msgs = []
    msgs.append((0, 0, MetaMessage("time_signature", numerator=4, denominator=2)))
    bpms = median_filter(tl.bpms(), 5)
    for j, bpm in enumerate(bpms):
        tick = int(j * TPQ)
        if tick >= end_tick:
            break
        msgs.append((tick, 1, MetaMessage(
            "set_tempo", tempo=int(round(60_000_000 / float(np.clip(bpm, 30, 300)))))))
    _flush(t0, msgs)
    t0.append(MetaMessage("end_of_track", time=0))
    mid.tracks.append(t0)

    # ---- EVENTS track
    te = MidiTrack()
    msgs = [(0, 0, MetaMessage("track_name", name="EVENTS"))]
    for tick, text in events:
        msgs.append((int(tick), 2, MetaMessage("text", text=text)))
    msgs.append((int(end_tick), 3, MetaMessage("text", text="[end]")))
    _flush(te, msgs)
    te.append(MetaMessage("end_of_track", time=0))
    mid.tracks.append(te)

    # ---- BEAT track
    tb = MidiTrack()
    msgs = [(0, 0, MetaMessage("track_name", name="BEAT"))]
    for tick, note in beat_notes:
        msgs.append((int(tick), 2, Message("note_on", note=note, velocity=100, channel=0)))
        msgs.append((int(tick + TPQ // 4), 1, Message("note_off", note=note, velocity=0, channel=0)))
    _flush(tb, msgs)
    tb.append(MetaMessage("end_of_track", time=0))
    mid.tracks.append(tb)

    # ---- instrument tracks
    for name, diffs in tracks:
        tr = MidiTrack()
        msgs = [(0, 0, MetaMessage("track_name", name=name))]
        if name == "PART DRUMS":
            for k in range(4):     # required drum mix events on the first 4 16ths
                msgs.append((k * (TPQ // 4), 1,
                             MetaMessage("text", text=f"[mix {k} drums0]")))
        for diff_name, notes in diffs:
            notes = sorted((n for n in notes if n[0] >= 0), key=lambda n: n[0])
            fixed = []
            for idx, (tick, lanes, ln) in enumerate(notes):
                nxt = next((n[0] for n in notes[idx + 1:] if n[0] > tick), None)
                if nxt is not None and tick + ln > nxt:
                    ln = max(1, nxt - tick)
                fixed.append((tick, lanes, ln))
            notes = fixed
            vel = {"expert": 100, "hard": 90, "medium": 80, "easy": 70}[diff_name]
            for tick, lanes, ln in notes:
                for lane in lanes:
                    note = D_BASE[diff_name] + lane if name == "PART DRUMS" \
                        else G_BASE[diff_name] + lane
                    msgs.append((tick, 2, Message("note_on", note=note, velocity=vel, channel=0)))
                    msgs.append((tick + ln, 1, Message("note_off", note=note, velocity=0, channel=0)))
        # overdrive phrases: 2 bars out of every 4, snapped to real expert notes
        ex = []
        for dn, dnotes in diffs:
            if dn == "expert":
                ex = sorted(n[0] for n in dnotes if n[0] >= 0)
        for b in range(0, tl.n - 8, 16):
            st = int(b * TPQ)
            en = st + 8 * TPQ
            if st < start_tick:
                continue
            inside = [t for t in ex if st <= t < en]
            if len(inside) < 4:
                continue
            msgs.append((inside[0], 2, Message("note_on", note=OD_NOTE, velocity=100, channel=0)))
            msgs.append((inside[-1] + TPQ // 8, 1, Message("note_off", note=OD_NOTE, velocity=0, channel=0)))
        _flush(tr, msgs)
        tr.append(MetaMessage("end_of_track", time=0))
        mid.tracks.append(tr)

    mid.save(path)


# ----------------------------------------------------------------------------
# validation
# ----------------------------------------------------------------------------

def validate(path):
    import mido
    mid = mido.MidiFile(path)
    names = [t.name for t in mid.tracks if hasattr(t, "name")]
    print(f"[validate] tracks: {names}")
    ok = True
    for need in ("EVENTS", "BEAT"):
        if need not in names:
            print(f"  ERROR: missing {need} track")
            ok = False
    if not any(n.startswith("PART ") for n in names):
        print("  ERROR: no instrument track")
        ok = False

    allowed = set()
    for b in (60, 72, 84, 96):
        allowed.update(range(b, b + 5))
    allowed.update({OD_NOTE, SOLO_NOTE, 101, 102, 126, 127})

    end_tick = 0
    last_ev = None
    for tr in mid.tracks:
        tick = 0
        active = {}
        spans = {}
        n = getattr(tr, "name", "")
        for msg in tr:
            tick += msg.time
            if n == "EVENTS" and msg.type == "text":
                last_ev = (tick, msg.text)
                if msg.text == "[end]":
                    end_tick = tick
            if msg.type == "note_on" and msg.velocity > 0:
                if n.startswith("PART "):
                    if msg.note not in allowed:
                        print(f"  ERROR: {n} note {msg.note} out of range")
                        ok = False
                    elif msg.note < 101:      # gem notes only (skip markers)
                        base = (msg.note // 12) * 12
                        active.setdefault((msg.note, base), []).append(tick)
                        spans.setdefault(base, [])
            elif msg.type == "note_off" or (msg.type == "note_on" and msg.velocity == 0):
                for key in list(active):
                    if key[0] == msg.note and active[key]:
                        st = active[key].pop(0)
                        spans[key[1]].append((st, tick))
        for base, sp in spans.items():
            sp.sort()
            for i in range(1, len(sp)):
                # same start = chord members (valid); only flag real overlaps
                if sp[i][0] < sp[i - 1][1] and sp[i][0] != sp[i - 1][0]:
                    print(f"  ERROR: overlapping notes in {n} diff-base {base} "
                          f"at {sp[i]}")
                    ok = False
                    break

    if last_ev is None or last_ev[1] != "[end]":
        print(f"  ERROR: last event is {last_ev}, expected [end]")
        ok = False
    else:
        print(f"  [end] at tick {end_tick} is the last item: OK")
    print("[validate]", "PASS" if ok else "FAIL")
    return ok


# ----------------------------------------------------------------------------
# main
# ----------------------------------------------------------------------------

def main():
    ap = argparse.ArgumentParser(description="MP3 -> RB3 chart MIDI")
    ap.add_argument("mp3")
    ap.add_argument("-o", "--outdir", default=None)
    ap.add_argument("--instruments", default="guitar,bass,drums")
    ap.add_argument("--density", type=float, default=1.0, help="note density multiplier (0.6 easier, 1.4 busier)")
    args = ap.parse_args()
    global DENSITY
    DENSITY = max(0.3, min(2.0, args.density))

    mp3 = os.path.abspath(args.mp3)
    stem = os.path.splitext(os.path.basename(mp3))[0]
    stem = stem.replace("(mp3.pm)", "").strip("_- ")
    outdir = os.path.abspath(args.outdir or os.path.join(os.path.dirname(__file__), "build", stem))
    os.makedirs(outdir, exist_ok=True)

    a = analyze(mp3)
    tl = Timeline(a["beats"])

    inst = [s.strip().lower() for s in args.instruments.split(",") if s.strip()]
    builders = {"guitar": build_guitar, "bass": build_bass, "drums": build_drums}
    expert = {}
    for name in inst:
        if name in builders:
            expert[name] = group_by_tick(builders[name](a, tl))

    # grids: hard=8ths, medium=quarters, easy=halves
    grids = {"expert": 0.25, "hard": 0.5, "medium": 1.0, "easy": 2.0}
    max_chord = {"expert": 3, "hard": 2, "medium": 2, "easy": 1}
    allow_go = {"expert": True, "hard": False, "medium": False, "easy": False}

    tracks = []
    last_note = 0
    first_note = None
    for name, notes in expert.items():
        diffs = []
        for diff in ("expert", "hard", "medium", "easy"):
            d = reduce_diff(notes, grids[diff], max_chord[diff], allow_go[diff])
            d = assign_lengths(d)
            diffs.append((diff, d))
            if d:
                last_note = max(last_note, d[-1][0] + d[-1][2])
                if first_note is None:
                    first_note = d[0][0]
        tracks.append((f"PART {name.upper()}", diffs))
        counts = {diff: len(d) for diff, d in diffs}
        print(f"[reduce] {name}: {counts}")

    if first_note is None:
        print("ERROR: no notes detected in any instrument")
        return 1

    start_tick = max(0, first_note - TPQ)
    music_end = last_note + TPQ
    end_tick = int(max(tl.tick(a["dur"]), music_end + 2 * TPQ))
    events = [(start_tick, "[music_start]"), (music_end, "[music_end]")]

    # BEAT track: downbeat phase chosen by onset strength
    import librosa
    env = librosa.onset.onset_strength(y=a["y"], sr=a["sr"], hop_length=HOP)
    times = a["frame_t"]
    benv = np.array([env[min(int(np.searchsorted(times, b)), len(env) - 1)] for b in a["beats"]])
    best_p, best_s = 0, -1e18
    for p in range(4):
        on = benv[p::4]
        off = np.delete(benv, slice(p, None, 4)) if len(benv) > p else np.array([0.0])
        if len(on) == 0 or len(off) == 0:
            continue
        s = float(on.mean() - off.mean())
        if s > best_s:
            best_s, best_p = s, p
    print(f"[beat] downbeat phase = beat {best_p} of the tracked grid")
    beat_notes = []
    for i, b in enumerate(a["beats"][:-1]):
        tick = int(i * TPQ)
        if tick + TPQ >= end_tick - TPQ:    # last note must be >=1 beat before [end]
            break
        beat_notes.append((tick, 12 if (i - best_p) % 4 == 0 else 13))

    midpath = os.path.join(outdir, stem + "_chart.mid")
    write_midi(midpath, tl, tracks, events, beat_notes, end_tick, start_tick)
    print(f"[write] {midpath}")

    ok = validate(midpath)
    summary = {
        "song": stem, "duration_s": round(a["dur"], 1),
        "avg_bpm": round(float(60.0 / np.median(np.diff(a["beats"]))), 1),
        "instruments": inst, "valid": ok,
        "tracks": {n: {d: len(x) for d, x in dd} for n, dd in tracks},
    }
    with open(os.path.join(outdir, stem + "_summary.json"), "w") as f:
        json.dump(summary, f, indent=2)
    print(json.dumps(summary, indent=2))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
