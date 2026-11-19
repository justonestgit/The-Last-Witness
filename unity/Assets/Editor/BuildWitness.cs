using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds Build/witness.bundle for The Last Witness: wiz's "Gorilla Tag Ik Rig" (glTF, imported by glTFast) with
/// IdkmaneGTH's horror textures on it, as one prefab "Witness".
/// Run: Unity -batchmode -projectPath unity -executeMethod BuildWitness.Build -quit
/// </summary>
public static class BuildWitness
{
    private const string Model = "Assets/Model/scene.gltf", Out = "Assets/Witness";

    /// <summary>
    /// Bends the rig's bones into a pose. Rotations are about world axes (the model faces +z, up +y), parents
    /// first: rotating about +x by a negative angle swings a hanging arm forward; a positive one leans the torso forward.
    /// </summary>
    private static void Pose(GameObject go, System.Collections.Generic.Dictionary<Transform, Quaternion> rest, string pose, float ph)
    {
        foreach (var kv in rest) kv.Key.localRotation = kv.Value;
        Transform Find(string prefix)
        {
            foreach (var t in rest.Keys) if (t.name.StartsWith(prefix)) return t;
            return null;
        }
        void R(string bone, Vector3 axis, float deg)
        {
            var t = Find(bone);
            if (t != null) t.Rotate(axis, deg, Space.World);
        }
        float s = Mathf.Sin(ph);
        switch (pose)
        {
            case "run": // knuckle-gallop: leaning in, arms pumping opposite each other
                R("torso", Vector3.right, 28f + 6f * Mathf.Sin(ph * 2f));
                R("upper_arm.L", Vector3.right, -35f - 55f * s);
                R("upper_arm.R", Vector3.right, -35f + 55f * s);
                R("forearm.L", Vector3.right, -20f - 25f * Mathf.Max(0f, s));
                R("forearm.R", Vector3.right, -20f - 25f * Mathf.Max(0f, -s));
                R("head", Vector3.right, -22f);
                break;
            case "reach": // the jumpscare: both arms up and at you, head cocked
                R("torso", Vector3.right, 20f);
                R("upper_arm.L", Vector3.right, -100f);
                R("upper_arm.R", Vector3.right, -95f);
                R("upper_arm.L", Vector3.forward, -18f);
                R("upper_arm.R", Vector3.forward, 18f);
                R("forearm.L", Vector3.right, -15f);
                R("forearm.R", Vector3.right, -15f);
                R("head", Vector3.forward, 22f);
                break;
            case "tilt": // standing and watching, head bent too far
                R("torso", Vector3.right, 12f);
                R("head", Vector3.forward, 35f);
                R("head", Vector3.right, 10f);
                R("upper_arm.L", Vector3.right, 6f);
                R("upper_arm.R", Vector3.right, -10f);
                break;
        }
    }

    /// <summary>For the public release (embedded in the DLL): without the private-use songs (see Sounds/PRIVATE.md).</summary>
    public static void BuildRelease() => Build(true);

    private static readonly string[] PrivateSounds = { "ost_main", "countdown", "final_god" };

    public static void Build() => Build(false);

    private static void Build(bool release)
    {
        Directory.CreateDirectory(Out);
        // Pixel-art textures: keep them crisp like Gorilla Tag's.
        foreach (var t in new[] { "face.png", "body.png", "chest.jpg" })
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/Model/horror/" + t);
            imp.filterMode = FilterMode.Point;
            imp.SaveAndReimport();
        }
        Material Mat(string name, string tex)
        {
            var m = new Material(Shader.Find("Unlit/Texture")) { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Model/horror/" + tex) };
            AssetDatabase.CreateAsset(m, $"{Out}/{name}.mat");
            return m;
        }
        var face = Mat("Face", "face.png");
        var body = Mat("Body", "body.png");
        var chest = Mat("Chest", "chest.jpg");

        var src = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        if (src == null) throw new System.Exception("glTF not imported: " + Model);
        var go = (GameObject)Object.Instantiate(src);
        go.name = "Witness";
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = mats[i] != null ? mats[i].name : "";
                mats[i] = n.Contains("Face") ? face : n.Contains("Chest") ? chest : body;
                Debug.Log($"BuildWitness: {r.name} [{i}] {n} -> {mats[i].name}");
            }
            r.sharedMaterials = mats;
        }
        // Bake the skinned meshes into static ones: no bones at runtime, the pose and facing are exactly what the
        // editor shows (skinned, it came out posed and turned differently in Gorilla Tag). Poses are baked too:
        // "<part>_rest", "<part>_run0..7" (a gallop), "<part>_reach" (jumpscare), "<part>_tilt" (crooked head).
        // The mod swaps them like a flipbook.
        var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        var bones = new System.Collections.Generic.Dictionary<Transform, Quaternion>();
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) bones[t] = t.localRotation;
        var meshes = new System.Collections.Generic.List<string>();
        void Bake(string pose)
        {
            foreach (var smr in smrs)
            {
                var mesh = new Mesh { name = smr.name + "_" + pose };
                smr.BakeMesh(mesh, true);
                mesh.RecalculateBounds();
                string path = $"{Out}/{mesh.name}.asset";
                AssetDatabase.CreateAsset(mesh, path);
                meshes.Add(path);
            }
        }
        Pose(go, bones, "rest", 0f);
        Bake("rest");
        for (int f = 0; f < 8; f++)
        {
            Pose(go, bones, "run", f / 8f * Mathf.PI * 2f);
            Bake("run" + f);
        }
        Pose(go, bones, "reach", 0f);
        Bake("reach");
        Pose(go, bones, "tilt", 0f);
        Bake("tilt");

        var baked = new GameObject("Witness");
        foreach (var smr in smrs)
        {
            var part = new GameObject(smr.name);
            part.transform.SetParent(baked.transform, false);
            part.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
            part.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{Out}/{smr.name}_rest.asset");
            part.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
        }
        Object.DestroyImmediate(go);
        PrefabUtility.SaveAsPrefabAsset(baked, Out + "/Witness.prefab");
        Object.DestroyImmediate(baked);

        // Sounds the mod plays from the bundle (Assets/Sounds/*.wav), loaded whole so they start on time.
        var sounds = new System.Collections.Generic.List<string>();
        foreach (var f in Directory.GetFiles("Assets/Sounds", "*.wav"))
        {
            string p = f.Replace('\\', '/');
            if (release && System.Array.IndexOf(PrivateSounds, Path.GetFileNameWithoutExtension(p)) >= 0) continue;
            var imp = (AudioImporter)AssetImporter.GetAtPath(p);
            var s = imp.defaultSampleSettings;
            // Long beds (soundtrack, ambience, songs) stay compressed and decode as they play: decompressed they cost ~80 MB.
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            s.loadType = clip != null && clip.length > 10f ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.85f;
            imp.defaultSampleSettings = s;
            imp.SaveAndReimport();
            sounds.Add(p);
        }

        string outDir = release ? "Build/release" : "Build";
        Directory.CreateDirectory(outDir);
        sounds.Insert(0, Out + "/Witness.prefab");
        sounds.AddRange(meshes);
        var build = new AssetBundleBuild { assetBundleName = "witness.bundle", assetNames = sounds.ToArray() };
        BuildPipeline.BuildAssetBundles(outDir, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        Debug.Log("BuildWitness: done, " + new FileInfo(outDir + "/witness.bundle").Length + " bytes");
    }
}
