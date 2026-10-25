using System;
using UnityEngine;

namespace TheLastWitness
{
    /// <summary>
    /// Every sound of the mod, synthesised on the spot from random numbers: no two whispers, screams or drones
    /// are the same, and nothing has to be shipped or licensed.
    /// </summary>
    public static class Synth
    {
        private const int SR = 44100;
        private static float Rf(float a, float b) => a + (float)Director.Rng.NextDouble() * (b - a);
        private static float Noise() => (float)Director.Rng.NextDouble() * 2f - 1f;
        private static int N(float seconds) => Mathf.Max(1, (int)(seconds * SR));

        private static AudioClip Clip(string name, float[] d)
        {
            // Filled through the reader callback: SetData's Span overload doesn't bind against net472's mscorlib.
            int pos = 0;
            return AudioClip.Create(name, d.Length, 1, SR, false, buf =>
            {
                int n = Math.Min(buf.Length, d.Length - pos);
                Array.Copy(d, pos, buf, 0, n);
                for (int i = n; i < buf.Length; i++) buf[i] = 0f;
                pos += n;
            }, p => pos = p);
        }

        /// <summary>RBJ biquad.</summary>
        private class Biquad
        {
            private float b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public static Biquad Band(float f, float q) => new Biquad().SetBand(f, q);
            public static Biquad Low(float f, float q = 0.707f) => new Biquad().SetLow(f, q);

            public Biquad SetBand(float f, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(f, 20f, SR * 0.45f) / SR, al = Mathf.Sin(w) / (2f * q), a0 = 1f + al;
                b0 = al / a0; b1 = 0f; b2 = -al / a0; a1 = -2f * Mathf.Cos(w) / a0; a2 = (1f - al) / a0;
                return this;
            }

            public Biquad SetLow(float f, float q)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(f, 20f, SR * 0.45f) / SR, al = Mathf.Sin(w) / (2f * q), c = Mathf.Cos(w), a0 = 1f + al;
                b0 = (1f - c) / 2f / a0; b1 = (1f - c) / a0; b2 = b0; a1 = -2f * c / a0; a2 = (1f - al) / a0;
                return this;
            }

            public float Run(float x)
            {
                float y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
            }
        }

        private static void Normalize(float[] d, float peak)
        {
            float m = 1e-6f;
            foreach (float v in d) m = Mathf.Max(m, Mathf.Abs(v));
            for (int i = 0; i < d.Length; i++) d[i] *= peak / m;
        }

        /// <summary>Schroeder reverb in place; the buffer must already hold room for the tail.</summary>
        private static void Reverb(float[] d, float wet, float room)
        {
            int[] combs = { 1557, 1617, 1491, 1422 }, alls = { 225, 556 };
            var outp = new float[d.Length];
            foreach (int len in combs)
            {
                var buf = new float[len];
                int p = 0;
                float lp = 0f;
                for (int i = 0; i < d.Length; i++)
                {
                    float y = buf[p];
                    lp = y * 0.7f + lp * 0.3f;
                    buf[p] = d[i] + lp * room;
                    p = (p + 1) % len;
                    outp[i] += y * 0.25f;
                }
            }
            foreach (int len in alls)
            {
                var buf = new float[len];
                int p = 0;
                for (int i = 0; i < d.Length; i++)
                {
                    float b = buf[p], y = -outp[i] + b;
                    buf[p] = outp[i] + b * 0.5f;
                    p = (p + 1) % len;
                    outp[i] = y;
                }
            }
            for (int i = 0; i < d.Length; i++) d[i] = d[i] * (1f - wet) + outp[i] * wet;
        }

        private static float Env(float t, float attack, float length) =>
            t < 0f || t > length ? 0f : Mathf.Min(1f, t / Mathf.Max(attack, 1e-4f)) * Mathf.Clamp01((length - t) / Mathf.Max(length * 0.3f, 1e-4f));

        // ---------- soundtrack ----------

        /// <summary>A seamless loop: low beating tones, wind and rumble; dissonant adds a tritone and a metallic shimmer.</summary>
        public static AudioClip Drone(float seconds, bool dissonant)
        {
            int n = N(seconds), fade = N(3f);
            var d = new float[n + fade];
            // Integer cycles per loop keep every tone seamless.
            float Q(float f) => Mathf.Max(1f, Mathf.Round(f * seconds)) / seconds;
            float f0 = Q(Rf(36f, 52f));
            var tones = dissonant
                ? new[] { (f0, 0.5f), (Q(f0 * 1.006f), 0.4f), (Q(f0 * 1.414f), 0.3f), (Q(f0 * 2.83f), 0.12f), (Q(f0 * 3.01f), 0.08f) }
                : new[] { (f0, 0.5f), (Q(f0 * 1.004f), 0.45f), (Q(f0 * 2f), 0.15f), (Q(f0 * 3f), 0.05f) };
            var lfos = new float[tones.Length];
            for (int k = 0; k < lfos.Length; k++) lfos[k] = Q(Rf(0.04f, 0.2f));
            var wind = Biquad.Band(500f, 2f);
            var rumble = Biquad.Low(120f);
            float brown = 0f, windLfo = Q(Rf(0.03f, 0.08f));
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, v = 0f;
                for (int k = 0; k < tones.Length; k++)
                {
                    float amp = tones[k].Item2 * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * lfos[k] * t + k));
                    v += amp * Mathf.Sin(2f * Mathf.PI * tones[k].Item1 * t);
                }
                if (i % 64 == 0) wind.SetBand(450f + 350f * Mathf.Sin(2f * Mathf.PI * windLfo * t), 1.5f);
                float nz = Noise();
                v += wind.Run(nz) * 0.9f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * windLfo * 2f * t));
                brown = Mathf.Clamp(brown + nz * 0.02f, -1f, 1f);
                v += rumble.Run(brown) * 1.5f;
                if (dissonant) v += 0.04f * Mathf.Sin(2f * Mathf.PI * Q(1730f) * t) * Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * lfos[0] * 3f * t));
                d[i] = v;
            }
            var loop = new float[n];
            for (int i = 0; i < n; i++)
            {
                float a = i < fade ? (float)i / fade : 1f;
                loop[i] = i < fade ? d[i] * a + d[n + i] * (1f - a) : d[i];
            }
            Normalize(loop, 0.8f);
            return Clip("drone", loop);
        }

        // ---------- body ----------

        public static AudioClip Heartbeat()
        {
            var d = new float[N(0.75f)];
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, v = 0f;
                if (t < 0.25f) v += Mathf.Sin(2f * Mathf.PI * (55f - 20f * t) * t) * Mathf.Exp(-t * 14f);
                float t2 = t - 0.27f;
                if (t2 > 0f) v += 0.7f * Mathf.Sin(2f * Mathf.PI * (48f - 15f * t2) * t2) * Mathf.Exp(-t2 * 16f);
                d[i] = v;
            }
            Normalize(d, 0.95f);
            return Clip("heart", d);
        }

        public static AudioClip Breath(float seconds)
        {
            var d = new float[N(seconds)];
            var bp = Biquad.Band(Rf(700f, 1100f), 1.2f);
            var lp = Biquad.Low(2500f);
            float inhale = Rf(0.35f, 0.5f);
            for (int i = 0; i < d.Length; i++)
            {
                float u = (float)i / d.Length;
                float env = u < inhale ? Mathf.Sin(Mathf.PI * u / inhale) * 0.6f : Mathf.Sin(Mathf.PI * (u - inhale) / (1f - inhale));
                d[i] = lp.Run(bp.Run(Noise())) * env * env;
            }
            Normalize(d, 0.7f);
            return Clip("breath", d);
        }

        public static AudioClip Ringing(float seconds)
        {
            var d = new float[N(seconds)];
            float f = Rf(5500f, 7800f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, u = (float)i / d.Length;
                d[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Sin(Mathf.PI * u) * 0.25f;
            }
            return Clip("ringing", d);
        }

        // ---------- voices ----------

        /// <summary>Breathy syllables with moving formants: speech you can't quite understand. Sometimes backwards.</summary>
        public static AudioClip Whisper(float seconds)
        {
            int n = N(seconds), tail = N(0.6f);
            var d = new float[n + tail];
            var hi = Biquad.Band(2600f, 1.4f);
            var air = Biquad.Band(5200f, 2f);
            var formant = Biquad.Band(900f, 3f);
            int i = 0;
            while (i < n)
            {
                int syl = N(Rf(0.07f, 0.26f)), gap = N(Rf(0.02f, 0.12f));
                formant.SetBand(Rf(500f, 1800f), 3f);
                hi.SetBand(Rf(2000f, 3200f), 1.4f);
                float loud = Rf(0.4f, 1f);
                for (int k = 0; k < syl && i < n; k++, i++)
                {
                    float e = Mathf.Sin(Mathf.PI * k / syl);
                    float x = Noise();
                    d[i] = (hi.Run(x) * 0.8f + air.Run(x) * 0.4f + formant.Run(x) * 0.6f) * e * loud;
                }
                i += gap;
                if (Director.Rng.NextDouble() < 0.15) i += N(Rf(0.15f, 0.4f)); // a pause between "words"
            }
            if (Director.Rng.NextDouble() < 0.35) Array.Reverse(d, 0, n);
            Reverb(d, 0.25f, 0.6f);
            Normalize(d, 0.9f);
            return Clip("whisper", d);
        }

        /// <summary>The jumpscare: detuned saws gliding up, distorted, over a noise burst and a hit.</summary>
        public static AudioClip Screech(float seconds)
        {
            int n = N(seconds), tail = N(0.5f);
            var d = new float[n + tail];
            float f = Rf(380f, 720f), glide = Rf(0.3f, 1.2f);
            var detune = new float[6];
            var phase = new float[6];
            for (int k = 0; k < 6; k++) detune[k] = Rf(0.97f, 1.03f) * (k % 3 == 2 ? 1.5f : 1f);
            var nzf = Biquad.Band(3000f, 0.8f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR, v = 0f;
                float pitch = f * (1f + glide * t / seconds) * (1f + 0.04f * Mathf.Sin(2f * Mathf.PI * Rf(5f, 9f) * t));
                for (int k = 0; k < 6; k++)
                {
                    phase[k] = (phase[k] + pitch * detune[k] / SR) % 1f;
                    v += phase[k] * 2f - 1f;
                }
                v = v / 6f + nzf.Run(Noise()) * 0.6f;
                if (t < 0.15f) v += Mathf.Sin(2f * Mathf.PI * 60f * t) * (1f - t / 0.15f) * 2f;
                d[i] = (float)Math.Tanh(v * 3f) * Env(t, 0.008f, seconds);
            }
            Reverb(d, 0.2f, 0.7f);
            Normalize(d, 0.98f);
            return Clip("screech", d);
        }

        /// <summary>A voice screaming far away in the trees: gliding formant, muffled, a lot of room.</summary>
        public static AudioClip DistantScream()
        {
            float seconds = Rf(0.9f, 2f);
            int n = N(seconds), tail = N(2.5f);
            var d = new float[n + tail];
            float f = Rf(280f, 520f), drop = Rf(0.2f, 0.5f), ph = 0f;
            var f1 = Biquad.Band(Rf(700f, 1000f), 4f);
            var f2 = Biquad.Band(Rf(1200f, 1700f), 5f);
            var lp = Biquad.Low(1400f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float pitch = f * (1f + 0.15f * Mathf.Sin(Mathf.PI * t / seconds) - drop * Mathf.Pow(t / seconds, 2f)) * (1f + 0.03f * Mathf.Sin(2f * Mathf.PI * 6f * t));
                ph = (ph + pitch / SR) % 1f;
                float saw = ph * 2f - 1f + Noise() * 0.15f;
                d[i] = lp.Run(f1.Run(saw) + f2.Run(saw) * 0.7f) * Env(t, 0.08f, seconds);
            }
            Reverb(d, 0.6f, 0.82f);
            Normalize(d, 0.8f);
            return Clip("scream", d);
        }

        // ---------- things ----------

        /// <summary>Steps on leaves: a thump and a crunch each, with human-ish irregular timing.</summary>
        public static AudioClip Footsteps(int steps, float interval)
        {
            var times = new float[steps];
            float t0 = 0.02f;
            for (int k = 0; k < steps; k++)
            {
                times[k] = t0;
                t0 += interval * Rf(0.8f, 1.2f);
            }
            var d = new float[N(t0 + 0.5f)];
            for (int k = 0; k < steps; k++)
            {
                int s = N(times[k]);
                var bp = Biquad.Band(Rf(1200f, 3200f), 1.2f);
                float loud = Rf(0.6f, 1f), thump = Rf(65f, 95f);
                for (int i = 0; i < N(0.22f) && s + i < d.Length; i++)
                {
                    float t = (float)i / SR;
                    float crackle = Director.Rng.NextDouble() < 0.08 ? Noise() * 3f : Noise() * 0.4f;
                    d[s + i] += loud * (Mathf.Sin(2f * Mathf.PI * thump * t) * Mathf.Exp(-t * 28f) * 1.2f + bp.Run(crackle) * Mathf.Exp(-t * 18f));
                }
            }
            Normalize(d, 0.85f);
            return Clip("steps", d);
        }

        public static AudioClip Knocks()
        {
            int count = Director.Rng.Next(2, 6);
            float t0 = 0.02f;
            var d = new float[N(count * 0.45f + 1.8f)];
            for (int k = 0; k < count; k++)
            {
                int s = N(t0);
                var bp = Biquad.Band(Rf(170f, 260f), 5f);
                for (int i = 0; i < N(0.15f); i++)
                {
                    float t = (float)i / SR;
                    d[s + i] += bp.Run(i < 40 ? Noise() * 4f : 0f) * 3f + Mathf.Sin(2f * Mathf.PI * 110f * t) * Mathf.Exp(-t * 40f);
                }
                t0 += Rf(0.18f, 0.45f);
            }
            Reverb(d, 0.35f, 0.7f);
            Normalize(d, 0.9f);
            return Clip("knocks", d);
        }

        /// <summary>Radio static with dropouts and crackle.</summary>
        public static AudioClip Static(float seconds)
        {
            var d = new float[N(seconds)];
            var bp = Biquad.Band(Rf(1500f, 4000f), 0.7f);
            bool on = true;
            for (int i = 0; i < d.Length; i++)
            {
                if (i % 512 == 0 && Director.Rng.NextDouble() < 0.12) on = !on;
                float x = Noise() + (Director.Rng.NextDouble() < 0.002 ? Noise() * 8f : 0f);
                d[i] = on ? bp.Run(x) : bp.Run(0f) * 0.2f;
            }
            Normalize(d, 0.8f);
            return Clip("static", d);
        }

        /// <summary>A music box playing a minor tune that slows down and goes out of tune.</summary>
        public static AudioClip MusicBox()
        {
            int[] scale = { 0, 2, 3, 5, 7, 8, 11, 12, 14, 15 };
            int notes = Director.Rng.Next(7, 13);
            float root = Rf(520f, 700f), t0 = 0.05f, step = Rf(0.28f, 0.42f);
            var d = new float[N(notes * step * 2f + 3f)];
            for (int k = 0; k < notes; k++)
            {
                float u = (float)k / notes;
                float f = root * Mathf.Pow(2f, scale[Director.Rng.Next(scale.Length)] / 12f) * (1f - u * Rf(0f, 0.06f));
                int s = N(t0);
                for (int i = 0; i < N(1.6f) && s + i < d.Length; i++)
                {
                    float t = (float)i / SR;
                    d[s + i] += (Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * f * 2f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * f * 3.01f * t)) * Mathf.Exp(-t * 3.5f);
                }
                t0 += step * (1f + u * 1.2f);
            }
            Reverb(d, 0.4f, 0.75f);
            Normalize(d, 0.6f);
            return Clip("musicbox", d);
        }

        /// <summary>Electric hum of a lamp: 50/60 Hz and harmonics with a crackle. Loops seamlessly.</summary>
        public static AudioClip Hum()
        {
            const float seconds = 2f;
            var d = new float[N(seconds)];
            float f = Director.Rng.NextDouble() < 0.5 ? 50f : 60f;
            var bp = Biquad.Band(3000f, 1f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                d[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * f * 2f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * f * 3f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * f * 5f * t)
                    + bp.Run(Director.Rng.NextDouble() < 0.003 ? Noise() * 6f : Noise() * 0.1f) * 0.4f;
            }
            Normalize(d, 0.6f);
            return Clip("hum", d);
        }

        /// <summary>A bulb bursting: a sharp crack, glassy ring, a fizz.</summary>
        public static AudioClip Pop()
        {
            var d = new float[N(1.2f)];
            var bp = Biquad.Band(Rf(2500f, 5000f), 6f);
            float ring = Rf(2800f, 4200f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                float crack = t < 0.01f ? Noise() * 3f : 0f;
                d[i] = crack + bp.Run(Noise()) * Mathf.Exp(-t * 9f) + 0.3f * Mathf.Sin(2f * Mathf.PI * ring * t) * Mathf.Exp(-t * 14f);
            }
            Normalize(d, 0.9f);
            return Clip("pop", d);
        }

        /// <summary>A two-tone alarm, rough and echoing, like a reactor about to fail.</summary>
        public static AudioClip Siren(float seconds)
        {
            int n = N(seconds);
            var d = new float[n + N(1.5f)];
            float lo = Rf(420f, 520f), hi = lo * Rf(1.25f, 1.4f), rate = Rf(0.6f, 1f), ph = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                float f = Mathf.Lerp(lo, hi, 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * rate * t));
                ph = (ph + f / SR) % 1f;
                d[i] = (float)Math.Tanh((ph < 0.5f ? 1f : -1f) * 0.6f + Mathf.Sin(2f * Mathf.PI * ph) * 0.8f) * Env(t, 0.2f, seconds);
            }
            Reverb(d, 0.45f, 0.8f);
            Normalize(d, 0.7f);
            return Clip("siren", d);
        }

        /// <summary>Thunder: a crack, then a long rolling rumble.</summary>
        public static AudioClip Thunder()
        {
            float seconds = Rf(3f, 5f);
            var d = new float[N(seconds)];
            var lp = Biquad.Low(Rf(150f, 300f));
            var crackF = Biquad.Band(1800f, 0.7f);
            float brown = 0f, rate = Rf(2f, 4f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR, nz = Noise();
                brown = Mathf.Clamp(brown + nz * 0.05f, -1f, 1f);
                float roll = 0.6f + 0.4f * Mathf.Sin(t * rate) * Mathf.Sin(t * 1.3f);
                d[i] = lp.Run(brown) * 4f * Mathf.Exp(-t * 0.9f) * roll + crackF.Run(nz) * Mathf.Exp(-t * 12f) * 1.5f;
            }
            Normalize(d, 0.95f);
            return Clip("thunder", d);
        }

        /// <summary>
        /// Many voices screaming at once, close and distorted, over a rumble that rises: "Hora vermelha".
        /// Each voice is a run of gliding formant screams that overlap the others.
        /// </summary>
        public static AudioClip ScreamChoir(float seconds)
        {
            int n = N(seconds);
            var d = new float[n + N(1f)];
            for (int v = 0; v < 6; v++)
            {
                float pos = Rf(0f, 0.8f);
                while (pos < seconds - 0.3f)
                {
                    float len = Mathf.Min(Rf(1.5f, 4f), seconds - pos);
                    float f0 = Rf(250f, 750f) * (v % 2 == 0 ? 1f : 1.5f), glide = Rf(-0.4f, 0.5f), vib = Rf(4f, 8f), ph = 0f;
                    var f1 = Biquad.Band(Rf(700f, 1100f), 3f);
                    var f2 = Biquad.Band(Rf(1300f, 2600f), 4f);
                    int s0 = N(pos), m = N(len);
                    for (int i = 0; i < m && s0 + i < n; i++)
                    {
                        float t = (float)i / SR, u = t / len;
                        float pitch = f0 * (1f + glide * u) * (1f + 0.05f * Mathf.Sin(2f * Mathf.PI * vib * t));
                        ph = (ph + pitch / SR) % 1f;
                        float x = ph * 2f - 1f + Noise() * 0.25f;
                        d[s0 + i] += (f1.Run(x) + f2.Run(x) * 0.8f + x * 0.15f) * Mathf.Sin(Mathf.PI * u);
                    }
                    pos += len * Rf(0.6f, 1.1f);
                }
            }
            var lp = Biquad.Low(120f);
            float brown = 0f;
            for (int i = 0; i < n; i++)
            {
                brown = Mathf.Clamp(brown + Noise() * 0.05f, -1f, 1f);
                d[i] = (float)Math.Tanh((d[i] + lp.Run(brown) * 3f * i / n) * 2.5f) * Mathf.Min(1f, i / (SR * 0.05f));
            }
            Reverb(d, 0.3f, 0.75f);
            Normalize(d, 0.98f);
            return Clip("screams", d);
        }

        /// <summary>A low, wet, rasping growl/roar (the Witness when it runs at you). Loops.</summary>
        public static AudioClip Growl(float seconds)
        {
            int n = N(seconds);
            var d = new float[n];
            float f0 = Rf(55f, 85f), ph = 0f;
            var f1 = Biquad.Band(Rf(350f, 500f), 2.5f);
            var f2 = Biquad.Band(Rf(900f, 1300f), 3f);
            var nz = Biquad.Band(1800f, 1f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                // Breath in, roar out: a slow swell, rough pitch.
                float sw = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * t / seconds * Mathf.Round(Rf(2f, 3f)));
                float pitch = f0 * (1f + 0.08f * Mathf.Sin(2f * Mathf.PI * 23f * t) + 0.04f * Noise());
                ph = (ph + pitch / SR) % 1f;
                float x = ph * 2f - 1f;
                d[i] = (float)Math.Tanh((f1.Run(x) * 1.2f + f2.Run(x) * 0.6f + nz.Run(Noise()) * 0.5f) * 3f * sw);
            }
            int fade = N(0.2f);
            for (int i = 0; i < fade; i++)
            {
                float a = (float)i / fade;
                d[i] = d[i] * a + d[n - fade + i] * (1f - a);
            }
            Normalize(d, 0.9f);
            return Clip("growl", d);
        }

        /// <summary>
        /// The jumpscare scream, built to hit as hard as possible: a short reversed swell that sucks the air in, a
        /// sub-bass impact with a crack, then three voices at once (a deep roar, a human scream, a shrieking squeal)
        /// diving in pitch, ring-modulated into something not human, distorted and bit-crushed. The swell ends at
        /// lead seconds, which is when the face should land.
        /// </summary>
        public static AudioClip ScreamHit(float lead = 0.13f)
        {
            float body = Rf(1.3f, 1.7f);
            int s0 = N(lead), n = s0 + N(body);
            var d = new float[n + N(0.6f)];
            // The swell: noise and a rising whine, growing fast into the hit.
            var sw = Biquad.Band(2000f, 0.7f);
            for (int i = 0; i < s0; i++)
            {
                float k = (float)i / s0;
                d[i] = (sw.Run(Noise()) * 0.8f + 0.4f * Mathf.Sin(2f * Mathf.PI * (300f + 2500f * k * k) * i / SR)) * k * k * k;
            }
            // Three voices.
            float[] f0 = { Rf(90f, 130f), Rf(380f, 600f), Rf(1100f, 1700f) };
            float[] gain = { 0.9f, 1f, 0.55f };
            var ph = new float[3];
            var form = new[] { Biquad.Band(Rf(500f, 700f), 2f), Biquad.Band(Rf(900f, 1400f), 3f), Biquad.Band(Rf(2500f, 3500f), 3f) };
            var crack = Biquad.Band(3500f, 0.6f);
            float ring = Rf(35f, 60f), hold = 0f;
            int crush = 0;
            for (int i = 0; i < N(body); i++)
            {
                float t = (float)i / SR, u = t / body;
                float v = 0f;
                for (int k = 0; k < 3; k++)
                {
                    // Up a little at the start (the scream catches), then a long dive.
                    float pitch = f0[k] * (1f + 0.25f * Mathf.Exp(-t * 18f) - 0.35f * u * u) * (1f + 0.06f * Mathf.Sin(2f * Mathf.PI * (6f + k * 3f) * t));
                    ph[k] = (ph[k] + pitch / SR) % 1f;
                    float saw = ph[k] * 2f - 1f + Noise() * 0.3f;
                    v += (form[k].Run(saw) * 1.5f + saw * 0.3f) * gain[k];
                }
                v *= 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * ring * t); // ring modulation: the inhuman throb
                // Impact: a sub drop and a crack.
                v += Mathf.Sin(2f * Mathf.PI * (55f - 25f * Mathf.Min(1f, t * 2f)) * t) * Mathf.Exp(-t * 4f) * 2.5f;
                v += crack.Run(Noise()) * Mathf.Exp(-t * 40f) * 3f;
                v = (float)Math.Tanh(v * 2.2f);
                // Bit-crush: hold samples and drop resolution, a broken, digital edge.
                if (crush++ % 3 == 0) hold = Mathf.Round(v * 24f) / 24f;
                v = Mathf.Lerp(v, hold, 0.45f);
                float env = Mathf.Min(1f, t / 0.01f) * Mathf.Clamp01((body - t) / 0.35f);
                d[s0 + i] = v * env;
            }
            Reverb(d, 0.18f, 0.7f);
            Normalize(d, 0.99f);
            return Clip("screamhit", d);
        }

        /// <summary>"Shhh", close to your ear.</summary>
        public static AudioClip Shh()
        {
            var d = new float[N(Rf(0.7f, 1.1f)) + N(0.4f)];
            var bp = Biquad.Band(Rf(3500f, 5000f), 1.2f);
            int n = d.Length - N(0.4f);
            for (int i = 0; i < n; i++)
            {
                float u = (float)i / n;
                d[i] = bp.Run(Noise()) * Mathf.Sqrt(Mathf.Sin(Mathf.PI * u)) * (0.8f + 0.2f * Mathf.Sin(u * 40f));
            }
            Reverb(d, 0.2f, 0.6f);
            Normalize(d, 0.8f);
            return Clip("shh", d);
        }

        /// <summary>One typewriter key: a dry click.</summary>
        public static AudioClip Tick()
        {
            var d = new float[N(0.05f)];
            var bp = Biquad.Band(Rf(2500f, 3500f), 2f);
            for (int i = 0; i < d.Length; i++)
            {
                float t = (float)i / SR;
                d[i] = bp.Run(i < 60 ? Noise() * 3f : 0f) + Mathf.Sin(2f * Mathf.PI * 180f * t) * Mathf.Exp(-t * 120f) * 0.5f;
            }
            Normalize(d, 0.8f);
            return Clip("tick", d);
        }

        /// <summary>A deep hit that falls in pitch: the "something happened" sting.</summary>
        public static AudioClip Boom()
        {
            int n = N(2.5f);
            var d = new float[n + N(1.5f)];
            float f = Rf(70f, 95f), ph = 0f;
            var lp = Biquad.Low(300f);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / SR;
                ph += (f * Mathf.Exp(-t * 0.8f) + 25f) / SR;
                d[i] = Mathf.Sin(2f * Mathf.PI * ph) * Mathf.Exp(-t * 1.6f) + lp.Run(Noise()) * Mathf.Exp(-t * 6f) * 2f;
            }
            Reverb(d, 0.3f, 0.75f);
            Normalize(d, 0.95f);
            return Clip("boom", d);
        }
    }
}
