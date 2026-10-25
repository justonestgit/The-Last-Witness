using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace TheLastWitness
{
    /// <summary>
    /// Gorilla Tag's own dynamic lighting, the one Ghost Reactor uses (GameLightingManager + GameLight): with the
    /// global keyword _ZONE_DYNAMIC_LIGHTS__CUSTOMVERTEX on, GT's shaders light the world from an ambient colour
    /// plus up to 50 point lights, and can desaturate and tint everything. Negative lights subtract light.
    /// </summary>
    public static class Lights
    {
        private static bool enabled;
        private static float nextSort;
        private static readonly List<GameLight> mine = new List<GameLight>();
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo zoneCount = typeof(GameLightingManager).GetField("zoneDynamicLightingEnableCount", Private);
        private static readonly FieldInfo immediateSort = typeof(GameLightingManager).GetField("immediateSort", Private);
        private static readonly int TintAmount = Shader.PropertyToID("_GT_DesaturateAndTint_TintAmount");
        private static readonly int TintColor = Shader.PropertyToID("_GT_DesaturateAndTint_TintColor");

        public static GameLightingManager M => GameLightingManager.instance;
        public static bool Available => M != null;
        public static bool On => enabled;
        /// <summary>While true the keyword stays off (the map's own baked light shows) but our lights stay registered.</summary>
        public static bool Suspended;

        public static void Enable(bool on)
        {
            if (M == null || on == enabled) return;
            enabled = on;
            Suspended = false;
            if (on)
            {
                M.SetCustomDynamicLightingEnabled(true);
            }
            else
            {
                // Back to whatever Gorilla Tag's own zones (Ghost Reactor...) want.
                bool zones = zoneCount != null && (int)zoneCount.GetValue(M) > 0;
                M.SetCustomDynamicLightingEnabled(zones);
                M.SetAmbientLightDynamic(Color.black);
                M.SetDesaturateAndTintEnabled(false, Color.black);
            }
            Plugin.Log.LogInfo("TheLastWitness: dynamic lighting " + (on ? "on" : "off"));
        }

        /// <summary>Every frame while on: GT's zone code switches the keyword off when you leave its zones.</summary>
        public static void Keep(bool movingLights)
        {
            if (!enabled || M == null) return;
            if (M.IsDynamicLightingEnabled == Suspended) M.SetCustomDynamicLightingEnabled(!Suspended);
            // Local lights are binned into a grid only when GT sorts; moving lights need a fresh sort now and then.
            if (movingLights && immediateSort != null && Time.time >= nextSort)
            {
                immediateSort.SetValue(M, true);
                nextSort = Time.time + 0.1f;
            }
        }

        public static void Ambient(Color c)
        {
            if (enabled) M.SetAmbientLightDynamic(c);
        }

        /// <summary>Colour drains out of the world (0..1), towards tint.</summary>
        public static void Tint(float amount, Color tint)
        {
            if (!enabled) return;
            Shader.SetGlobalColor(TintColor, tint);
            Shader.SetGlobalFloat(TintAmount, Mathf.Clamp01(amount));
        }

        /// <summary>A point light lit by GT's shaders, reaching about radius metres. negative: it eats light (the Witness's aura).</summary>
        public static GameLight Add(Vector3 pos, Color color, float intensity, float radius, bool negative = false, Transform parent = null)
        {
            var go = new GameObject("TLW light");
            go.SetActive(false); // GameLight.Awake reads .light: set it before Awake runs
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            // GT's falloff coefficient is 0.005 / light.range and its influence radius is sqrt(light.range / 0.005).
            l.range = 0.005f * radius * radius;
            l.shadows = LightShadows.None;
            var g = go.AddComponent<GameLight>();
            g.light = l;
            g.negativeLight = negative;
            g.applyRange = true;
            go.SetActive(true);
            mine.Add(g);
            return g;
        }

        public static void Remove(GameLight g)
        {
            if (g == null) return;
            mine.Remove(g);
            Object.Destroy(g.gameObject);
        }

        public static void RemoveAll()
        {
            foreach (var g in mine) if (g != null) Object.Destroy(g.gameObject);
            mine.Clear();
        }
    }
}
