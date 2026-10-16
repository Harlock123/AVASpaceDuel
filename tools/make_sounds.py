#!/usr/bin/env python3
"""Synthesises AVASpaceDuel's sound effects and tunes into ../src/AVASpaceDuel/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVASpaceDuel", "Sounds")
RNG = random.Random(1982)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The craft: a shot's zap, the engine's rumble, and the shield humming up.
    write("Fire.wav", tone(0.12, 1800, 500, "square", release=0.6), 0.2)
    write("Thrust.wav", tone(0.26, 160, 120, "noise", attack=0.03, release=0.4), 0.22)
    write("Shield.wav", tone(0.35, 300, 900, "tri", attack=0.02, release=0.5, vibrato=0.1, vib_rate=30), 0.26)
    # Shapes: breaking in two, smashed to nothing, and a mine going off.
    write("Split.wav", mix(tone(0.3, 600, 200, "noise", release=0.6), tone(0.25, 400, 180, "square", release=0.6, gain=0.4)), 0.34)
    write("Smash.wav", mix(tone(0.45, 900, 80, "noise", release=0.5), tone(0.4, 300, 60, "square", release=0.5, gain=0.5)), 0.4)
    write("MineBlast.wav", mix(tone(0.7, 1500, 50, "noise", release=0.4), tone(0.6, 200, 30, "saw", release=0.4, gain=0.7)), 0.5)
    # The saucer: its shot, and coming down.
    write("SaucerShot.wav", tone(0.14, 2200, 900, "pulse", release=0.6), 0.22)
    write("SaucerDown.wav", cat(mix(tone(0.4, 1200, 200, "noise", release=0.5), tone(0.4, 900, 150, "square", release=0.5, gain=0.5)), *[tone(0.06, hz(n), kind="pulse", release=0.4) for n in ["C6", "E6", "G6", "C7"]]), 0.4)
    # Hit: one of the twins, or the last ship.
    write("ShipHit.wav", mix(tone(0.6, 1000, 80, "noise", release=0.4), tone(0.5, 500, 60, "saw", release=0.4, gain=0.6)), 0.46)
    write("Death.wav", cat(mix(tone(0.9, 1200, 40, "noise", release=0.3), tone(0.8, 400, 30, "saw", release=0.3, gain=0.7)), *[tone(0.12, hz(n), kind="tri", release=0.4) for n in ["E5", "C5", "A4"]]), 0.5)
    write("WaveStart.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["E5", "B5", "E6"]]), 0.3)
    write("WaveClear.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.4) for n in ["E6", "D6", "E6", "G6", "B6", "E7"]]), 0.36)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["A5", "E6", "A5", "E6", "A6"]]), 0.38)

    # The saucer's warble, looping while it's about (exactly one second, so it loops cleanly).
    write("Music.wav", tone(1.0, 520, 520, "square", attack=0.002, release=0.0, vibrato=0.35, vib_rate=6), 0.12)

    # The title: a driving, spacey theme in E minor (an original composition).
    lead = [("E5", 0.5), ("B4", 0.5), ("E5", 0.5), ("G5", 0.5), ("F#5", 1), ("D5", 1), ("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("A5", 0.5), ("G5", 1), ("F#5", 1),
            ("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 0.5), ("D6", 1), ("B5", 1), ("C6", 0.5), ("B5", 0.5), ("A5", 0.5), ("F#5", 0.5), ("E5", 2)]
    bass = [("E2", 0.5), ("E3", 0.5)] * 4 + [("G2", 0.5), ("G3", 0.5)] * 4 + [("C3", 0.5), ("C2", 0.5)] * 4 + [("B1", 0.5), ("B2", 0.5)] * 2 + [("E2", 2)]
    write("TitleTune.wav", cat(tune(140, lead, bass, kind="pulse"), silence(1.2)), 0.42)
    write("StartTune.wav", tune(160, [("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 0.5), ("D6", 0.5), ("E6", 2)], [("E3", 1), ("B2", 1), ("E3", 2)], kind="pulse"), 0.45)
    write("GameOver.wav", tune(72, [("B4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)], [("E2", 2), ("C2", 2), ("B1", 2), ("E2", 2)], kind="tri"), 0.45)


if __name__ == "__main__":
    main()
