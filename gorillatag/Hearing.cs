using System;
using UnityEngine;

namespace TheLastWitness
{
    /// <summary>
    /// "Ele ouve sua voz": the Witness hears you through your microphone. Nothing new opens the mic: Gorilla Tag's
    /// own GorillaSpeakerLoudness on your gorilla already measures it (offline it records the mic itself, in a room
    /// it reads the voice chat) for the mouth animation; we read its 0..1 loudness. The room's background noise is
    /// learned and ignored. A sustained sound or a shout fires Heard(loudness); the events decide what comes.
    /// </summary>
    public static class Hearing
    {
        private static GorillaSpeakerLoudness src;
        private static float floor = 0.05f, burst, burstTime, cooldownUntil, fakeLevel, fakeUntil;

        /// <summary>How loud you are right now above the background, 0..1.</summary>
        public static float Level { get; private set; }
        /// <summary>How much you have been heard lately (decays), 0..1: the wrist meter, chase speed.</summary>
        public static float Noise { get; private set; }
        public static bool Available => src != null && src.IsMicEnabled;
        public static event Action<float> Heard;

        public static void Tick(bool active)
        {
            float dt = Time.deltaTime;
            if (src == null) src = GorillaTagger.Instance?.offlineVRRig?.GetComponent<GorillaSpeakerLoudness>();
            bool fake = Time.time < fakeUntil;
            if (!active || !Settings.IsOn("ouvir") || (!fake && !Available))
            {
                Level = 0f;
                Noise = Mathf.MoveTowards(Noise, 0f, dt);
                return;
            }
            float raw = fake ? fakeLevel : src.LoudnessNormalized * Settings.MicSensitivity.Value;
            // The background (fan, PC, wind): follow it down fast, up very slowly, so a voice never becomes "background".
            floor = Mathf.Lerp(floor, raw, dt * (raw < floor ? 2f : 0.02f));
            Level = fake ? fakeLevel : Mathf.Clamp01((raw - floor - 0.05f) / 0.5f);
            Noise = Mathf.Max(Noise - dt * 0.25f, Level);
            if (Director.Hold || Time.time < cooldownUntil) { burst = burstTime = 0f; return; }
            if (Level > 0.15f)
            {
                burst = Mathf.Max(burst, Level);
                burstTime += dt;
                // Talking for a moment, or one shout, is enough.
                if (burstTime > 0.6f || Level > 0.7f && burstTime > 0.12f) Fire();
            }
            else
            {
                if (burstTime > 0.15f) Fire(); // a short sound still counts, softly
                burst = burstTime = 0f;
            }
        }

        private static void Fire()
        {
            float loud = burst;
            burst = burstTime = 0f;
            // Later in the night it listens more closely.
            cooldownUntil = Time.time + Mathf.Lerp(25f, 8f, Director.Progress);
            Plugin.Log.LogInfo($"TheLastWitness: it heard you (loudness {loud:0.00})");
            Heard?.Invoke(loud);
        }

        /// <summary>Test hook: pretend the player made this much noise for a while (no microphone needed).</summary>
        public static void Fake(float level, float seconds)
        {
            fakeLevel = level;
            fakeUntil = Time.time + seconds;
            cooldownUntil = 0f;
        }

        public static string State() => $"mic {(src == null ? "none" : $"enabled {src.IsMicEnabled} speaking {src.IsSpeaking} loudness {src.LoudnessNormalized:0.000}")} floor {floor:0.000} level {Level:0.00} noise {Noise:0.00}";
    }
}
