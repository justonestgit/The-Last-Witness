using UnityEngine;
using UnityEngine.InputSystem;

namespace TheLastWitness
{
    /// <summary>
    /// Keyboard-and-mouse mode for running without a headset (testing, recording): WASD + Space move the
    /// gorilla, the mouse looks. On automatically when no headset is active; F8 toggles it.
    /// </summary>
    public class DesktopTest
    {
        public bool Active;
        private float yaw, pitch;

        public DesktopTest(bool active)
        {
            Active = active;
        }

        /// <summary>Test hook: face this way (Unity degrees).</summary>
        public void Look(float y, float p)
        {
            yaw = y;
            pitch = p;
        }

        public void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            if (kb.f8Key.wasPressedThisFrame)
            {
                Active = !Active;
                Plugin.Log.LogInfo("TheLastWitness: desktop test mode " + (Active ? "on" : "off"));
            }
            var cam = Fx.Head;
            var p = Fx.Player;
            if (!Active || cam == null || p == null) return;
            // First person, like the headset: Gorilla Tag's desktop view is its over-the-shoulder camera.
            foreach (var c in Camera.allCameras)
            {
                if (c.name == "Shoulder Camera") c.enabled = false;
            }
            Vector2 d = Application.isFocused ? mouse.delta.ReadValue() * 0.15f : Vector2.zero;
            if (Application.isFocused)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            yaw += d.x;
            pitch = Mathf.Clamp(pitch - d.y, -85f, 85f);
            cam.rotation = Quaternion.Euler(pitch, yaw, 0f);

            Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward, right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            if (Application.isFocused)
            {
                Vector3 move = Vector3.zero;
                if (kb.wKey.isPressed) move += fwd;
                if (kb.sKey.isPressed) move -= fwd;
                if (kb.dKey.isPressed) move += right;
                if (kb.aKey.isPressed) move -= right;
                var rb = p.playerRigidBody;
                Vector3 v = rb.linearVelocity;
                Vector3 flat = move.normalized * (kb.leftShiftKey.isPressed ? 6.5f : 4f);
                if (move != Vector3.zero)
                {
                    v.x = flat.x;
                    v.z = flat.z;
                }
                if (kb.spaceKey.wasPressedThisFrame && Physics.Raycast(cam.position, Vector3.down, 1.6f, Fx.World, QueryTriggerInteraction.Ignore)) v.y = 5f;
                rb.linearVelocity = v;
            }

            foreach (bool left in new[] { true, false })
            {
                var c = p.GetControllerTransform(left);
                if (c != null)
                {
                    c.position = cam.position + cam.forward * 0.35f - cam.up * 0.3f + cam.right * (left ? -0.22f : 0.22f);
                    c.rotation = cam.rotation;
                }
            }
        }
    }
}
