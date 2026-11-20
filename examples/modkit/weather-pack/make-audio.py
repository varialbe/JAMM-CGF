#!/usr/bin/env python3
"""Render original gapless Ogg Vorbis music and ambience. Requires ffmpeg."""
from array import array
from math import exp, pi, sin
from pathlib import Path
from random import Random
import subprocess
import sys

RATE = 24000
TAU = 2 * pi
OUT = Path(__file__).resolve().parent / "audio"
# MIDI voicings, chord length, and preferred melodic scale tones.
SCORES = {
    "super-sunny": (4.5, [(48, 55, 60, 64, 67), (45, 55, 60, 64, 69),
                         (41, 53, 57, 64, 69), (43, 55, 59, 62, 67)], [0, 2, 3, 4, 2, 1, 3]),
    "rainy": (4.8, [(45, 52, 57, 60, 64), (41, 48, 55, 57, 64),
                    (48, 55, 59, 62, 64), (43, 50, 57, 60, 62)], [3, 2, 4, 1, 2]),
    "windy": (4.0, [(50, 57, 62, 66, 69), (47, 54, 62, 66, 71),
                    (43, 50, 55, 59, 66), (45, 52, 57, 62, 64)], [1, 3, 2, 4, 1, 2, 4, 3]),
    "fog": (5.25, [(50, 57, 60, 64, 69), (46, 53, 57, 60, 65),
                   (48, 55, 62, 64, 67), (45, 52, 55, 60, 64)], [2, 4, 3, 2]),
}


def add_voice(samples, start, note, length, volume, tone):
    frequency = 440 * 2 ** ((note - 69) / 12)
    phase = TAU * frequency / RATE
    offset = round(start * RATE)
    count = round(length * RATE)
    size = len(samples)
    for i in range(count):
        t = i / RATE
        theta = phase * i
        if tone == "pluck":
            envelope = min(1, t * 100) * exp(-3.3 * t) * min(1, (length - t) * 8)
            wave = sin(theta) + 0.24 * exp(-3 * t) * sin(2 * theta) + 0.08 * exp(-8 * t) * sin(3 * theta)
        elif tone == "pad":
            envelope = sin(pi * t / length) ** 1.5
            wave = sin(theta) + 0.26 * sin(theta * 1.002 + 0.35 * sin(TAU * 0.42 * t))
        else:
            envelope = sin(pi * t / length) ** 2
            wave = sin(theta + 0.45 * sin(5 * t)) + 0.18 * sin(2 * theta)
        samples[(offset + i) % size] += volume * envelope * wave


def music(chord_seconds, chords, melody):
    samples = array("f", [0]) * round(RATE * chord_seconds * 16)
    for i in range(16):
        chord = chords[i % len(chords)]
        start = i * chord_seconds
        add_voice(samples, start - 0.42, chord[0], chord_seconds * 1.19, 0.095, "pad")
        add_voice(samples, start - 0.28, chord[1], chord_seconds * 1.14, 0.040, "pad")
        for j, step in enumerate(melody):
            if i % 4 == 3 and j == len(melody) - 1:
                continue
            time = start + chord_seconds * j / (len(melody) + 1)
            note = chord[step] + (12 if (j + i) % 7 == 3 else 0)
            add_voice(samples, time, note, 1.5, 0.15 if j % 3 == 0 else 0.095, "pluck")
            add_voice(samples, time + 0.275, note, 1.3, 0.028, "pluck")
    return samples


def ambience(kind):
    seconds = 24
    size = RATE * seconds
    samples = array("f", [0]) * size
    rng = Random(214 + list(SCORES).index(kind))
    body = 0
    grain = 0
    for i in range(-RATE, size):
        t = i / RATE
        noise = rng.uniform(-1, 1)
        body = body * 0.985 + noise * 0.015
        grain = grain * 0.67 + noise * 0.33
        if i < 0:
            continue
        # The quiet seam keeps the stochastic bed continuous at the wrap.
        seam = min(1, t * 8, (seconds - t) * 8)
        if kind == "rainy":
            samples[i] = seam * (0.62 * grain + 0.16 * (noise - grain))
        elif kind == "windy":
            swell = 0.60 + 0.21 * sin(TAU * t / 8) + 0.13 * sin(TAU * t / 4.8)
            samples[i] = seam * (1.8 * body + 0.19 * grain) * swell
        else:
            samples[i] = seam * (0.82 * body + 0.07 * grain) * (0.72 + 0.23 * sin(TAU * t / 8))
    if kind == "fog":
        for t, note, length in ((5, 37, 3.7), (12, 40, 2.8), (19, 37, 3.4)):
            add_voice(samples, t, note, length, 0.2, "horn")
    if kind == "super-sunny":
        for start in (3, 9.7, 16.2, 20.6):
            for j in range(3):
                length = 0.21 + j * 0.04
                for i in range(round(length * RATE)):
                    t = i / RATE
                    env = sin(pi * t / length) ** 2
                    theta = TAU * (950 * t - 820 * t * t + 13 * sin(TAU * t * 7) / (TAU * 7))
                    samples[round((start + j * 0.33) * RATE) + i] += 0.09 * env * (sin(theta) + 0.35 * sin(2.05 * theta))
    return samples


def encode(samples, path):
    if sys.byteorder == "big":
        samples.byteswap()
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-f", "f32le", "-ar", str(RATE), "-ac", "1",
                    "-i", "-", "-c:a", "libvorbis", "-q:a", "4", str(path)], input=samples.tobytes(), check=True)


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    for weather, score in SCORES.items():
        encode(music(*score), OUT / f"{weather}-music.ogg")
        encode(ambience(weather), OUT / f"{weather}-air.ogg")
