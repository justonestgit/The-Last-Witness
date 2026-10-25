using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders the Witness prefab's baked poses into Build/preview_*.png (check pose and orientation without the game):
/// run frames 0/2/4/6 from the side, reach and tilt from the front.
/// </summary>
public static class PreviewWitness
{
    public static void Render()
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Witness/Witness.prefab"));
        var parts = go.GetComponentsInChildren<MeshFilter>();
        var b = new Bounds();
        bool any = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        Debug.Log($"PreviewWitness: total bounds center {b.center} size {b.size}");
        var light = new GameObject("l").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(30, 30, 0);
        var cam = new GameObject("cam").AddComponent<Camera>();
        cam.backgroundColor = new Color(0.3f, 0.5f, 0.3f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.orthographic = true;
        cam.orthographicSize = b.extents.magnitude * 1.1f;
        var rt = new RenderTexture(384, 384, 24);
        cam.targetTexture = rt;
        foreach (var (pose, dir) in new[] { ("run0", Vector3.right), ("run2", Vector3.right), ("run4", Vector3.right), ("run6", Vector3.right), ("reach", Vector3.forward), ("tilt", Vector3.forward), ("reach_side", Vector3.right) })
        {
            string meshPose = pose.Replace("_side", "");
            foreach (var p in parts)
            {
                var m = AssetDatabase.LoadAssetAtPath<Mesh>($"Assets/Witness/{p.name}_{meshPose}.asset");
                if (m != null) p.sharedMesh = m;
            }
            cam.transform.position = b.center + dir * b.extents.magnitude * 3f;
            cam.transform.LookAt(b.center, Vector3.up);
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(384, 384, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 384, 384), 0, 0);
            File.WriteAllBytes($"Build/preview_{pose}.png", tex.EncodeToPNG());
        }
        Debug.Log("PreviewWitness: done");
    }
}
