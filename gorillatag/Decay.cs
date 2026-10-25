using System.Collections.Generic;
using GorillaLocomotion;
using TMPro;
using UnityEngine;
using static TheLastWitness.Director;

namespace TheLastWitness
{
    /// <summary>
    /// The world rotting: corrupt materials (missing texture, void, flesh, static), black glitching cubes hanging
    /// in the air like holes in the world, and the "Mundo corrompido" layer that keeps part of everything around you
    /// corrupted once sanity is low. Every renderer's own materials are kept once, with a count of who is
    /// corrupting it, so overlapping corruptions always heal back to the original.
    /// </summary>
    public static class Decay
    {
        private static Material[] mats;
        private static readonly Dictionary<Renderer, (Material[] orig, int holds)> state = new Dictionary<Renderer, (Material[], int)>();
        private static readonly List<Renderer> held = new List<Renderer>();
        private static readonly List<GameObject> voids = new List<GameObject>();
        private static float nextRoll;

        public static Material[] Mats
        {
            get
            {
                if (mats != null) return mats;
                var unlit = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("UI/Default");
                Material Make(Texture2D tex, Color tint)
                {
                    var m = new Material(unlit) { color = tint };
                    m.mainTexture = tex;
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                    return m;
                }
                Texture2D Tex(System.Func<int, int, Color32> px)
                {
                    const int S = 32;
                    var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                    var c = new Color32[S * S];
                    for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) c[y * S + x] = px(x, y);
                    t.SetPixels32(c);
                    t.Apply();
                    return t;
                }
                var r = new System.Random(Rng.Next());
                mats = new[]
                {
                    Make(Tex((x, y) => ((x / 4 + y / 4) & 1) == 0 ? new Color32(255, 0, 255, 255) : new Color32(0, 0, 0, 255)), Color.white), // missing texture
                    Make(Tex((x, y) => new Color32(0, 0, 0, 255)), Color.black), // void
                    Make(Tex((x, y) => { byte g = (byte)r.Next(256); return new Color32(g, (byte)(g / 6), (byte)(g / 8), 255); }), new Color(0.7f, 0.1f, 0.1f)), // flesh
                    Make(Tex((x, y) => { byte g = (byte)r.Next(256); return new Color32(g, g, g, 255); }), Color.white), // static
                    Make(Tex((x, y) => y % 4 < 2 ? new Color32((byte)r.Next(256), 0, (byte)r.Next(256), 255) : new Color32(0, (byte)r.Next(256), 0, 255)), Color.white), // torn scanlines
                };
                return mats;
            }
        }

        public static Material Any => Mats[Rng.Next(Mats.Length)];

        /// <summary>Scene renderers near you. movable: small loose things only (static-batched meshes can't move).</summary>
        public static List<Renderer> Nearby(Vector3 at, float maxDist, bool movable)
        {
            var list = new List<Renderer>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                if ((b.ClosestPoint(at) - at).sqrMagnitude > maxDist * maxDist) continue;
                string root = r.transform.root.name;
                if (root.StartsWith("TLW") || root == "TheLastWitness") continue;
                if (r.GetComponent<TMP_Text>() != null || r.GetComponentInParent<VRRig>() != null || r.GetComponentInParent<GTPlayer>() != null) continue;
                if (movable)
                {
                    float ext = b.extents.magnitude;
                    if (r.isPartOfStaticBatch || ext < 0.08f || ext > 1.5f || r.transform.childCount > 8) continue;
                    var rb = r.GetComponentInParent<Rigidbody>();
                    if (rb != null && !rb.isKinematic) continue;
                }
                else if (b.extents.magnitude > 30f) continue;
                list.Add(r);
            }
            return list;
        }

        public static void Shuffle<T>(List<T> l)
        {
            for (int i = l.Count - 1; i > 0; i--)
            {
                int j = Rng.Next(i + 1);
                (l[i], l[j]) = (l[j], l[i]);
            }
        }

        /// <summary>Corrupt r with m (one more holder). Heal it as many times to get the original back.</summary>
        public static void Taint(Renderer r, Material m)
        {
            if (r == null) return;
            if (!state.TryGetValue(r, out var s)) s = (r.sharedMaterials, 0);
            state[r] = (s.orig, s.holds + 1);
            Swap(r, m);
        }

        /// <summary>Show another corrupt material on an already corrupted renderer (flicker).</summary>
        public static void Swap(Renderer r, Material m)
        {
            if (r == null || !state.TryGetValue(r, out var s)) return;
            var a = new Material[s.orig.Length];
            for (int i = 0; i < a.Length; i++) a[i] = m;
            r.sharedMaterials = a;
        }

        public static void Heal(Renderer r)
        {
            if (r == null || !state.TryGetValue(r, out var s)) { if (r == null) Prune(); return; }
            if (s.holds > 1) { state[r] = (s.orig, s.holds - 1); return; }
            r.sharedMaterials = s.orig;
            state.Remove(r);
        }

        private static void Prune()
        {
            var dead = new List<Renderer>();
            foreach (var k in state.Keys) if (k == null) dead.Add(k);
            foreach (var k in dead) state.Remove(k);
        }

        public static void HealAll()
        {
            foreach (var kv in state) if (kv.Key != null) kv.Key.sharedMaterials = kv.Value.orig;
            state.Clear();
            held.Clear();
            foreach (var v in voids) if (v != null) Object.Destroy(v);
            voids.Clear();
        }

        /// <summary>A hole in the world: a cube of corruption hanging in the air, slowly turning and glitching.</summary>
        public static GameObject Void(Vector3 pos, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "TLW void";
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.position = pos;
            go.transform.rotation = Random.rotation;
            go.transform.localScale = new Vector3(Rf(0.4f, 1.6f), Rf(0.4f, 1.6f), Rf(0.4f, 1.6f)) * size;
            go.GetComponent<MeshRenderer>().sharedMaterial = Rng.NextDouble() < 0.5 ? Mats[1] : Any;
            go.AddComponent<VoidSpin>();
            return go;
        }

        /// <summary>A spot near the surface of something around you, for a void to hang at.</summary>
        public static bool SpotNear(List<Renderer> around, out Vector3 p)
        {
            p = Vector3.zero;
            if (around.Count == 0) return false;
            var b = around[Rng.Next(around.Count)].bounds;
            p = b.center + Vector3.Scale(b.extents, Random.insideUnitSphere) * 1.2f;
            return Fx.Head != null && (p - Fx.Head.position).sqrMagnitude > 2.5f * 2.5f;
        }

        // ---------- "Mundo corrompido": the layer ----------

        public static void Tick(float ins, bool active)
        {
            bool want = active && Settings.IsOn("corrompido") && ins > 0.3f && Fx.Head != null;
            if (!want)
            {
                if (held.Count > 0 || voids.Count > 0) ClearLayer();
                return;
            }
            // A few of the corrupted things flicker between corruptions every frame.
            for (int i = 0; i < 3 && held.Count > 0; i++) Swap(held[Rng.Next(held.Count)], Any);
            if (Time.time < nextRoll) return;
            nextRoll = Time.time + Rf(12f, 22f);
            ClearLayer();
            float k = Mathf.InverseLerp(0.3f, 1f, ins);
            var around = Nearby(Fx.Head.position, 25f, false);
            Shuffle(around);
            int n = (int)(around.Count * k * 0.3f);
            for (int i = 0; i < n; i++)
            {
                Taint(around[i], Any);
                held.Add(around[i]);
            }
            int nv = (int)(k * 20f);
            for (int i = 0; i < nv; i++)
                if (SpotNear(around, out var p)) voids.Add(Void(p, Rf(0.3f, 1.4f)));
        }

        private static void ClearLayer()
        {
            foreach (var r in held) Heal(r);
            held.Clear();
            foreach (var v in voids) if (v != null) Object.Destroy(v);
            voids.Clear();
        }
    }

    /// <summary>Turns a void cube slowly and makes it skip: a jump in place, a flicker of another corruption.</summary>
    public class VoidSpin : MonoBehaviour
    {
        private Vector3 axis, home;
        private float speed;
        private MeshRenderer mr;

        private void Start()
        {
            axis = Random.onUnitSphere;
            speed = Random.Range(5f, 40f);
            home = transform.position;
            mr = GetComponent<MeshRenderer>();
        }

        private void Update()
        {
            transform.Rotate(axis, speed * Time.deltaTime, Space.World);
            if (Random.value < 0.02f) transform.position = home + Random.insideUnitSphere * 0.3f;
            else if (Random.value < 0.05f) transform.position = home;
            if (Random.value < 0.03f) mr.sharedMaterial = Decay.Any;
        }
    }
}
