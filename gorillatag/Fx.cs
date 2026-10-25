using System;
using System.Collections.Generic;
using GorillaLocomotion;
using TMPro;
using UnityEngine;

namespace TheLastWitness
{
    /// <summary>
    /// What the events draw and play with: overlays fixed to the head (darkness, vignette, flash, glitch),
    /// 3D sounds, floating text, the always-on layers (soundtrack, darkness, night, heartbeat), and the undo list
    /// that puts the world back exactly as it was.
    /// </summary>
    public static class Fx
    {
        public static Transform Head => GorillaTagger.Instance != null && GorillaTagger.Instance.mainCamera != null ? GorillaTagger.Instance.mainCamera.transform : null;
        public static GameObject Root;
        /// <summary>Drawn after the darkness overlays (4001-4004) so eyes and writing glow in the dark; walls still hide them.</summary>
        public const int GlowQueue = 4010;

        // Event controls, read by the layers every frame.
        public static float Blackout, Fear, RedTint, Glitch;
        /// <summary>1 while an event's own music plays: the soundtrack steps aside.</summary>
        public static float Duck;
        public static bool ForceDay, ForceNight, Muted;

        private static Renderer dark, vignette, flash, glitch;
        private static Material darkMat, vignetteMat, flashMat, glitchMat;
        private static Texture2D[] glitchFrames;
        private static AudioSource drone, drone2, ost, atmo;
        private static AudioClip heart;
        private static float nextBeat, flashUntil, flashStart;
        private static Color flashColor;
        private static TMP_FontAsset font;

        // ---------- undo ----------

        private static readonly Dictionary<int, Action> undo = new Dictionary<int, Action>();
        private static int nextUndo;

        public static int Track(Action restore)
        {
            undo[++nextUndo] = restore;
            return nextUndo;
        }

        public static void Restore(int id)
        {
            if (!undo.TryGetValue(id, out var a)) return;
            undo.Remove(id);
            try { a(); }
            catch (Exception e) { Plugin.Log.LogWarning("TheLastWitness: restore failed: " + e.Message); }
        }

        public static void RestoreAll()
        {
            foreach (int id in new List<int>(undo.Keys)) Restore(id);
            Blackout = Fear = RedTint = Glitch = Duck = 0f;
            ForceDay = ForceNight = Muted = false;
            flashUntil = 0f;
        }

        // ---------- setup ----------

        public static void Build()
        {
            Root = new GameObject("TheLastWitness");
            UnityEngine.Object.DontDestroyOnLoad(Root);
            darkMat = Overlay(4001, null);
            vignetteMat = Overlay(4002, Radial());
            glitchMat = Overlay(4003, null);
            flashMat = Overlay(4004, null);
            dark = HeadSphere("dark", darkMat, 0.5f);
            vignette = HeadQuad("vignette", vignetteMat, 0.22f, 1.4f);
            glitch = HeadQuad("glitch", glitchMat, 0.21f, 1.4f);
            flash = HeadSphere("flash", flashMat, 0.4f);
            glitchFrames = new Texture2D[6];
            for (int i = 0; i < glitchFrames.Length; i++) glitchFrames[i] = GlitchTexture();

            heart = Synth.Heartbeat();
            drone = Source("drone", Synth.Drone(24f, false));
            drone2 = Source("drone2", Synth.Drone(19f, true));
            Plugin.Log.LogInfo("TheLastWitness: overlays and soundtrack ready");
        }

        private static Material Overlay(int queue, Texture tex)
        {
            var m = Flat(new Color(0, 0, 0, 0), queue);
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        /// <summary>
        /// A flat, see-through-capable colour material that draws in both eyes in VR. UI/Default: Gorilla Tag's own
        /// VR menus use it (Sprites/Default drew on desktop but the sky made with it never showed in the headset).
        /// Two-sided, no depth write, depth-tested unless overAll.
        /// </summary>
        /// <summary>
        /// A solid material on Gorilla Tag's own UberShader (as the Floral mod does): it is lit by the Ghost Reactor
        /// lights, so it shows under a lamp or the flashlight and disappears in the dark. Flat if the shader is missing.
        /// </summary>
        public static Material Lit(Color c)
        {
            var s = Shader.Find("GorillaTag/UberShader");
            if (s == null) return Flat(c);
            var m = new Material(s) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        public static Material Flat(Color c, int queue = -1, bool overAll = false)
        {
            var m = new Material(Shader.Find("UI/Default")) { color = c };
            m.SetFloat("unity_GUIZTestMode", (float)(overAll ? UnityEngine.Rendering.CompareFunction.Always : UnityEngine.Rendering.CompareFunction.LessEqual));
            if (queue >= 0) m.renderQueue = queue;
            return m;
        }

        private static Renderer HeadSphere(string name, Material m, float size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "TLW " + name;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(Root.transform, false);
            go.transform.localScale = Vector3.one * size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m; // two-sided: the inside of the sphere covers the view
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = false;
            return r;
        }

        private static Renderer HeadQuad(string name, Material m, float distance, float width)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "TLW " + name;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(Root.transform, false);
            go.transform.localScale = new Vector3(width, width, 1f);
            go.transform.localPosition = Vector3.forward * distance;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.enabled = false;
            return r;
        }

        private static AudioSource Source(string name, AudioClip clip)
        {
            var go = new GameObject("TLW " + name);
            go.transform.SetParent(Root.transform, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.loop = true;
            a.spatialBlend = 0f;
            a.volume = 0f;
            a.playOnAwake = false;
            return a;
        }

        /// <summary>Clear in the middle, black at the edges.</summary>
        private static Texture2D Radial()
        {
            const int S = 128;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.85f, Mathf.Sqrt(dx * dx + dy * dy)));
                    px[y * S + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>Torn scanlines, colour blocks and noise: a broken video frame.</summary>
        private static Texture2D GlitchTexture()
        {
            const int W = 64, H = 64;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var px = new Color32[W * H];
            var r = Director.Rng;
            for (int y = 0; y < H; y++)
            {
                bool band = r.NextDouble() < 0.25;
                var bc = new Color32((byte)r.Next(256), (byte)r.Next(64), (byte)r.Next(256), (byte)(band ? r.Next(90, 200) : 0));
                for (int x = 0; x < W; x++)
                {
                    bool speck = r.NextDouble() < 0.05;
                    byte g = (byte)r.Next(256);
                    px[y * W + x] = speck ? new Color32(g, g, g, 160) : bc;
                }
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        // ---------- every frame ----------

        /// <summary>insanity 0 (calm) .. 1 (lost). active false: everything off and quiet.</summary>
        public static void Tick(float insanity, bool active)
        {
            var head = Head;
            if (Root == null || head == null) return;
            Root.transform.SetPositionAndRotation(head.position, head.rotation);
            // The overlays sit 0.2 m from the eye at scale 1; push them out past the camera's near plane.
            var cam = head.GetComponent<Camera>();
            Root.transform.localScale = Vector3.one * Mathf.Max(1f, (cam != null ? cam.nearClipPlane : 0.01f) * 1.5f / 0.2f);
            float cap = Mathf.Clamp(Settings.MaxDarkness.Value, 0.5f, 0.97f);

            float darkness = active && Settings.IsOn("escuro") ? Mathf.Lerp(0.2f, 0.8f, Mathf.Pow(insanity, 1.2f)) : 0f;
            if (Lights.On) darkness *= 0.2f; // Ghost Reactor lighting makes the real darkness; the overlay only deepens it
            float a = Mathf.Min(cap, Mathf.Max(darkness, Blackout));
            darkMat.color = Color.Lerp(new Color(0, 0, 0, a), new Color(0.35f, 0f, 0f, Mathf.Max(a, RedTint * 0.45f)), RedTint);
            dark.enabled = darkMat.color.a > 0.003f;

            float pulse = Fear > 0.05f ? 0.15f * Fear * Mathf.Max(0f, Mathf.Sin(Time.time * Mathf.PI * 2f * Mathf.Lerp(1f, 2.4f, Fear))) : 0f;
            float vig = active ? Mathf.Min(cap, Mathf.Lerp(0.15f, 0.9f, insanity) + pulse) : 0f;
            vignetteMat.color = new Color(1, 1, 1, vig);
            vignette.enabled = vig > 0.003f;

            glitch.enabled = Glitch > 0f;
            if (glitch.enabled)
            {
                glitchMat.mainTexture = glitchFrames[Director.Rng.Next(glitchFrames.Length)];
                glitchMat.color = new Color(1, 1, 1, Mathf.Clamp01(Glitch) * (Settings.SoftFlashes.Value ? 0.35f : 0.85f));
                glitch.transform.localPosition = new Vector3(Rf(-0.03f, 0.03f), Rf(-0.03f, 0.03f), 0.21f);
            }

            float f = Time.time < flashUntil ? 1f - (Time.time - flashStart) / (flashUntil - flashStart) : 0f;
            flashMat.color = new Color(flashColor.r, flashColor.g, flashColor.b, f * (Settings.SoftFlashes.Value ? 0.25f : 0.9f));
            flash.enabled = f > 0f;

            Fear = Mathf.MoveTowards(Fear, 0f, Time.deltaTime * 0.08f);
            Sound(insanity, active);
            Atmosphere(insanity, active);
        }

        private static float mutedFor, blackFor, duckFor;

        private static void Sound(float insanity, bool active)
        {
            float vol = Settings.Volume.Value;
            bool music = active && Settings.IsOn("trilha") && !Muted;
            // The soundtrack follows the director's tension (calm, build, peak, release), not just sanity.
            float k = Mathf.Max(Director.Tension, insanity * 0.4f);
            // The real soundtrack (witness.bundle): the dark ambience holds the calm, the OST groove rises with the tension.
            // The synthesized drones stay underneath, quieter once those are there.
            if (ost == null && Entity.Clip("ost_main") != null) ost = Source("ost", Entity.Clip("ost_main"));
            if (atmo == null && Entity.Clip("atmo_dark") != null) atmo = Source("atmo", Entity.Clip("atmo_dark"));
            float under = ost != null ? 0.5f : 1f;
            Fade(drone, music ? Mathf.Lerp(0.08f, 0.55f, k) * vol * (1f - Duck) * under : 0f, Mathf.Lerp(1f, 0.82f, insanity));
            Fade(drone2, music ? Mathf.InverseLerp(0.35f, 1f, k) * 0.45f * vol * (1f - Duck) * under : 0f, Mathf.Lerp(1f, 0.75f, insanity));
            if (atmo != null) Fade(atmo, music ? 0.55f * (1f - Director.Tension * 0.6f) * vol * (1f - Duck) : 0f, 1f);
            if (ost != null) Fade(ost, music ? Mathf.Lerp(0f, 0.6f, Mathf.InverseLerp(0.2f, 0.8f, k)) * vol * (1f - Duck) : 0f, 1f);
            // Watchdog: no effect mutes for more than ~3 s, blacks out for more than ~10 s or ducks the soundtrack
            // for more than ~35 s. Anything longer is a stuck effect (a player once lost all sound for good).
            mutedFor = Muted ? mutedFor + Time.deltaTime : 0f;
            blackFor = Blackout > 0.9f ? blackFor + Time.deltaTime : 0f;
            duckFor = Duck > 0.5f && !Director.Hold ? duckFor + Time.deltaTime : 0f; // a tape or the finale holds it on purpose
            if (mutedFor > 6f || blackFor > 15f || duckFor > 45f)
            {
                Plugin.Log.LogWarning($"TheLastWitness: stuck effect cleared (muted {mutedFor:0} s, black {blackFor:0} s, ducked {duckFor:0} s)");
                Muted = false;
                if (blackFor > 15f) Blackout = 0f;
                if (duckFor > 45f) Duck = 0f;
                mutedFor = blackFor = duckFor = 0f;
            }
            AudioListener.volume = Muted ? 0f : 1f;

            // Heartbeat: once you're losing it, and whenever something is close.
            float fear = Mathf.Max(Fear, Director.Tension * 0.6f);
            if (active && Settings.IsOn("coracao") && fear > 0.05f && !Muted && Time.time >= nextBeat)
            {
                Play(heart, Head.position - Head.up * 0.3f, Mathf.Lerp(0.25f, 0.9f, fear), false, 0f);
                nextBeat = Time.time + 60f / Mathf.Lerp(60f, 150f, fear);
            }
        }

        /// <summary>After an event fails half-way: never leave the player blind, deaf or stuck in red.</summary>
        public static void ReleaseScreenAndSound()
        {
            Muted = false;
            Blackout = Glitch = RedTint = Duck = 0f;
            Ambience.Blood = Ambience.Alarm = Ambience.Lightning = 0f;
            Sky.Glitch = 0f;
        }

        /// <summary>Test hook: why is it quiet?</summary>
        public static string AudioState()
        {
            int playing = 0, all = 0;
            foreach (var a in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)) { all++; if (a.isPlaying) playing++; }
            var cfg = AudioSettings.GetConfiguration();
            return $"listener {AudioListener.volume} paused {AudioListener.pause} muted {Muted} duck {Duck:0.00} drone {drone?.volume:0.00}/{drone?.isPlaying} drone2 {drone2?.volume:0.00} " +
                $"sources {playing}/{all} realVoices {cfg.numRealVoices} virtual {cfg.numVirtualVoices} listeners {UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length}";
        }

        private static void Fade(AudioSource a, float target, float pitch)
        {
            a.volume = Mathf.MoveTowards(a.volume, target, Time.deltaTime * 0.2f);
            a.pitch = pitch;
            if (a.volume > 0f && !a.isPlaying) a.Play();
            else if (a.volume <= 0f && a.isPlaying) a.Stop();
        }

        // ---------- fog and time of day ----------

        private static bool atmosphereSet, savedFog;
        private static Color savedFogColor;
        private static float savedDensity;
        private static FogMode savedMode;
        private static int forcedTime = -2;

        private static void Atmosphere(float insanity, bool active)
        {
            bool dark = active && Settings.IsOn("escuro");
            if (dark && !atmosphereSet)
            {
                savedFog = RenderSettings.fog;
                savedFogColor = RenderSettings.fogColor;
                savedDensity = RenderSettings.fogDensity;
                savedMode = RenderSettings.fogMode;
                atmosphereSet = true;
            }
            if (atmosphereSet)
            {
                if (dark)
                {
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.Exponential;
                    RenderSettings.fogColor = Color.Lerp(Color.black, new Color(0.25f, 0f, 0f), RedTint);
                    RenderSettings.fogDensity = Mathf.Lerp(0.02f, 0.14f, insanity);
                }
                else
                {
                    RenderSettings.fog = savedFog;
                    RenderSettings.fogColor = savedFogColor;
                    RenderSettings.fogDensity = savedDensity;
                    RenderSettings.fogMode = savedMode;
                    atmosphereSet = false;
                }
            }

            int want = !active ? -1 : ForceDay ? DayIndex() : (ForceNight || dark) ? NightIndex() : -1;
            if (want == forcedTime) return;
            var dn = BetterDayNightManager.instance;
            if (dn == null) return;
            if (want < 0) dn.ClearTimeOfDay(true);
            else dn.SetTimeOfDay(want, true);
            Plugin.Log.LogInfo($"TheLastWitness: time of day {(want < 0 ? "back to normal" : dn.dayNightLightmapNames[want])}");
            forcedTime = want;
        }

        private static int Find(params string[] names)
        {
            var dn = BetterDayNightManager.instance;
            if (dn == null || dn.dayNightLightmapNames == null) return -1;
            foreach (string n in names)
                for (int i = 0; i < dn.dayNightLightmapNames.Length; i++)
                    if (dn.dayNightLightmapNames[i] != null && dn.dayNightLightmapNames[i].ToLowerInvariant().Contains(n)) return i;
            return -1;
        }

        private static int NightIndex()
        {
            int i = Find("night", "midnight", "12am", "1am", "11pm", "10pm");
            return i >= 0 ? i : 0;
        }

        private static int DayIndex()
        {
            int i = Find("noon", "12pm", "1pm", "10am", "day");
            return i >= 0 ? i : 3;
        }

        public static void LogTimes()
        {
            var dn = BetterDayNightManager.instance;
            if (dn != null && dn.dayNightLightmapNames != null) Plugin.Log.LogInfo("TheLastWitness: times of day: " + string.Join(", ", dn.dayNightLightmapNames));
        }

        // ---------- helpers for events ----------

        private static float Rf(float a, float b) => a + (float)Director.Rng.NextDouble() * (b - a);

        public static void Flash(Color c, float seconds)
        {
            flashColor = c;
            flashStart = Time.time;
            flashUntil = Time.time + seconds;
        }

        /// <summary>A one-shot 3D sound; follow keeps it attached (a whisper at your ear).</summary>
        /// <summary>
        /// Static noise for seconds: most of the time a random slice of the recorded static (witness.bundle
        /// "static_real"), otherwise synthesized, so it never sounds the same twice.
        /// </summary>
        public static AudioSource Static(float seconds, Vector3 pos, float volume, bool scare, float spatial = 1f, Transform follow = null)
        {
            var real = Entity.Clip("static_real");
            if (real == null || Director.Rng.NextDouble() > 0.65 || seconds >= real.length - 0.1f) return Play(Synth.Static(seconds), pos, volume, scare, spatial, follow);
            var a = Play(real, pos, volume, scare, spatial, follow);
            if (a == null) return null;
            a.time = Director.Rf(0f, real.length - seconds);
            UnityEngine.Object.Destroy(a.gameObject, seconds);
            return a;
        }

        public static AudioSource Play(AudioClip clip, Vector3 pos, float volume, bool scare, float spatial = 1f, Transform follow = null)
        {
            if (clip == null || Root == null) return null;
            var go = new GameObject("TLW sound " + clip.name);
            if (follow != null) go.transform.SetParent(follow, true);
            go.transform.position = pos;
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.spatialBlend = spatial;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 1f;
            a.maxDistance = 40f;
            a.dopplerLevel = 0f;
            a.volume = Mathf.Clamp01(volume * Settings.Volume.Value * (scare ? Settings.ScareVolume.Value : 1f));
            a.Play();
            UnityEngine.Object.Destroy(go, clip.length + 0.2f);
            return a;
        }

        public static TMP_FontAsset Font
        {
            get
            {
                if (font != null) return font;
                var counts = new Dictionary<TMP_FontAsset, int>();
                foreach (var t in Resources.FindObjectsOfTypeAll<TMP_Text>())
                    if (t.font != null) counts[t.font] = counts.TryGetValue(t.font, out int n) ? n + 1 : 1;
                int most = -1;
                foreach (var kv in counts)
                    if (kv.Value > most) { most = kv.Value; font = kv.Key; }
                return font;
            }
        }

        /// <summary>Drops accents the font can't draw (Gorilla Tag's font may lack some): "VOCÊ" -> "VOCE".</summary>
        public static string Safe(string s)
        {
            var f = Font;
            if (f == null || f.HasCharacters(s)) return s;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Normalize(System.Text.NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        /// <summary>World-space text; height in metres per line.</summary>
        public static TextMeshPro Text(string s, Vector3 pos, Quaternion rot, float height, Color color)
        {
            var go = new GameObject("TLW text");
            go.transform.SetPositionAndRotation(pos, rot);
            var t = go.AddComponent<TextMeshPro>();
            if (Font != null) t.font = Font;
            t.text = Safe(s);
            t.fontSize = height * 16f;
            t.color = color;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(height * 40f, height * 4f);
            return t;
        }

        public static Vector3 Feet
        {
            get
            {
                var h = Head;
                return Ground(h.position, out var g) ? g : h.position - Vector3.up * 1f;
            }
        }

        /// <summary>The floor under p (searched from a little above it).</summary>
        public static bool Ground(Vector3 p, out Vector3 hit)
        {
            hit = p;
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var h, 40f, World, QueryTriggerInteraction.Ignore) && h.normal.y > 0.55f)
            {
                hit = h.point;
                return true;
            }
            return false;
        }

        /// <summary>The lowest walkable floor under p (the forest floor, not a treetop or a platform).</summary>
        public static bool Floor(Vector3 p, out Vector3 hit)
        {
            hit = p;
            bool any = false;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, World, QueryTriggerInteraction.Ignore))
            {
                // Not more than 15 m down: some maps have catch floors far below.
                if (h.normal.y < 0.7f || h.point.y < p.y - 15f || (any && h.point.y >= hit.y)) continue;
                hit = h.point;
                any = true;
            }
            return any;
        }

        /// <summary>Is p on screen and not behind a wall?</summary>
        public static bool Seen(Vector3 p, float angle = 35f)
        {
            var h = Head;
            if (h == null || Vector3.Angle(h.forward, p - h.position) > angle) return false;
            return !Physics.Linecast(h.position, p, World, QueryTriggerInteraction.Ignore);
        }

        /// <summary>A spot on the ground around the player, out of view, between min and max metres away.</summary>
        public static bool SpotOutOfView(float min, float max, out Vector3 spot, float behindAngle = 70f)
        {
            var h = Head;
            spot = Vector3.zero;
            for (int i = 0; i < 25; i++)
            {
                float yaw = Rf(behindAngle, 360f - behindAngle);
                Vector3 fwd = Vector3.ProjectOnPlane(h.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                Vector3 dir = Quaternion.Euler(0, yaw, 0) * fwd;
                Vector3 p = h.position + dir * Rf(min, max);
                if (!Ground(p, out var g) || Mathf.Abs(g.y - h.position.y) > 6f) continue;
                if (Seen(g + Vector3.up)) continue;
                spot = g;
                return true;
            }
            return false;
        }

        public static GTPlayer Player => GTPlayer.Instance;

        /// <summary>The world the gorilla can climb: never the player's own colliders.</summary>
        public static int World => Player != null ? Player.locomotionEnabledLayers.value : ~0;
    }
}
