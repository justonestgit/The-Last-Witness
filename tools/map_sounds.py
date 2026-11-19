"""Renders the mod's synthesized heartbeat and footsteps (Synth.cs) to WAV for the custom map,
which can't generate audio at runtime. Usage: python tools/map_sounds.py <out_dir>"""
import math
import random
import struct
import sys
import wave

SR = 44100


def write(path, d, peak):
    m = max(abs(x) for x in d) or 1.0
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(b"".join(struct.pack("<h", int(x / m * peak * 32767)) for x in d))


class Band:
    """RBJ band-pass biquad, as in Synth.Biquad.Band."""

    def __init__(self, f, q):
        w0 = 2 * math.pi * f / SR
        a = math.sin(w0) / (2 * q)
        a0 = 1 + a
        self.b = (a / a0, 0.0, -a / a0)
        self.a = (-2 * math.cos(w0) / a0, (1 - a) / a0)
        self.x1 = self.x2 = self.y1 = self.y2 = 0.0

    def run(self, x):
        y = self.b[0] * x + self.b[2] * self.x2 - self.a[0] * self.y1 - self.a[1] * self.y2
        self.x2, self.x1, self.y2, self.y1 = self.x1, x, self.y1, y
        return y


def noise():
    return random.uniform(-1, 1)


def heartbeat(beats=10, bpm=88):
    """Synth.Heartbeat (lub-dub), repeated beats times."""
    period = 60 / bpm
    d = [0.0] * int(SR * (beats * period + 0.5))
    for k in range(beats):
        s = int(SR * k * period)
        for i in range(int(SR * 0.75)):
            t = i / SR
            v = 0.0
            if t < 0.25:
                v += math.sin(2 * math.pi * (55 - 20 * t) * t) * math.exp(-t * 14)
            t2 = t - 0.27
            if t2 > 0:
                v += 0.7 * math.sin(2 * math.pi * (48 - 15 * t2) * t2) * math.exp(-t2 * 16)
            if s + i < len(d):
                d[s + i] += v
    return d


def footsteps(steps=6, interval=0.55):
    """Synth.Footsteps: a low thump plus band-passed crackle per step."""
    times, t0 = [], 0.02
    for _ in range(steps):
        times.append(t0)
        t0 += interval * random.uniform(0.8, 1.2)
    d = [0.0] * int(SR * (t0 + 0.5))
    for tk in times:
        s = int(SR * tk)
        bp = Band(random.uniform(1200, 3200), 1.2)
        loud, thump = random.uniform(0.6, 1), random.uniform(65, 95)
        for i in range(int(SR * 0.22)):
            t = i / SR
            crackle = noise() * 3 if random.random() < 0.08 else noise() * 0.4
            if s + i < len(d):
                d[s + i] += loud * (math.sin(2 * math.pi * thump * t) * math.exp(-t * 28) * 1.2 + bp.run(crackle) * math.exp(-t * 18))
    return d


if __name__ == "__main__":
    out = sys.argv[1]
    random.seed(7)
    write(out + "/heart.wav", heartbeat(), 0.95)
    write(out + "/steps.wav", footsteps(), 0.85)
    print("ok")
