using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GorillaLocomotion;
using UnityEngine;
using UnityEngine.XR;

namespace TheLastWitness
{
    /// <summary>
    /// The Last Witness: Gorilla Tag as a horror game. A sanity bar falls the longer you stay; the lower it is,
    /// the more random events (sounds, the Witness stalking you, jumpscares, glitches, a corrupted world, moved
    /// objects, anomalies, day and night swapping...) happen. Everything is local to your game, and it switches
    /// itself off in public rooms.
    /// </summary>
    [BepInPlugin("dev.thelastwitness.gorillatag", "The Last Witness", "0.11.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static string DataDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TheLastWitness");

        private ConfigEntry<string> desktopMode;
        private ConfigEntry<float> repositionSeconds;
        private Director director;
        private Events events;
        private Menu menu;
        private DesktopTest desktop;
        private bool ready, wasAllowed = true;

        private void Awake()
        {
            Log = Logger;
            Settings.Bind(Config);
            desktopMode = Config.Bind("Teste", "ModoDesktop", "auto", "auto: teclado e mouse quando não há headset; on; off. F8 alterna no jogo.");
            repositionSeconds = Config.Bind("Menu", "SegundosParaReposicionar", 5f, "Segurar B/Y conta estes segundos e traz o menu para a sua frente.");
            Menu.RepositionSeconds = repositionSeconds.Value;
            Directory.CreateDirectory(DataDir);
            Entity.Dir = Path.GetDirectoryName(Info.Location); // the model loads on first use, not at startup
            Log.LogInfo("TheLastWitness: loaded");
        }

        /// <summary>Only alone (offline) or in a private room: never changes anything in public lobbies.</summary>
        private static bool Allowed()
        {
            var n = NetworkSystem.Instance;
            return n == null || !n.InRoom || n.SessionIsPrivate;
        }

        private bool Ready()
        {
            if (ready) return true;
            if (GorillaTagger.Instance == null || GTPlayer.Instance == null || Fx.Head == null || GorillaTagger.Instance.offlineVRRig == null) return false;
            Fx.Build();
            director = Fx.Root.AddComponent<Director>();
            Director.I = director;
            events = new Events(director);
            director.ResetSanity();
            bool headset = XRSettings.isDeviceActive;
            desktop = new DesktopTest(desktopMode.Value == "on" || (desktopMode.Value == "auto" && !headset));
            menu = new Menu(director, desktop, Allowed, Awaken, NewNight);
            Fx.LogTimes();
            StartCoroutine(Entity.Preload()); // on the plugin: Director.StopAll must not cut it short
            Log.LogInfo($"TheLastWitness: ready (headset {(headset ? "active" : "not active")}, desktop mode {(desktop.Active ? "on" : "off")}, {director.Events.Count} events)");
            return ready = true;
        }

        /// <summary>The player turns the horror on: it starts with the intro.</summary>
        private void Awaken()
        {
            // A new night after an ending (or the first one): the tapes and sanity start over.
            if (Events.Ended || !Events.NightStarted)
            {
                events.NewNight();
                director.ResetSanity();
                Events.NightStarted = true;
            }
            Settings.Running = true;
            director.StartIntro(events.Intro());
            Log.LogInfo("TheLastWitness: horror on");
        }

        /// <summary>"Nova noite": stop, and start over from the first act (and the first tape) next time.</summary>
        private void NewNight()
        {
            Settings.Running = false;
            director.StopAll();
            events.NewNight();
            director.ResetSanity();
        }

        private string lastError;

        /// <summary>Report exceptions ourselves (once per distinct one) and keep the game running.</summary>
        private void Guard(Action a)
        {
            try
            {
                a();
            }
            catch (Exception e)
            {
                string s = e.ToString();
                if (s != lastError) Log.LogError("TheLastWitness: " + s);
                lastError = s;
            }
        }

        private void Update()
        {
            Guard(() =>
            {
                if (!Ready()) return;
                desktop.Update();
                TestHooks();
                bool allowed = Allowed();
                if (allowed != wasAllowed) Log.LogInfo("TheLastWitness: " + (allowed ? "alone or private room: on" : "public room: everything off"));
                wasAllowed = allowed;
                bool active = Settings.Running && allowed;
                Director.Paused = menu.IsOpen && Settings.PauseInMenu.Value;
                director.Tick(active);
                events.Tick();
                events.StoryTick(active);
                Hearing.Tick(active);
                Fx.Tick(Director.Insanity, active);
                Ambience.Tick(Director.Insanity, active);
                Decay.Tick(Director.Insanity, active);
                Sky.Tick(Director.Insanity, active);
                menu.Update();
            });
        }

        private void OnDestroy()
        {
            if (director != null) director.StopAll();
            Ambience.Teardown();
            Decay.HealAll();
            Sky.Tick(0f, false);
        }

        /// <summary>
        /// Test hooks for automated checks: lines in %LOCALAPPDATA%\TheLastWitness\cmd.req.
        /// event ID | sanity N | diff Facil|Media|Dificil|TerrorAbsoluto | stop | run | menu | click LABEL | labels | look YAW PITCH | snap NAME
        /// </summary>
        private void TestHooks()
        {
            string req = Path.Combine(DataDir, "cmd.req");
            if (!File.Exists(req)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(req);
                File.Delete(req);
            }
            catch (IOException)
            {
                return;
            }
            foreach (string line in lines)
            {
                var a = line.Trim().Split(new[] { ' ' }, 2);
                if (a[0].Length == 0) continue;
                string arg = a.Length > 1 ? a[1] : "";
                Log.LogInfo("TheLastWitness: test " + line);
                switch (a[0])
                {
                    case "event": if (!director.Begin(arg)) Log.LogWarning("TheLastWitness: no event " + arg); break;
                    case "sanity": Director.Sanity = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "diff": Settings.Diff.Value = (Difficulty)Enum.Parse(typeof(Difficulty), arg); break;
                    case "stop": director.StopAll(); break;
                    case "run": Settings.Running = arg != "off"; break;
                    case "awaken": Awaken(); break;
                    case "tape": events.TestGoToTape(); break;
                    case "mode": Settings.Mode.Value = (GameMode)Enum.Parse(typeof(GameMode), arg); break;
                    case "props": Log.LogInfo("TheLastWitness: props " + events.PropsState()); break;
                    case "haunt": events.TestTapeHaunt(int.Parse(arg) - 1, 8f); break;
                    case "tapes": events.TestTapes(int.Parse(arg)); break;
                    case "act": Director.Act = int.Parse(arg); break;
                    case "audio": Log.LogInfo("TheLastWitness: audio " + Fx.AudioState() + " | " + Hearing.State()); break;
                    case "hear": Hearing.Fake(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), 1.2f); break;
                    case "scare": director.Run(events.TestScare(int.Parse(arg)), "test scare"); break;
                    case "chase": director.Run(events.TestChase(arg == "model" ? true : arg == "shadow" ? false : (bool?)null), "test chase"); break;
                    case "menu": menu.Toggle(); break;
                    case "click": Log.LogInfo("TheLastWitness: click " + (menu.ClickLabel(arg) ? "ok" : "no such button")); break;
                    case "labels": Log.LogInfo("TheLastWitness: buttons: " + string.Join(" | ", menu.Labels())); break;
                    case "look":
                        var yp = arg.Split(' ');
                        desktop.Look(float.Parse(yp[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(yp[1], System.Globalization.CultureInfo.InvariantCulture));
                        break;
                    case "probe":
                        var cam = Fx.Head.GetComponent<Camera>();
                        Log.LogInfo($"TheLastWitness: camera {(cam != null ? $"near {cam.nearClipPlane} far {cam.farClipPlane} mask {cam.cullingMask:X} clear {cam.clearFlags} bg {cam.backgroundColor} stereo {cam.stereoEnabled}" : "none")}, root scale {Fx.Root.transform.localScale.x}");
                        Log.LogInfo($"TheLastWitness: skybox {RenderSettings.skybox?.name} shader {RenderSettings.skybox?.shader?.name} fog {RenderSettings.fog} {RenderSettings.fogColor}");
                        foreach (var c in Camera.allCameras) Log.LogInfo($"TheLastWitness:   cam {c.name} depth {c.depth} clear {c.clearFlags} bg {c.backgroundColor} target {c.targetTexture?.name} mask {c.cullingMask:X}");
                        foreach (var r in Fx.Root.GetComponentsInChildren<Renderer>(true))
                            Log.LogInfo($"TheLastWitness:   {r.name} enabled {r.enabled} shader {r.sharedMaterial?.shader?.name} supported {r.sharedMaterial?.shader?.isSupported} color {r.sharedMaterial?.color}");
                        break;
                    case "near":
                        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            if (r.enabled && r.bounds.SqrDistance(Fx.Head.position) < 0.3f * 0.3f)
                                Log.LogInfo($"TheLastWitness:   near head: {r.name} (root {r.transform.root.name}) layer {r.gameObject.layer} mat {r.sharedMaterial?.name}");
                        break;
                    case "hideabove":
                        Vector3 pt = Fx.Head.position + Fx.Head.up * 0.12f + Fx.Head.forward * 0.15f;
                        foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                            if (r.enabled && r.bounds.Contains(pt) && r.bounds.size.magnitude < 4f)
                            {
                                Log.LogInfo($"TheLastWitness:   hiding {r.name} (parent {r.transform.parent?.name}, root {r.transform.root.name}) layer {r.gameObject.layer} size {r.bounds.size}");
                                r.enabled = false;
                            }
                        break;
                    case "gl":
                        // gl on | gl off | gl amb R G B | gl light R G B INTENSITY RANGE [neg] | gl tint AMOUNT | gl clear
                        var p = arg.Split(' ');
                        float F(int i) => float.Parse(p[i], System.Globalization.CultureInfo.InvariantCulture);
                        Log.LogInfo($"TheLastWitness: lighting manager {(Lights.Available ? "present" : "missing")}");
                        if (p[0] == "on") Lights.Enable(true);
                        else if (p[0] == "off") { Lights.RemoveAll(); Lights.Enable(false); }
                        else if (p[0] == "amb") Lights.Ambient(new Color(F(1), F(2), F(3)));
                        else if (p[0] == "tint") Lights.Tint(F(1), new Color(0.6f, 0.1f, 0.1f));
                        else if (p[0] == "clear") Lights.RemoveAll();
                        else if (p[0] == "state")
                        {
                            var fl = typeof(GameLightingManager).GetField("gameLights", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            var list = fl?.GetValue(GameLightingManager.instance) as System.Collections.Generic.List<GameLight>;
                            Log.LogInfo($"TheLastWitness: keyword {Shader.IsKeywordEnabled("_ZONE_DYNAMIC_LIGHTS__CUSTOMVERTEX")} ambient {Shader.GetGlobalColor("_GT_GameLight_Ambient_Color")} " +
                                $"tint {Shader.GetGlobalFloat("_GT_DesaturateAndTint_TintAmount")} uselights {Shader.GetGlobalInt("_GT_GameLight_UseMaxLights")} global {Shader.GetGlobalInt("_GT_GameLight_GlobalLightCount")} registered {list?.Count}");
                            if (list != null) foreach (var gl in list) if (gl != null) Log.LogInfo($"TheLastWitness:   light {gl.name} at {gl.cachedPosition} col {gl.cachedColorAndIntensity} range {gl.range} unity range {gl.light?.range}");
                        }
                        else if (p[0] == "light")
                            Lights.Add(Fx.Head.position + Fx.Head.forward * 2f, new Color(F(1), F(2), F(3)), F(4), F(5), p.Length > 6);
                        break;
                    case "tppost":
                        var post = Ambience.Lamps.Find(x => x.Go != null && x.Go.name == "TLW post");
                        if (post == null) { Log.LogInfo("TheLastWitness: no posts"); break; }
                        Vector3 at = post.Go.transform.position + post.Go.transform.forward * 4f + Vector3.up * 1.2f;
                        GTPlayer.Instance.TeleportTo(at, Quaternion.LookRotation(-post.Go.transform.forward), false, false);
                        desktop.Look(Quaternion.LookRotation(-post.Go.transform.forward).eulerAngles.y, 10f);
                        Log.LogInfo("TheLastWitness: teleported next to a post at " + post.Go.transform.position);
                        break;
                    case "join":
                        // A room joined by code is private: the mod stays on there.
                        GorillaNetworking.PhotonNetworkController.Instance.AttemptToJoinSpecificRoom(arg, GorillaNetworking.JoinType.Solo);
                        break;
                    case "tpopen":
                        // A spot nearby with nothing overhead (to look at the sky).
                        for (int i = 0; i < 400; i++)
                        {
                            var probe = Fx.Head.position + Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(5f, 80f);
                            if (!Fx.Floor(probe, out var fl) || Physics.Raycast(fl + Vector3.up * 2f, Vector3.up, 200f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            GTPlayer.Instance.TeleportTo(fl + Vector3.up * 1.2f, Quaternion.identity, false, false);
                            Log.LogInfo("TheLastWitness: open sky at " + fl);
                            break;
                        }
                        break;
                    case "where":
                        Log.LogInfo($"TheLastWitness: head {Fx.Head.position} player {GTPlayer.Instance.transform.position} lights on {Lights.On} lamps {Ambience.Lamps.Count} " +
                            $"net {(NetworkSystem.Instance == null ? "none" : $"inRoom {NetworkSystem.Instance.InRoom} private {NetworkSystem.Instance.SessionIsPrivate}")}");
                        break;
                    case "witness":
                        var w = Entity.Spawn(Entity.PlayerHeight() * 1.3f, arg == "model" ? true : arg == "shadow" ? false : (bool?)null);
                        Fx.Ground(Fx.Head.position + Vector3.ProjectOnPlane(Fx.Head.forward, Vector3.up).normalized * 2.5f, out var g);
                        w.transform.position = g;
                        Entity.Face(w.transform, Fx.Head.position);
                        Destroy(w, 8f);
                        break;
                    case "eye": // show what the headset's left eye sees in the game window, instead of the shoulder camera
                        UnityEngine.XR.XRSettings.gameViewRenderMode = UnityEngine.XR.GameViewRenderMode.LeftEye;
                        foreach (var c in Camera.allCameras) if (c.name == "Shoulder Camera") c.enabled = arg == "off";
                        break;
                    case "tilt": // pitch the headset's parent, to look at the sky without wearing it
                        Fx.Head.parent.localRotation = Quaternion.Euler(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture), 0f, 0f);
                        Log.LogInfo($"TheLastWitness: head parent {Fx.Head.parent.name}, head forward {Fx.Head.forward}");
                        break;
                    case "snap": ScreenCapture.CaptureScreenshot(Path.Combine(DataDir, (arg.Length > 0 ? arg : "gt") + ".png")); break;
                }
            }
        }
    }
}
