using System.Collections;
using System.Collections.Generic;
using GorillaLocomotion;
using TMPro;
using UnityEngine;
using static TheLastWitness.Director;

namespace TheLastWitness
{
    /// <summary>
    /// The events. Each one rolls its own details (where, how many, how long, which sound) every time it runs,
    /// and registers an undo for everything it changes so pausing or leaving puts the world back.
    /// </summary>
    public partial class Events
    {
        private readonly Director d;
        private static Transform Head => Fx.Head;
        private static float Dt => Time.deltaTime;
        private static Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up).normalized;

        // Where you were a moment ago (for the rubber-band glitch).
        private readonly Queue<(float t, Vector3 p, Quaternion r)> trail = new Queue<(float, Vector3, Quaternion)>();
        // Objects moved by "objetos": they stay moved until the horror stops or sanity is reset.
        private readonly Queue<int> moved = new Queue<int>();

        public Events(Director d)
        {
            this.d = d;
            Director.OnInferno += () => d.Run(Inferno(), "inferno");
            Hearing.Heard += OnHeard;
            // Signs: short, during the tension before a peak. (id, name, from progress, weight, cooldown s)
            d.Add("sussurros", "Sussurros", 0f, 2f, 25f, Whispers);
            d.Add("passos", "Passos atrás de você", 0.1f, 2f, 40f, Footsteps);
            d.Add("sons", "Sons distantes", 0f, 1.5f, 20f, Distant);
            d.Add("glitch", "Bugs propositais", 0.1f, 1.5f, 30f, Glitch);
            d.Add("objetos", "Objetos mudando de lugar", 0.05f, 1.5f, 30f, MoveObjects);
            d.Add("mensagens", "Mensagens", 0.15f, 1.5f, 45f, Messages);
            d.Add("olhos", "Olhos no escuro", 0.2f, 1.5f, 45f, Eyes);
            d.Add("espreita", "Espreitando", 0.1f, 2f, 40f, Peek);
            d.Add("anomalia", "Anomalias", 0.2f, 1.2f, 50f, Anomaly);
            d.Add("tempo", "Dia e noite trocados", 0.25f, 0.6f, 120f, TimeFlip);
            d.Add("gravidade", "Gravidade falha", 0.3f, 0.6f, 90f, Gravity, moves: true);
            // Set pieces: one per peak, never one of the last four again.
            d.Add("stalker", "Stalker", 0f, 2f, 60f, Stalker, entity: true, major: true);
            d.Add("relampago", "Relâmpagos", 0f, 1.5f, 90f, Lightning, entity: true, major: true);
            d.Add("jumpscare", "Jumpscares", 0.2f, 1.5f, 90f, Jumpscare, entity: true, major: true);
            d.Add("perseguicao", "Perseguição", 0.2f, 1.5f, 120f, ChaseEvent, entity: true, major: true);
            d.Add("apagao", "Apagão", 0.2f, 1.2f, 120f, Blackout, entity: true, major: true);
            d.Add("doppel", "Doppelgänger", 0.3f, 1f, 150f, Doppelganger, entity: true, major: true);
            d.Add("luzes", "Luzes morrendo", 0.3f, 1f, 150f, LightsDie, entity: true, major: true);
            d.Add("corrupcao", "Cenário corrompido", 0.3f, 1.2f, 120f, Corruption, major: true);
            d.Add("alarme", "Alarme vermelho", 0.4f, 0.8f, 180f, Alarm, major: true);
            d.Add("queda", "Algo caiu", 0.4f, 1f, 150f, Fall, entity: true, major: true);
            d.Add("contagem", "Contagem regressiva", 0.4f, 1f, 300f, Countdown, entity: true, major: true);
            d.Add("desconexao", "Falsa desconexão", 0.5f, 0.7f, 240f, FakeDisconnect, major: true);
            d.Add("multidao", "Multidão", 0.6f, 1f, 240f, Crowd, entity: true, major: true);
            d.Add("hora", "Hora vermelha", 0.6f, 1f, 240f, RedHour, entity: true, major: true);
            d.Add("colapso", "Colapso", 0.6f, 0.8f, 240f, Collapse, major: true);
            d.Add("mimico", "Mímico", 0.2f, 1.2f, 180f, Mimic, entity: true, major: true);
            d.Add("desorientacao", "Desorientação", 0.4f, 0.8f, 200f, Lost, moves: true, major: true);
            d.Add("gigante", "O Gigante", 0.6f, 1f, 240f, Giant, major: true);
        }

        public void Tick()
        {
            var p = Fx.Player;
            if (p == null) return;
            trail.Enqueue((Time.time, p.transform.position, p.transform.rotation));
            while (trail.Count > 0 && Time.time - trail.Peek().t > 1.5f) trail.Dequeue();
        }

        private static int Own(GameObject go) => Fx.Track(() => { if (go != null) Object.Destroy(go); });

        // ---------- sound ----------

        /// <summary>Whispers at your ear, and something standing just at the edge of your sight while they last.</summary>
        private IEnumerator Whispers()
        {
            d.Run(Glimpse(75f, 110f, Rf(2.5f, 4.5f), 4f), "glimpse");
            int count = 1 + Rng.Next(1 + (int)(Insanity * 3f));
            for (int i = 0; i < count; i++)
            {
                var h = Head;
                float side = Rng.NextDouble() < 0.5 ? -1f : 1f;
                var behind = Entity.Clip("whisper_behind");
                if (behind != null && Rng.NextDouble() < 0.35)
                {
                    Fx.Play(behind, h.position - h.forward * 0.35f, 0.9f, false, 1f, h);
                    Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.25f);
                    Hurt(1f);
                    yield return new WaitForSeconds(behind.length + Rf(0.5f, 1.5f));
                    continue;
                }
                var clip = Synth.Whisper(Rf(1.2f, 3.5f));
                Fx.Play(clip, h.position + h.right * side * Rf(0.25f, 0.6f) - h.forward * Rf(0f, 0.3f), Rf(0.5f, 0.9f), false, 1f, h);
                Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.1f);
                Hurt(0.5f);
                yield return new WaitForSeconds(clip.length * Rf(0.6f, 1.2f));
            }
        }

        /// <summary>
        /// Steps coming up behind you. Turn around and the Witness is standing right there; late in the game it
        /// doesn't vanish, it runs at you.
        /// </summary>
        private IEnumerator Footsteps()
        {
            var h = Head;
            Vector3 start = h.position - Flat(h.forward) * Rf(7f, 11f);
            Fx.Ground(start, out start);
            var clip = Synth.Footsteps(Rng.Next(6, 13), Rf(0.38f, 0.6f));
            var src = Fx.Play(clip, start, 0.95f, false);
            bool caught = false;
            for (float t = 0f; src != null && t < clip.length; t += Dt)
            {
                h = Head;
                Vector3 behind = h.position - Flat(h.forward) * 1.3f;
                src.transform.position = Vector3.Lerp(start, behind, t / clip.length);
                if (t > 0.8f && Fx.Seen(src.transform.position, 55f))
                {
                    src.Stop();
                    caught = true;
                    break;
                }
                yield return null;
            }
            Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.3f);
            Hurt(1.5f);
            if (caught && src != null && Fx.Ground(src.transform.position, out var at))
            {
                // You turned around: it is there.
                float height = Entity.PlayerHeight() * Rf(1.2f, 1.45f);
                var w = Entity.Spawn(height);
                int u = Own(w);
                w.transform.position = at;
                Entity.Face(w.transform, Head.position);
                w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
                Fx.Play(Synth.Breath(1.5f), at + Vector3.up * height * 0.85f, 0.9f, false);
                yield return new WaitForSeconds(Rf(0.5f, 1f));
                if (Insanity > 0.45f && Rng.NextDouble() < 0.55)
                {
                    // Back off first, then it comes.
                    Fx.Ground(Head.position + Flat(at - Head.position) * 9f, out var back);
                    w.transform.position = back;
                    yield return Chase(w, height, Rf(8f, 12f), Mathf.Lerp(5.5f, 7.5f, Insanity));
                    if (caughtYou) { yield return Scare(w, u, height); yield break; }
                }
                Fx.Static(0.4f, w.transform.position, 0.7f, false);
                Fx.Restore(u);
            }
            else if (Insanity > 0.4f)
            {
                yield return new WaitForSeconds(Rf(1f, 2.5f));
                Fx.Play(Synth.Breath(Rf(1.5f, 2.5f)), Head.position - Head.forward * 0.3f, 0.8f, false, 1f, Head);
            }
        }

        private IEnumerator Distant()
        {
            var h = Head;
            Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
            switch (Rng.Next(4))
            {
                case 0:
                    // A scream, and something sprints across in front of you between the trees.
                    Fx.Play(Synth.DistantScream(), h.position + dir * Rf(15f, 30f), 1f, false);
                    if (!Settings.IsOn("perseguicao") || Insanity < 0.2f) break;
                    yield return Runner();
                    break;
                case 1: Fx.Play(Synth.Knocks(), h.position + dir * Rf(5f, 12f), 0.9f, false); break;
                case 2: Fx.Play(Synth.MusicBox(), h.position + dir * Rf(6f, 14f), 0.7f, false); break;
                default: Fx.Play(Synth.Footsteps(Rng.Next(4, 9), Rf(0.25f, 0.4f)), h.position + dir * Rf(10f, 20f), 1f, false); break;
            }
            Hurt(1f);
            yield return new WaitForSeconds(3f);
        }

        // ---------- the Witness ----------

        /// <summary>
        /// It stands far off where you aren't looking, comes closer each time you look away, and vanishes when you
        /// look at it. Late in the game, looking at it can make it run at you.
        /// </summary>
        private IEnumerator Stalker()
        {
            float ins = Insanity, height = Entity.PlayerHeight() * Rf(1.15f, 1.5f);
            if (!Fx.SpotOutOfView(Mathf.Lerp(22f, 9f, ins), Mathf.Lerp(32f, 15f, ins), out var spot)) yield break;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = spot;
            float life = Rf(20f, 45f), seen = 0f, unseen = 0f, patience = Rf(5f, 11f) * (1f - ins * 0.6f), react = Rf(0.3f, 1.2f);
            for (float t = 0f; t < life; t += Dt)
            {
                var h = Head;
                Entity.Face(w.transform, h.position);
                Ambience.SetThreat(w.transform.position);
                if (Rng.NextDouble() < 0.02) w.transform.rotation *= Quaternion.Euler(0f, Rf(-12f, 12f), Rf(-6f, 6f)); // twitch
                float dist = Vector3.ProjectOnPlane(w.transform.position - h.position, Vector3.up).magnitude;
                Fx.Fear = Mathf.Max(Fx.Fear, Mathf.InverseLerp(20f, 3f, dist));
                if (Fx.Seen(w.transform.position + Vector3.up * height * 0.8f, 30f))
                {
                    unseen = 0f;
                    seen += Dt;
                    if (seen < react) { yield return null; continue; }
                    if (Insanity > 0.5f && Rng.NextDouble() < 0.5)
                    {
                        // You saw it, and it saw you: it runs at you.
                        Fx.Play(Synth.Screech(0.6f), w.transform.position + Vector3.up * height, 0.9f, true);
                        yield return Chase(w, height, Rf(10f, 16f), Mathf.Lerp(5.5f, 7.5f, Insanity));
                        if (caughtYou) yield return Scare(w, u, height);
                        else Fx.Restore(u);
                        yield break;
                    }
                    Fx.Static(Rf(0.3f, 0.7f), w.transform.position, 0.6f, false);
                    Hurt(3f);
                    break;
                }
                seen = 0f;
                unseen += Dt * (Hearing.Level > 0.2f ? 8f : 1f); // it heard you: it moves now
                if (unseen > patience)
                {
                    unseen = 0f;
                    float closer = Mathf.Max(4f, dist * Rf(0.5f, 0.75f));
                    if (Fx.SpotOutOfView(closer * 0.9f, closer * 1.1f, out var next))
                    {
                        w.transform.position = next;
                        if (Rng.NextDouble() < 0.5) Fx.Play(Synth.Footsteps(Rng.Next(2, 5), 0.3f), next, 0.8f, false);
                    }
                }
                if (dist < 6f && Rng.NextDouble() < Dt * 0.3) Fx.Play(Synth.Breath(Rf(1.5f, 2.5f)), w.transform.position + Vector3.up * height * 0.85f, 0.9f, false);
                yield return null;
            }
            Fx.Restore(u);
        }

        // ---------- jumpscares ----------

        private static void Buzz(float amplitude, float seconds)
        {
            var g = GorillaTagger.Instance;
            if (g == null) return;
            g.StartVibration(true, amplitude, seconds);
            g.StartVibration(false, amplitude, seconds);
        }

        /// <summary>Where the Witness's root goes so its face is dist metres in front of your eyes (or hanging upside down).</summary>
        private static void InYourFace(Transform w, float height, float dist, bool upsideDown, Vector3 shake, Quaternion twist)
        {
            var h = Head;
            float s = Fx.Player != null ? Fx.Player.scale : 1f;
            Vector3 face = h.position + h.forward * dist * s;
            if (upsideDown) w.SetPositionAndRotation(face + h.up * height * 0.9f + shake, Quaternion.LookRotation(-h.forward, -h.up) * twist);
            else w.SetPositionAndRotation(face - h.up * height * 0.9f + shake, Quaternion.LookRotation(-h.forward, h.up) * twist);
        }

        /// <summary>
        /// "Jumpscares": one of five ways it gets you, each with a moment of dread first, all ending in Scare.
        /// Investida (it stares from far, then rushes you), Atrás de você (breathing at your neck; turn around),
        /// De cima (knocks overhead, it drops upside down into your face), Falha (its face cut into the picture for
        /// a frame at a time, faster, then for real), Escuridão (lights die, silence, a red light: already there).
        /// </summary>
        private IEnumerator Jumpscare()
        {
            float height = Entity.PlayerHeight() * 1.15f;
            switch (Rng.Next(5))
            {
                case 0: yield return ScareRush(height); break;
                case 1: yield return ScareBehind(height); break;
                case 2: yield return ScareAbove(height); break;
                case 3: yield return ScareCuts(height); break;
                default: yield return ScareDark(height); break;
            }
        }

        /// <summary>Test hook: one jumpscare style (0 rush, 1 behind, 2 above, 3 cuts, 4 dark), with a given form.</summary>
        public IEnumerator TestScare(int style)
        {
            float height = Entity.PlayerHeight() * 1.15f;
            var all = new System.Func<float, IEnumerator>[] { ScareRush, ScareBehind, ScareAbove, ScareCuts, ScareDark };
            yield return all[Mathf.Clamp(style, 0, all.Length - 1)](height);
        }

        private IEnumerator ScareRush(float height)
        {
            var h = Head;
            if (!Fx.Ground(h.position + Flat(h.forward) * Rf(12f, 18f), out var g)) g = h.position + Flat(h.forward) * 12f - Vector3.up * height;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = g;
            Entity.Face(w.transform, h.position);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
            // Everything goes quiet while it stares.
            int um = Fx.Track(() => Fx.Muted = false);
            Fx.Muted = true;
            Fx.Fear = 0.7f;
            for (float t = 0f, stare = Rf(1.2f, 2.2f); t < stare; t += Dt) { Entity.Face(w.transform, Head.position); yield return null; }
            Fx.Restore(um);
            yield return Scare(w, u, height, 0.35f);
        }

        private IEnumerator ScareBehind(float height)
        {
            float yaw0 = Head.eulerAngles.y;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
            var breath = Loop(w.transform, Synth.Breath(2.2f), 1f);
            var whisper = Entity.Clip("whisper_behind");
            if (whisper != null) Fx.Play(whisper, Head.position - Head.forward * 0.3f, 1f, false, 1f, Head);
            int ub = Fx.Track(() => Fx.Blackout = 0f);
            bool turned = false;
            for (float t = 0f; t < 6f && !turned; t += Dt)
            {
                var h = Head;
                // Always right at the back of your neck, until you turn.
                Fx.Ground(h.position - Flat(h.forward) * 0.8f, out var g);
                w.transform.position = g;
                Entity.Face(w.transform, h.position);
                Fx.Fear = Mathf.Min(1f, t / 3f);
                Fx.Blackout = Mathf.Min(0.45f, t * 0.12f);
                turned = Mathf.Abs(Mathf.DeltaAngle(h.eulerAngles.y, yaw0)) > 100f || Hearing.Level > 0.3f;
                if (t > 0.5f && t < 0.6f) Buzz(0.15f, 0.1f); // a touch on the hands
                yield return null;
            }
            Object.Destroy(breath);
            Fx.Restore(ub);
            // Turned: it is there. Didn't: it rises up in front of you instead.
            if (!turned) w.transform.position = Head.position + Flat(Head.forward) * 0.8f - Vector3.up * (height + 1.5f);
            yield return Scare(w, u, height, turned ? 0.06f : 0.12f);
        }

        private IEnumerator ScareAbove(float height)
        {
            var h = Head;
            for (int i = 0; i < 3; i++)
            {
                Fx.Play(Synth.Knocks(), Head.position + Vector3.up * Rf(2f, 4f), 0.9f, false);
                yield return new WaitForSeconds(Rf(0.6f, 1.2f));
            }
            int um = Fx.Track(() => Fx.Muted = false);
            Fx.Muted = true;
            yield return new WaitForSeconds(Rf(0.8f, 1.6f));
            Fx.Restore(um);
            var w = Entity.Spawn(height);
            int u = Own(w);
            h = Head;
            w.transform.SetPositionAndRotation(h.position + Vector3.up * (height + 2.5f), Quaternion.LookRotation(-Flat(h.forward), Vector3.down));
            yield return Scare(w, u, height, 0.22f, true);
        }

        private IEnumerator ScareCuts(float height)
        {
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
            w.SetActive(false);
            int cuts = Rng.Next(5, 9);
            var heart = Synth.Heartbeat();
            for (int i = 0; i < cuts; i++)
            {
                // A frame or two of its face, somewhere just in front of you.
                var h = Head;
                Vector3 off = Quaternion.AngleAxis(Rf(-35f, 35f), h.up) * Quaternion.AngleAxis(Rf(-20f, 20f), h.right) * h.forward;
                w.SetActive(true);
                w.transform.SetPositionAndRotation(h.position + off * Rf(0.6f, 1.2f) - h.up * height * 0.9f, Quaternion.LookRotation(-off, h.up));
                Fx.Glitch = 1f;
                Fx.Static(0.08f, Head.position, 0.7f, false, 0f);
                yield return null;
                yield return null;
                w.SetActive(false);
                Fx.Glitch = 0f;
                Fx.Play(heart, Head.position, 0.9f, false, 0f);
                Fx.Fear = (float)(i + 1) / cuts;
                yield return new WaitForSeconds(Mathf.Lerp(0.9f, 0.15f, (float)i / cuts));
            }
            w.SetActive(true);
            yield return Scare(w, u, height, 0f);
        }

        private IEnumerator ScareDark(float height)
        {
            var lamps = Ambience.Ready ? Ambience.LampsFarFirst(60f) : new List<Ambience.Lamp>();
            int ul = Fx.Track(() => { foreach (var l in lamps) l.Dead = false; });
            for (int i = lamps.Count - 1; i >= 0; i--)
            {
                if (lamps[i].Go == null) continue;
                lamps[i].Dead = true;
                Fx.Play(Synth.Pop(), lamps[i].Go.transform.position, 0.8f, false);
                yield return new WaitForSeconds(0.08f);
            }
            int ub = Fx.Track(() => { Fx.Blackout = 0f; Fx.Muted = false; });
            Fx.Blackout = 1f;
            yield return new WaitForSeconds(0.3f);
            Fx.Muted = true;
            yield return new WaitForSeconds(Rf(1.8f, 2.8f));
            Fx.Restore(ub);
            var w = Entity.Spawn(height);
            int u = Own(w);
            yield return Scare(w, u, height, 0f);
            Fx.Restore(ul);
        }

        /// <summary>
        /// The jumpscare itself. It lunges from wherever it is into your face (lunge seconds), stops a hand's
        /// breadth from your eyes with its arms out, and shakes and swells there while a scream hits, the world turns
        /// blood red, the picture strobes and tears, faces flicker all around you, a word flashes, and both
        /// controllers buzz. Then a hard cut: black and total silence. Two red eyes stay where its face was.
        /// </summary>
        private IEnumerator Scare(GameObject w, int u, float height, float lunge = 0.13f, bool upsideDown = false)
        {
            if (w == null) yield break;
            var anim = w.GetComponent<WitnessAnim>();
            if (anim != null) anim.Current = WitnessAnim.Mode.Reach;
            int ue = Fx.Track(() =>
            {
                Fx.Duck = 0f; Fx.Glitch = 0f; Fx.Muted = false; Fx.Blackout = 0f; Ambience.Blood = 0f; Fx.RedTint = 0f; Sky.Glitch = 0f;
            });
            Fx.Muted = false;
            Fx.Blackout = 0f;
            Fx.Duck = 1f;
            Fx.Play(Synth.ScreamHit(Mathf.Max(0.05f, lunge)), Head.position, 1f, true, 0f, Head);
            // Its light-eating aura would black out its own face this close: off, and a light of its own on the face.
            foreach (var gl in w.GetComponentsInChildren<GameLight>(true)) if (gl.negativeLight) gl.gameObject.SetActive(false);
            int uf = 0;
            if (Lights.On)
            {
                var faceLight = Lights.Add(Head.position, new Color(1f, 0.6f, 0.55f), 2.5f, 2.5f, false, Head);
                faceLight.isHighPriorityPlayerLight = true;
                uf = Own(faceLight.gameObject);
            }

            // The lunge.
            Vector3 from = w.transform.position;
            Quaternion fromRot = w.transform.rotation;
            for (float t = 0f; t < lunge; t += Dt)
            {
                float k = Mathf.Pow(t / lunge, 2.2f); // slow, then all at once
                InYourFace(w.transform, height, 0.33f, upsideDown, Vector3.zero, Quaternion.identity);
                w.transform.SetPositionAndRotation(Vector3.Lerp(from, w.transform.position, k), Quaternion.Slerp(fromRot, w.transform.rotation, k));
                yield return null;
            }

            // The hit: one of the recorded jumpscares lands with the face, on top of the synthesized scream.
            var hit = Rng.NextDouble() < 0.5 ? Entity.Clip("jumpscare_intense") : Entity.Clip("jumpscare_one");
            if (hit != null) Fx.Play(hit, Head.position, 1f, true, 0f, Head);
            Buzz(1f, 1.3f);
            var swarm = new List<GameObject>();
            for (int i = 0; i < 4; i++)
            {
                var f = Entity.Spawn(height * Rf(0.8f, 1.1f));
                f.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Reach;
                f.SetActive(false);
                swarm.Add(f);
            }
            int us = Fx.Track(() => { foreach (var f in swarm) if (f != null) Object.Destroy(f); });
            string word = Fx.Safe(Pick(new[] { "TE PEGUEI", "ACHEI VOCÊ", "OLHE PARA MIM", "É TARDE", "FIQUE" }));
            var flashText = Screen(0.14f, new Color(1f, 0.05f, 0.03f));
            int ut = Own(flashText.gameObject);
            Color[] strobe = { new Color(0.9f, 0f, 0f), Color.black, Color.white, Color.black };
            bool soft = Settings.SoftFlashes.Value;
            if (soft) Fx.Flash(new Color(0.8f, 0f, 0f), 0.5f);
            float dur = Rf(1.1f, 1.45f);
            int lastStrobe = -1, shown = -1;
            for (float t = 0f; t < dur && w != null; t += Dt)
            {
                // Locked in your face, shaking, swelling.
                Vector3 shake = Random.insideUnitSphere * 0.03f * (1f - t / dur * 0.5f);
                Quaternion twist = Quaternion.Euler(Rf(-9f, 9f), Rf(-9f, 9f), Rf(-14f, 14f));
                InYourFace(w.transform, height, 0.33f - 0.06f * Mathf.Abs(Mathf.Sin(t * 20f)), upsideDown, shake, twist);
                w.transform.localScale = Vector3.one * (1f + 0.12f * Mathf.Abs(Mathf.Sin(t * 45f)));
                // The world bleeds, the picture breaks.
                if (Ambience.Ready) Ambience.Blood = 1f;
                else Fx.RedTint = 1f;
                Fx.Glitch = Rng.NextDouble() < 0.5 ? 1f : 0.4f;
                Sky.Glitch = 1f;
                Fx.Fear = 1f;
                int si = (int)(t * 14f);
                if (!soft && t < 0.55f && si != lastStrobe) { lastStrobe = si; Fx.Flash(strobe[si % strobe.Length], 0.07f); }
                // Faces flicker around you, one at a time.
                int fi = (int)(t * 12f) % (swarm.Count * 2);
                if (fi != shown)
                {
                    shown = fi;
                    for (int i = 0; i < swarm.Count; i++) swarm[i].SetActive(i == fi && t > 0.2f);
                    if (fi < swarm.Count && t > 0.2f)
                    {
                        var h = Head;
                        Vector3 dir = Quaternion.AngleAxis(Rf(35f, 70f) * (fi % 2 == 0 ? 1f : -1f), h.up) * Quaternion.AngleAxis(Rf(-20f, 25f), h.right) * h.forward;
                        swarm[fi].transform.SetPositionAndRotation(h.position + dir * Rf(0.7f, 1.1f) - h.up * height * 0.85f, Quaternion.LookRotation(-dir, h.up));
                    }
                }
                // A word, for a few frames, twice.
                bool word1 = (t > 0.35f && t < 0.42f) || (t > 0.85f && t < 0.92f);
                flashText.text = word1 ? word : "";
                Follow(flashText, 0f);
                yield return null;
            }
            Fx.Restore(ut);
            Fx.Restore(us);

            // The cut: gone, black, silent.
            Fx.Restore(u);
            if (uf != 0) Fx.Restore(uf);
            Ambience.Blood = 0f;
            Fx.RedTint = 0f;
            Fx.Glitch = 0f;
            Sky.Glitch = 0f;
            Fx.Blackout = 1f;
            Fx.Muted = true;
            Buzz(0.4f, 0.15f);
            yield return new WaitForSeconds(Rf(0.9f, 1.4f));
            Fx.Muted = false;

            // Two red eyes stay where its face was, fading as the light comes back.
            var eyes = new GameObject("TLW afterimage");
            int ua = Own(eyes);
            var red = Fx.Flat(new Color(1f, 0.05f, 0.02f), Fx.GlowQueue + 1);
            foreach (float side in new[] { -1f, 1f })
            {
                var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(e.GetComponent<Collider>());
                e.GetComponent<MeshRenderer>().sharedMaterial = red;
                e.transform.SetParent(eyes.transform, false);
                e.transform.localPosition = new Vector3(side * 0.045f, 0f, 0f);
                e.transform.localScale = Vector3.one * 0.022f;
            }
            Fx.Play(Synth.Whisper(Rf(1f, 1.6f)), Head.position + Head.right * 0.3f, 0.8f, false, 1f, Head);
            for (float t = 0f; t < 1.4f; t += Dt)
            {
                var h = Head;
                eyes.transform.SetPositionAndRotation(h.position + h.forward * 0.6f, Quaternion.LookRotation(h.forward, h.up));
                red.color = new Color(1f, 0.05f, 0.02f, 1f - t / 1.4f);
                Fx.Blackout = 1f - t / 1.4f;
                yield return null;
            }
            Fx.Restore(ua);
            Fx.Restore(ue);
            Fx.Fear = 1f;
            Hurt(10f);
        }

        /// <summary>Your own gorilla, standing in the dark, copying every move you make.</summary>
        private IEnumerator Doppelganger()
        {
            if (!Fx.SpotOutOfView(6f, 12f, out var spot)) yield break;
            var dop = Entity.Doppel(out var repose);
            int u = Own(dop);
            dop.transform.position = spot;
            float seen = 0f, unseen = 0f, nextPose = 0f;
            for (float t = 0f; t < 30f; t += Dt)
            {
                var h = Head;
                Entity.Face(dop.transform, h.position);
                if (Time.time > nextPose) { repose(); nextPose = Time.time + 0.1f; }
                if (Fx.Seen(dop.transform.position + Vector3.up * 0.6f, 30f))
                {
                    unseen = 0f;
                    seen += Dt;
                    Fx.Fear = Mathf.Max(Fx.Fear, 0.5f);
                    if (seen > Rf(2f, 4f))
                    {
                        Fx.Play(Synth.Boom(), dop.transform.position, 0.9f, true);
                        Fx.Glitch = 0.6f;
                        yield return new WaitForSeconds(0.15f);
                        Fx.Glitch = 0f;
                        Hurt(6f);
                        break;
                    }
                }
                else
                {
                    seen = 0f;
                    unseen += Dt;
                    if (unseen > 6f && Fx.SpotOutOfView(3f, 5f, out var near))
                    {
                        dop.transform.position = near;
                        unseen = 0f;
                    }
                }
                yield return null;
            }
            Fx.Restore(u);
        }

        private IEnumerator Blackout()
        {
            Fx.Fear = 1f;
            Fx.Play(Synth.Boom(), Head.position, 0.6f, false, 0f);
            for (float t = 0f; t < 0.3f; t += Dt) { Fx.Blackout = t / 0.3f; yield return null; }
            Fx.Blackout = 1f;
            float hold = Rf(3f, 8f);
            Fx.Play(Synth.Ringing(hold), Head.position, 0.5f, false, 0f, Head);
            yield return new WaitForSeconds(hold);
            GameObject w = null;
            int u = 0;
            if (Insanity > 0.5f && Rng.NextDouble() < 0.6)
            {
                float height = Entity.PlayerHeight() * 1.3f;
                w = Entity.Spawn(height);
                u = Own(w);
                var h = Head;
                Fx.Ground(h.position + Flat(h.forward) * Rf(2f, 3f), out var g);
                w.transform.position = g;
                Entity.Face(w.transform, h.position);
            }
            for (float t = 0f; t < 0.5f; t += Dt) { Fx.Blackout = 1f - t / 0.5f; yield return null; }
            Fx.Blackout = 0f;
            if (w != null)
            {
                float stare = Rf(0.8f, 1.6f);
                for (float t = 0f; t < stare; t += Dt) yield return null;
                Fx.Static(0.4f, w.transform.position, 0.8f, true);
                Fx.Restore(u);
                Hurt(5f);
            }
            Hurt(3f);
        }

        // ---------- the world ----------

        private static List<Renderer> Nearby(float maxDist, bool movable) => Decay.Nearby(Head.position, maxDist, movable);
        private static void Shuffle<T>(List<T> l) => Decay.Shuffle(l);

        private static bool loggedCandidates;

        /// <summary>Things move while you're not looking. They stay where they went.</summary>
        private IEnumerator MoveObjects()
        {
            var list = Nearby(20f, true);
            if (!loggedCandidates) { Plugin.Log.LogInfo($"TheLastWitness: {list.Count} movable objects near you"); loggedCandidates = true; }
            Shuffle(list);
            int want = 1 + Rng.Next(1 + (int)(Insanity * 4f)), done = 0;
            foreach (var r in list)
            {
                if (done >= want) break;
                var t = r.transform;
                if (Fx.Seen(r.bounds.center, 50f)) continue;
                Vector3 p = t.position;
                Quaternion q = t.rotation;
                Vector3 np = p + Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward * Rf(0.5f, 2.5f);
                // Never onto the player (a moved leaf once sat over the camera).
                var nb = r.bounds;
                nb.center += np - p;
                if (Fx.Seen(np, 50f) || nb.SqrDistance(Head.position) < 2.5f * 2.5f || r.bounds.SqrDistance(Head.position) < 1f) continue;
                t.position = np;
                t.rotation = (Insanity > 0.6f && Rng.NextDouble() < 0.3 ? Quaternion.Euler(180f, 0f, 0f) : Quaternion.identity) * Quaternion.Euler(0f, Rf(-180f, 180f), 0f) * q;
                moved.Enqueue(Fx.Track(() => { if (t != null) t.SetPositionAndRotation(p, q); }));
                while (moved.Count > 20) Fx.Restore(moved.Dequeue());
                done++;
                Hurt(0.5f);
            }
            yield break;
        }

        /// <summary>Corrupts these renderers until the returned undo is restored; the loose ones also shake and stretch.</summary>
        private static int Rot(List<Renderer> list, List<(Transform t, Vector3 p, Vector3 s)> shakers)
        {
            foreach (var r in list)
            {
                Decay.Taint(r, Decay.Any);
                if (!r.isPartOfStaticBatch && r.bounds.extents.magnitude < 2f && Rng.NextDouble() < 0.5)
                {
                    var t = r.transform;
                    var s = t.localScale;
                    if (Rng.NextDouble() < 0.4) { var ns = s; ns[Rng.Next(3)] *= Rf(0.3f, 3f); t.localScale = ns; }
                    shakers.Add((t, t.position, s));
                }
            }
            return Fx.Track(() =>
            {
                foreach (var r in list) Decay.Heal(r);
                foreach (var (t, p, s) in shakers) if (t != null) { t.position = p; t.localScale = s; }
            });
        }

        private static void Shake(List<(Transform t, Vector3 p, Vector3 s)> shakers, float amount)
        {
            foreach (var (t, p, s) in shakers)
                if (t != null) t.position = Rng.NextDouble() < 0.15 ? p + Random.insideUnitSphere * amount : p;
        }

        /// <summary>
        /// "Cenário corrompido": everything around you breaks at once: dozens to hundreds of things turn into missing
        /// textures, void, flesh, static and torn scanlines that keep flickering; loose objects shake and stretch;
        /// holes of corruption hang in the air; errors float around; the sky tears.
        /// </summary>
        private IEnumerator Corruption()
        {
            var list = Nearby(25f, false);
            Shuffle(list);
            int n = Mathf.Min(list.Count, Rng.Next(30, 60 + (int)(Insanity * 140f)));
            var hit = list.GetRange(0, n);
            var shakers = new List<(Transform t, Vector3 p, Vector3 s)>();
            int u = Rot(hit, shakers);
            var extra = new List<int>();
            for (int i = 0, nv = 10 + Rng.Next(16); i < nv; i++)
                if (Decay.SpotNear(hit, out var p)) extra.Add(Own(Decay.Void(p, Rf(0.4f, 2f))));
            for (int i = 0, ne = 3 + Rng.Next(5); i < ne; i++)
            {
                Vector3 p = Head.position + Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward * Rf(3f, 9f) + Vector3.up * Rf(-0.5f, 2f);
                var t = Fx.Text(Pick(Errors), p, Quaternion.LookRotation(p - Head.position, Vector3.up), Rf(0.08f, 0.2f), new Color(1f, Rf(0f, 0.3f), Rf(0f, 0.6f)));
                extra.Add(Own(t.gameObject));
            }
            int us = Fx.Track(() => Sky.Glitch = 0f);
            var hum = Loop(Head, Synth.Static(2f), 0.35f);
            extra.Add(Own(hum));
            Fx.Play(Synth.Boom(), Head.position, 0.8f, true, 0f);
            Hurt(4f);
            float dur = Rf(5f, 8f + Insanity * 12f);
            for (float t = 0f; t < dur; t += Dt)
            {
                for (int i = 0; i < 6; i++) Decay.Swap(hit[Rng.Next(hit.Count)], Decay.Any);
                Shake(shakers, 0.25f);
                Sky.Glitch = 0.15f + 0.5f * Mathf.PerlinNoise(t * 3f, 1f);
                Fx.Glitch = Rng.NextDouble() < 0.04 ? 0.6f : 0f;
                yield return null;
            }
            Fx.Glitch = 0f;
            foreach (int id in extra) Fx.Restore(id);
            Fx.Restore(us);
            Fx.Restore(u);
        }

        /// <summary>
        /// "Colapso": a wall of corruption comes at you from the distance, eating everything it passes and leaving
        /// holes in the air. When it reaches you the screen tears apart ("ERRO FATAL"); then it all snaps back at once.
        /// </summary>
        private IEnumerator Collapse()
        {
            var h = Head;
            Vector3 origin = h.position + Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward * 35f;
            var list = Decay.Nearby(h.position, 40f, false);
            list.Sort((a, b) => (a.bounds.center - origin).sqrMagnitude.CompareTo((b.bounds.center - origin).sqrMagnitude));
            var eaten = new List<Renderer>();
            var shakers = new List<(Transform t, Vector3 p, Vector3 s)>();
            int u = Fx.Track(() => { foreach (var r in eaten) Decay.Heal(r); foreach (var (t, p, s) in shakers) if (t != null) { t.position = p; t.localScale = s; } });
            var extra = new List<int>();
            var roar = new GameObject("TLW collapse");
            roar.transform.position = origin;
            extra.Add(Own(roar));
            var a1 = Loop(roar.transform, Synth.Static(2f), 1f);
            var a2 = Loop(roar.transform, Synth.Growl(3f), 0.8f);
            int us = Fx.Track(() => { Sky.Glitch = 0f; Fx.Glitch = 0f; });
            Fx.Play(Synth.Thunder(), origin, 1f, true, 0.5f);
            int next = 0;
            float radius = 0f;
            float reach = Vector3.Distance(origin, h.position);
            while (radius < reach + 6f)
            {
                radius += 7f * Dt;
                // The front: everything inside the radius is eaten, voids spawn along it.
                while (next < list.Count && (list[next].bounds.ClosestPoint(origin) - origin).magnitude < radius)
                {
                    var r = list[next++];
                    Decay.Taint(r, Decay.Any);
                    eaten.Add(r);
                    if (!r.isPartOfStaticBatch && r.bounds.extents.magnitude < 2f) shakers.Add((r.transform, r.transform.position, r.transform.localScale));
                    if (Rng.NextDouble() < 0.15) extra.Add(Own(Decay.Void(r.bounds.center + Random.insideUnitSphere * 1.5f, Rf(0.5f, 2f))));
                }
                Vector3 toYou = Vector3.ProjectOnPlane(Head.position - origin, Vector3.up).normalized;
                roar.transform.position = origin + toYou * radius;
                for (int i = 0; i < 4 && eaten.Count > 0; i++) Decay.Swap(eaten[Rng.Next(eaten.Count)], Decay.Any);
                Shake(shakers, 0.4f);
                float close = Mathf.InverseLerp(reach - 15f, reach, radius);
                Fx.Fear = Mathf.Max(Fx.Fear, close);
                Sky.Glitch = 0.2f + close * 0.6f;
                Fx.Glitch = Rng.NextDouble() < close * 0.2 ? 0.8f : 0f;
                yield return null;
            }
            // It reached you.
            var err = Screen(0.12f, new Color(1f, 0.05f, 0.03f));
            extra.Add(Own(err.gameObject));
            err.text = Fx.Safe("ERRO FATAL");
            for (float t = 0f; t < 3f; t += Dt)
            {
                Follow(err, 0f);
                Fx.Glitch = Rng.NextDouble() < 0.5 ? 1f : 0.4f;
                Fx.Blackout = Rng.NextDouble() < 0.1 ? 1f : 0f;
                Sky.Glitch = 1f;
                Shake(shakers, 0.6f);
                yield return null;
            }
            // Snap back, all at once, into silence.
            foreach (int id in extra) Fx.Restore(id);
            Fx.Restore(u);
            Fx.Restore(us);
            int um = Fx.Track(() => { Fx.Muted = false; Fx.Blackout = 0f; });
            Fx.Blackout = 1f;
            Fx.Muted = true;
            yield return new WaitForSeconds(1.2f);
            Fx.Muted = false;
            for (float t = 0f; t < 1f; t += Dt) { Fx.Blackout = 1f - t; yield return null; }
            Fx.Restore(um);
            Hurt(8f);
        }

        private static readonly string[] Errors =
        {
            "NullReferenceException: Witness.Watch()", "ERRO: jogador 2 não existe", "Object reference not set to an instance of a monke",
            "IndexOutOfRangeException: alma[0]", "[Photon] Jogador desconhecido entrou na sala", "Assertion failed: você.está_sozinho == true",
            "MissingReferenceException: o mundo foi destruído", "StackOverflow em Pesadelo.Loop()",
        };

        /// <summary>The game "breaks": torn frames, static, error messages, sometimes it drags you back a second.</summary>
        private IEnumerator Glitch()
        {
            float dur = Rf(0.4f, 1.5f + Insanity * 2f);
            var h = Head;
            Fx.Static(dur, h.position, 0.5f, false, 0f, h);
            TextMeshPro text = null;
            int u = 0;
            if (Rng.NextDouble() < 0.6)
            {
                text = Fx.Text(Pick(Errors), h.position + h.forward * 1.2f + h.up * Rf(-0.2f, 0.2f), Quaternion.LookRotation(h.forward, Vector3.up), 0.05f, new Color(1f, 0.25f, 0.2f));
                u = Own(text.gameObject);
            }
            if (!Settings.NoMovement.Value && Insanity > 0.3f && Rng.NextDouble() < 0.4 && trail.Count > 0 && Fx.Player != null)
            {
                var (_, p, r) = trail.Peek();
                Fx.Player.TeleportTo(p, r, false, false);
            }
            for (float t = 0f; t < dur; t += 0.05f)
            {
                Fx.Glitch = Rng.NextDouble() < 0.5 ? 1f : 0.3f;
                yield return new WaitForSeconds(Rf(0.03f, 0.12f));
            }
            Fx.Glitch = 0f;
            Hurt(1.5f);
            if (text != null)
            {
                yield return new WaitForSeconds(Rf(1.5f, 4f));
                Fx.Restore(u);
            }
        }

        /// <summary>One of: things float up, the world turns red, or the Witness stands in plain sight, still.</summary>
        private IEnumerator Anomaly()
        {
            switch (Rng.Next(3))
            {
                case 0:
                {
                    var list = Nearby(15f, true);
                    Shuffle(list);
                    var lifted = new List<(Transform t, Vector3 p, Quaternion q, float h, float spin)>();
                    foreach (var r in list)
                    {
                        if (lifted.Count >= 3 + Rng.Next(6)) break;
                        var t = r.transform;
                        var (p, q) = (t.position, t.rotation);
                        lifted.Add((t, p, q, Rf(0.5f, 2f), Rf(-40f, 40f)));
                        Fx.Track(() => { if (t != null) t.SetPositionAndRotation(p, q); });
                    }
                    if (lifted.Count == 0) goto case 1;
                    Fx.Play(Synth.MusicBox(), Head.position, 0.5f, false, 0f);
                    float up = Rf(3f, 5f), hover = Rf(3f, 6f);
                    for (float t = 0f; t < up + hover; t += Dt)
                    {
                        float k = Mathf.SmoothStep(0f, 1f, t / up);
                        foreach (var l in lifted)
                            if (l.t != null) l.t.SetPositionAndRotation(l.p + Vector3.up * (l.h * k + Mathf.Sin(t * 2f + l.spin) * 0.05f), Quaternion.Euler(0f, l.spin * t, 0f) * l.q);
                        yield return null;
                    }
                    foreach (var l in lifted)
                        if (l.t != null) l.t.SetPositionAndRotation(l.p, l.q); // they drop back all at once
                    Fx.Play(Synth.Knocks(), Head.position, 0.7f, false, 0f);
                    Hurt(3f);
                    break;
                }
                case 1:
                {
                    Fx.Play(Synth.MusicBox(), Head.position, 0.6f, false, 0f);
                    for (float t = 0f; t < 2f; t += Dt) { Fx.RedTint = t / 2f; yield return null; }
                    Fx.RedTint = 1f;
                    yield return new WaitForSeconds(Rf(6f, 15f));
                    for (float t = 0f; t < 2f; t += Dt) { Fx.RedTint = 1f - t / 2f; yield return null; }
                    Fx.RedTint = 0f;
                    Hurt(2f);
                    break;
                }
                default:
                {
                    var h = Head;
                    float height = Entity.PlayerHeight() * Rf(1.3f, 1.8f);
                    if (!Fx.Ground(h.position + Flat(h.forward) * Rf(15f, 25f), out var g)) break;
                    var w = Entity.Spawn(height);
                    int u = Own(w);
                    w.transform.position = g;
                    Entity.Face(w.transform, h.position);
                    bool lookedAway = false;
                    for (float t = 0f; t < 25f; t += Dt)
                    {
                        bool seen = Fx.Seen(w.transform.position + Vector3.up * height * 0.7f, 40f);
                        if (!seen) lookedAway = true;
                        if (lookedAway && seen) break; // look away and back: gone
                        if (Vector3.Distance(w.transform.position, Head.position) < 8f) break;
                        yield return null;
                    }
                    Fx.Static(0.5f, w.transform.position, 0.7f, false);
                    Fx.Restore(u);
                    Hurt(4f);
                    break;
                }
            }
        }

        /// <summary>Night turns to day (wrong, too bright) or day to night, for a while.</summary>
        private IEnumerator TimeFlip()
        {
            bool toDay = Settings.IsOn("escuro") || Rng.NextDouble() < 0.3;
            Fx.Flash(Color.white, 0.4f);
            Fx.Play(Synth.Boom(), Head.position, 0.7f, false, 0f);
            if (toDay) Fx.ForceDay = true;
            else Fx.ForceNight = true;
            int u = Fx.Track(() => { Fx.ForceDay = Fx.ForceNight = false; });
            Hurt(2f);
            yield return new WaitForSeconds(Rf(8f, 25f));
            Fx.Flash(Color.black, 0.4f);
            Fx.Restore(u);
        }

        /// <summary>Writing on the walls and trees, somewhere you'll only notice when you turn around.</summary>
        private IEnumerator Messages()
        {
            string name = "";
            try { name = NetworkSystem.Instance != null ? NetworkSystem.Instance.GetMyNickName() : ""; } catch { }
            string[] lines =
            {
                "ELE ESTÁ AQUI", "NÃO OLHE PARA TRÁS", "VOCÊ É A ÚLTIMA TESTEMUNHA", "SAIA ENQUANTO PODE", "EU VEJO VOCÊ",
                "POR QUE VOCÊ VOLTOU?", "NINGUÉM MAIS ESTÁ AQUI", "CONTE ATÉ DEZ", "ELE NÃO DORME", "VOCÊ JÁ ESTEVE AQUI",
                string.IsNullOrEmpty(name) ? "EU SEI SEU NOME" : name.ToUpperInvariant() + ", OLHE PARA TRÁS",
                string.IsNullOrEmpty(name) ? "VOCÊ NÃO DEVIA TER VISTO" : "FIQUE, " + name.ToUpperInvariant(),
            };
            var h = Head;
            Vector3 pos = Vector3.zero, normal = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 20 && !found; i++)
            {
                Vector3 dir = Quaternion.Euler(Rf(-10f, 10f), Rf(60f, 300f), 0f) * Flat(h.forward);
                if (Physics.Raycast(h.position, dir, out var hit, 12f, Fx.World, QueryTriggerInteraction.Ignore) && hit.distance > 1.5f && Mathf.Abs(hit.normal.y) < 0.5f)
                {
                    pos = hit.point + hit.normal * 0.03f;
                    normal = hit.normal;
                    found = true;
                }
            }
            if (!found)
            {
                normal = Flat(h.forward);
                pos = h.position - normal * Rf(2f, 4f);
            }
            var text = Fx.Text(Pick(lines), pos, Quaternion.LookRotation(-normal, Vector3.up), Rf(0.12f, 0.25f), new Color(0.75f, 0.02f, 0.02f, 0.95f));
            text.fontMaterial.renderQueue = Fx.GlowQueue;
            Plugin.Log.LogInfo($"TheLastWitness: message \"{text.text}\" {(found ? "on a surface" : "in the air")} {Vector3.Distance(pos, h.position):0.0} m away");
            int u = Own(text.gameObject);
            float seen = 0f;
            bool noticed = false;
            for (float t = 0f; t < 90f; t += Dt)
            {
                if (Fx.Seen(pos, 30f))
                {
                    seen += Dt;
                    if (!noticed && seen > 0.3f)
                    {
                        noticed = true;
                        Fx.Play(Synth.Whisper(Rf(1f, 2f)), Head.position + Head.right * 0.4f, 0.6f, false, 1f, Head);
                        Hurt(2f);
                    }
                }
                else if (noticed && seen > 2f) break; // read it, looked away: gone
                yield return null;
            }
            Fx.Restore(u);
        }

        // ---------- chases and entities ----------

        private bool caughtYou;

        private static GameObject Loop(Transform at, AudioClip clip, float volume)
        {
            var go = new GameObject("TLW loop " + clip.name);
            go.transform.SetParent(at, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = true;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 1.5f;
            a.maxDistance = 35f;
            a.volume = Mathf.Clamp01(volume * Settings.Volume.Value * Settings.ScareVolume.Value);
            a.time = Rf(0f, clip.length * 0.9f);
            a.Play();
            return go;
        }

        /// <summary>
        /// The Witness runs at you, galloping and growling, for up to seconds. It follows you anywhere: it climbs
        /// to where you are, and if you get more than 35 m away it comes back out of nowhere behind you.
        /// caughtYou says whether it reached you.
        /// </summary>
        private IEnumerator Chase(GameObject w, float height, float seconds, float speed)
        {
            caughtYou = false;
            if (w == null) yield break;
            var anim = w.GetComponent<WitnessAnim>();
            if (anim != null) anim.Current = WitnessAnim.Mode.Run;
            var growl = Loop(w.transform, Synth.Growl(Rf(2.5f, 3.5f)), 1f);
            var steps = Loop(w.transform, Synth.Footsteps(16, 0.17f), 1f);
            float s = Fx.Player != null ? Fx.Player.scale : 1f;
            for (float t = 0f; t < seconds && w != null; t += Dt)
            {
                var h = Head;
                Vector3 pos = w.transform.position, target = h.position - Vector3.up * height * 0.85f, to = target - pos;
                float flat = Vector3.ProjectOnPlane(to, Vector3.up).magnitude;
                if (flat > 35f * s && Fx.SpotOutOfView(14f, 20f, out var spot))
                {
                    w.transform.position = spot;
                    yield return null;
                    continue;
                }
                float sp = speed * s * (1f + 0.25f * Insanity) * (1f + 0.3f * Hearing.Noise) * (0.9f + 0.2f * Mathf.PerlinNoise(t, 3f));
                Vector3 next = pos + to.normalized * Mathf.Min(sp * Dt, to.magnitude);
                // On the ground when there is ground right under it; otherwise it climbs straight at you.
                if (Fx.Ground(next, out var g) && Mathf.Abs(g.y - next.y) < 1.5f) next = g;
                w.transform.position = next;
                Entity.Face(w.transform, h.position);
                Ambience.SetThreat(next);
                Fx.Fear = Mathf.Max(Fx.Fear, Mathf.InverseLerp(18f, 2f, flat));
                if (Vector3.Distance(next + Vector3.up * height * 0.85f, h.position) < 1.2f * s)
                {
                    caughtYou = true;
                    break;
                }
                yield return null;
            }
            if (growl != null) Object.Destroy(growl);
            if (steps != null) Object.Destroy(steps);
            if (anim != null) anim.Current = WitnessAnim.Mode.Tilt;
        }

        /// <summary>Test hook: a Witness of the given form 6 m in front of you runs at you, slowly (to watch the gallop).</summary>
        public IEnumerator TestChase(bool? model)
        {
            float height = Entity.PlayerHeight() * 1.35f;
            if (!Fx.Ground(Head.position + Flat(Head.forward) * 6f, out var g)) yield break;
            var w = Entity.Spawn(height, model);
            int u = Own(w);
            w.transform.position = g;
            yield return Chase(w, height, 6f, 1.2f);
            if (caughtYou) yield return Scare(w, u, height);
            else Fx.Restore(u);
        }

        /// <summary>"Perseguição": a shriek behind you, "CORRA.", and it comes.</summary>
        private IEnumerator ChaseEvent()
        {
            float height = Entity.PlayerHeight() * Rf(1.2f, 1.5f);
            if (!Fx.SpotOutOfView(18f, 26f, out var spot, 100f)) yield break;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = spot;
            Entity.Face(w.transform, Head.position);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
            Fx.Play(Synth.Screech(0.7f), spot + Vector3.up * height, 1f, true);
            Fx.Play(Synth.Boom(), Head.position, 0.6f, false, 0f);
            yield return Flash(Pick(new[] { "CORRA.", "ELE TE VIU.", "NÃO PARE." }), 1.2f);
            yield return new WaitForSeconds(Rf(0.3f, 0.8f));
            yield return Chase(w, height, Rf(12f, 18f), Mathf.Lerp(5.5f, 7.5f, Insanity));
            if (caughtYou) { yield return Scare(w, u, height); yield break; }
            // You outran it. It stops, stares, and is gone.
            yield return new WaitForSeconds(Rf(1f, 2f));
            Fx.Static(0.5f, w.transform.position, 0.8f, false);
            Fx.Restore(u);
            Hurt(4f);
        }

        /// <summary>"Algo caiu": it drops out of the sky in front of you, gets up crooked, stares, then charges.</summary>
        private IEnumerator Fall()
        {
            var h = Head;
            float height = Entity.PlayerHeight() * Rf(1.25f, 1.5f);
            if (!Fx.Ground(h.position + Quaternion.Euler(0f, Rf(-25f, 25f), 0f) * Flat(h.forward) * Rf(5f, 8f), out var g)) yield break;
            var w = Entity.Spawn(height);
            int u = Own(w);
            var anim = w.GetComponent<WitnessAnim>();
            anim.Current = WitnessAnim.Mode.Reach;
            Entity.Face(w.transform, h.position);
            for (float t = 0f; t < 0.55f; t += Dt)
            {
                float k = t / 0.55f;
                w.transform.position = g + Vector3.up * 18f * (1f - k * k);
                yield return null;
            }
            w.transform.position = g;
            Fx.Play(Synth.Boom(), g, 1f, true);
            Fx.Play(Synth.Knocks(), g, 0.8f, false);
            Fx.Flash(new Color(0.6f, 0f, 0f), 0.15f);
            Fx.Fear = 1f;
            anim.Current = WitnessAnim.Mode.Tilt;
            var growl = Loop(w.transform, Synth.Growl(3f), 0.8f);
            yield return new WaitForSeconds(Rf(1f, 1.8f));
            Object.Destroy(growl);
            yield return Chase(w, height, Rf(8f, 12f), Mathf.Lerp(6f, 8f, Insanity));
            if (caughtYou) { yield return Scare(w, u, height); yield break; }
            Fx.Static(0.5f, w.transform.position, 0.8f, false);
            Fx.Restore(u);
            Hurt(4f);
        }

        /// <summary>
        /// "Multidão": a second of black, then they are everywhere, ten or more, all facing you, perfectly still.
        /// The one you look at twitches closer. Then all of them are gone at once.
        /// </summary>
        private IEnumerator Crowd()
        {
            int ub = Fx.Track(() => Fx.Blackout = 0f);
            Fx.Blackout = 1f;
            Fx.Play(Synth.Boom(), Head.position, 0.8f, true, 0f);
            yield return new WaitForSeconds(0.8f);
            var ws = new List<(GameObject w, int u, float h)>();
            int count = 10 + Rng.Next(7);
            for (int i = 0; i < count * 3 && ws.Count < count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
                if (!Fx.Ground(Head.position + dir * Rf(6f, 28f), out var g)) continue;
                float h = Entity.PlayerHeight() * Rf(1.1f, 1.6f);
                var w = Entity.Spawn(h);
                w.transform.position = g;
                Entity.Face(w.transform, Head.position);
                w.GetComponent<WitnessAnim>().Current = Rng.NextDouble() < 0.5 ? WitnessAnim.Mode.Tilt : WitnessAnim.Mode.Idle;
                ws.Add((w, Own(w), h));
            }
            for (float t = 0f; t < 0.3f; t += Dt) { Fx.Blackout = 1f - t / 0.3f; yield return null; }
            Fx.Restore(ub);
            float next = 0.6f;
            (GameObject w, int u, float h) caught = (null, 0, 0f);
            for (float t = 0f; t < Rf(8f, 12f) && caught.w == null; t += Dt)
            {
                Fx.Fear = 1f;
                if (t >= next)
                {
                    next = t + Rf(0.4f, 0.9f);
                    foreach (var c in ws)
                    {
                        if (c.w == null || !Fx.Seen(c.w.transform.position + Vector3.up * c.h * 0.8f, 20f)) continue;
                        // The one you are looking at jerks a step closer.
                        Vector3 off = Vector3.ProjectOnPlane(c.w.transform.position - Head.position, Vector3.up);
                        float nd = Mathf.Max(1f, off.magnitude - Rf(1f, 2.5f));
                        if (Fx.Ground(Head.position + off.normalized * nd, out var g)) c.w.transform.position = g;
                        Fx.Static(0.15f, c.w.transform.position, 0.6f, false);
                        if (nd <= 1.2f) caught = c;
                    }
                }
                foreach (var c in ws) if (c.w != null) Entity.Face(c.w.transform, Head.position);
                yield return null;
            }
            foreach (var c in ws) if (c.w != caught.w) Fx.Restore(c.u);
            if (caught.w != null) { yield return Scare(caught.w, caught.u, caught.h); yield break; }
            Fx.Play(Synth.ScreamChoir(1.2f), Head.position, 0.9f, true, 0f);
            Fx.Flash(new Color(0.5f, 0f, 0f), 0.2f);
            Hurt(6f);
        }

        /// <summary>"Espreitando": half a face looking at you from behind a tree or a wall; it pulls back when seen. Again. Closer.</summary>
        private IEnumerator Peek()
        {
            int times = 2 + Rng.Next(3);
            for (int n = 0; n < times; n++)
            {
                var h = Head;
                bool placed = false;
                Vector3 pos = Vector3.zero;
                for (int i = 0; i < 25 && !placed; i++)
                {
                    Vector3 dir = Quaternion.Euler(0f, Rf(-150f, 150f), 0f) * Flat(h.forward);
                    if (!Physics.Raycast(h.position, dir, out var hit, 10f - n * 2f, Fx.World, QueryTriggerInteraction.Ignore)) continue;
                    if (hit.distance < 2.5f || Mathf.Abs(hit.normal.y) > 0.4f) continue;
                    // Just behind the edge of what you hit, a little to the side: half hidden.
                    Vector3 side = Vector3.Cross(Vector3.up, dir) * (Rng.NextDouble() < 0.5 ? 0.45f : -0.45f);
                    if (!Fx.Ground(hit.point - hit.normal * 0.5f + side, out pos)) continue;
                    placed = true;
                }
                if (!placed) yield break;
                float height = Entity.PlayerHeight() * Rf(1.2f, 1.4f);
                var w = Entity.Spawn(height);
                int u = Own(w);
                w.transform.position = pos;
                Entity.Face(w.transform, h.position);
                w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
                float seen = 0f;
                for (float t = 0f; t < 8f && seen < 0.35f; t += Dt)
                {
                    Entity.Face(w.transform, Head.position);
                    if (Fx.Seen(w.transform.position + Vector3.up * height * 0.85f, 30f)) seen += Dt;
                    yield return null;
                }
                if (seen >= 0.35f)
                {
                    Fx.Play(Synth.Whisper(0.8f), w.transform.position + Vector3.up * height, 0.7f, false);
                    Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.3f);
                    Hurt(2f);
                }
                Fx.Restore(u);
                yield return new WaitForSeconds(Rf(1.5f, 4f));
            }
            // The last time it is not behind anything: right beside you.
            if (Insanity > 0.5f && Fx.Ground(Head.position + Head.right * (Rng.NextDouble() < 0.5 ? 1.2f : -1.2f), out var near))
            {
                float height = Entity.PlayerHeight() * 1.3f;
                var w = Entity.Spawn(height);
                int u = Own(w);
                w.transform.position = near;
                Entity.Face(w.transform, Head.position);
                for (float t = 0f; t < 6f; t += Dt)
                {
                    if (Fx.Seen(w.transform.position + Vector3.up * height * 0.85f, 35f)) { yield return Scare(w, u, height); yield break; }
                    yield return null;
                }
                Fx.Restore(u);
            }
        }

        /// <summary>Something standing at the edge of your sight; gone the moment you look.</summary>
        private IEnumerator Glimpse(float yawMin, float yawMax, float dist, float life)
        {
            var h = Head;
            Vector3 dir = Quaternion.Euler(0f, Rf(yawMin, yawMax) * (Rng.NextDouble() < 0.5 ? -1f : 1f), 0f) * Flat(h.forward);
            if (!Fx.Ground(h.position + dir * dist, out var g)) yield break;
            float height = Entity.PlayerHeight() * Rf(1.1f, 1.4f);
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = g;
            Entity.Face(w.transform, h.position);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
            float seen = 0f;
            for (float t = 0f; t < life && seen < 0.12f; t += Dt)
            {
                if (Fx.Seen(g + Vector3.up * height * 0.8f, 35f)) seen += Dt;
                yield return null;
            }
            if (seen > 0f) Fx.Static(0.25f, g, 0.6f, false);
            Fx.Restore(u);
        }

        /// <summary>It sprints across in front of you, between the trees, and is gone.</summary>
        private IEnumerator Runner()
        {
            var h = Head;
            Vector3 fwd = Flat(h.forward), right = Vector3.Cross(Vector3.up, fwd) * (Rng.NextDouble() < 0.5 ? 1f : -1f);
            float dist = Rf(12f, 20f);
            Vector3 a = h.position + fwd * dist - right * 14f, b = h.position + fwd * dist + right * 14f;
            if (!Fx.Ground(a, out a)) yield break;
            float height = Entity.PlayerHeight() * Rf(1.2f, 1.5f);
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Run;
            var steps = Loop(w.transform, Synth.Footsteps(12, 0.15f), 1f);
            float len = Vector3.Distance(a, b);
            for (float x = 0f; x < len; x += 10f * Dt)
            {
                Vector3 p = Vector3.Lerp(a, b, x / len);
                if (Fx.Ground(p, out var g)) p = g;
                w.transform.position = p;
                Entity.Face(w.transform, b);
                yield return null;
            }
            Object.Destroy(steps);
            Fx.Restore(u);
            Hurt(2f);
        }

        /// <summary>Big red words in front of your eyes for a moment.</summary>
        private IEnumerator Flash(string text, float seconds)
        {
            var t = Screen(0.12f, new Color(1f, 0.05f, 0.03f));
            int u = Own(t.gameObject);
            t.text = Fx.Safe(text);
            for (float k = 0f; k < seconds; k += Dt)
            {
                Follow(t, 0f);
                t.color = new Color(1f, 0.05f, 0.03f, Rng.NextDouble() < 0.08 ? 0.2f : 1f);
                yield return null;
            }
            Fx.Restore(u);
        }

        /// <summary>Sanity -1: one scream from everything at once, and the words.</summary>
        private IEnumerator Inferno()
        {
            Fx.Play(Synth.ScreamChoir(2.5f), Head.position, 1f, true, 0f, Head);
            Fx.Flash(new Color(0.9f, 0f, 0f), 0.6f);
            Buzz(1f, 1f);
            yield return Flash("SANIDADE: -1", 2f);
            yield return Flash("TODOS ELES. AGORA.", 1.8f);
        }

        // ---------- it hears you (Hearing) ----------

        /// <summary>A sound from you: a whisper back, steps coming to where you are, or (a shout) it comes running.</summary>
        private void OnHeard(float loud)
        {
            if (!Settings.Running || Fx.Head == null) return;
            if (loud > 0.65f && Settings.IsOn("perseguicao") && !d.EntityBusyNow) d.Run(HeardShout(), "heard shout");
            else if (loud > 0.3f && !d.EntityBusyNow) d.Run(HeardTalk(), "heard talk");
            else d.Run(Shh(), "shh");
        }

        private IEnumerator Shh()
        {
            Fx.Play(Synth.Shh(), Head.position + Head.right * (Rng.NextDouble() < 0.5 ? 0.3f : -0.3f), 0.9f, false, 1f, Head);
            Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.2f);
            yield break;
        }

        /// <summary>You talked: steps come from out there and stop a few metres away. It is standing there.</summary>
        private IEnumerator HeardTalk()
        {
            if (!Fx.SpotOutOfView(18f, 26f, out var far)) yield break;
            Vector3 dir = Vector3.ProjectOnPlane(far - Head.position, Vector3.up).normalized;
            if (!Fx.Ground(Head.position + dir * Rf(7f, 9f), out var near)) yield break;
            var src = Fx.Play(Synth.Footsteps(Rng.Next(6, 10), 0.32f), far, 1f, false);
            for (float t = 0f; src != null && t < 3f; t += Dt)
            {
                src.transform.position = Vector3.Lerp(far, near, t / 3f);
                Fx.Fear = Mathf.Max(Fx.Fear, t / 3f * 0.6f);
                Ambience.SetThreat(src.transform.position);
                yield return null;
            }
            float height = Entity.PlayerHeight() * Rf(1.2f, 1.4f);
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = near;
            Entity.Face(w.transform, Head.position);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
            float seen = 0f;
            for (float t = 0f; t < 6f && seen < 1.2f; t += Dt)
            {
                Entity.Face(w.transform, Head.position);
                Ambience.SetThreat(near);
                if (Fx.Seen(near + Vector3.up * height * 0.8f, 35f)) seen += Dt;
                // Say something else while it is there, and it is done waiting.
                if (Hearing.Level > 0.3f && t > 0.5f)
                {
                    yield return Chase(w, height, Rf(6f, 9f), Mathf.Lerp(6f, 8f, Insanity));
                    if (caughtYou) { yield return Scare(w, u, height); yield break; }
                    break;
                }
                yield return null;
            }
            Fx.Static(0.3f, w.transform.position, 0.6f, false);
            Fx.Restore(u);
            Hurt(2f);
        }

        /// <summary>You shouted: a shriek behind you answers, and it comes running from wherever it was.</summary>
        private IEnumerator HeardShout()
        {
            float height = Entity.PlayerHeight() * 1.35f;
            if (!Fx.SpotOutOfView(16f, 24f, out var spot, 100f)) yield break;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = spot;
            Entity.Face(w.transform, Head.position);
            Fx.Play(Synth.Screech(0.6f), spot + Vector3.up * height, 1f, true);
            yield return new WaitForSeconds(Rf(0.3f, 0.7f));
            yield return Chase(w, height, Rf(8f, 12f), Mathf.Lerp(6f, 8f, Insanity));
            if (caughtYou) { yield return Scare(w, u, height); yield break; }
            Fx.Static(0.4f, w.transform.position, 0.7f, false);
            Fx.Restore(u);
            Hurt(4f);
        }

        // ---------- the big ones ----------

        /// <summary>
        /// "Contagem regressiva": a song (witness.bundle, Assets/Sounds/countdown.wav) plays loud while a red counter
        /// falls from 00:30 two at a time, in time with it. The lamps die with the count and the Witness stands
        /// ahead of you, nearer at every step, wherever you look. At 00:00 (the song's drop) it is behind you and the
        /// counter goes negative and dissolves; turn around before the song ends and it is in your face. If you don't,
        /// silence, a whisper, and "00:30" for an instant: it will count again.
        /// </summary>
        private IEnumerator Countdown()
        {
            var song = Entity.Clip("countdown");
            if (song == null) yield break;
            const float drop = 15.9f, step = drop / 15f; // the song's drop lands on 00:00
            Fx.Duck = 1f;
            int ud = Fx.Track(() => { Fx.Duck = 0f; Fx.Glitch = 0f; Fx.Blackout = 0f; });
            var src = Fx.Play(song, Head.position, 1f, true, 0f, Head);
            var counter = Screen(0.16f, new Color(1f, 0.05f, 0.03f));
            var msg = Screen(0.045f, new Color(0.75f, 0.72f, 0.7f));
            int uc = Own(counter.gameObject), um = Own(msg.gameObject);
            string m1 = Fx.Safe(Pick(new[] { "QUANDO CHEGAR A ZERO, ELE VEM.", "VOCÊ TEM 30 SEGUNDOS.", "ESCONDA-SE.", "ELE ESTÁ CONTANDO." }));
            string m2 = Fx.Safe(Pick(new[] { "ELE ESTÁ MAIS PERTO.", "QUANDO ZERAR, CORRA.", "CORRA.", "TARDE DEMAIS." }));
            var lamps = Ambience.Ready ? Ambience.LampsFarFirst(45f) : new List<Ambience.Lamp>();
            int ul = Fx.Track(() => { foreach (var l in lamps) l.Dead = false; });
            int killed = 0, lastStep = -1, uw = 0;
            GameObject w = null;
            float height = Entity.PlayerHeight() * 1.35f, yaw = Rf(-35f, 35f);
            for (float t = 0f; t < drop; t += Dt)
            {
                int stepNo = Mathf.FloorToInt(t / step);
                counter.text = "00:" + Mathf.Max(0, 30 - 2 * stepNo).ToString("00");
                counter.color = new Color(1f, 0.05f, 0.03f, 0.75f + 0.25f * Mathf.Sin(t * 12f));
                msg.text = t < drop * 0.55f ? m1 : m2;
                Follow(counter, -0.05f);
                Follow(msg, 0.17f);
                Fx.Fear = t / drop;
                while (killed < lamps.Count && killed < lamps.Count * t / drop)
                {
                    var l = lamps[killed++];
                    if (l.Go == null) continue;
                    l.Dead = true;
                    Fx.Play(Synth.Pop(), l.Go.transform.position, 0.8f, false);
                }
                // From 00:24 it is out there ahead of you, and it only moves on a count, one snap nearer each time.
                if (t > 3f && stepNo != lastStep)
                {
                    lastStep = stepNo;
                    if (w == null)
                    {
                        w = Entity.Spawn(height);
                        uw = Own(w);
                    }
                    var h = Head;
                    float dist = Mathf.Lerp(18f, 2.2f, Mathf.InverseLerp(3f, drop, t));
                    if (Fx.Ground(h.position + Quaternion.Euler(0f, yaw, 0f) * Flat(h.forward) * dist, out var g)) w.transform.position = g;
                }
                if (w != null)
                {
                    Entity.Face(w.transform, Head.position);
                    Ambience.SetThreat(w.transform.position);
                }
                yield return null;
            }

            // The drop: it is gone from in front of you, shrieks somewhere behind, and comes for you wherever you are,
            // while the counter goes negative and dissolves.
            Fx.Flash(new Color(0.8f, 0f, 0f), 0.25f);
            bool chasing = w != null;
            if (chasing)
            {
                if (Fx.SpotOutOfView(12f, 18f, out var far, 100f)) w.transform.position = far;
                Fx.Play(Synth.Screech(0.7f), w.transform.position + Vector3.up * height, 1f, true);
                IEnumerator Hunt()
                {
                    yield return Chase(w, height, 10f, Mathf.Lerp(6.5f, 8f, Insanity));
                    chasing = false;
                }
                d.Run(Hunt(), "hunt");
            }
            float end = Mathf.Max(drop + 1f, song.length - 0.2f);
            string run = Fx.Safe("CORRA.");
            for (float t = drop; (chasing || t < end) && t < end + 12f; t += Dt)
            {
                float k = Mathf.InverseLerp(drop, end, t);
                counter.text = t < end ? "-" + (2 * (1 + Mathf.FloorToInt((t - drop) / step))).ToString("00") : "";
                counter.fontSize = Mathf.Lerp(0.16f, 0.03f, k) * 16f;
                msg.text = t - drop < 1.5f ? run : "";
                Fx.Glitch = t < end ? k * 0.6f : 0f;
                Follow(counter, -0.05f);
                Follow(msg, 0.17f);
                yield return null;
            }
            Fx.Glitch = 0f;
            Fx.Restore(uc);
            Fx.Restore(um);
            if (src != null) src.Stop();
            if (caughtYou && w != null)
            {
                yield return Scare(w, uw, height);
            }
            else
            {
                if (uw != 0) Fx.Restore(uw);
                Fx.Blackout = 1f;
                yield return new WaitForSeconds(1.5f);
                Fx.Play(Synth.Whisper(Rf(1.2f, 2f)), Head.position + Head.right * 0.4f, 0.8f, false, 1f, Head);
                var again = Screen(0.16f, new Color(1f, 0.05f, 0.03f));
                int ua = Own(again.gameObject);
                again.text = "00:30";
                for (float t = 0f; t < 0.35f; t += Dt) { Follow(again, -0.05f); yield return null; }
                Fx.Restore(ua);
                for (float t = 0f; t < 1.5f; t += Dt) { Fx.Blackout = 1f - t / 1.5f; yield return null; }
            }
            Fx.Restore(ul);
            Fx.Restore(ud);
            Hurt(10f);
        }

        /// <summary>
        /// "Hora vermelha": the world turns blood red (Ghost Reactor lighting), a choir of screams plays loud, and
        /// three to six Witnesses stand around you. They only move while you aren't looking at them. One reaching you
        /// is a jumpscare; otherwise everything cuts at once to black and silence.
        /// </summary>
        private IEnumerator RedHour()
        {
            float dur = Rf(9f, 13f);
            Fx.Duck = 1f;
            int u = Fx.Track(() => { Fx.Duck = 0f; Ambience.Blood = 0f; Fx.RedTint = 0f; Fx.Muted = false; Fx.Blackout = 0f; });
            Fx.Flash(new Color(0.9f, 0f, 0f), 0.4f);
            var src = Fx.Play(Synth.ScreamChoir(dur), Head.position, 1f, true, 0f, Head);
            var title = Screen(0.09f, new Color(1f, 0.05f, 0.03f));
            int ut = Own(title.gameObject);
            title.text = Fx.Safe(Pick(new[] { "ELES TE ENCONTRARAM.", "ELES ESTÃO AQUI.", "OLHE EM VOLTA.", "VOCÊ NÃO ESTÁ SOZINHO." }));

            var ws = new List<(GameObject w, int u, float h)>();
            int count = 3 + Rng.Next(4);
            float baseYaw = Rf(0f, 360f);
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, baseYaw + i * 360f / count + Rf(-15f, 15f), 0f) * Vector3.forward;
                if (!Fx.Ground(Head.position + dir * Rf(7f, 11f), out var g)) continue;
                float h = Entity.PlayerHeight() * Rf(1.2f, 1.6f);
                var w = Entity.Spawn(h);
                w.transform.position = g;
                ws.Add((w, Own(w), h));
            }

            GameObject caught = null;
            int caughtU = 0;
            float caughtH = 1f, nextStep = 0.8f;
            for (float t = 0f; t < dur && caught == null; t += Dt)
            {
                float k = Mathf.Clamp01(t / 0.6f) * Mathf.Clamp01((dur - t) / 0.4f);
                if (Ambience.Ready) Ambience.Blood = k;
                else Fx.RedTint = k;
                Fx.Fear = 1f;
                if (title != null)
                {
                    if (t < 2.2f) Follow(title, 0f);
                    else { Fx.Restore(ut); title = null; }
                }
                var hp = Head.position;
                bool step = t >= nextStep;
                if (step) nextStep = t + Rf(0.5f, 1.1f);
                float nearest = 999f;
                foreach (var (w, wu, h) in ws)
                {
                    if (w == null) continue;
                    Vector3 off = Vector3.ProjectOnPlane(w.transform.position - hp, Vector3.up);
                    // Weeping-angel rule: they only come closer while you aren't looking at them.
                    if (step && !Fx.Seen(w.transform.position + Vector3.up * h * 0.8f, 50f))
                    {
                        float nd = Mathf.Max(0.9f, off.magnitude * Rf(0.6f, 0.8f));
                        if (Fx.Ground(hp + off.normalized * nd, out var g)) w.transform.position = g;
                        if (Rng.NextDouble() < 0.4) Fx.Play(Synth.Footsteps(1, 0.1f), w.transform.position, 0.7f, false);
                        if (nd <= 1f) { caught = w; caughtU = wu; caughtH = h; }
                    }
                    Entity.Face(w.transform, hp);
                    if (off.magnitude < nearest) { nearest = off.magnitude; Ambience.SetThreat(w.transform.position); }
                }
                yield return null;
            }
            if (title != null) Fx.Restore(ut);
            if (src != null) src.Stop();
            Ambience.Blood = 0f;
            Fx.RedTint = 0f;
            foreach (var (w, wu, h) in ws) if (w != caught) Fx.Restore(wu);
            if (caught != null)
            {
                yield return Scare(caught, caughtU, caughtH);
            }
            else
            {
                // Everything stops at once.
                Fx.Blackout = 1f;
                Fx.Muted = true;
                yield return new WaitForSeconds(1.5f);
                Fx.Muted = false;
                for (float t = 0f; t < 1f; t += Dt) { Fx.Blackout = 1f - t; yield return null; }
            }
            Fx.Restore(u);
            Hurt(8f);
        }

        // ---------- the intro (when the player turns the horror on) ----------

        private static string PlayerName()
        {
            try { return NetworkSystem.Instance != null ? NetworkSystem.Instance.GetMyNickName() ?? "" : ""; }
            catch { return ""; }
        }

        /// <summary>Text fixed in front of the eyes, over the darkness (never hidden by it or by walls).</summary>
        private static TextMeshPro Screen(float height, Color color)
        {
            var h = Head;
            var t = Fx.Text("", h.position + h.forward * 1.4f, Quaternion.LookRotation(h.forward, Vector3.up), height, color);
            // Wrapped into a 1.6 m wide block: GT's font is wide and long lines ran off the sides of the view.
            t.textWrappingMode = TextWrappingModes.Normal;
            t.rectTransform.sizeDelta = new Vector2(1.6f, height * 6f);
            t.fontMaterial.renderQueue = Fx.GlowQueue;
            t.fontMaterial.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            if (t.fontMaterial.HasProperty("_ZTestMode")) t.fontMaterial.SetFloat("_ZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);
            return t;
        }

        private static void Follow(TextMeshPro t, float down)
        {
            var h = Head;
            if (t != null) t.transform.SetPositionAndRotation(h.position + h.forward * 1.4f - h.up * down, Quaternion.LookRotation(h.forward, h.up));
        }

        /// <summary>
        /// Turning the horror on: everything goes black, a hit and static, then a message types itself out in red
        /// (sometimes with your name), the sanity line glitches, the heartbeat races, and the dark opens again.
        /// </summary>
        public IEnumerator Intro()
        {
            string name = PlayerName().ToUpperInvariant();
            string line = Pick(new[]
            {
                "ELE ACORDOU.", "VOCÊ NÃO DEVIA TER LIGADO ISSO.", "AGORA VOCÊ É A ÚLTIMA TESTEMUNHA.", "NÃO EXISTE SAIR.",
                "CONTE SEUS PASSOS. EU ESTOU CONTANDO OS MEUS.", "EU VI VOCÊ CHEGAR.",
                name.Length > 0 ? name + "... EU ESTAVA ESPERANDO." : "EU ESTAVA ESPERANDO VOCÊ.",
                name.Length > 0 ? "OLÁ, " + name + "." : "OLÁ DE NOVO.",
            });
            int u = Fx.Track(() => Fx.Blackout = 0f);
            Fx.Blackout = 1f;
            Fx.Fear = 1f;
            Fx.Static(0.5f, Head.position, 0.7f, true, 0f);
            Fx.Play(Synth.Boom(), Head.position, 1f, true, 0f);
            yield return new WaitForSeconds(1.4f);

            var text = Screen(0.075f, new Color(0.85f, 0.03f, 0.02f));
            var sub = Screen(0.04f, new Color(0.7f, 0.68f, 0.66f));
            int ut = Own(text.gameObject), us = Own(sub.gameObject);
            string shown = Fx.Safe(line);
            var tick = Synth.Tick();
            for (int i = 1; i <= shown.Length; i++)
            {
                text.text = shown.Substring(0, i);
                if (shown[i - 1] != ' ') Fx.Play(tick, Head.position, 0.5f, false, 0f);
                for (float t = 0f, wait = Rf(0.05f, 0.13f) * (shown[i - 1] == '.' ? 4f : 1f); t < wait; t += Dt)
                {
                    Follow(text, 0f);
                    yield return null;
                }
            }
            Fx.Play(Synth.Whisper(Rf(1.5f, 2.5f)), Head.position + Head.right * (Rng.NextDouble() < 0.5 ? 0.4f : -0.4f), 0.8f, false, 1f, Head);
            const string glyphs = "#%&@?!/\\";
            for (float t = 0f; t < 3.2f; t += Dt)
            {
                Follow(text, 0f);
                Follow(sub, 0.3f);
                sub.text = t < 1f ? "" : t < 1.8f ? "SANIDADE: 100%" : "SANIDADE: " + (Rng.NextDouble() < 0.5 ? "??" : Rng.Next(0, 100).ToString()) + "%";
                // The words break for a frame now and then.
                if (Rng.NextDouble() < 0.06)
                {
                    var c = shown.ToCharArray();
                    c[Rng.Next(c.Length)] = glyphs[Rng.Next(glyphs.Length)];
                    text.text = new string(c);
                }
                else text.text = shown;
                yield return null;
            }
            Fx.Glitch = 1f;
            Fx.Static(0.3f, Head.position, 0.6f, true, 0f);
            yield return new WaitForSeconds(0.15f);
            Fx.Glitch = 0f;
            Fx.Restore(ut);
            Fx.Restore(us);
            for (float t = 0f; t < 2.5f; t += Dt)
            {
                Fx.Blackout = 1f - t / 2.5f;
                yield return null;
            }
            Fx.Restore(u);
        }

        /// <summary>Red eyes in the dark that blink, and close one pair at a time when you look at them.</summary>
        private IEnumerator Eyes()
        {
            var red = Fx.Flat(new Color(1f, 0.08f, 0.03f), Fx.GlowQueue);
            var pairs = new List<(GameObject go, float seen, float blink)>();
            int count = 2 + Rng.Next(1 + (int)(Insanity * 6f));
            var h = Head;
            for (int i = 0; i < count; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
                if (!Fx.Ground(h.position + dir * Rf(8f, 22f), out var g)) continue;
                var go = new GameObject("TLW eyes");
                go.transform.position = g + Vector3.up * Rf(0.3f, 2.5f);
                go.transform.rotation = Quaternion.LookRotation(Flat(h.position - go.transform.position), Vector3.up);
                float size = Rf(0.025f, 0.05f), gap = size * Rf(1.6f, 2.4f);
                foreach (float side in new[] { -1f, 1f })
                {
                    var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Object.Destroy(e.GetComponent<Collider>());
                    e.GetComponent<MeshRenderer>().sharedMaterial = red;
                    e.transform.SetParent(go.transform, false);
                    e.transform.localPosition = new Vector3(side * gap, 0f, 0f);
                    e.transform.localScale = new Vector3(size, size * 0.7f, size * 0.5f);
                }
                Own(go);
                pairs.Add((go, 0f, Time.time + Rf(1f, 5f)));
            }
            Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.3f);
            for (float t = 0f; t < 25f && pairs.Count > 0; t += Dt)
            {
                for (int i = pairs.Count - 1; i >= 0; i--)
                {
                    var (go, seen, blink) = pairs[i];
                    if (go == null) { pairs.RemoveAt(i); continue; }
                    // Blink: squash for a moment every few seconds.
                    float y = Time.time > blink && Time.time < blink + 0.12f ? 0.1f : 1f;
                    if (Time.time > blink + 0.12f) blink = Time.time + Rf(2f, 5f);
                    go.transform.localScale = new Vector3(1f, y, 1f);
                    seen = Fx.Seen(go.transform.position, 25f) ? seen + Dt : 0f;
                    if (seen > Rf(0.4f, 1f))
                    {
                        Object.Destroy(go);
                        pairs.RemoveAt(i);
                        Hurt(1f);
                        continue;
                    }
                    pairs[i] = (go, seen, blink);
                }
                yield return null;
            }
            foreach (var p in pairs) if (p.go != null) Object.Destroy(p.go);
        }

        /// <summary>A fake "connection lost": all sound cuts, a grey message, then it finds you.</summary>
        private IEnumerator FakeDisconnect()
        {
            Fx.Muted = true;
            int um = Fx.Track(() => Fx.Muted = false);
            var h = Head;
            var text = Fx.Text("CONEXÃO PERDIDA\nTentando reconectar", h.position + h.forward * 1.3f, Quaternion.LookRotation(h.forward, Vector3.up), 0.06f, new Color(0.85f, 0.85f, 0.85f));
            text.rectTransform.sizeDelta = new Vector2(3f, 1f);
            int u = Own(text.gameObject);
            float hold = Rf(4f, 7f);
            for (float t = 0f; t < hold; t += Dt)
            {
                h = Head;
                text.transform.SetPositionAndRotation(h.position + h.forward * 1.3f, Quaternion.LookRotation(h.forward, Vector3.up));
                text.text = Fx.Safe("CONEXÃO PERDIDA\nTentando reconectar" + new string('.', 1 + (int)(t * 2f) % 3));
                yield return null;
            }
            text.text = Fx.Safe(Pick(new[] { "ELE ENCONTROU VOCÊ", "VOCÊ NÃO ESTÁ SOZINHO", "BEM-VINDO DE VOLTA" }));
            text.color = new Color(0.8f, 0f, 0f);
            Fx.Glitch = 1f;
            yield return new WaitForSeconds(Rf(0.6f, 1.2f));
            Fx.Glitch = 0f;
            Fx.Restore(um);
            Fx.Restore(u);
            Fx.Play(Rng.NextDouble() < 0.5 ? Synth.Screech(0.8f) : Synth.Boom(), Head.position, 1f, true, 0f);
            Fx.Flash(new Color(0.6f, 0f, 0f), 0.4f);
            Hurt(5f);
        }

        // ---------- light (Gorilla Tag's Ghost Reactor lighting, see Ambience) ----------

        /// <summary>Lightning: the night flashes white, and in the flash the Witness stands among the trees. Each flash, closer.</summary>
        private IEnumerator Lightning()
        {
            var h = Head;
            GameObject w = null;
            int u = 0;
            float height = Entity.PlayerHeight() * Rf(1.3f, 1.7f);
            Vector3 fwd = Flat(h.forward);
            float dist = Rf(10f, 18f);
            if (Insanity > 0.2f && Rng.NextDouble() < 0.75 && Fx.Ground(h.position + Quaternion.Euler(0f, Rf(-45f, 45f), 0f) * fwd * dist, out var g))
            {
                w = Entity.Spawn(height);
                u = Own(w);
                w.transform.position = g;
                w.SetActive(false);
            }
            int flashes = Rng.Next(1, 4);
            for (int f = 0; f < flashes; f++)
            {
                if (w != null)
                {
                    w.SetActive(true);
                    Entity.Face(w.transform, Head.position);
                }
                foreach (var (level, time) in new[] { (1f, 0.06f), (0.25f, 0.05f), (1f, Rf(0.05f, 0.14f)) })
                {
                    if (Ambience.Ready) Ambience.Lightning = level;
                    else Fx.Flash(new Color(0.85f, 0.9f, 1f), time);
                    yield return new WaitForSeconds(time);
                }
                Ambience.Lightning = 0f;
                if (w != null)
                {
                    w.SetActive(false);
                    // Next flash, it's nearer.
                    dist *= Rf(0.45f, 0.65f);
                    if (Fx.Ground(Head.position + Quaternion.Euler(0f, Rf(-30f, 30f), 0f) * Flat(Head.forward) * Mathf.Max(2.5f, dist), out var n)) w.transform.position = n;
                }
                Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.3f);
                var thunderAt = Head.position + Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward * 30f;
                yield return new WaitForSeconds(Rf(0.4f, 1.5f));
                Fx.Play(Synth.Thunder(), thunderAt, 1f, false, 0.5f);
                yield return new WaitForSeconds(Rf(1f, 3f));
            }
            if (u != 0) Fx.Restore(u);
            Hurt(3f);
        }

        /// <summary>The lamps burst one by one, coming towards you. Darkness. Then they come back, and something stands under one.</summary>
        private IEnumerator LightsDie()
        {
            if (!Ambience.Ready) yield break;
            var lamps = Ambience.LampsFarFirst(45f);
            if (lamps.Count == 0) yield break;
            int u = Fx.Track(() => { foreach (var l in lamps) l.Dead = false; });
            foreach (var l in lamps)
            {
                if (l.Go == null) continue;
                l.Dead = true;
                Fx.Play(Synth.Pop(), l.Go.transform.position, 1f, false);
                Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.1f);
                yield return new WaitForSeconds(Rf(0.2f, 0.7f));
            }
            Hurt(3f);
            yield return new WaitForSeconds(Rf(4f, 9f));
            GameObject w = null;
            int uw = 0;
            lamps.Reverse(); // nearest first
            if (Insanity > 0.5f && Rng.NextDouble() < 0.6 && lamps[0].Go != null && Fx.Ground(lamps[0].Go.transform.position + Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward * 1.2f, out var g))
            {
                w = Entity.Spawn(Entity.PlayerHeight() * 1.4f);
                uw = Own(w);
                w.transform.position = g;
                Entity.Face(w.transform, Head.position);
            }
            foreach (var l in lamps)
            {
                l.Dead = false;
                yield return new WaitForSeconds(Rf(0.15f, 0.5f));
            }
            Fx.Restore(u);
            if (w != null)
            {
                for (float t = 0f; t < 8f; t += Dt)
                {
                    if (Fx.Seen(w.transform.position + Vector3.up, 30f) && t > 1f) break;
                    yield return null;
                }
                Fx.Static(0.5f, w.transform.position, 0.8f, true);
                Fx.Restore(uw);
                Hurt(4f);
            }
        }

        /// <summary>A reactor alarm: the world pulses red, the lamps turn red, a siren wails somewhere.</summary>
        private IEnumerator Alarm()
        {
            float hold = Rf(8f, 15f);
            Fx.Play(Synth.Siren(hold + 2f), Head.position + Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward * Rf(12f, 25f), 0.9f, false, 0.7f);
            int u = Fx.Track(() => { Ambience.Alarm = 0f; Fx.RedTint = 0f; });
            for (float t = 0f; t < hold + 2f; t += Dt)
            {
                float k = Mathf.Clamp01(Mathf.Min(t, hold + 2f - t));
                if (Ambience.Ready) Ambience.Alarm = k;
                else Fx.RedTint = k * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 1.6f));
                yield return null;
            }
            Fx.Restore(u);
            Hurt(3f);
        }

        /// <summary>Gravity fails for a few seconds: you float, or you're pulled down harder.</summary>
        private IEnumerator Gravity()
        {
            var p = Fx.Player;
            if (p == null) yield break;
            float k = Rng.NextDouble() < 0.7 ? Rf(0.1f, 0.4f) : Rf(1.6f, 2.2f);
            p.SetGravityOverride(d, pl => pl.playerRigidBody.AddForce(Physics.gravity * k * pl.scale, ForceMode.Acceleration));
            int u = Fx.Track(() => { if (Fx.Player != null) Fx.Player.UnsetGravityOverride(d); });
            Fx.Play(Synth.Ringing(Rf(3f, 5f)), Head.position, 0.3f, false, 0f, Head);
            Fx.Glitch = 0.3f;
            yield return new WaitForSeconds(0.2f);
            Fx.Glitch = 0f;
            yield return new WaitForSeconds(Rf(3f, 7f));
            Fx.Restore(u);
            Hurt(2f);
        }
    }
}
