using UnityEngine;

namespace TheLastWitness
{
    /// <summary>
    /// Moves a Witness. The horror rig has poses baked in witness.bundle ("rest", a gallop "run0..7", "reach",
    /// "tilt") swapped like a flipbook; the shadow form has none, so it leans and bounces instead. Both get
    /// procedural motion on top: a bob while running, twitches while reaching or watching.
    /// </summary>
    public class WitnessAnim : MonoBehaviour
    {
        public enum Mode { Idle, Tilt, Run, Reach }

        public Mode Current = Mode.Idle;
        private MeshFilter[] parts;
        private Transform body;
        private Vector3 bodyPos;
        private Quaternion bodyRot;
        private float height = 1f, seed;
        private string shown;

        public void Init(Transform body, MeshFilter[] parts, float height)
        {
            this.body = body;
            this.parts = parts != null && parts.Length > 0 && Entity.Pose(parts[0].name, "rest") != null ? parts : null;
            this.height = height;
            bodyPos = body.localPosition;
            bodyRot = body.localRotation;
            seed = Random.value * 100f;
        }

        private const float Fps = 11f; // gallop: 8 frames, about 0.73 s a stride

        private void LateUpdate()
        {
            if (body == null) return;
            float t = Time.time + seed;
            if (parts != null)
            {
                string pose = Current == Mode.Run ? "run" + (int)(t * Fps) % 8 : Current == Mode.Reach ? "reach" : Current == Mode.Tilt ? "tilt" : "rest";
                if (pose != shown)
                {
                    foreach (var p in parts)
                    {
                        var m = Entity.Pose(p.name, pose);
                        if (m != null) p.sharedMesh = m;
                    }
                    shown = pose;
                }
            }
            Vector3 off = Vector3.zero;
            Quaternion rot = Quaternion.identity;
            switch (Current)
            {
                case Mode.Run:
                    // Two bounds per stride; the shadow (no baked gallop) leans into the run instead.
                    off.y = Mathf.Abs(Mathf.Sin(t * Fps * Mathf.PI / 4f)) * 0.08f * height;
                    rot = Quaternion.Euler(parts == null ? 28f : 4f, 0f, Mathf.Sin(t * Fps * Mathf.PI / 8f) * 6f);
                    break;
                case Mode.Reach:
                    off = Random.insideUnitSphere * 0.015f * height;
                    rot = Quaternion.Euler(Random.Range(-4f, 4f), Random.Range(-6f, 6f), Random.Range(-8f, 8f));
                    break;
                case Mode.Tilt:
                    // Still, then a sudden jerk of the whole body now and then.
                    float jerk = Mathf.PerlinNoise(t * 2f, 0f) > 0.8f ? Mathf.Sin(t * 60f) * 5f : 0f;
                    rot = Quaternion.Euler(0f, jerk, (parts == null ? 18f : 0f) + jerk);
                    break;
                default:
                    rot = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 0.7f) * 1.5f);
                    break;
            }
            body.localPosition = bodyPos + off;
            body.localRotation = rot * bodyRot;
        }
    }
}
