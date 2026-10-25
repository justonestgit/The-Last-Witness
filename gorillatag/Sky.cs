using System.Collections.Generic;
using UnityEngine;
using static TheLastWitness.Director;

namespace TheLastWitness
{
    /// <summary>
    /// "Céu sinistro": while the horror is on, Gorilla Tag's sky is hidden and a sky of our own is drawn around the
    /// player (a huge sphere, pixel-art like GT). Three stages follow sanity: a blood moon over a purple-black sky,
    /// then redder with a bigger moon, then the moon is an eye and the sky is cracked. Corruption makes it glitch.
    /// </summary>
    public static class Sky
    {
        /// <summary>0..1: how often the sky shows a glitch frame instead (set by corruption events).</summary>
        public static float Glitch;

        private const int W = 512, H = 256;
        private static GameObject sphere;
        private static Material mat;
        private static readonly Texture2D[] stages = new Texture2D[3];
        private static Texture2D[] glitch;
        private static readonly List<Renderer> hidden = new List<Renderer>();
        private static int stage = -1;
        private static float nextScan;
        private static float moonLon, moonLat;

        public static void Tick(float ins, bool active)
        {
            bool want = active && Settings.IsOn("ceu") && !Fx.ForceDay && Fx.Head != null;
            if (!want)
            {
                if (sphere != null) Object.Destroy(sphere);
                sphere = null;
                stage = -1;
                Unhide();
                return;
            }
            if (sphere == null) Build();
            if (Time.time >= nextScan)
            {
                HideGameSky();
                nextScan = Time.time + 5f;
            }
            var cam = Fx.Head.GetComponent<Camera>();
            float far = cam != null ? cam.farClipPlane : 500f;
            sphere.transform.position = Fx.Head.position;
            sphere.transform.rotation = Quaternion.Euler(0f, Time.time * 0.3f, 0f); // the clouds drift
            sphere.transform.localScale = Vector3.one * far * 1.8f;
            int s = ins < 0.45f ? 0 : ins < 0.75f ? 1 : 2;
            if (stages[s] == null) stages[s] = Make(s);
            var tex = Glitch > 0f && Rng.NextDouble() < Glitch ? glitch[Rng.Next(glitch.Length)] : stages[s];
            if (mat.mainTexture != tex) mat.mainTexture = tex;
            if (s != stage) Plugin.Log.LogInfo("TheLastWitness: sky stage " + s);
            stage = s;
        }

        private static void Build()
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "TLW sky";
            Object.Destroy(sphere.GetComponent<Collider>());
            Object.DontDestroyOnLoad(sphere);
            // Two-sided, never writes depth: drawn first, everything else covers it.
            mat = Fx.Flat(Color.white, 1000);
            var r = sphere.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            moonLon = Rf(0f, 360f);
            moonLat = Rf(22f, 34f);
            glitch = new Texture2D[4];
            for (int i = 0; i < glitch.Length; i++) glitch[i] = GlitchFrame();
            // All three stages now (when the horror starts, under the intro's blackout), not mid-game: each takes a moment.
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int s = 0; s < stages.Length; s++) if (stages[s] == null) stages[s] = Make(s);
            Plugin.Log.LogInfo($"TheLastWitness: sky painted in {watch.ElapsedMilliseconds} ms");
        }

        /// <summary>GT's own sky pieces (big renderers named sky/cloud/sun/moon/star) are switched off while ours is up.</summary>
        private static void HideGameSky()
        {
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || r.transform.root.name.StartsWith("TLW") || r.bounds.extents.magnitude < 40f) continue;
                string n = r.name.ToLowerInvariant();
                if (!(n.Contains("sky") || n.Contains("cloud") || n.Contains("sun") || n.Contains("moon") || n.Contains("star"))) continue;
                r.enabled = false;
                hidden.Add(r);
                Plugin.Log.LogInfo("TheLastWitness: hid game sky piece " + r.name);
            }
        }

        private static void Unhide()
        {
            foreach (var r in hidden) if (r != null) r.enabled = true;
            hidden.Clear();
        }

        // ---------- painting ----------

        private static float Hash(int x, int y, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }

        private static float ValueNoise(float x, float y, int s)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(xi, yi, s), b = Hash(xi + 1, yi, s), c = Hash(xi, yi + 1, s), d = Hash(xi + 1, yi + 1, s);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Fbm(float x, float y, int s)
        {
            float v = 0f, amp = 0.5f;
            for (int o = 0; o < 4; o++, x *= 2f, y *= 2f, amp *= 0.5f) v += ValueNoise(x, y, s + o) * amp;
            return v;
        }

        /// <summary>Angle in degrees between two directions given as (longitude, latitude).</summary>
        private static float Angle(float lon1, float lat1, float lon2, float lat2)
        {
            float a1 = lat1 * Mathf.Deg2Rad, a2 = lat2 * Mathf.Deg2Rad, dl = (lon1 - lon2) * Mathf.Deg2Rad;
            return Mathf.Acos(Mathf.Clamp(Mathf.Sin(a1) * Mathf.Sin(a2) + Mathf.Cos(a1) * Mathf.Cos(a2) * Mathf.Cos(dl), -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static Texture2D Make(int s)
        {
            int seed = Rng.Next(1, 100000);
            // Red overhead from the first stage: a purple-black zenith reads as blue in the headset (user, VR, 2026-10-25).
            Color zenith = s == 0 ? new Color(0.08f, 0.005f, 0.02f) : s == 1 ? new Color(0.12f, 0.01f, 0.015f) : new Color(0.1f, 0f, 0f);
            Color glow = s == 0 ? new Color(0.4f, 0.03f, 0.04f) : s == 1 ? new Color(0.6f, 0.05f, 0.03f) : new Color(0.75f, 0.04f, 0.01f);
            float moonR = s == 0 ? 5f : s == 1 ? 8f : 13f;
            // Cracks (stage 2): a few jagged lines from the moon across the sky, as (lon, lat) polylines.
            var cracks = new List<Vector2[]>();
            if (s == 2)
            {
                for (int c = 0; c < 5; c++)
                {
                    var pts = new Vector2[14];
                    float lon = moonLon, lat = moonLat, dir = Rf(0f, 360f);
                    for (int i = 0; i < pts.Length; i++)
                    {
                        pts[i] = new Vector2(lon, lat);
                        dir += Rf(-50f, 50f);
                        lon += Mathf.Cos(dir * Mathf.Deg2Rad) * Rf(5f, 12f);
                        lat = Mathf.Clamp(lat + Mathf.Sin(dir * Mathf.Deg2Rad) * Rf(3f, 8f), 2f, 85f);
                    }
                    cracks.Add(pts);
                }
            }
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            {
                float lat = (y + 0.5f) / H * 180f - 90f;
                for (int x = 0; x < W; x++)
                {
                    float lon = (x + 0.5f) / W * 360f;
                    Color c;
                    if (lat < 0f) c = Color.Lerp(glow * 0.35f, new Color(0.005f, 0f, 0.005f), Mathf.InverseLerp(0f, -25f, lat));
                    else
                    {
                        float k = Mathf.InverseLerp(0f, 80f, lat);
                        c = Color.Lerp(glow, zenith, k);
                        // Stars, fewer as it gets worse.
                        if (lat > 12f && Hash(x, y, seed) > 1f - 0.004f * (1f - s * 0.4f))
                            c = Color.Lerp(c, s == 2 ? new Color(1f, 0.3f, 0.2f) : new Color(0.85f, 0.8f, 0.9f), Hash(y, x, seed));
                        // Cloud silhouettes low on the sky, lit red from below once it turns.
                        if (lat < 40f)
                        {
                            float n = Fbm(lon / 360f * 24f, lat / 12f, seed);
                            float band = Mathf.InverseLerp(40f, 8f, lat) * Mathf.InverseLerp(0f, 6f, lat);
                            float cloud = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.62f, n)) * band;
                            Color body = Color.Lerp(new Color(0.01f, 0f, 0.01f), glow * 0.6f, s * 0.35f * Mathf.InverseLerp(0.62f, 0.5f, n));
                            c = Color.Lerp(c, body, cloud);
                        }
                    }
                    // The moon (or the eye), with a glow around it.
                    float a = Angle(lon, lat, moonLon, moonLat);
                    if (a < moonR * 3f) c += (s == 0 ? new Color(0.35f, 0.05f, 0.04f) : new Color(0.6f, 0.06f, 0.02f)) * Mathf.Pow(1f - a / (moonR * 3f), 2f) * 0.6f;
                    if (a < moonR)
                    {
                        if (s < 2)
                        {
                            float crater = Fbm(lon * 0.6f, lat * 0.6f, seed + 7);
                            c = Color.Lerp(new Color(0.55f, 0.06f, 0.04f), new Color(0.95f, 0.2f, 0.1f), crater) * Mathf.Lerp(1f, 0.75f, a / moonR);
                        }
                        else
                        {
                            // An eye: dark red rim, bright iris, a vertical slit for a pupil.
                            float dLon = Mathf.DeltaAngle(moonLon, lon) * Mathf.Cos(moonLat * Mathf.Deg2Rad), dLat = lat - moonLat;
                            float r = a / moonR;
                            c = r > 0.82f ? new Color(0.25f, 0f, 0f) : Color.Lerp(new Color(1f, 0.55f, 0.1f), new Color(0.75f, 0.02f, 0f), r / 0.82f);
                            if (Mathf.Abs(dLon) < moonR * 0.12f * (1f - Mathf.Abs(dLat) / moonR)) c = Color.black;
                            if (Fbm(lon * 2f, lat * 2f, seed + 3) > 0.7f && r > 0.5f) c *= 0.5f; // veins
                        }
                    }
                    px[y * W + x] = c;
                }
            }
            // Draw the cracks.
            foreach (var pts in cracks)
                for (int i = 0; i + 1 < pts.Length; i++)
                    for (float t = 0f; t <= 1f; t += 0.02f)
                    {
                        var p = Vector2.Lerp(pts[i], pts[i + 1], t);
                        int cx = Mathf.FloorToInt(Mathf.Repeat(p.x, 360f) / 360f * W), cy = Mathf.FloorToInt((p.y + 90f) / 180f * H);
                        if (cy >= 0 && cy < H) px[cy * W + Mathf.Clamp(cx, 0, W - 1)] = new Color32(255, 70, 50, 255);
                    }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        private static Texture2D GlitchFrame()
        {
            const int GW = 128, GH = 64;
            var px = new Color32[GW * GH];
            for (int y = 0; y < GH; y++)
            {
                bool band = Rng.NextDouble() < 0.3;
                var bc = new Color32((byte)Rng.Next(256), (byte)Rng.Next(80), (byte)Rng.Next(256), 255);
                for (int x = 0; x < GW; x++)
                {
                    byte g = (byte)Rng.Next(120);
                    px[y * GW + x] = band ? bc : new Color32(g, (byte)(g / 3), (byte)(g / 2), 255);
                }
            }
            var tex = new Texture2D(GW, GH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
