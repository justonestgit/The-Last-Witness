using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static TheLastWitness.Director;

namespace TheLastWitness
{
    /// <summary>
    /// What makes every event visual: anomalies (things vanish, swap places, float, fly at you, turn to look at you,
    /// tremble) and the Witness's presence (at the edge of sight, staring from afar, right behind you). Every event
    /// that starts brings both along (Companion), each tape has its own haunting, and five set pieces built on them
    /// are worse than anything else: the statues, the drag, the poltergeist, the hanged and the hunt in the dark.
    /// </summary>
    public partial class Events
    {
        private float companionUntil;

        // At most one new companion every few seconds (the inferno starts an event every 5-7 s).
        private void HookCompanions() => d.Began += e =>
        {
            if (Time.time < companionUntil) return;
            companionUntil = Time.time + 3f;
            d.Run(Companion(e), e.Id + " haunt");
        };

        /// <summary>Alongside any event: anomalies around you, and it is there too (unless the event is its own entity).</summary>
        private IEnumerator Companion(HorrorEvent e)
        {
            yield return new WaitForSeconds(Rf(0.5f, 3f));
            float k = 0.35f + Insanity * 0.65f;
            d.Run(Anomalies(k, Rf(4f, 8f) + Insanity * 6f), e.Id + " anomalies");
            if (!e.UsesEntity && !d.EntityBusyNow && Rng.NextDouble() < 0.45 + Insanity * 0.45)
            {
                yield return new WaitForSeconds(Rf(0.5f, 3f));
                yield return Presence();
            }
        }

        // ---------- anomalies ----------

        private enum Kind { Vanish, Swap, Float, Fly, FaceYou, Tremble }

        /// <summary>One to three kinds of anomaly at once, more of them the stronger it is (0..1).</summary>
        private IEnumerator Anomalies(float strength, float seconds)
        {
            int kinds = 1 + (strength > 0.5f ? 1 : 0) + (strength > 0.85f && Rng.NextDouble() < 0.5 ? 1 : 0);
            var all = new List<Kind> { Kind.Vanish, Kind.Swap, Kind.Float, Kind.Fly, Kind.FaceYou, Kind.Tremble };
            Shuffle(all);
            int n = Mathf.RoundToInt(Mathf.Lerp(2f, 9f, strength));
            Plugin.Log.LogInfo($"TheLastWitness: anomalies {string.Join(", ", all.GetRange(0, kinds))} x{n} ({Nearby(16f, true).Count} loose things near)");
            for (int i = 0; i < kinds; i++)
            {
                switch (all[i])
                {
                    case Kind.Vanish: d.Run(Vanish(n, seconds), "vanish"); break;
                    case Kind.Swap: Swap(Mathf.Max(1, n / 2)); break;
                    case Kind.Float: d.Run(Levitate(n, seconds, Rf(0.6f, 2.5f)), "float"); break;
                    case Kind.Fly: d.Run(FlyAtYou(Mathf.Max(1, n / 3)), "fly"); break;
                    case Kind.FaceYou: FaceYou(n * 2); break;
                    default: d.Run(Tremble(n * 2, seconds), "tremble"); break;
                }
            }
            yield break;
        }

        /// <summary>Things around you that you aren't looking at go missing, and come back when you look away again.</summary>
        private IEnumerator Vanish(int n, float seconds)
        {
            var list = Nearby(18f, false);
            Shuffle(list);
            var gone = new List<Renderer>();
            foreach (var r in list)
            {
                if (gone.Count >= n) break;
                if (!r.enabled || Fx.Seen(r.bounds.center, 55f) || r.bounds.extents.magnitude > 6f) continue;
                r.enabled = false;
                gone.Add(r);
            }
            if (gone.Count == 0) yield break;
            int u = Fx.Track(() => { foreach (var r in gone) if (r != null) r.enabled = true; });
            yield return new WaitForSeconds(seconds);
            // Back one by one, only behind your back.
            for (float t = 0f; t < 20f && gone.Count > 0; t += Dt)
            {
                for (int i = gone.Count - 1; i >= 0; i--)
                    if (gone[i] == null || !Fx.Seen(gone[i].bounds.center, 55f)) { if (gone[i] != null) gone[i].enabled = true; gone.RemoveAt(i); }
                yield return null;
            }
            Fx.Restore(u);
        }

        /// <summary>Pairs of loose things trade places while you aren't looking. They stay swapped.</summary>
        private void Swap(int pairs)
        {
            var list = Nearby(16f, true);
            list.RemoveAll(r => Fx.Seen(r.bounds.center, 55f) || r.bounds.SqrDistance(Head.position) < 2f * 2f);
            Shuffle(list);
            for (int i = 0; i + 1 < list.Count && i / 2 < pairs; i += 2)
            {
                Transform a = list[i].transform, b = list[i + 1].transform;
                var (pa, qa, pb, qb) = (a.position, a.rotation, b.position, b.rotation);
                a.SetPositionAndRotation(pb, qb);
                b.SetPositionAndRotation(pa, qa);
                moved.Enqueue(Fx.Track(() => { if (a != null) a.SetPositionAndRotation(pa, qa); if (b != null) b.SetPositionAndRotation(pb, qb); }));
                while (moved.Count > 20) Fx.Restore(moved.Dequeue());
                Hurt(0.25f);
            }
        }

        /// <summary>Loose things rise, hover and turn slowly, then all drop at once.</summary>
        private IEnumerator Levitate(int n, float seconds, float height)
        {
            var list = Nearby(14f, true);
            Shuffle(list);
            var lifted = new List<(Transform t, Vector3 p, Quaternion q, float h, float spin)>();
            foreach (var r in list)
            {
                if (lifted.Count >= n) break;
                if (r.bounds.SqrDistance(Head.position) < 1.5f * 1.5f) continue;
                lifted.Add((r.transform, r.transform.position, r.transform.rotation, height * Rf(0.6f, 1.3f), Rf(-40f, 40f)));
            }
            if (lifted.Count == 0) yield break;
            int u = Fx.Track(() => { foreach (var l in lifted) if (l.t != null) l.t.SetPositionAndRotation(l.p, l.q); });
            float up = Rf(1.5f, 3f);
            for (float t = 0f; t < up + seconds; t += Dt)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / up);
                foreach (var l in lifted)
                    if (l.t != null) l.t.SetPositionAndRotation(l.p + Vector3.up * (l.h * k + Mathf.Sin(t * 2f + l.spin) * 0.06f), Quaternion.Euler(0f, l.spin * t, 0f) * l.q);
                yield return null;
            }
            Fx.Restore(u);
            Fx.Play(Synth.Knocks(), Head.position, 0.6f, false, 0f);
            Hurt(0.75f);
        }

        /// <summary>A loose thing tears free and flies straight at your face, past it, and lands behind you.</summary>
        private IEnumerator FlyAtYou(int n)
        {
            var list = Nearby(12f, true);
            list.RemoveAll(r => r.bounds.extents.magnitude > 1.2f || r.bounds.SqrDistance(Head.position) < 3f * 3f);
            Shuffle(list);
            for (int i = 0; i < Mathf.Min(n, list.Count); i++)
            {
                var t = list[i].transform;
                var (p, q) = (t.position, t.rotation);
                moved.Enqueue(Fx.Track(() => { if (t != null) t.SetPositionAndRotation(p, q); }));
                while (moved.Count > 20) Fx.Restore(moved.Dequeue());
                // Shiver first, so you see which one.
                for (float k = 0f; k < 0.6f; k += Dt) { t.position = p + Random.insideUnitSphere * 0.04f; yield return null; }
                Vector3 target = Head.position + Random.insideUnitSphere * 0.25f;
                Vector3 past = target + (target - p).normalized * Rf(3f, 6f);
                if (!Fx.Ground(past, out var land)) land = past;
                Fx.Play(Synth.Breath(0.5f), p, 0.8f, false);
                Vector3 axis = Random.onUnitSphere;
                float len = Vector3.Distance(p, land), speed = Rf(14f, 20f);
                for (float x = 0f; x < len; x += speed * Dt)
                {
                    float u = x / len;
                    // through your face on a slight arc, then down to the ground behind you
                    Vector3 pos = u < 0.7f ? Vector3.Lerp(p, target, u / 0.7f) : Vector3.Lerp(target, land, (u - 0.7f) / 0.3f);
                    t.SetPositionAndRotation(pos + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.3f, Quaternion.AngleAxis(x * 120f, axis) * q);
                    yield return null;
                }
                Fx.Play(Synth.Knocks(), land, 0.9f, false);
                Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.25f);
                Hurt(0.75f);
                yield return new WaitForSeconds(Rf(0.3f, 1.2f));
            }
        }

        /// <summary>Loose things turn, all at once, to face you. They stay that way.</summary>
        private void FaceYou(int n)
        {
            var list = Nearby(18f, true);
            list.RemoveAll(r => Fx.Seen(r.bounds.center, 50f));
            Shuffle(list);
            for (int i = 0; i < Mathf.Min(n, list.Count); i++)
            {
                var t = list[i].transform;
                var q = t.rotation;
                Vector3 to = Flat(Head.position - t.position);
                if (to.sqrMagnitude < 0.01f) continue;
                t.rotation = Quaternion.LookRotation(to, Vector3.up) * Quaternion.Euler(0f, Rf(-8f, 8f), 0f);
                moved.Enqueue(Fx.Track(() => { if (t != null) t.rotation = q; }));
                while (moved.Count > 20) Fx.Restore(moved.Dequeue());
            }
            Fx.Play(Synth.Knocks(), Head.position - Flat(Head.forward) * 3f, 0.5f, false);
            Hurt(0.5f);
        }

        /// <summary>Things around you shake as if something heavy walked past.</summary>
        private IEnumerator Tremble(int n, float seconds)
        {
            var list = Nearby(12f, true);
            Shuffle(list);
            var shakers = new List<(Transform t, Vector3 p, Vector3 s)>();
            for (int i = 0; i < Mathf.Min(n, list.Count); i++) shakers.Add((list[i].transform, list[i].transform.position, list[i].transform.localScale));
            if (shakers.Count == 0) yield break;
            int u = Fx.Track(() => { foreach (var (t, p, s) in shakers) if (t != null) t.position = p; });
            Fx.Play(Synth.Boom(), Head.position + Random.onUnitSphere * 4f, 0.5f, false);
            for (float t = 0f; t < seconds; t += Dt) { Shake(shakers, 0.05f + 0.1f * Mathf.PerlinNoise(t * 4f, 0f)); yield return null; }
            Fx.Restore(u);
        }

        // ---------- presence ----------

        /// <summary>It is there: at the edge of sight, staring from afar, or right behind you.</summary>
        private IEnumerator Presence()
        {
            switch (Rng.Next(3))
            {
                case 0: yield return Glimpse(55f, 85f, Rf(7f, 14f), 6f); break;
                case 1: yield return Stare(Rf(14f, 24f), 20f); break;
                default: yield return Behind(); break;
            }
        }

        /// <summary>It stands in plain sight, still, looking at you. Look away and back, or walk to it: gone.</summary>
        private IEnumerator Stare(float dist, float life)
        {
            var h = Head;
            if (!Fx.Ground(h.position + Quaternion.Euler(0f, Rf(-35f, 35f), 0f) * Flat(h.forward) * dist, out var g)) yield break;
            float height = Entity.PlayerHeight() * Rf(1.3f, 1.7f);
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = g;
            Entity.Face(w.transform, h.position);
            bool away = false;
            for (float t = 0f; t < life; t += Dt)
            {
                bool seen = Fx.Seen(g + Vector3.up * height * 0.7f, 40f);
                if (!seen) away = true;
                if ((away && seen) || Vector3.Distance(g, Head.position) < 7f) break;
                Entity.Face(w.transform, Head.position);
                yield return null;
            }
            Fx.Static(0.4f, g, 0.6f, false);
            Fx.Restore(u);
            Hurt(2f);
        }

        /// <summary>Breathing right behind you. Turn around in time and it is there, for a moment.</summary>
        private IEnumerator Behind()
        {
            var h = Head;
            if (!Fx.Ground(h.position - Flat(h.forward) * 1.3f, out var g)) yield break;
            float height = Entity.PlayerHeight() * 1.25f;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = g;
            Entity.Face(w.transform, h.position);
            Fx.Play(Synth.Breath(2.5f), g + Vector3.up * height * 0.85f, 0.9f, false);
            for (float t = 0f; t < 4f; t += Dt)
            {
                // It stays at your back as you turn, until you catch it.
                if (Fx.Seen(w.transform.position + Vector3.up * height * 0.8f, 30f))
                {
                    Fx.Fear = Mathf.Min(1f, Fx.Fear + 0.5f);
                    yield return new WaitForSeconds(0.25f);
                    Fx.Static(0.3f, w.transform.position, 0.8f, true);
                    Hurt(3f);
                    break;
                }
                yield return null;
            }
            Fx.Restore(u);
        }

        // ---------- the five worst ----------

        /// <summary>
        /// "Estátuas": statues of it stand in a ring around you, every one looking at you. They only move when no
        /// one is looking at them. Each time you turn, they are closer. If one reaches you, it is not a statue.
        /// </summary>
        private IEnumerator Statues()
        {
            int count = 6 + Rng.Next(4 + (int)(Insanity * 4f));
            float height = Entity.PlayerHeight() * 1.35f;
            var ring = new List<(GameObject w, int u)>();
            float r0 = Rf(12f, 16f);
            for (int i = 0; i < count; i++)
            {
                float yaw = 360f / count * i + Rf(-12f, 12f);
                if (!Fx.Ground(Head.position + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * r0, out var g)) continue;
                var w = Entity.Spawn(height);
                w.transform.position = g;
                Entity.Face(w.transform, Head.position);
                w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
                ring.Add((w, Own(w)));
            }
            Plugin.Log.LogInfo($"TheLastWitness: statues {ring.Count}");
            if (ring.Count == 0) yield break;
            Fx.Play(Synth.Thunder(), Head.position, 0.8f, true, 0f);
            var msg = Screen(0.05f, new Color(0.85f, 0.82f, 0.78f));
            int um = Own(msg.gameObject);
            msg.text = Fx.Safe("NÃO TIRE OS OLHOS DELES.");
            var scrape = Synth.Knocks();
            float speed = 2.2f + Insanity * 2.5f, dur = Rf(30f, 40f);
            for (float t = 0f; t < dur; t += Dt)
            {
                if (t < 3f) Follow(msg, 0.05f); else if (msg.gameObject.activeSelf) msg.gameObject.SetActive(false);
                foreach (var (w, u) in ring)
                {
                    var p = w.transform.position;
                    if (Fx.Seen(p + Vector3.up * height * 0.8f, 45f)) continue;
                    Vector3 to = Flat(Head.position - p);
                    Vector3 np = p + to * speed * Dt;
                    if (Fx.Ground(np, out var g)) np = g;
                    w.transform.position = np;
                    Entity.Face(w.transform, Head.position);
                    if (Rng.NextDouble() < 0.004) Fx.Play(scrape, np, 0.5f, false);
                    if (Vector3.ProjectOnPlane(np - Head.position, Vector3.up).magnitude < 1.3f)
                    {
                        foreach (var (o, ou) in ring) if (o != w) Fx.Restore(ou);
                        Fx.Restore(um);
                        yield return Scare(w, u, height);
                        yield break;
                    }
                }
                Fx.Fear = Mathf.Max(Fx.Fear, 0.4f);
                yield return null;
            }
            // You held them off: all gone at once, with one sound.
            Fx.Static(0.5f, Head.position, 0.7f, false, 0f);
            foreach (var (_, u) in ring) Fx.Restore(u);
            Fx.Restore(um);
            Hurt(6f);
        }

        /// <summary>
        /// "Arrastado": it stands ahead of you, arm out, and you are pulled to it in jerks, whatever you do. Every pull
        /// tears the picture. At the end, either it lets go (black, silence) or it has you.
        /// </summary>
        private IEnumerator Drag()
        {
            var p = Fx.Player;
            if (p == null) yield break;
            var h = Head;
            if (!Fx.Ground(h.position + Flat(h.forward) * Rf(10f, 14f), out var g)) yield break;
            float height = Entity.PlayerHeight() * 1.5f;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = g;
            Entity.Face(w.transform, h.position);
            w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Reach;
            var growl = Loop(w.transform, Synth.Growl(3f), 0.9f);
            int ug = Own(growl);
            int ux = Fx.Track(() => { Fx.Glitch = 0f; });
            Fx.Play(Synth.Screech(1.2f), g + Vector3.up * height, 0.9f, true);
            yield return new WaitForSeconds(1.2f);
            while (true)
            {
                Vector3 me = p.transform.position;
                Vector3 to = Flat(w.transform.position - me);
                float dist = Vector3.ProjectOnPlane(w.transform.position - Head.position, Vector3.up).magnitude;
                if (dist < 1.6f) break;
                // A jerk towards it: a teleport step on the ground, with the picture torn.
                Vector3 step = me + to * Mathf.Min(dist - 1.4f, Rf(0.6f, 1.4f));
                // keep the body the same height above the ground as it is now
                if (Fx.Ground(me, out var under) && Fx.Ground(step + Vector3.up * 0.5f, out var sg)) step = sg + Vector3.up * (me.y - under.y);
                p.TeleportTo(step, p.transform.rotation, false, false);
                Fx.Glitch = 0.8f;
                Fx.Play(Synth.Knocks(), Head.position, 0.6f, false, 0f);
                yield return new WaitForSeconds(0.08f);
                Fx.Glitch = 0f;
                Entity.Face(w.transform, Head.position);
                Hurt(1f);
                yield return new WaitForSeconds(Rf(0.25f, 0.6f));
            }
            Fx.Restore(ux);
            Fx.Restore(ug);
            if (Rng.NextDouble() < 0.4 + Insanity * 0.5) { yield return Scare(w, u, height); yield break; }
            // It lets go.
            Fx.Restore(u);
            int um = Fx.Track(() => { Fx.Muted = false; Fx.Blackout = 0f; });
            Fx.Blackout = 1f;
            Fx.Muted = true;
            yield return new WaitForSeconds(1.5f);
            Fx.Muted = false;
            for (float t = 0f; t < 1f; t += Dt) { Fx.Blackout = 1f - t; yield return null; }
            Fx.Restore(um);
            Hurt(5f);
        }

        /// <summary>
        /// "Poltergeist": everything loose around you tears free and circles you, faster and higher, while it stands
        /// in the middle distance lit by red flashes. Then all of it comes at you at once.
        /// </summary>
        private IEnumerator Poltergeist()
        {
            var list = Nearby(18f, true);
            list.RemoveAll(r => r.bounds.extents.magnitude > 1.5f);
            Shuffle(list);
            if (list.Count > 40) list.RemoveRange(40, list.Count - 40);
            Plugin.Log.LogInfo($"TheLastWitness: poltergeist {list.Count} objects");
            if (list.Count < 3) { yield return Levitate(6, 6f, 1.5f); yield break; }
            var orbit = new List<(Transform t, Vector3 p, Quaternion q, float r, float a, float y, Vector3 spin)>();
            foreach (var r in list) orbit.Add((r.transform, r.transform.position, r.transform.rotation, Rf(2.5f, 6f), Rf(0f, 360f), Rf(-0.5f, 2.5f), Random.onUnitSphere * Rf(60f, 200f)));
            int u = Fx.Track(() => { foreach (var o in orbit) if (o.t != null) o.t.SetPositionAndRotation(o.p, o.q); });
            var wind = Loop(Head, Synth.Static(2f), 0.4f);
            int uw = Own(wind);
            float height = Entity.PlayerHeight() * 1.5f;
            GameObject w = null;
            int ue = 0;
            if (Fx.Ground(Head.position + Flat(Head.forward) * Rf(9f, 13f), out var g))
            {
                w = Entity.Spawn(height);
                ue = Own(w);
                w.transform.position = g;
                Entity.Face(w.transform, Head.position);
            }
            int ur = Fx.Track(() => Fx.RedTint = 0f);
            float spinUp = Rf(8f, 12f);
            Vector3 center = Head.position;
            for (float t = 0f; t < spinUp; t += Dt)
            {
                float k = t / spinUp, lift = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t / 2f));
                center = Vector3.Lerp(center, Head.position, Dt * 2f);
                for (int i = 0; i < orbit.Count; i++)
                {
                    var o = orbit[i];
                    if (o.t == null) continue;
                    float ang = (o.a + t * Mathf.Lerp(30f, 260f, k)) * Mathf.Deg2Rad;
                    Vector3 ring = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * o.r * Mathf.Lerp(1f, 0.7f, k) + Vector3.up * (o.y + k * 1.5f);
                    o.t.SetPositionAndRotation(Vector3.Lerp(o.p, ring, lift), Quaternion.Euler(o.spin * t) * o.q);
                }
                if (w != null) Entity.Face(w.transform, Head.position);
                Fx.RedTint = Rng.NextDouble() < 0.04 + k * 0.1 ? 0.8f : 0f;
                Fx.Fear = Mathf.Max(Fx.Fear, k);
                yield return null;
            }
            // All at once, at you.
            Fx.RedTint = 1f;
            Fx.Play(Synth.ScreamChoir(1.2f), Head.position, 1f, true, 0f);
            var starts = new List<Vector3>();
            foreach (var o in orbit) starts.Add(o.t != null ? o.t.position : Vector3.zero);
            for (float t = 0f; t < 0.5f; t += Dt)
            {
                for (int i = 0; i < orbit.Count; i++) if (orbit[i].t != null) orbit[i].t.position = Vector3.Lerp(starts[i], Head.position + Random.insideUnitSphere * 0.3f, t / 0.5f);
                yield return null;
            }
            Fx.Flash(Color.white, 0.25f);
            Fx.Play(Synth.Boom(), Head.position, 1f, true, 0f);
            // They fall where they are, scattered around you, and stay there.
            for (int i = 0; i < orbit.Count; i++)
            {
                var o = orbit[i];
                if (o.t == null) continue;
                if (Fx.Ground(Head.position + Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward * Rf(1.5f, 5f), out var land))
                    o.t.SetPositionAndRotation(land, Quaternion.Euler(Rf(0f, 360f), Rf(0f, 360f), Rf(0f, 360f)));
            }
            Fx.Restore(uw);
            if (ue != 0) Fx.Restore(ue);
            for (float t = 0f; t < 1.5f; t += Dt) { Fx.RedTint = 1f - t / 1.5f; yield return null; }
            Fx.Restore(ur);
            moved.Enqueue(u);
            while (moved.Count > 20) Fx.Restore(moved.Dequeue());
            Hurt(8f);
        }

        /// <summary>
        /// "Enforcados": hanged shapes of it appear under the branches all around you, ropes creaking, slowly turning
        /// to face you, one after another. Then the one right above you drops.
        /// </summary>
        private IEnumerator Hanged()
        {
            var head = Head;
            float h = Entity.PlayerHeight() * 1.1f;
            var hung = new List<(GameObject w, int u, float phase)>();
            for (int tries = 0; tries < 40 && hung.Count < 5 + Rng.Next(4); tries++)
            {
                Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
                if (!Fx.Floor(head.position + dir * Rf(4f, 14f), out var g)) continue;
                if (!Physics.Raycast(g + Vector3.up * 0.5f, Vector3.up, out var branch, 9f, Fx.World, QueryTriggerInteraction.Ignore) || branch.distance < 2.2f) continue;
                if (Fx.Seen(branch.point, 50f)) continue;
                var w = Entity.Spawn(h);
                w.transform.position = branch.point - Vector3.up * 0.1f;
                hung.Add((w, Own(w), Rf(0f, 10f)));
            }
            Plugin.Log.LogInfo($"TheLastWitness: hanged {hung.Count}");
            if (hung.Count == 0) { yield return Glimpse(60f, 120f, 6f, 6f); yield break; }
            var creak = Loop(hung[0].w.transform, Synth.Hum(), 0.4f);
            int uc = Own(creak);
            Fx.Play(Synth.DistantScream(), hung[0].w.transform.position, 0.6f, false);
            float dur = Rf(12f, 18f);
            for (float t = 0f; t < dur; t += Dt)
            {
                for (int i = 0; i < hung.Count; i++)
                {
                    var (w, _, ph) = hung[i];
                    if (w == null) continue;
                    // They turn to you one after another, swaying.
                    float turn = Mathf.Clamp01((t - i * dur / (hung.Count + 1)) / 2f);
                    Vector3 to = Flat(Head.position - w.transform.position);
                    float yawTo = to.sqrMagnitude > 0.01f ? Quaternion.LookRotation(to).eulerAngles.y : 0f;
                    float yaw = Mathf.LerpAngle(ph * 36f + t * 4f, yawTo, turn);
                    w.transform.rotation = Quaternion.Euler(Mathf.Sin(t * 0.8f + ph) * 7f, yaw, 180f);
                }
                Fx.Fear = Mathf.Max(Fx.Fear, t / dur);
                yield return null;
            }
            Fx.Restore(uc);
            foreach (var (_, u, _) in hung) Fx.Restore(u);
            // The one right above you drops.
            float height = Entity.PlayerHeight() * 1.3f;
            var drop = Entity.Spawn(height);
            int ud = Own(drop);
            drop.transform.SetPositionAndRotation(Head.position + Flat(Head.forward) * 0.6f + Vector3.up * 2.2f, Quaternion.Euler(0f, Quaternion.LookRotation(-Flat(Head.forward)).eulerAngles.y, 180f));
            yield return Scare(drop, ud, height, 0.08f, true);
        }

        /// <summary>
        /// "Caçada no escuro": the lights go, it is pitch black, and somewhere out there two red eyes. "NÃO SE MEXA."
        /// It only comes when you move. Hold still until the dark lifts, or it finds you.
        /// </summary>
        private IEnumerator DarkHunt()
        {
            if (!Fx.SpotOutOfView(14f, 20f, out var g, 40f) && !Fx.Ground(Head.position + Flat(Head.forward) * 16f, out g)) yield break;
            float height = Entity.PlayerHeight() * 1.4f;
            var w = Entity.Spawn(height);
            int u = Own(w);
            w.transform.position = g;
            Entity.Face(w.transform, Head.position);
            var lamps = Ambience.Ready ? Ambience.LampsFarFirst(60f) : new List<Ambience.Lamp>();
            int ul = Fx.Track(() => { foreach (var l in lamps) l.Dead = false; Fx.Blackout = 0f; });
            foreach (var l in lamps) l.Dead = true;
            Fx.Play(Synth.Boom(), Head.position, 0.8f, true, 0f);
            for (float t = 0f; t < 0.6f; t += Dt) { Fx.Blackout = t / 0.6f * 0.93f; yield return null; }
            var msg = Screen(0.05f, new Color(0.85f, 0.08f, 0.06f));
            int um = Own(msg.gameObject);
            msg.text = Fx.Safe("NÃO SE MEXA.");
            var steps = Loop(w.transform, Synth.Footsteps(10, 0.5f), 0f);
            var stepSrc = steps.GetComponent<AudioSource>();
            Vector3 last = Head.position;
            Quaternion lastRot = Head.rotation;
            float dur = Rf(22f, 30f), moving = 0f;
            for (float t = 0f; t < dur; t += Dt)
            {
                if (t < 2.5f) Follow(msg, 0.05f); else if (msg.gameObject.activeSelf) msg.gameObject.SetActive(false);
                Fx.Blackout = 0.93f;
                // How much you moved: your head, and your hands' swings count too.
                float motion = (Head.position - last).magnitude / Mathf.Max(Dt, 0.001f) + Quaternion.Angle(Head.rotation, lastRot) / Mathf.Max(Dt, 0.001f) * 0.01f;
                last = Head.position;
                lastRot = Head.rotation;
                moving = Mathf.Lerp(moving, motion > 0.35f ? 1f : 0f, Dt * 4f);
                Vector3 to = Flat(Head.position - w.transform.position);
                float dist = Vector3.ProjectOnPlane(Head.position - w.transform.position, Vector3.up).magnitude;
                if (moving > 0.2f)
                {
                    Vector3 np = w.transform.position + to * (2f + 4f * moving + Insanity * 2f) * Dt;
                    if (Fx.Ground(np, out var gg)) np = gg;
                    w.transform.position = np;
                    w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Run;
                }
                else w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
                Entity.Face(w.transform, Head.position);
                stepSrc.volume = Mathf.Clamp01(moving) * Settings.Volume.Value;
                Fx.Fear = Mathf.Max(Fx.Fear, Mathf.InverseLerp(16f, 2f, dist));
                if (dist < 1.3f)
                {
                    Fx.Restore(um);
                    Fx.Blackout = 0f;
                    yield return Scare(w, u, height);
                    Fx.Restore(ul);
                    yield break;
                }
                yield return null;
            }
            // You held still. It leaves, and says so.
            Fx.Restore(u);
            Fx.Restore(um);
            Fx.Play(Synth.Whisper(1.5f), Head.position + Head.right * 0.3f, 0.8f, false, 1f, Head);
            for (float t = 0f; t < 1.5f; t += Dt) { Fx.Blackout = 0.93f * (1f - t / 1.5f); yield return null; }
            Fx.Restore(ul);
            yield return Flash("EU AINDA TE VEJO", 1.5f);
            Hurt(5f);
        }

        // ---------- tapes ----------

        /// <summary>While a tape plays, and right after: each tape has its own haunting.</summary>
        private IEnumerator TapeHaunt(int index, System.Func<bool> reading)
        {
            switch (index)
            {
                case 0: // it starts small: things turn to you, some go missing, and it is out there
                    FaceYou(10);
                    d.Run(Vanish(4, 8f), "tape vanish");
                    while (reading()) yield return null;
                    yield return Stare(Rf(16f, 22f), 15f);
                    break;
                case 1: // "Mas ele ouve...": it comes up behind you while you read
                    d.Run(Tremble(10, 6f), "tape tremble");
                    while (reading()) yield return null;
                    yield return Behind();
                    break;
                case 2: // the lamps die one by one as the words come, and eyes open in the dark
                {
                    var lamps = Ambience.Ready ? Ambience.LampsFarFirst(40f) : new List<Ambience.Lamp>();
                    int ul = Fx.Track(() => { foreach (var l in lamps) l.Dead = false; });
                    int i = 0;
                    while (reading()) { if (i < lamps.Count && Rng.NextDouble() < 0.02) lamps[i++].Dead = true; yield return null; }
                    yield return Eyes();
                    Fx.Restore(ul);
                    break;
                }
                case 3: // everything lifts while it plays, and drops when it ends; something runs past
                    d.Run(Levitate(12, 30f, 1.8f), "tape float");
                    while (reading()) yield return null;
                    yield return Runner();
                    break;
                case 4: // statues gather around you while you read, nearer each time you look up
                    yield return TapeStatues(reading);
                    break;
                case 5: // the world turns red and shakes; then it pulls you
                    d.Run(Tremble(14, 12f), "tape tremble");
                    int ur = Fx.Track(() => Fx.RedTint = 0f);
                    for (float t = 0f; reading(); t += Dt) { Fx.RedTint = Mathf.Min(0.7f, t / 6f); yield return null; }
                    Fx.Restore(ur);
                    if (!Settings.NoMovement.Value) yield return Drag();
                    else yield return Stare(10f, 10f);
                    break;
                case 6: // the poltergeist
                    while (reading()) { if (Rng.NextDouble() < 0.01) d.Run(FlyAtYou(1), "tape fly"); yield return null; }
                    yield return Poltergeist();
                    break;
                default: // the last tape: things vanish all around, and the hanged turn to you (then the finale)
                    d.Run(Vanish(14, 20f), "tape vanish");
                    while (reading()) yield return null;
                    break;
            }
        }

        /// <summary>Test hook: one tape's haunting as if the tape played for seconds.</summary>
        public void TestTapeHaunt(int index, float seconds)
        {
            float end = Time.time + seconds;
            d.Run(TapeHaunt(index, () => Time.time < end), "test tape haunt " + index);
        }

        /// <summary>A smaller "Estátuas" that only gathers while a tape plays, then is gone.</summary>
        private IEnumerator TapeStatues(System.Func<bool> reading)
        {
            float height = Entity.PlayerHeight() * 1.3f;
            var ring = new List<(GameObject w, int u)>();
            for (int i = 0; i < 4; i++)
            {
                if (!Fx.Ground(Head.position + Quaternion.Euler(0f, 90f * i + Rf(-20f, 20f), 0f) * Vector3.forward * Rf(9f, 12f), out var g)) continue;
                var w = Entity.Spawn(height);
                w.transform.position = g;
                Entity.Face(w.transform, Head.position);
                w.GetComponent<WitnessAnim>().Current = WitnessAnim.Mode.Tilt;
                ring.Add((w, Own(w)));
            }
            while (reading())
            {
                foreach (var (w, _) in ring)
                {
                    var p = w.transform.position;
                    if (Fx.Seen(p + Vector3.up * height * 0.8f, 45f) || Vector3.ProjectOnPlane(p - Head.position, Vector3.up).magnitude < 3f) continue;
                    Vector3 np = p + Flat(Head.position - p) * 1.5f * Dt;
                    if (Fx.Ground(np, out var g)) np = g;
                    w.transform.position = np;
                    Entity.Face(w.transform, Head.position);
                }
                yield return null;
            }
            yield return new WaitForSeconds(2f);
            Fx.Static(0.4f, Head.position, 0.6f, false, 0f);
            foreach (var (_, u) in ring) Fx.Restore(u);
        }
    }
}
