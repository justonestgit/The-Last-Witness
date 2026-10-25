using System.Collections.Generic;
using UnityEngine;
using static TheLastWitness.Director;

namespace TheLastWitness
{
    /// <summary>
    /// The lighting of the horror, on Gorilla Tag's own Ghost Reactor lighting (see Lights): the night gets
    /// truly black as sanity falls and the world drains of colour; buzzing lamp posts and the map's own lamps
    /// and candles are the only light, and they flicker, die, or turn red; a flashlight in the right hand
    /// lights where you point; and whatever the Witness is near goes dark.
    /// </summary>
    public static class Ambience
    {
        public class Lamp
        {
            public GameObject Go;
            public GameLight G;
            public float Base;
            public Color Color;
            public bool Dead;
            public float FlickerUntil;
            public AudioSource Hum;
            public Material Bulb;
        }

        public static readonly List<Lamp> Lamps = new List<Lamp>();
        public static float Lightning, Alarm;
        /// <summary>"Hora vermelha": a steady blood-red world, lamps red and failing.</summary>
        public static float Blood;
        public static bool Ready => Lights.On && built;

        private static GameLight flashlight;
        private static bool built;
        private static Vector3 center;
        private static Vector3 threat;
        private static float threatUntil;
        private static AudioClip hum;
        private static Material pole;

        /// <summary>Events call this every frame while the Witness is out: lights near it die.</summary>
        public static void SetThreat(Vector3 p)
        {
            threat = p;
            threatUntil = Time.time + 0.3f;
        }

        private static float ThreatDistance(Vector3 p) => Time.time < threatUntil ? Vector3.Distance(p, threat) : 999f;

        public static void Tick(float ins, bool active)
        {
            bool want = active && Settings.IsOn("escuro") && Lights.Available;
            if (!want)
            {
                if (Lights.On) Teardown();
                return;
            }
            var head = Fx.Head;
            if (!Lights.On) Lights.Enable(true);
            // Forced day ("dia e noite trocados"): the map's own midday lightmaps, not a flat grey ambient.
            Lights.Suspended = Fx.ForceDay;
            if (!built || (head.position - center).sqrMagnitude > 70f * 70f) Rebuild(head.position);

            // The night: moonlit at full sanity, near-black when it's gone. Comfort's darkness cap keeps a floor.
            float floor = (1f - Mathf.Clamp(Settings.MaxDarkness.Value, 0.5f, 0.97f)) * 0.2f;
            Color amb = Color.Lerp(new Color(0.09f, 0.1f, 0.15f), new Color(0.008f, 0.006f, 0.01f), Mathf.Pow(ins, 0.7f));
            amb = new Color(Mathf.Max(amb.r, floor), Mathf.Max(amb.g, floor), Mathf.Max(amb.b, floor * 1.2f));
            if (Fx.ForceDay) amb = new Color(0.95f, 0.92f, 0.85f);
            float red = Mathf.Max(Fx.RedTint, Alarm * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 1.6f)));
            amb = Color.Lerp(amb, new Color(0.7f, 0.03f, 0.02f), red);
            amb = Color.Lerp(amb, new Color(0.95f, 0.02f, 0.01f), Blood);
            amb = Color.Lerp(amb, new Color(1.1f, 1.2f, 1.5f), Lightning);
            amb *= 1f - Fx.Blackout;
            // The night breathes: when tension is high the dark swells and recedes, slowly.
            amb *= 1f - 0.45f * Director.Tension * (0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f * Mathf.Lerp(0.15f, 0.5f, Director.Tension)));
            Lights.Ambient(amb);
            float drain = Fx.ForceDay ? 0f : Mathf.InverseLerp(0.15f, 1f, ins) * 0.9f;
            Lights.Tint(Mathf.Max(drain, Blood * 0.8f), Color.Lerp(new Color(0.85f, 0.5f, 0.45f), new Color(1f, 0.15f, 0.1f), Blood));
            Lights.Keep(flashlight != null || Time.time < threatUntil);

            bool lampsOn = Settings.IsOn("postes");
            float vol = Settings.Volume.Value;
            for (int i = Lamps.Count - 1; i >= 0; i--)
            {
                var l = Lamps[i];
                if (l.Go == null || l.G == null) { Lamps.RemoveAt(i); continue; }
                float k = 1f;
                float td = ThreatDistance(l.Go.transform.position);
                if (l.Dead || !lampsOn || Fx.Blackout > 0.5f || td < 5f) k = 0f;
                else
                {
                    // A failing bulb: always a slight uneven wobble, and now and then it stutters (more when it is near).
                    float chance = Mathf.Lerp(0.02f, 0.5f, ins) + (td < 12f ? 3f : 0f) + Blood * 4f;
                    k = 0.82f + 0.18f * Mathf.PerlinNoise(Time.time * 2.5f, i * 7.3f);
                    if (Time.time < l.FlickerUntil) k = Mathf.PerlinNoise(Time.time * 30f, i * 3.1f) > 0.55f ? k : Rf(0f, 0.15f);
                    else if (Rng.NextDouble() < chance * Time.deltaTime) l.FlickerUntil = Time.time + Rf(0.2f, 1.5f);
                }
                l.G.light.intensity = l.Base * k;
                l.G.light.color = Color.Lerp(l.Color, new Color(1f, 0.05f, 0.02f), Mathf.Max(Alarm, Blood));
                if (l.Bulb != null) l.Bulb.color = new Color(l.G.light.color.r, l.G.light.color.g, l.G.light.color.b, 0.15f + 0.85f * k);
                if (l.Hum != null) l.Hum.volume = 0.3f * k * vol * (Time.time < l.FlickerUntil ? 2f : 1f);
            }
            Flashlight(ins);
        }

        // ---------- flashlight ----------

        private static void Flashlight(float ins)
        {
            bool want = Settings.IsOn("lanterna");
            if (!want)
            {
                if (flashlight != null) Lights.Remove(flashlight);
                flashlight = null;
                return;
            }
            if (flashlight == null)
            {
                flashlight = Lights.Add(Fx.Head.position, new Color(1f, 0.93f, 0.8f), 2.5f, 5f);
                flashlight.isHighPriorityPlayerLight = true;
            }
            var hand = Fx.Player != null ? Fx.Player.GetControllerTransform(false) : null;
            var from = hand != null ? hand : Fx.Head;
            float s = Fx.Player != null ? Fx.Player.scale : 1f;
            // A point light where the beam lands: up to 1.5 m ahead, short of the first wall.
            float reach = 1.5f * s;
            if (Physics.Raycast(from.position, from.forward, out var hit, reach, Fx.World, QueryTriggerInteraction.Ignore)) reach = Mathf.Max(0.2f, hit.distance - 0.15f);
            flashlight.transform.position = from.position + from.forward * reach;
            float k = 1f;
            float td = ThreatDistance(from.position);
            if (Fx.Blackout > 0.5f || td < 4f) k = 0f;
            else if ((td < 14f || ins > 0.6f) && Rng.NextDouble() < (td < 14f ? 0.25 : ins * 0.06)) k = Rf(0f, 0.4f);
            flashlight.light.intensity = Mathf.Lerp(2.5f, 1.6f, ins) * k;
        }

        // ---------- lamps ----------

        // GT lights per vertex (_ZONE_DYNAMIC_LIGHTS__CUSTOMVERTEX): the forest floor's big triangles need wide lights.
        private static readonly string[] LampNames = { "lamp", "lantern", "candle", "torch", "bulb", "fire", "lightbulb" };

        /// <summary>Lamps around where you are: the map's own lamps and candles, plus buzzing posts in the open.</summary>
        private static void Rebuild(Vector3 at)
        {
            ClearLamps();
            center = at;
            built = true;
            if (hum == null) hum = Synth.Hum();

            var found = new List<Vector3>();
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || r.bounds.size.magnitude > 3f) continue;
                if ((r.bounds.center - at).sqrMagnitude > 60f * 60f || r.transform.root.name.StartsWith("TLW")) continue;
                string n = r.name.ToLowerInvariant();
                bool lamp = false;
                foreach (var k in LampNames) if (n.Contains(k)) lamp = true;
                if (!lamp) continue;
                Vector3 p = r.bounds.center;
                if (found.Exists(f => (f - p).sqrMagnitude < 1.5f * 1.5f)) continue;
                found.Add(p);
            }
            found.Sort((a, b) => (a - at).sqrMagnitude.CompareTo((b - at).sqrMagnitude));
            int mapLamps = Mathf.Min(found.Count, 16);
            for (int i = 0; i < mapLamps; i++) AddLamp(found[i], new Color(1f, 0.6f, 0.3f), 1.8f, 6f, null);

            int posts = 0;
            var placed = new List<Vector3>();
            float h = Entity.PlayerHeight() * 2.6f;
            for (int tries = 0; tries < 120 && posts < 10; tries++)
            {
                Vector3 dir = Quaternion.Euler(0f, Rf(0f, 360f), 0f) * Vector3.forward;
                if (!Fx.Floor(at + dir * Rf(6f, 40f), out var g)) continue;
                if (placed.Exists(p => (p - g).sqrMagnitude < 8f * 8f)) continue;
                if (Physics.Raycast(g + Vector3.up * 0.2f, Vector3.up, h, Fx.World, QueryTriggerInteraction.Ignore)) continue; // needs headroom
                placed.Add(g);
                Post(g, h);
                posts++;
            }
            Plugin.Log.LogInfo($"TheLastWitness: lighting: {mapLamps} map lamps, {posts} posts");
        }

        private static void Post(Vector3 ground, float h)
        {
            var root = new GameObject("TLW post");
            root.transform.position = ground;
            if (pole == null) pole = Fx.Lit(new Color(0.25f, 0.24f, 0.26f)); // lit by its own lamp and the flashlight, gone in the dark
            var stick = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stick.transform.SetParent(root.transform, false);
            stick.transform.localScale = new Vector3(0.07f * h / 2.6f, h / 2f, 0.07f * h / 2.6f);
            stick.transform.localPosition = Vector3.up * h / 2f;
            stick.GetComponent<MeshRenderer>().sharedMaterial = pole;
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(arm.GetComponent<Collider>());
            arm.transform.SetParent(root.transform, false);
            arm.transform.localScale = new Vector3(0.05f, 0.05f, 0.35f) * h / 2.6f;
            arm.transform.localPosition = new Vector3(0f, h, 0.15f * h / 2.6f);
            arm.GetComponent<MeshRenderer>().sharedMaterial = pole;
            root.transform.rotation = Quaternion.Euler(0f, Rf(0f, 360f), 0f);
            Vector3 bulb = root.transform.TransformPoint(new Vector3(0f, h - 0.06f * h / 2.6f, 0.3f * h / 2.6f));
            AddLamp(bulb, new Color(1f, 0.75f, 0.45f), 3f, 12f, root);
        }

        private static void AddLamp(Vector3 pos, Color c, float intensity, float range, GameObject root)
        {
            if (root == null)
            {
                root = new GameObject("TLW lamp");
                root.transform.position = pos;
            }
            var l = new Lamp { Go = root, Color = c, Base = intensity };
            l.G = Lights.Add(pos, c, intensity, range, false, root.transform);
            if (root.name == "TLW post")
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.Destroy(b.GetComponent<Collider>());
                b.transform.SetParent(root.transform, true);
                b.transform.position = pos;
                b.transform.localScale = Vector3.one * 0.08f;
                l.Bulb = Fx.Flat(c, Fx.GlowQueue);
                b.GetComponent<MeshRenderer>().sharedMaterial = l.Bulb;
            }
            var a = root.AddComponent<AudioSource>();
            a.clip = hum;
            a.loop = true;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 0.5f;
            a.maxDistance = 7f;
            a.volume = 0f;
            a.time = Rf(0f, hum.length - 0.1f);
            a.Play();
            l.Hum = a;
            Lamps.Add(l);
        }

        /// <summary>Lamps within reach, farthest first.</summary>
        public static List<Lamp> LampsFarFirst(float within)
        {
            var hp = Fx.Head.position;
            var list = Lamps.FindAll(l => l.Go != null && !l.Dead && (l.Go.transform.position - hp).sqrMagnitude < within * within);
            list.Sort((a, b) => (b.Go.transform.position - hp).sqrMagnitude.CompareTo((a.Go.transform.position - hp).sqrMagnitude));
            return list;
        }

        private static void ClearLamps()
        {
            foreach (var l in Lamps) if (l.Go != null) Object.Destroy(l.Go);
            Lamps.Clear();
        }

        public static void Teardown()
        {
            ClearLamps();
            if (flashlight != null) Lights.Remove(flashlight);
            flashlight = null;
            Lights.RemoveAll();
            Lights.Enable(false);
            Lightning = Alarm = Blood = 0f;
            built = false;
        }
    }
}
