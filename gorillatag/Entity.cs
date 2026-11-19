using System;
using System.IO;
using UnityEngine;

namespace TheLastWitness
{
    /// <summary>
    /// The Witness itself. With witness.bundle next to the plugin (the Sketchfab "gorilla tag horror rig" built
    /// into an asset bundle) that model is used; without it, a pitch-black copy of your own gorilla with red eyes.
    /// Every spawn is rooted at its feet and faces +z, so events only place and turn the root.
    /// </summary>
    public static class Entity
    {
        private static GameObject prefab;
        private static Material black, red;
        /// <summary>Glowing red for eyes, drawn after the darkness overlays.</summary>
        private static Material RedEye => red != null ? red : (red = Fx.Flat(new Color(1f, 0.05f, 0.02f, 1f), Fx.GlowQueue));
        public static bool HasModel { get { Load(); return prefab != null; } }

        public static string Dir;
        private static readonly System.Collections.Generic.Dictionary<string, AudioClip> clips = new System.Collections.Generic.Dictionary<string, AudioClip>();

        /// <summary>A sound shipped in witness.bundle (Assets/Sounds/NAME.wav), or null without the bundle.</summary>
        public static AudioClip Clip(string name)
        {
            Load();
            return clips.TryGetValue(name, out var c) ? c : null;
        }
        private static bool started;

        /// <summary>Nothing to wait for: everything is read in the background by Preload (until then, the shadow form).</summary>
        private static void Load() { }

        /// <summary>
        /// Reads witness.bundle in the background once the game is ready: read in one go it froze the game for
        /// seconds at the first Witness (2 MB of poses and a song to decompress).
        /// </summary>
        public static System.Collections.IEnumerator Preload()
        {
            if (started) yield break;
            started = true;
            string path = Path.Combine(Dir, "witness.bundle");
            if (!File.Exists(path))
            {
                Plugin.Log.LogInfo("TheLastWitness: no witness.bundle; the Witness is a shadow of your gorilla");
                yield break;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var req = AssetBundle.LoadFromFileAsync(path);
            yield return req;
            var bundle = req.assetBundle;
            if (bundle == null) yield break;
            var meshes = bundle.LoadAllAssetsAsync<Mesh>();
            yield return meshes;
            foreach (var o in meshes.allAssets) if (o is Mesh m) poses[m.name] = m;
            var sounds = bundle.LoadAllAssetsAsync<AudioClip>();
            yield return sounds;
            foreach (var o in sounds.allAssets) if (o is AudioClip c) clips[c.name] = c;
            var models = bundle.LoadAllAssetsAsync<GameObject>();
            yield return models;
            prefab = models.asset as GameObject;
            Plugin.Log.LogInfo("TheLastWitness: witness.bundle " + (prefab != null ? "loaded: " + prefab.name : "has no model") +
                $", {clips.Count} sounds, {poses.Count} pose meshes, in the background in {watch.ElapsedMilliseconds} ms");
        }

        private static readonly System.Collections.Generic.Dictionary<string, Mesh> poses = new System.Collections.Generic.Dictionary<string, Mesh>();

        /// <summary>One part of the horror rig baked in a pose ("rest", "run0".."run7", "reach", "tilt"), or null.</summary>
        public static Mesh Pose(string part, string pose) => poses.TryGetValue(part + "_" + pose, out var m) ? m : null;

        private static Shader Unlit => Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("UI/Default");
        private static readonly System.Collections.Generic.Dictionary<Texture, Material> lit = new System.Collections.Generic.Dictionary<Texture, Material>();

        /// <summary>
        /// The model's texture on Gorilla Tag's own gorilla-body material, so the Ghost Reactor lighting lights it:
        /// black in the dark, seen only under lamps, the flashlight or lightning. Unlit if that material isn't there.
        /// </summary>
        private static Material Lit(Texture tex)
        {
            if (tex != null && lit.TryGetValue(tex, out var m)) return m;
            var skin = GorillaTagger.Instance?.offlineVRRig?.mainSkin?.sharedMaterial;
            m = skin != null ? new Material(skin) : new Material(Unlit);
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            // The gorilla-body material samples GT's texture atlas (white fur tinted by the player colour): use ours.
            foreach (var k in m.shaderKeywords)
                if (k.Contains("ATLAS") || k.Contains("TEX_ARRAY")) m.DisableKeyword(k);
            Plugin.Log.LogInfo("TheLastWitness: model keywords " + string.Join(" ", m.shaderKeywords));
            if (tex != null) lit[tex] = m;
            Plugin.Log.LogInfo($"TheLastWitness: model material {m.shader.name} (texture props: {string.Join(",", m.GetTexturePropertyNames())})");
            return m;
        }

        private static Material Flat(Color c)
        {
            var m = Fx.Flat(c);
            return m;
        }

        /// <summary>
        /// A new Witness, height metres tall, standing at the origin of its root. Both forms stay in the game: each
        /// spawn is the horror rig or the shadow of your gorilla at random (model: true/false forces one, for tests).
        /// </summary>
        public static GameObject Spawn(float height, bool? model = null)
        {
            Load();
            var root = new GameObject("TLW witness");
            GameObject body;
            // The menu's choice wins; with "Ambos" the caller (or chance) decides.
            bool useModel = Settings.Form.Value switch
            {
                EntityForm.Cinza => true,
                EntityForm.Sombra => false,
                _ => model ?? Director.Rng.NextDouble() < 0.5,
            };
            if (prefab != null && useModel)
            {
                body = UnityEngine.Object.Instantiate(prefab);
                foreach (var r in body.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = Lit(mats[i] != null ? mats[i].mainTexture : null);
                    r.sharedMaterials = mats;
                }
                foreach (var c in body.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.Destroy(c);
                foreach (var a in body.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                body.transform.SetParent(root.transform, false);
                body.transform.localRotation = Quaternion.Euler(0f, Settings.ModelYaw.Value, 0f); // the rig faces +z, like the root
            }
            else
            {
                body = BakeRig(root.transform, null, true);
            }
            Fit(root.transform, body.transform, height);
            var face = body.transform.Find("Object_13"); // the horror rig's face part (the bundle's own naming)
            if (face != null)
            {
                // Its painted eyes are dark under GT's lighting: two red points that glow, so it is seen coming in the dark.
                var b = face.GetComponent<Renderer>().bounds;
                var t = root.transform;
                foreach (float side in new[] { -1f, 1f })
                {
                    var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.Destroy(eye.GetComponent<Collider>());
                    eye.GetComponent<MeshRenderer>().sharedMaterial = RedEye;
                    eye.transform.position = b.center + t.forward * b.extents.z * 0.9f + Vector3.up * b.extents.y * 0.15f + t.right * side * b.extents.x * 0.35f;
                    eye.transform.localScale = Vector3.one * b.extents.x * 0.28f;
                    eye.transform.SetParent(body.transform, true);
                }
            }
            var parts = new System.Collections.Generic.List<MeshFilter>();
            foreach (var mf in body.GetComponentsInChildren<MeshFilter>()) if (mf.name.StartsWith("Object_")) parts.Add(mf);
            root.AddComponent<WitnessAnim>().Init(body.transform, parts.ToArray(), height);
            if (Lights.On)
            {
                // Red glow from the eyes on whatever is in front of it, and an aura that eats the light around it.
                var t = root.transform;
                Lights.Add(t.position + Vector3.up * height * 0.88f + t.forward * height * 0.25f, new Color(1f, 0.05f, 0.02f), 2f, 4f, false, t);
                Lights.Add(t.position + Vector3.up * height * 0.5f, Color.white, 4f, 8f, true, t);
            }
            return root;
        }

        /// <summary>Your double: your gorilla in your colours, re-posed by calling the returned action.</summary>
        public static GameObject Doppel(out Action repose)
        {
            var root = new GameObject("TLW doppel");
            var mesh = new Mesh();
            var body = BakeRig(root.transform, mesh, false);
            var skin = GorillaTagger.Instance.offlineVRRig.mainSkin;
            repose = () => { if (skin != null) skin.BakeMesh(mesh, false); };
            Fit(root.transform, body.transform, -1f);
            return root;
        }

        /// <summary>The local gorilla's skin, baked in its current pose, placed in the root as it stands now (yaw removed).</summary>
        private static GameObject BakeRig(Transform root, Mesh mesh, bool shadow)
        {
            var rig = GorillaTagger.Instance.offlineVRRig;
            var skin = rig.mainSkin;
            mesh = mesh ?? new Mesh();
            skin.BakeMesh(mesh, false);
            float yaw = rig.transform.eulerAngles.y;
            root.SetPositionAndRotation(rig.transform.position, Quaternion.Euler(0f, yaw, 0f));

            var body = new GameObject("body");
            body.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
            body.transform.SetParent(root, true);
            body.transform.localScale = Vector3.one;
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = body.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (shadow)
            {
                if (black == null) black = Flat(new Color(0.01f, 0.01f, 0.012f, 1f));
                var mats = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = black;
                mr.sharedMaterials = mats;
                // Eyes: two small red dots on the front of the head.
                var head = rig.headMesh != null ? rig.headMesh.transform.position : skin.bounds.center + Vector3.up * skin.bounds.extents.y * 0.6f;
                float s = Fx.Player != null ? Fx.Player.scale : 1f;
                foreach (float side in new[] { -1f, 1f })
                {
                    var eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    UnityEngine.Object.Destroy(eye.GetComponent<Collider>());
                    eye.GetComponent<MeshRenderer>().sharedMaterial = RedEye;
                    eye.transform.position = head + root.rotation * new Vector3(side * 0.045f, 0.03f, 0.1f) * s;
                    eye.transform.localScale = Vector3.one * 0.04f * s;
                    eye.transform.SetParent(body.transform, true);
                }
            }
            else
            {
                mr.sharedMaterials = skin.sharedMaterials;
            }
            return body;
        }

        /// <summary>Scale body to height (or keep size if height &lt; 0), feet at the root's origin, centred.</summary>
        private static void Fit(Transform root, Transform body, float height)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                var rb = r.bounds;
                foreach (var c in new[] { rb.min, rb.max, new Vector3(rb.min.x, rb.max.y, rb.min.z), new Vector3(rb.max.x, rb.min.y, rb.max.z) })
                {
                    var lp = root.InverseTransformPoint(c);
                    if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
                    else b.Encapsulate(lp);
                }
            }
            if (!any || b.size.y < 1e-3f) return;
            float k = height > 0f ? height / b.size.y : 1f;
            body.localScale *= k;
            body.localPosition = (body.localPosition - new Vector3(b.center.x, b.min.y, b.center.z)) * k;
        }

        /// <summary>Turn to look at the player (yaw only).</summary>
        public static void Face(Transform t, Vector3 target)
        {
            Vector3 d = Vector3.ProjectOnPlane(target - t.position, Vector3.up);
            if (d.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        /// <summary>The player's standing height (head over floor), for sizing the Witness.</summary>
        public static float PlayerHeight()
        {
            var h = Fx.Head;
            if (h == null) return 1f;
            return Fx.Ground(h.position, out var g) ? Mathf.Clamp(h.position.y - g.y, 0.4f, 3f) : 1f;
        }
    }
}
