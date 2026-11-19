using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TheLastWitness
{
    public class HorrorEvent
    {
        public string Id, Name;
        public float MinInsanity, Weight, Cooldown;
        public bool UsesEntity, Moves, Major;
        public Func<IEnumerator> Run;
        public int Active;
        public float LastRun = -9999f;
    }

    public enum Phase { Calm, Tension, Peak, Release }

    /// <summary>
    /// Sanity and the director. Horror needs rhythm, not a constant stream: the night runs in cycles (acts) of
    /// calm (almost nothing: dread), tension (signs: whispers, glimpses, steps, writing), one peak (a single big
    /// set piece) and release (quiet, a little sanity back). Each act is shorter and heavier and unlocks worse
    /// things. It never repeats a recent set piece, sometimes builds up for nothing, and sometimes hits without
    /// warning. Which event, where, how long and how it sounds are all rolled on the spot.
    /// </summary>
    public class Director : MonoBehaviour
    {
        public static readonly System.Random Rng = new System.Random(Guid.NewGuid().GetHashCode());
        public static Director I;
        public static float Sanity = 100f;
        public static float Insanity => Mathf.Clamp01(1f - Sanity / 100f);
        public static bool Paused;
        public static Phase Phase = Phase.Calm;
        public static int Act = 1;
        /// <summary>0..1, how tense this moment is (the soundtrack and heartbeat follow it).</summary>
        public static float Tension;
        /// <summary>The tapes mode drives the acts itself (each tape), the endless mode advances one per cycle.</summary>
        public static bool ActsByStory;
        /// <summary>The story holds the night still (a tape being played, the finale): nothing new starts.</summary>
        public static bool Hold;

        public readonly List<HorrorEvent> Events = new List<HorrorEvent>();
        private readonly List<string> recentMajors = new List<string>();
        private float phaseEnd, nextMinor;
        private HorrorEvent peak;
        private bool wasActive;

        /// <summary>The layers: always-on effects with a switch in the events page.</summary>
        public static readonly (string id, string name)[] Layers =
        {
            ("trilha", "Trilha sonora"), ("escuro", "Ambiente escuro"), ("coracao", "Batimentos"), ("postes", "Postes e luzes"), ("lanterna", "Lanterna"), ("corrompido", "Mundo corrompido"), ("ceu", "Céu sinistro"), ("ouvir", "Ele ouve sua voz"),
        };

        public static void Hurt(float amount) => Sanity = Mathf.Clamp(Sanity - amount, Floor, 100f);

        /// <summary>Pesadelo sem fim on Terror absoluto: only the set pieces, no rest, and sanity can fall to -1.</summary>
        public static bool Heavy => Settings.Mode.Value == GameMode.Pesadelo && Settings.Diff.Value == Difficulty.TerrorAbsoluto;
        private static float Floor => Heavy ? -1f : 0f;
        /// <summary>Sanity -1 (Heavy only): every event, one after another every 5-7 s, all on top of each other, for good.</summary>
        public static bool Inferno => Heavy && Sanity <= -0.999f;
        public static event Action OnInferno;
        private bool infernoStarted;
        private float nextInferno;
        private readonly List<HorrorEvent> infernoQueue = new List<HorrorEvent>();

        /// <summary>How far into the night: the worse of sanity and the act (gates the nastier events).</summary>
        public static float Progress => Mathf.Max(Insanity, Mathf.Clamp01((Act - 1) / 5f));

        private float introUntil;

        public void StartIntro(IEnumerator intro)
        {
            introUntil = Time.time + 10f;
            Run(intro, "intro");
        }

        public void ResetSanity()
        {
            StopAll();
            Sanity = 100f;
            Act = 1;
            infernoStarted = false;
            recentMajors.Clear();
            Enter(Phase.Calm, 20f);
        }

        public void Add(string id, string name, float minInsanity, float weight, float cooldown, Func<IEnumerator> run, bool entity = false, bool moves = false, bool major = false)
        {
            Events.Add(new HorrorEvent { Id = id, Name = name, MinInsanity = minInsanity, Weight = weight, Cooldown = cooldown, Run = run, UsesEntity = entity, Moves = moves, Major = major });
            Settings.IsOn(id); // creates its config entry
        }

        // ---------- the rhythm ----------

        /// <summary>Shorter phases on harder settings and in later acts.</summary>
        private static float Scale => Settings.Diff.Value switch { Difficulty.Facil => 1.4f, Difficulty.Media => 1f, Difficulty.Dificil => 0.75f, _ => 0.55f }
            * Mathf.Lerp(1f, 0.55f, Mathf.Clamp01((Act - 1) / 5f));

        private void Enter(Phase p, float seconds)
        {
            Phase = p;
            phaseEnd = Time.time + seconds;
            nextMinor = Time.time + Rf(4f, 9f);
            Plugin.Log.LogInfo($"TheLastWitness: act {Act} {p} for {seconds:0} s (sanity {Sanity:0})");
        }

        public void Tick(bool active)
        {
            if (!active)
            {
                if (wasActive) StopAll();
                wasActive = false;
                return;
            }
            if (!wasActive) Enter(Phase.Calm, Mathf.Max(Rf(10f, 18f), introUntil - Time.time + Rf(8f, 14f)));
            wasActive = true;
            if (Paused || Hold) return;
            if (Inferno) { InfernoTick(); return; }
            float drain = 100f / (Settings.DrainMinutes * 60f);
            // Sanity comes back a little in the quiet, never above what the night has already taken.
            float cap = 100f - (Act - 1) * 12f;
            switch (Phase)
            {
                case Phase.Calm:
                    Tension = Mathf.MoveTowards(Tension, 0.1f, Time.deltaTime * 0.05f);
                    if (Sanity < cap && !Heavy) Sanity = Mathf.Min(cap, Sanity + 0.04f * Time.deltaTime);
                    if (Time.time < phaseEnd) break;
                    // Sometimes it comes with no warning at all.
                    if (Act > 1 && Rng.NextDouble() < 0.15) StartPeak();
                    else Enter(Phase.Tension, Rf(25f, 45f) * Scale);
                    break;
                case Phase.Tension:
                    Tension = Mathf.MoveTowards(Tension, 0.75f, Time.deltaTime * 0.03f);
                    Sanity = Mathf.Max(Floor, Sanity - drain * 1.5f * Time.deltaTime);
                    if (Time.time >= nextMinor)
                    {
                        nextMinor = Time.time + Rf(6f, 12f) * Mathf.Lerp(1f, 0.6f, Mathf.Clamp01((Act - 1) / 5f));
                        var e = Pick(false);
                        if (e != null) Begin(e);
                    }
                    if (Time.time < phaseEnd) break;
                    // Now and then it builds up for nothing: the release comes and the peak doesn't.
                    if (Rng.NextDouble() < 0.18) Enter(Phase.Release, Rf(10f, 18f));
                    else StartPeak();
                    break;
                case Phase.Peak:
                    Tension = Mathf.MoveTowards(Tension, 1f, Time.deltaTime * 0.3f);
                    Sanity = Mathf.Max(Floor, Sanity - drain * Time.deltaTime);
                    if ((peak == null || peak.Active == 0) && Time.time > phaseEnd - 55f || Time.time >= phaseEnd) Enter(Phase.Release, Rf(15f, 28f) * Mathf.Max(0.7f, Scale));
                    break;
                case Phase.Release:
                    Tension = Mathf.MoveTowards(Tension, 0f, Time.deltaTime * 0.15f);
                    if (Sanity < cap && !Heavy) Sanity = Mathf.Min(cap, Sanity + 0.12f * Time.deltaTime);
                    if (Time.time < phaseEnd) break;
                    if (!ActsByStory) Act = Mathf.Min(Act + 1, 7);
                    Enter(Phase.Calm, Heavy ? Rf(8f, 15f) : Rf(35f, 70f) * Scale);
                    break;
            }
        }

        private void StartPeak()
        {
            peak = Pick(true);
            Enter(Phase.Peak, 60f);
            if (peak != null)
            {
                Begin(peak);
                recentMajors.Add(peak.Id);
                if (recentMajors.Count > 4) recentMajors.RemoveAt(0);
            }
        }

        private void InfernoTick()
        {
            Tension = 1f;
            Phase = Phase.Peak;
            if (!infernoStarted)
            {
                infernoStarted = true;
                nextInferno = Time.time + 3f;
                Plugin.Log.LogInfo("TheLastWitness: sanity -1: inferno");
                OnInferno?.Invoke();
            }
            if (Time.time < nextInferno || Running() >= 8) return;
            nextInferno = Time.time + Rf(5f, 7f);
            // Every event once, in a new random order each round.
            if (infernoQueue.Count == 0)
            {
                foreach (var e in Events) if (Settings.IsOn(e.Id) && !(e.Moves && Settings.NoMovement.Value)) infernoQueue.Add(e);
                for (int i = infernoQueue.Count - 1; i > 0; i--) { int j = Rng.Next(i + 1); (infernoQueue[i], infernoQueue[j]) = (infernoQueue[j], infernoQueue[i]); }
            }
            for (int i = 0; i < infernoQueue.Count; i++)
            {
                var e = infernoQueue[i];
                if (e.Active > 0) continue;
                infernoQueue.RemoveAt(i);
                Begin(e);
                return;
            }
        }

        /// <summary>Lets the story (a tape picked up) start a peak now.</summary>
        public void ForcePeak() => StartPeak();

        private int Running()
        {
            int n = 0;
            foreach (var e in Events) n += e.Active;
            return n;
        }

        public bool EntityBusyNow => EntityBusy();

        private bool EntityBusy()
        {
            foreach (var e in Events) if (e.UsesEntity && e.Active > 0) return true;
            return false;
        }

        /// <summary>
        /// Weighted random among the events allowed now: signs (major false) or set pieces (major true). Set pieces
        /// seen recently are left out, and the ones not seen for longest weigh more.
        /// </summary>
        private HorrorEvent Pick(bool major)
        {
            float p = Progress;
            if (Heavy) major = true; // only the heaviest, from the start
            if (Running() >= (Heavy ? 3 : 2) && (!major || Heavy)) return null;
            var pool = new List<(HorrorEvent e, float w)>();
            float total = 0f;
            foreach (var e in Events)
            {
                if (e.Major != major || !Settings.IsOn(e.Id) || (!Heavy && p < e.MinInsanity) || e.Active > 0) continue;
                if (Time.time - e.LastRun < e.Cooldown) continue;
                if (major && recentMajors.Contains(e.Id)) continue;
                if (e.UsesEntity && EntityBusy()) continue;
                if (e.Moves && Settings.NoMovement.Value) continue;
                float unseen = Mathf.Clamp((Time.time - e.LastRun) / 240f, 0.5f, 3f);
                float w = e.Weight * Mathf.Lerp(0.7f, 1.4f, Mathf.InverseLerp(e.MinInsanity, 1f, p)) * (major ? unseen : 1f);
                pool.Add((e, w));
                total += w;
            }
            double roll = Rng.NextDouble() * total;
            foreach (var (e, w) in pool)
            {
                roll -= w;
                if (roll <= 0) return e;
            }
            return null;
        }

        /// <summary>Every event that starts (the haunting rides along with each one).</summary>
        public event Action<HorrorEvent> Began;

        public void Begin(HorrorEvent e)
        {
            e.LastRun = Time.time;
            Plugin.Log.LogInfo($"TheLastWitness: event {e.Id} (act {Act}, {Phase}, sanity {Sanity:0})");
            e.Active++;
            Run(e.Run(), e.Id, () => e.Active = Mathf.Max(0, e.Active - 1));
            Began?.Invoke(e);
        }

        public bool Begin(string id)
        {
            var e = Events.Find(x => x.Id == id);
            if (e == null) return false;
            Begin(e);
            return true;
        }

        /// <summary>Runs a coroutine (and every coroutine it yields) inside one guard.</summary>
        public Coroutine Run(IEnumerator it, string name, Action done = null) => StartCoroutine(Guarded(it, name, done));

        /// <summary>
        /// Steps nested coroutines ourselves: Unity runs a yielded IEnumerator as a separate coroutine, so a throw in
        /// it skipped our catch, left the event "running" for good (the entity slot was never freed: the big set
        /// pieces stopped coming) and could leave the sound muted. Now any throw ends the event and lets go of
        /// the screen and sound effects.
        /// </summary>
        private IEnumerator Guarded(IEnumerator root, string name, Action done)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"TheLastWitness: {name} failed: {ex}");
                    Fx.ReleaseScreenAndSound();
                    break;
                }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator sub) { stack.Push(sub); continue; }
                yield return top.Current;
            }
            done?.Invoke();
        }

        public void StopAll()
        {
            StopAllCoroutines();
            foreach (var e in Events) e.Active = 0;
            Fx.RestoreAll();
            Decay.HealAll();
            Ambience.Lightning = Ambience.Alarm = Ambience.Blood = 0f;
            Tension = 0f;
            Hold = false;
        }

        public static float Rf(float a, float b) => a + (float)Rng.NextDouble() * (b - a);
        public static T Pick<T>(IList<T> list) => list[Rng.Next(list.Count)];
    }
}
