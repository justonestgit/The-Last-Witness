using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static TheLastWitness.Director;

namespace TheLastWitness
{
    public enum GameMode { Fitas, Pesadelo }

    /// <summary>
    /// "As Fitas": the night has a goal. Eight tapes are hidden around you, glowing and humming. Each one plays a
    /// recording (the story of the other gorillas and of what the Witness erases) and pushes the night one act
    /// further. The world shows how far you are: statues of it where you have been (act 2+), shapes hanging from
    /// the trees (act 3+), a giant looking over the forest (act 4+). With all eight, the finale: survive it and
    /// it is dawn; be caught and you are erased. Also here: the set pieces that need the world props
    /// (O Gigante, Mímico, Desorientação).
    /// </summary>
    public partial class Events
    {
        public const int TapeCount = 8;
        public static readonly List<int> Found = new List<int>();
        public static bool Ended, NightStarted;
        public static string Ending = "";

        public static readonly string[] Tapes =
        {
            "FITA 1. Se alguém achar isso: eu não sou o primeiro. Essa floresta tinha gente. Muita gente. Agora só tem eu. E ele.",
            "FITA 2. Ele não anda quando você olha. Mas ele ouve. Cada palavra, cada respiração. Fale baixo. Se você gritar, ele vem.",
            "FITA 3. As luzes. Quando as lâmpadas piscam perto de você, ele está chegando. Elas morrem antes dele.",
            "FITA 4. Os outros viraram estátuas. Vi a do meu amigo na clareira, olhando para mim. Ontem ela não estava lá.",
            "FITA 5. Ele copia a gente. Usou a minha cor. Acenou para mim como se fosse eu. Não acene de volta.",
            "FITA 6. Tem uma coisa maior. Lá longe, acima das árvores. Acho que o que corre atrás da gente são só os olhos dela.",
            "FITA 7. Testemunhas. É isso que ele apaga: quem viu. Eu vi. E agora você ouviu. Você também viu.",
            "FITA 8. Se você juntou todas... me desculpa. Agora ele sabe que você sabe. Quando a música começar, CORRA.",
        };

        private readonly List<(GameObject go, int index, GameLight light)> tapes = new List<(GameObject, int, GameLight)>();
        private readonly List<GameObject> statues = new List<GameObject>(), hangers = new List<GameObject>();
        private readonly List<Vector3> crumbs = new List<Vector3>();
        private GameObject giant;
        private float giantDist, nextCrumb, nextProps, giantHeight, nightTime;
        private bool reading, finale, storyBuilt;

        private static bool Story => Settings.Mode.Value == GameMode.Fitas;

        /// <summary>Every frame: the tapes, the props of the act, the finale. Everything goes when the horror stops.</summary>
        public void StoryTick(bool active)
        {
            ActsByStory = Story;
            if (!active)
            {
                if (storyBuilt) ClearStory();
                return;
            }
            var head = Head;
            if (head == null) return;
            if (Story && !storyBuilt && !Ended) PlaceTapes(head.position);
            storyBuilt = true;
            if (Time.time >= nextCrumb)
            {
                nextCrumb = Time.time + 5f;
                if (Fx.Floor(head.position, out var f)) crumbs.Add(f);
                if (crumbs.Count > 60) crumbs.RemoveAt(0);
            }
            if (Story && !Ended)
            {
                TickTapes(head);
                // The night gets worse with the tapes, and with time even if you don't pick any up.
                nightTime += Time.deltaTime;
                Act = Mathf.Clamp(Mathf.Max(1 + Found.Count * 5 / TapeCount, 1 + (int)(nightTime / 420f)), 1, 7);
            }
            if (Time.time >= nextProps)
            {
                nextProps = Time.time + 2f;
                Props(head);
            }
            if (giant != null) Entity.Face(giant.transform, head.position);
            foreach (var s in statues) if (s != null && !Fx.Seen(s.transform.position + Vector3.up, 60f)) Entity.Face(s.transform, head.position);
            for (int i = 0; i < hangers.Count; i++)
                if (hangers[i] != null) hangers[i].transform.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.7f + i) * 6f, Time.time * 4f + i * 50f, 180f);
        }

        private void ClearStory()
        {
            foreach (var t in tapes) if (t.go != null) Object.Destroy(t.go);
            tapes.Clear();
            foreach (var s in statues) if (s != null) Object.Destroy(s);
            statues.Clear();
            foreach (var h in hangers) if (h != null) Object.Destroy(h);
            hangers.Clear();
            if (giant != null) Object.Destroy(giant);
            giant = null;
            crumbs.Clear();
            storyBuilt = reading = finale = false;
        }

        /// <summary>A new night: the tapes and the act start over.</summary>
        public void NewNight()
        {
            ClearStory();
            Found.Clear();
            nightTime = 0f;
            Ended = NightStarted = false;
            Ending = "";
            Act = 1;
        }

        // ---------- tapes ----------

        private void PlaceTapes(Vector3 at)
        {
            var spots = new List<Vector3>();
            for (int tries = 0; tries < 400 && spots.Count < TapeCount; tries++)
            {
                Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
                // Spread out: the first ones near, the last ones far.
                float dist = Rf(12f, 25f) + spots.Count * Rf(5f, 9f);
                if (!Fx.Floor(at + dir * dist, out var g)) continue;
                if (spots.Exists(s => (s - g).sqrMagnitude < 12f * 12f)) continue;
                if (Physics.Raycast(g + Vector3.up * 0.3f, Vector3.up, 2f, Fx.World, QueryTriggerInteraction.Ignore)) continue; // not under something
                spots.Add(g);
            }
            var left = new List<int>();
            for (int i = 0; i < TapeCount; i++) if (!Found.Contains(i)) left.Add(i);
            for (int i = 0; i < spots.Count && i < left.Count; i++) tapes.Add(MakeTape(spots[i], left[i]));
            Plugin.Log.LogInfo($"TheLastWitness: {tapes.Count} tapes placed ({Found.Count} found before)");
        }

        private (GameObject, int, GameLight) MakeTape(Vector3 ground, int index)
        {
            float s = Fx.Player != null ? Fx.Player.scale : 1f;
            var root = new GameObject("TLW tape " + (index + 1));
            root.transform.position = ground + Vector3.up * 0.5f * s;
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(box.GetComponent<Collider>());
            box.transform.SetParent(root.transform, false);
            box.transform.localScale = new Vector3(0.3f, 0.06f, 0.18f) * s;
            box.GetComponent<MeshRenderer>().sharedMaterial = Fx.Lit(new Color(0.3f, 0.3f, 0.32f));
            var label = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(label.GetComponent<Collider>());
            label.transform.SetParent(root.transform, false);
            label.transform.localPosition = Vector3.up * 0.031f * s;
            label.transform.localScale = new Vector3(0.22f, 0.005f, 0.09f) * s;
            label.GetComponent<MeshRenderer>().sharedMaterial = Fx.Flat(new Color(0.85f, 0.08f, 0.05f), Fx.GlowQueue + 1);
            var a = root.AddComponent<AudioSource>();
            a.clip = Synth.Static(2f);
            a.loop = true;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 1f;
            a.maxDistance = 14f;
            a.volume = 0.35f * Settings.Volume.Value;
            a.Play();
            GameLight light = Lights.On ? Lights.Add(root.transform.position, new Color(1f, 0.25f, 0.1f), 1.5f, 4f, false, root.transform) : null;
            return (root, index, light);
        }

        private void TickTapes(Transform head)
        {
            if (reading || finale) return;
            float s = Fx.Player != null ? Fx.Player.scale : 1f;
            var g = GorillaTagger.Instance;
            for (int i = tapes.Count - 1; i >= 0; i--)
            {
                var (go, index, light) = tapes[i];
                if (go == null) { tapes.RemoveAt(i); continue; }
                go.transform.rotation = Quaternion.Euler(0f, Time.time * 40f, 0f);
                if (light != null) light.light.intensity = 1.2f + 0.6f * Mathf.Sin(Time.time * 3f + i);
                Vector3 p = go.transform.position;
                bool touched = (head.position - p).sqrMagnitude < 0.6f * 0.6f * s * s
                    || g != null && ((g.leftHandTransform.position - p).sqrMagnitude < 0.35f * 0.35f * s * s || (g.rightHandTransform.position - p).sqrMagnitude < 0.35f * 0.35f * s * s);
                if (!touched) continue;
                Object.Destroy(go);
                tapes.RemoveAt(i);
                d.Run(Play(index), "tape");
                break;
            }
        }

        /// <summary>A tape: the recording plays (static, a whisper, the words typed in front of you), then the night gets worse.</summary>
        private IEnumerator Play(int index)
        {
            reading = true;
            Hold = true;
            Found.Add(index);
            Act = 1 + Found.Count * 5 / TapeCount;
            Plugin.Log.LogInfo($"TheLastWitness: tape {index + 1} ({Found.Count}/{TapeCount}), act {Act}");
            int ud = Fx.Track(() => Fx.Duck = 0f);
            Fx.Duck = 1f;
            d.Run(TapeHaunt(index, () => reading), "tape haunt");
            Fx.Static(0.6f, Head.position, 0.8f, false, 0f);
            Fx.Play(Synth.Whisper(Rf(4f, 6f)), Head.position + Head.right * 0.35f, 0.6f, false, 1f, Head);
            var text = Screen(0.045f, new Color(0.85f, 0.82f, 0.78f));
            int ut = Own(text.gameObject);
            string words = Fx.Safe(Tapes[index]);
            var tick = Synth.Tick();
            for (int i = 1; i <= words.Length; i++)
            {
                text.text = words.Substring(0, i);
                if (i % 2 == 0) Fx.Play(tick, Head.position, 0.25f, false, 0f);
                for (float t = 0f, w = words[i - 1] == '.' ? 0.3f : 0.035f; t < w; t += Dt) { Follow(text, 0.05f); yield return null; }
            }
            for (float t = 0f; t < 3.5f; t += Dt) { Follow(text, 0.05f); yield return null; }
            Fx.Restore(ut);
            Fx.Restore(ud);
            Hurt(5f);
            reading = false;
            Hold = false;
            if (Found.Count >= TapeCount) { yield return Finale(); yield break; }
            // The night answers: often at once.
            if (Rng.NextDouble() < 0.45 + Found.Count * 0.06) d.ForcePeak();
        }

        // ---------- the finale and the endings ----------

        /// <summary>
        /// The last tape, set to "god-is-coming" (witness.bundle "final_god": 45 s, one long crescendo that steps up
        /// at the times in Steps, bursts at 31 s and cuts dead at the end). Everything else goes quiet; the lamps die;
        /// on every step the world shakes; from 11 s things float and spin and holes open in the air; at 16 s
        /// statues of it stand all around; from 22 s lightning shows the giant closing in and gravity weakens; at 31 s
        /// the Witness comes for you, until the song stops. Without the song: the countdown finale.
        /// </summary>
        private IEnumerator Finale()
        {
            var god = Entity.Clip("final_god");
            if (god == null) { yield return FinaleCountdown(); yield break; }
            finale = true;
            Hold = true;
            Plugin.Log.LogInfo("TheLastWitness: finale (god is coming)");
            int u0 = Fx.Track(() => { Fx.Duck = 0f; Ambience.Blood = Ambience.Lightning = Ambience.Alarm = 0f; Fx.RedTint = Fx.Glitch = 0f; Sky.Glitch = 0f; });
            Fx.Duck = 1f;
            if (giant == null) MakeGiant();
            var lamps = Ambience.Ready ? Ambience.LampsFarFirst(80f) : new List<Ambience.Lamp>();
            int ul = Fx.Track(() => { foreach (var l in lamps) l.Dead = false; });
            var floaters = new List<(Transform t, Vector3 p, Quaternion q, float h, Vector3 spin)>();
            foreach (var r in Nearby(35f, true))
            {
                if (floaters.Count >= 70) break;
                floaters.Add((r.transform, r.transform.position, r.transform.rotation, Rf(1f, 6f), Random.onUnitSphere * Rf(10f, 60f)));
            }
            int uf = Fx.Track(() => { foreach (var f in floaters) if (f.t != null) f.t.SetPositionAndRotation(f.p, f.q); });
            var extra = new List<int>();
            int ug = 0;
            float height = Entity.PlayerHeight() * 1.45f, len = god.length, nextBolt = 22f, boltStart = -9f, nextVoid = 11f;
            float[] steps = { 3f, 5.5f, 8.5f, 11f, 16.5f, 19f, 22f, 27.5f, 31f };
            int step = 0, lampIdx = 0;
            bool statuesUp = false, said = false;
            GameObject w = null;
            int uw = 0;
            caughtYou = false;
            var src = Fx.Play(god, Head.position, 1f, true, 0f, Head);
            IEnumerator Hunt()
            {
                float until = Time.time + (len - 31f) + 0.5f;
                while (Time.time < until && !caughtYou) yield return Chase(w, height, until - Time.time, 7f);
            }
            for (float t = 0f; t < len && !caughtYou; t += Dt)
            {
                float shake = Mathf.InverseLerp(0f, 36f, t);
                // A tremor on every step up of the song.
                if (step < steps.Length && t >= steps[step])
                {
                    step++;
                    Fx.Play(step % 2 == 0 ? Synth.Thunder() : Synth.Boom(), Head.position - Vector3.up, 0.5f + step * 0.06f, true, 0.3f);
                    Buzz(0.3f + step * 0.07f, 0.3f);
                    Fx.Glitch = 0.6f;
                    Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.15f);
                }
                else if (Fx.Glitch > 0f && Rng.NextDouble() < 0.2) Fx.Glitch = 0f;
                // The lamps go, one by one, in the first eleven seconds.
                while (lampIdx < lamps.Count && lampIdx < lamps.Count * Mathf.Min(1f, t / 11f))
                {
                    var l = lamps[lampIdx++];
                    if (l.Go == null) continue;
                    l.Dead = true;
                    Fx.Play(Synth.Pop(), l.Go.transform.position, 0.6f, false);
                }
                // The ground shakes under you (unless movement effects are off), the hands buzz.
                if (Rng.NextDouble() < Dt * 6f * shake) Buzz(0.2f + 0.5f * shake, 0.08f);
                if (!Settings.NoMovement.Value && Fx.Player != null && Rng.NextDouble() < Dt * 3f * shake)
                {
                    var kick = Random.insideUnitSphere * 1.5f * shake;
                    kick.y = Mathf.Abs(kick.y) * 0.5f;
                    Fx.Player.playerRigidBody.AddForce(kick, ForceMode.VelocityChange);
                }
                // Things lift off and turn, faster on the last stretch, jittering with the tremor.
                float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(11f, 22f, t)), fast = 1f + 2f * Mathf.InverseLerp(36f, 45f, t);
                foreach (var f in floaters)
                {
                    if (f.t == null) continue;
                    Vector3 p = f.p + Vector3.up * (f.h * lift + Mathf.Sin(t * 1.7f + f.h) * 0.15f * lift) + Random.insideUnitSphere * 0.05f * shake * lift;
                    f.t.SetPositionAndRotation(p, Quaternion.Euler(f.spin * t * lift * fast) * f.q);
                }
                // Holes open in the air.
                if (t >= nextVoid && extra.Count < 30)
                {
                    nextVoid = t + Rf(0.5f, 1.2f);
                    var around = Head.position + Random.onUnitSphere * Rf(4f, 18f);
                    extra.Add(Own(Decay.Void(new Vector3(around.x, Head.position.y + Rf(-0.5f, 6f), around.z), Rf(0.5f, 2.5f))));
                }
                // Statues of it, all around, at 16 s.
                if (!statuesUp && t >= 16f)
                {
                    statuesUp = true;
                    for (int i = 0; i < 7; i++)
                    {
                        Vector3 dir = Quaternion.Euler(0f, i * 360f / 7f + Rf(-15f, 15f), 0f) * Vector3.forward;
                        if (!Fx.Ground(Head.position + dir * Rf(10f, 20f), out var g)) continue;
                        var s = Entity.Spawn(Entity.PlayerHeight() * Rf(1.2f, 1.5f), false);
                        s.transform.position = g;
                        Entity.Face(s.transform, Head.position);
                        s.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
                        extra.Add(Own(s));
                    }
                }
                // Lightning shows the giant coming.
                if (t >= nextBolt)
                {
                    boltStart = t;
                    nextBolt = t + Rf(2f, 3.5f);
                    if (Rng.NextDouble() < 0.5) Fx.Play(Synth.Thunder(), Head.position + Random.onUnitSphere * 30f, 0.8f, false, 0.5f);
                }
                float b = t - boltStart;
                if (Ambience.Ready) Ambience.Lightning = b < 0.07f ? 1f : b < 0.12f ? 0.3f : b < 0.2f ? 1f : 0f;
                else if (b < 0.07f) Fx.Flash(new Color(0.85f, 0.9f, 1f), 0.15f);
                giantDist = Mathf.Lerp(110f, 30f, Mathf.InverseLerp(11f, 36f, t));
                PlaceGiant();
                // Gravity lets go a little (only with movement effects allowed).
                if (ug == 0 && t >= 24f && !Settings.NoMovement.Value && Fx.Player != null)
                {
                    Fx.Player.SetGravityOverride(d, pl => pl.playerRigidBody.AddForce(Physics.gravity * 0.45f * pl.scale, ForceMode.Acceleration));
                    ug = Fx.Track(() => { if (Fx.Player != null) Fx.Player.UnsetGravityOverride(d); });
                }
                if (!said && t >= 25f) { said = true; d.Run(Flash("ELE ESTÁ AQUI.", 1.5f), "finale words"); }
                // The world bleeds, the sky breaks, and on the plateau the red pulses.
                float red = Mathf.InverseLerp(16f, 36f, t) * (t > 36f ? 0.75f + 0.25f * Mathf.Sin(t * 8f) : 1f);
                if (Ambience.Ready) Ambience.Blood = red;
                else Fx.RedTint = red;
                Sky.Glitch = Mathf.InverseLerp(11f, 45f, t) * 0.6f;
                // The burst: it comes for you.
                if (w == null && t >= 31f)
                {
                    if (!Fx.SpotOutOfView(18f, 24f, out var spot, 100f)) Fx.Ground(Head.position - Flat(Head.forward) * 20f, out spot);
                    w = Entity.Spawn(height, true);
                    uw = Own(w);
                    w.transform.position = spot;
                    Fx.Play(Synth.Screech(0.8f), spot + Vector3.up * height, 1f, true);
                    d.Run(Hunt(), "finale hunt");
                }
                yield return null;
            }
            // The song is over, or it has you.
            if (src != null) src.Stop();
            Ambience.Blood = Ambience.Lightning = 0f;
            Fx.RedTint = Fx.Glitch = 0f;
            Sky.Glitch = 0f;
            foreach (int id in extra) Fx.Restore(id);
            Fx.Restore(uf);
            if (ug != 0) Fx.Restore(ug);
            Fx.Restore(ul);
            if (caughtYou && w != null)
            {
                yield return Scare(w, uw, height);
                yield return End(false);
            }
            else
            {
                if (uw != 0) Fx.Restore(uw);
                yield return End(true);
            }
            Fx.Restore(u0);
            Hold = false;
        }

        /// <summary>The older finale (the countdown song and a chase), used when the bundle has no "final_god".</summary>
        private IEnumerator FinaleCountdown()
        {
            finale = true;
            Hold = true;
            Plugin.Log.LogInfo("TheLastWitness: finale");
            var song = Entity.Clip("countdown");
            int u0 = Fx.Track(() => { Fx.Duck = 0f; Ambience.Blood = 0f; Fx.RedTint = 0f; });
            Fx.Duck = 1f;
            // The giant comes close, the lamps die, the world goes red, the song starts.
            if (giant == null) MakeGiant();
            giantDist = 45f;
            PlaceGiant();
            var lamps = Ambience.Ready ? Ambience.LampsFarFirst(80f) : new List<Ambience.Lamp>();
            int ul = Fx.Track(() => { foreach (var l in lamps) l.Dead = false; });
            foreach (var l in lamps) { l.Dead = true; if (l.Go != null) Fx.Play(Synth.Pop(), l.Go.transform.position, 0.7f, false); yield return new WaitForSeconds(0.05f); }
            var src = song != null ? Fx.Play(song, Head.position, 1f, true, 0f, Head) : Fx.Play(Synth.ScreamChoir(20f), Head.position, 1f, true, 0f, Head);
            yield return Flash("CORRA.", 1.5f);
            float height = Entity.PlayerHeight() * 1.45f;
            if (!Fx.SpotOutOfView(18f, 26f, out var spot, 100f)) Fx.Ground(Head.position - Flat(Head.forward) * 20f, out spot);
            var w = Entity.Spawn(height, true);
            int uw = Own(w);
            w.transform.position = spot;
            Fx.Play(Synth.Screech(0.8f), spot + Vector3.up * height, 1f, true);
            float survive = (song != null ? song.length : 20f) + 6f;
            bool hunting = true;
            IEnumerator Hunt()
            {
                // It keeps coming for the whole song; it only gives up when the song is over.
                float t0 = Time.time;
                while (Time.time - t0 < survive && !caughtYou)
                {
                    yield return Chase(w, height, survive - (Time.time - t0), 7.5f);
                }
                hunting = false;
            }
            caughtYou = false;
            d.Run(Hunt(), "finale hunt");
            for (float t = 0f; hunting && t < survive + 2f; t += Dt)
            {
                if (Ambience.Ready) Ambience.Blood = 0.6f + 0.4f * Mathf.Sin(t * 3f);
                else Fx.RedTint = 0.7f;
                Sky.Glitch = 0.05f + t / survive * 0.3f;
                giantDist = Mathf.Lerp(45f, 25f, t / survive);
                PlaceGiant();
                yield return null;
            }
            Ambience.Blood = 0f;
            Fx.RedTint = 0f;
            Sky.Glitch = 0f;
            if (src != null) src.Stop();
            Fx.Restore(ul);
            if (caughtYou)
            {
                yield return Scare(w, uw, height);
                yield return End(false);
            }
            else
            {
                Fx.Restore(uw);
                yield return End(true);
            }
            Fx.Restore(u0);
            Hold = false;
        }

        private IEnumerator End(bool survived)
        {
            Ended = true;
            Ending = survived ? "Você sobreviveu à noite." : "Você foi apagado.";
            Plugin.Log.LogInfo("TheLastWitness: ending " + (survived ? "survived" : "erased"));
            int ub = Fx.Track(() => { Fx.Blackout = 0f; Fx.Muted = false; });
            Fx.Blackout = 1f;
            Fx.Muted = true;
            yield return new WaitForSeconds(1.5f);
            Fx.Muted = false;
            string[] lines = survived
                ? new[] { "AMANHECEU.", "VOCÊ SOBREVIVEU.", "MAS ELE LEMBRA DE VOCÊ." }
                : new[] { "VOCÊ FOI APAGADO.", "AGORA VOCÊ TAMBÉM É UMA TESTEMUNHA.", "ELE ESTÁ ESPERANDO O PRÓXIMO." };
            var t = Screen(0.07f, survived ? new Color(0.9f, 0.88f, 0.8f) : new Color(0.85f, 0.05f, 0.03f));
            int ut = Own(t.gameObject);
            foreach (var line in lines)
            {
                t.text = Fx.Safe(line);
                for (float k = 0f; k < 3f; k += Dt) { Follow(t, 0f); t.alpha = Mathf.Clamp01(Mathf.Min(k, 3f - k) * 2f); yield return null; }
            }
            Fx.Restore(ut);
            if (survived) Fx.ForceDay = true;
            for (float k = 0f; k < 2f; k += Dt) { Fx.Blackout = 1f - k / 2f; yield return null; }
            Fx.Restore(ub);
            // The night is over: the horror stops; the menu offers a new night.
            Settings.Running = false;
            Fx.ForceDay = false;
        }

        // ---------- props of the act ----------

        private void Props(Transform head)
        {
            // Statues: the shadow of it, standing where you have been, always facing you when you aren't looking.
            int wantStatues = Mathf.Clamp(Act - 1, 0, 4);
            while (statues.Count < wantStatues)
            {
                // Where you have been; if you haven't gone far yet, anywhere out of sight.
                var spot = crumbs.Count > 6 ? crumbs[Rng.Next(crumbs.Count - 6)] : head.position;
                if ((spot - head.position).sqrMagnitude < 12f * 12f && !Fx.SpotOutOfView(14f, 30f, out spot)) break;
                if (Fx.Seen(spot + Vector3.up, 60f)) break;
                var s = Entity.Spawn(Entity.PlayerHeight() * Rf(1.1f, 1.3f), false);
                s.transform.position = spot;
                s.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
                statues.Add(s);
            }
            // Now and then one is somewhere else when you look back.
            if (statues.Count > 0 && Rng.NextDouble() < 0.1)
            {
                var s = statues[Rng.Next(statues.Count)];
                var spot = crumbs.Count > 0 ? crumbs[Rng.Next(crumbs.Count)] : s.transform.position;
                if (s != null && !Fx.Seen(s.transform.position + Vector3.up, 60f) && !Fx.Seen(spot + Vector3.up, 60f) && (spot - head.position).sqrMagnitude > 10f * 10f) s.transform.position = spot;
            }
            // Hanging shapes under the branches.
            int wantHangers = Mathf.Clamp(Act - 2, 0, 5);
            for (int tries = 0; tries < 6 && hangers.Count < wantHangers; tries++)
            {
                Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
                if (!Fx.Floor(head.position + dir * Rf(10f, 35f), out var g)) continue;
                if (!Physics.Raycast(g + Vector3.up * 0.5f, Vector3.up, out var branch, 9f, Fx.World, QueryTriggerInteraction.Ignore) || branch.distance < 2.5f) continue;
                if (Fx.Seen(branch.point, 60f)) continue;
                float h = Entity.PlayerHeight() * Rf(1f, 1.2f);
                var hang = Entity.Spawn(h, false);
                hang.transform.position = branch.point - Vector3.up * 0.1f; // feet up at the branch, head down
                hangers.Add(hang);
            }
            // The giant, over the trees, a little nearer each act.
            if (Act >= 4 && giant == null) MakeGiant();
            if (giant != null && !finale)
            {
                float want = Mathf.Lerp(110f, 65f, Mathf.InverseLerp(4f, 7f, Act));
                if (Mathf.Abs(giantDist - want) > 1f && !Fx.Seen(giant.transform.position + Vector3.up * giantHeight * 0.8f, 50f)) { giantDist = want; PlaceGiant(); }
            }
        }

        public string PropsState() => $"statues {statues.Count} hangers {hangers.Count} giant {(giant != null ? giant.transform.position.ToString() : "none")} dist {giantDist:0} crumbs {crumbs.Count} tapes left {tapes.Count}";

        /// <summary>Test hook: stand next to the nearest tape.</summary>
        public void TestGoToTape()
        {
            GameObject best = null;
            foreach (var t in tapes) if (t.go != null && (best == null || (t.go.transform.position - Head.position).sqrMagnitude < (best.transform.position - Head.position).sqrMagnitude)) best = t.go;
            if (best == null || Fx.Player == null) { Plugin.Log.LogInfo("TheLastWitness: no tapes"); return; }
            Fx.Player.TeleportTo(best.transform.position + Vector3.up * 0.1f, Quaternion.identity, false, false); // right onto it: picks it up
            Plugin.Log.LogInfo("TheLastWitness: next to tape at " + best.transform.position);
        }

        /// <summary>Test hook: pick up every tape but one, or all of them (the finale).</summary>
        public void TestTapes(int count)
        {
            for (int i = 0; i < TapeCount && Found.Count < count; i++) if (!Found.Contains(i)) Found.Add(i);
            if (Found.Count >= TapeCount) { Found.RemoveAt(Found.Count - 1); }
            Plugin.Log.LogInfo($"TheLastWitness: {Found.Count} tapes marked found");
        }

        private Vector3 giantDir;

        private void MakeGiant()
        {
            giantHeight = 55f;
            giant = Entity.Spawn(giantHeight, true);
            giant.name = "TLW giant";
            giantDir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
            giantDist = 110f;
            PlaceGiant();
            Plugin.Log.LogInfo("TheLastWitness: the giant is watching");
        }

        private void PlaceGiant()
        {
            if (giant == null || Head == null) return;
            Vector3 p = Head.position + giantDir * giantDist;
            giant.transform.position = Fx.Floor(p, out var g) ? g : new Vector3(p.x, Head.position.y - 5f, p.z);
        }

        // ---------- set pieces that use the props ----------

        /// <summary>"O Gigante": it leans in over the trees, close, roaring, the world shaking red; then it is far again.</summary>
        private IEnumerator Giant()
        {
            bool temp = giant == null;
            if (temp) MakeGiant();
            giantDir = Flat(Head.forward);
            float back = giantDist;
            int u = Fx.Track(() => { Ambience.Blood = 0f; Fx.Glitch = 0f; Sky.Glitch = 0f; });
            Fx.Play(Synth.Thunder(), Head.position + giantDir * 40f, 1f, true, 0.4f);
            var roar = Loop(giant.transform, Synth.Growl(4f), 1f);
            roar.GetComponent<AudioSource>().maxDistance = 200f;
            roar.GetComponent<AudioSource>().pitch = 0.5f;
            for (float t = 0f; t < 7f; t += Dt)
            {
                giantDist = Mathf.Lerp(back, 35f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t / 2f)));
                PlaceGiant();
                giant.transform.rotation *= Quaternion.Euler(Mathf.Sin(t * 1.3f) * 8f, 0f, 0f); // leaning over
                if (Ambience.Ready) Ambience.Blood = 0.5f + 0.3f * Mathf.Sin(t * 9f);
                Fx.Glitch = Rng.NextDouble() < 0.1 ? 0.4f : 0f;
                Sky.Glitch = 0.1f;
                Fx.Fear = 1f;
                if (Rng.NextDouble() < Dt * 2f) Buzz(0.3f, 0.1f);
                yield return null;
            }
            Object.Destroy(roar);
            Fx.Restore(u);
            if (temp) { Object.Destroy(giant); giant = null; }
            else { giantDist = back; PlaceGiant(); }
            Hurt(6f);
        }

        /// <summary>"Mímico": a gorilla in your colours, moving exactly like you, comes over like a friend. Then its face is not yours.</summary>
        private IEnumerator Mimic()
        {
            if (!Fx.Ground(Head.position + Quaternion.Euler(0f, Rf(-30f, 30f), 0f) * Flat(Head.forward) * Rf(8f, 12f), out var g)) yield break;
            var dop = Entity.Doppel(out var repose);
            int u = Own(dop);
            dop.transform.position = g;
            float nextPose = 0f;
            for (float t = 0f; t < 9f; t += Dt)
            {
                Vector3 to = Vector3.ProjectOnPlane(Head.position - dop.transform.position, Vector3.up);
                if (to.magnitude < 2.2f) break;
                dop.transform.position += to.normalized * 1.3f * Dt;
                if (Fx.Ground(dop.transform.position, out var gg)) dop.transform.position = gg;
                Entity.Face(dop.transform, Head.position);
                if (Time.time > nextPose) { repose(); nextPose = Time.time + 0.08f; }
                yield return null;
            }
            // It stops. For a moment it doesn't copy you any more. Then it is the Witness.
            yield return new WaitForSeconds(Rf(0.6f, 1.4f));
            float height = Entity.PlayerHeight() * 1.2f;
            var w = Entity.Spawn(height, true);
            int uw = Own(w);
            w.transform.position = dop.transform.position;
            Entity.Face(w.transform, Head.position);
            Fx.Restore(u);
            yield return Scare(w, uw, height, 0.1f);
        }

        /// <summary>"Desorientação": a blink, and you are somewhere else in the dark, and it is near.</summary>
        private IEnumerator Lost()
        {
            int u = Fx.Track(() => { Fx.Blackout = 0f; Fx.Muted = false; });
            Fx.Blackout = 1f;
            Fx.Muted = true;
            yield return new WaitForSeconds(0.4f);
            var p = Fx.Player;
            for (int i = 0; i < 40 && p != null; i++)
            {
                Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
                if (!Fx.Floor(Head.position + dir * Rf(30f, 60f), out var g)) continue;
                if (Physics.Raycast(g + Vector3.up * 0.3f, Vector3.up, 2.5f, Fx.World, QueryTriggerInteraction.Ignore)) continue;
                p.TeleportTo(g + Vector3.up * 1.2f * p.scale, Quaternion.Euler(0f, Rf(0f, 360f), 0f), false, false);
                break;
            }
            yield return new WaitForSeconds(Rf(1.2f, 2f));
            Fx.Muted = false;
            Fx.Play(Synth.Whisper(1.5f), Head.position + Head.right * 0.3f, 0.8f, false, 1f, Head);
            for (float t = 0f; t < 1.5f; t += Dt) { Fx.Blackout = 1f - t / 1.5f; yield return null; }
            Fx.Restore(u);
            yield return Flash("ONDE VOCÊ ESTÁ?", 1.5f);
            Hurt(4f);
            d.Run(Glimpse(60f, 120f, Rf(4f, 7f), 6f), "glimpse");
        }
    }
}
