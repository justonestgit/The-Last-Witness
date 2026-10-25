using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace TheLastWitness
{
    /// <summary>
    /// The Last Witness menu, controlled like MonkeCraft's: B/Y twice opens or closes it, the right hand points
    /// with a laser that stops on the panel, the trigger clicks, holding B/Y counts down and brings it in front
    /// of you again. Pages: main (sanity, start/pause, intensity), events (each one on/off) and comfort.
    /// Also draws the sanity bar on the left wrist. Desktop test mode: Tab toggles, the view aims, left click.
    /// </summary>
    public class Menu
    {
        private const float W = 1000f, H = 700f, Width = 1.3f, Distance = 0.9f;

        private class Clickable
        {
            public RectTransform Rt;
            public Image Bg;
            public Color Normal, Hover;
            public Action OnClick;
            public string Label;
        }

        private enum Page { Main, Events, Comfort, Diary }

        public bool IsOpen { get; private set; }
        public static float RepositionSeconds = 5f;

        private readonly Director director;
        private readonly DesktopTest desktop;
        private readonly Func<bool> allowed;
        private readonly Action awaken, newNight;
        private TextMeshProUGUI title, subtitle, actText;
        private RectTransform eye, pupil;
        private RawImage scan;
        private Vector2 hitLocal;
        private float nextFace, nextLine, blinkUntil, nextBlink;
        private const string TitleText = "THE LAST WITNESS";
        private GameObject root, laserGo, countGo, wristGo;
        private Transform dot;
        private LineRenderer laser;
        private TextMeshProUGUI countdown, status, percent, wristText;
        private RectTransform fill, wristFill, wristNoise;
        private Image fillImg, wristFillImg;
        private Sprite rounded;
        private readonly List<Clickable> clickables = new List<Clickable>();
        private Page page;
        private bool byHeld, holdDone, lastTrigger;
        private float lastPress = -10f, holdStart = -1f;

        private static readonly Color Panel = new Color(0.04f, 0.035f, 0.035f, 1f), Surface = new Color(0.14f, 0.12f, 0.12f, 1f),
            SurfaceHover = new Color(0.26f, 0.15f, 0.15f, 1f), Text = new Color(0.93f, 0.9f, 0.88f, 1f), TextDim = new Color(0.6f, 0.55f, 0.53f, 1f),
            Accent = new Color(0.62f, 0.04f, 0.04f, 1f), AccentHover = new Color(0.8f, 0.1f, 0.08f, 1f), Off = new Color(0.1f, 0.1f, 0.1f, 1f);

        public Menu(Director director, DesktopTest desktop, Func<bool> allowed, Action awaken, Action newNight)
        {
            this.awaken = awaken;
            this.newNight = newNight;
            this.director = director;
            this.desktop = desktop;
            this.allowed = allowed;
        }

        private static Transform Head => Fx.Head;

        // ---------- every frame ----------

        public void Update()
        {
            var head = Head;
            if (head == null) return;
            if (root == null) Build();
            Buttons(head);
            if (IsOpen && (root.transform.position - head.position).sqrMagnitude > 2.5f * 2.5f) Place(head);
            if (IsOpen) { Live(); Animate(); }
            Pointer();
            Wrist(head);
        }

        private void Buttons(Transform head)
        {
            var c = ControllerInputPoller.instance;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool held = c != null && (c.leftControllerSecondaryButton || c.rightControllerSecondaryButton);
            if (desktop.Active && kb != null && kb.tabKey.wasPressedThisFrame) Toggle();
            if (held && !byHeld)
            {
                if (Time.time - lastPress < 0.4f)
                {
                    Toggle();
                    lastPress = -10f;
                }
                else
                {
                    lastPress = Time.time;
                }
                holdStart = Time.time;
                holdDone = false;
            }
            byHeld = held;
            float t = byHeld && IsOpen && !holdDone ? Time.time - holdStart - 0.4f : -1f;
            if (t >= RepositionSeconds)
            {
                holdDone = true;
                Place(head);
            }
            countGo.SetActive(t >= 0f && !holdDone);
            if (countGo.activeSelf)
            {
                countdown.text = $"Reposicionando: {Mathf.CeilToInt(RepositionSeconds - t)}...";
                countGo.transform.SetPositionAndRotation(head.position + head.forward * 1.0f + head.up * 0.12f, Quaternion.LookRotation(head.forward, head.up));
            }
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (Head == null) return;
            if (root == null) Build();
            page = Page.Main;
            IsOpen = true;
            root.SetActive(true);
            Rebuild();
            Place(Head);
        }

        public void Close()
        {
            IsOpen = false;
            if (root != null) root.SetActive(false);
            if (laserGo != null) laserGo.SetActive(false);
        }

        private void Place(Transform head)
        {
            Vector3 fwd = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = head.forward;
            root.transform.SetPositionAndRotation(head.position + fwd * Distance, Quaternion.LookRotation(fwd, Vector3.up));
        }

        private void Pointer()
        {
            laserGo.SetActive(IsOpen);
            if (!IsOpen) return;
            var ctrl = Fx.Player != null ? Fx.Player.GetControllerTransform(false) : null;
            var head = Head;
            Ray ray = desktop.Active || ctrl == null ? new Ray(head.position, head.forward) : new Ray(ctrl.position, ctrl.forward);
            Vector3 from = desktop.Active || ctrl == null ? head.position - head.up * 0.15f + head.right * 0.1f : ctrl.position;
            var t = root.transform;
            var plane = new Plane(-t.forward, t.position);
            Vector3 end = ray.origin + ray.direction * 2f;
            Clickable over = null;
            if (plane.Raycast(ray, out float dist))
            {
                Vector3 p = ray.GetPoint(dist);
                Vector2 local = ((RectTransform)t).InverseTransformPoint(p);
                if (Mathf.Abs(local.x) <= W / 2 && Mathf.Abs(local.y) <= H / 2)
                {
                    end = p; // stops on the panel
                    hitLocal = local;
                    foreach (var c in clickables)
                    {
                        Vector2 lp = c.Rt.InverseTransformPoint(p);
                        if (c.Rt.rect.Contains(lp)) over = c;
                    }
                }
            }
            foreach (var c in clickables) c.Bg.color = c == over ? c.Hover : c.Normal;
            dot.gameObject.SetActive(end != ray.origin + ray.direction * 2f);
            dot.SetPositionAndRotation(end - t.forward * 0.002f, t.rotation);
            laser.SetPosition(0, from);
            laser.SetPosition(1, end);
            var poller = ControllerInputPoller.instance;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool trigger = desktop.Active ? mouse != null && Application.isFocused && mouse.leftButton.isPressed
                : poller != null && poller.rightControllerIndexFloat > 0.5f;
            if (trigger && !lastTrigger && over != null) Click(over);
            lastTrigger = trigger;
        }

        private void Click(Clickable c)
        {
            Plugin.Log.LogInfo("TheLastWitness: menu " + c.Label);
            c.OnClick?.Invoke();
        }

        /// <summary>Test hook: click the button with this label (case-insensitive, accents as shown).</summary>
        public bool ClickLabel(string label)
        {
            var c = clickables.Find(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase));
            if (c == null) return false;
            Click(c);
            return true;
        }

        public IEnumerable<string> Labels() { foreach (var c in clickables) yield return c.Label; }

        // ---------- live parts ----------

        private static Color SanityColor(float s) => Color.Lerp(new Color(0.75f, 0.02f, 0.02f), new Color(0.75f, 0.75f, 0.72f), s / 100f);

        private string StatusText()
        {
            if (!allowed()) return "Desligado em salas públicas: jogue sozinho ou numa sala privada.";
            if (Events.Ended) return Events.Ending + " Aperte \"Começar a noite\" para outra.";
            if (!Settings.Running) return "Parado. A floresta espera você.";
            string s = "Ativo · " + Settings.DiffName(Settings.Diff.Value);
            if (Director.Paused) s += " · tudo parado enquanto o menu está aberto";
            return s;
        }

        private void Live()
        {
            if (page != Page.Main || fill == null) return;
            float s = Director.Sanity;
            fill.sizeDelta = new Vector2(592f * Mathf.Max(0f, s) / 100f, fill.sizeDelta.y);
            fillImg.color = SanityColor(s);
            percent.text = $"{s:0}%";
            status.text = Fx.Safe(StatusText());
            if (actText != null)
                actText.text = Fx.Safe("ATO " + Roman[Mathf.Clamp(Director.Act - 1, 0, Roman.Length - 1)] +
                    (Settings.Mode.Value == GameMode.Fitas ? $"\nFITAS {Events.Found.Count}/{Events.TapeCount}" : ""));
        }

        private void Wrist(Transform head)
        {
            bool show = Settings.Wrist.Value && Settings.Running && allowed() && GorillaTagger.Instance != null;
            wristGo.SetActive(show);
            if (!show) return;
            var hand = GorillaTagger.Instance.leftHandTransform;
            if (hand == null) return;
            Vector3 toHead = (head.position - hand.position).normalized;
            Vector3 p = hand.position + toHead * 0.06f + Vector3.up * 0.07f;
            wristGo.transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position, Vector3.up));
            float s = Director.Sanity;
            bool broken = Fx.Glitch > 0f;
            wristFill.sizeDelta = new Vector2(380f * (broken ? Director.Rf(0f, 1f) : Mathf.Max(0f, s) / 100f), wristFill.sizeDelta.y);
            wristFillImg.color = SanityColor(s);
            wristNoise.sizeDelta = new Vector2(380f * Hearing.Noise, 6f); // how loud you have been: it is listening
            string tapes = Settings.Mode.Value == GameMode.Fitas ? $"  {Events.Found.Count}/{Events.TapeCount}" : "";
            wristText.text = broken ? "SANIDADE ??%" : $"SANIDADE {s:0}%{tapes}";
        }

        // ---------- layout ----------

        private void Rebuild()
        {
            foreach (Transform child in root.transform) UnityEngine.Object.Destroy(child.gameObject);
            clickables.Clear();
            fill = null;
            actText = null;
            Box("panel", root.transform, 0, 0, W, H, Panel);
            // Red scanlines crawling over the panel, and the edges sinking into black.
            scan = RawImg("scanlines", root.transform, 0, 0, W, H, Scanlines(), new Color(1f, 1f, 1f, 0.5f));
            RawImg("edges", root.transform, 0, 0, W, H, Edges(), Color.white);
            title = Label(root.transform, 40, 18, 700, 64, TitleText, 52, new Color(0.85f, 0.08f, 0.06f), TextAlignmentOptions.Left, true);
            subtitle = Label(root.transform, 44, 78, 700, 30, "", 20, new Color(0.55f, 0.12f, 0.1f), TextAlignmentOptions.Left, false);
            subtitle.fontStyle = FontStyles.Italic;
            nextLine = 0f;
            // An eye in the corner that follows where you point, and blinks.
            var e = RawImg("eye", root.transform, W - 190, 20, 92, 60, EyeTex(), Color.white);
            eye = e.rectTransform;
            var pu = RawImg("pupil", eye, 0, 0, 14, 44, PupilTex(), Color.white);
            pupil = pu.rectTransform;
            pupil.anchorMin = pupil.anchorMax = pupil.pivot = new Vector2(0.5f, 0.5f);
            pupil.anchoredPosition = Vector2.zero;
            Button(root.transform, W - 76, 28, 46, 46, "X", Surface, new Color(0.75f, 0.22f, 0.2f, 1f), 24, Close);
            if (page == Page.Events) BuildEvents();
            else if (page == Page.Comfort) BuildComfort();
            else if (page == Page.Diary) BuildDiary();
            else BuildMain();
        }

        private void Go(Page p)
        {
            page = p;
            Rebuild();
        }

        private static readonly string[] Lines =
        {
            "ele está ouvindo", "não olhe para trás", "você já esteve aqui", "conte as fitas", "a floresta lembra de você",
            "ninguém mais está aqui", "ele não dorme", "testemunhas são apagadas", "feche o menu, se tiver coragem",
        };

        /// <summary>The menu is part of the horror: the title breaks, lines change, the eye watches, and late at night it is behind the panel.</summary>
        private void Animate()
        {
            float ins = Director.Insanity;
            if (scan != null) scan.uvRect = new Rect(0f, -Time.time * 0.15f, 1f, 18f);
            if (title != null)
            {
                string t = TitleText;
                if (ins > 0.45f && Director.Rng.NextDouble() < 0.004) t = "VOCÊ É A TESTEMUNHA";
                else if (Director.Rng.NextDouble() < 0.06)
                {
                    var c = t.ToCharArray();
                    c[Director.Rng.Next(c.Length)] = "#%&@?!/"[Director.Rng.Next(7)];
                    t = new string(c);
                }
                title.text = Fx.Safe(t);
                title.color = Director.Rng.NextDouble() < 0.03 ? new Color(1f, 0.9f, 0.9f) : new Color(0.85f, 0.08f, 0.06f);
            }
            if (subtitle != null && Time.time >= nextLine)
            {
                nextLine = Time.time + Director.Rf(5f, 9f);
                subtitle.text = Fx.Safe(Director.Pick(Lines));
            }
            if (eye != null)
            {
                // The eye's centre in the panel's own coordinates (origin in the middle, y up).
                var center = new Vector2(W - 190 + 46 - W / 2, H / 2 - (20 + 30));
                pupil.anchoredPosition = Vector2.ClampMagnitude((hitLocal - center) * 0.08f, 26f) * new Vector2(1f, 0.25f);
                if (Time.time >= nextBlink) { blinkUntil = Time.time + 0.12f; nextBlink = Time.time + Director.Rf(2.5f, 7f); }
                eye.localScale = new Vector3(1f, Time.time < blinkUntil ? 0.08f : 1f, 1f);
            }
            // Late at night, for two frames, it is standing right behind the menu.
            if (ins > 0.45f && Settings.Running && Time.time >= nextFace)
            {
                nextFace = Time.time + Director.Rf(8f, 20f);
                director.Run(FaceBehind(), "menu face");
            }
        }

        private System.Collections.IEnumerator FaceBehind()
        {
            float h = Entity.PlayerHeight() * 1.2f;
            var w = Entity.Spawn(h);
            var t = root.transform;
            w.transform.position = t.position + t.forward * 0.7f - Vector3.up * h * 0.85f;
            Entity.Face(w.transform, Head.position);
            Fx.Play(Synth.Static(0.12f), t.position, 0.5f, false);
            yield return null;
            yield return null;
            UnityEngine.Object.Destroy(w);
        }

        private void BuildMain()
        {
            bool story = Settings.Mode.Value == GameMode.Fitas;
            Label(root.transform, 40, 122, 300, 30, "SANIDADE", 22, TextDim, TextAlignmentOptions.Left, true);
            Box("bar", root.transform, 40, 154, 600, 36, Off);
            var f = Box("fill", root.transform, 44, 158, 592, 28, SanityColor(Director.Sanity));
            fill = f.rectTransform;
            fillImg = f;
            percent = Label(root.transform, 440, 120, 200, 34, "", 28, Text, TextAlignmentOptions.Right, true);
            actText = Label(root.transform, 670, 118, 290, 80, "", 34, new Color(0.85f, 0.08f, 0.06f), TextAlignmentOptions.TopRight, true);
            status = Label(root.transform, 40, 200, 920, 30, "", 20, TextDim, TextAlignmentOptions.Left, false);

            // Mode: changes only while the night is stopped (and starts a new one).
            Label(root.transform, 40, 238, 200, 30, "MODO", 22, TextDim, TextAlignmentOptions.Left, true);
            void ModeButton(float x, GameMode m, string name)
            {
                bool sel = Settings.Mode.Value == m;
                Button(root.transform, x, 270, 300, 60, name, sel ? Accent : Surface, sel ? AccentHover : SurfaceHover, 24, () =>
                {
                    if (Settings.Running || Settings.Mode.Value == m) return;
                    Settings.Mode.Value = m;
                    newNight();
                    Rebuild();
                });
            }
            ModeButton(40, GameMode.Fitas, "As Fitas");
            ModeButton(360, GameMode.Pesadelo, "Pesadelo sem fim");
            Label(root.transform, 680, 270, 280, 60, story ? "Ache as 8 fitas. Sobreviva ao fim." : "Sem objetivo. Sem fim.", 18, TextDim, TextAlignmentOptions.Left, false).textWrappingMode = TextWrappingModes.Normal;

            Label(root.transform, 40, 344, 300, 30, "INTENSIDADE", 22, TextDim, TextAlignmentOptions.Left, true);
            float x = 40;
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                bool sel = Settings.Diff.Value == d;
                var dd = d;
                Button(root.transform, x, 376, 215, 60, Settings.DiffName(d), sel ? Accent : Surface, sel ? AccentHover : SurfaceHover, 24, () =>
                {
                    Settings.Diff.Value = dd;
                    Rebuild();
                });
                x += 235;
            }

            bool on = Settings.Running;
            string go = on ? "Pausar" : Events.NightStarted && !Events.Ended ? "Continuar a noite" : "Começar a noite";
            Button(root.transform, 40, 456, 600, 84, go, on ? Surface : Accent, on ? SurfaceHover : AccentHover, 34, () =>
            {
                if (Settings.Running)
                {
                    Settings.Running = false;
                    Rebuild();
                }
                else
                {
                    Close();
                    awaken();
                }
            });
            Button(root.transform, 660, 456, 300, 84, "Nova noite", Surface, SurfaceHover, 26, () => { newNight(); Rebuild(); });

            Button(root.transform, 40, 558, 293, 64, "Eventos", Surface, SurfaceHover, 26, () => Go(Page.Events));
            Button(root.transform, 353, 558, 293, 64, "Conforto", Surface, SurfaceHover, 26, () => Go(Page.Comfort));
            Button(root.transform, 666, 558, 294, 64, "Diário", Surface, SurfaceHover, 26, () => Go(Page.Diary));
            Label(root.transform, 40, 638, 920, 50,
                "B/Y duas vezes: abrir ou fechar · Segure B/Y: trazer o menu · Gatilho: escolher · Entidades: sombra do seu gorila" +
                (Entity.HasModel ? " + horror rig (IdkmaneGTH, rig de wiz, CC BY)" : ""),
                16, TextDim, TextAlignmentOptions.TopLeft, false).textWrappingMode = TextWrappingModes.Normal;
            Live();
        }

        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII" };

        /// <summary>The tapes found so far, in full; the rest are blacked out.</summary>
        private void BuildDiary()
        {
            Back();
            Label(root.transform, 40, 110, 920, 30, $"FITAS ENCONTRADAS: {Events.Found.Count}/{Events.TapeCount}", 22, TextDim, TextAlignmentOptions.Left, true);
            for (int i = 0; i < Events.TapeCount; i++)
            {
                bool got = Events.Found.Contains(i);
                var t = Label(root.transform, 40, 148 + i * 66, 920, 62, got ? Events.Tapes[i] : $"FITA {i + 1}. ██████ ███ ████████ ██ ███████ ████", 17,
                    got ? Text : new Color(0.3f, 0.27f, 0.26f), TextAlignmentOptions.TopLeft, false);
                t.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        private void Back() => Button(root.transform, W - 300, 28, 200, 46, "< Voltar", Surface, SurfaceHover, 24, () => Go(Page.Main));

        private void Toggle(float x, float y, float w, string label, bool on, Action flip, float h = 46f)
        {
            Button(root.transform, x, y, w, h, label + (on ? "  ·  LIGADO" : "  ·  DESLIGADO"), on ? Accent : Off, on ? AccentHover : SurfaceHover, 22, () =>
            {
                flip();
                Rebuild();
            }, on ? Color.white : TextDim).Label = label;
        }

        private void BuildEvents()
        {
            Back();
            var items = new List<(string id, string name)>();
            foreach (var e in director.Events) items.Add((e.Id, e.Name));
            items.AddRange(Director.Layers);
            for (int i = 0; i < items.Count; i++)
            {
                var (id, name) = items[i];
                Toggle(40 + (i % 3) * 310, 96 + (i / 3) * 46, 300, name, Settings.IsOn(id), () => Settings.Toggle(id), 40f);
            }
        }

        private static float Next(float v, float[] steps)
        {
            for (int i = 0; i < steps.Length; i++) if (v < steps[i] - 0.001f) return steps[i];
            return steps[0];
        }

        private void BuildComfort()
        {
            Back();
            float y = 100;
            void Row(string label, bool on, Action flip) { Toggle(40, y, 920, label, on, flip); y += 58; }
            void Step(string label, BepInEx.Configuration.ConfigEntry<float> e, float[] steps)
            {
                Button(root.transform, 40, y, 920, 46, $"{label}: {e.Value * 100f:0}%", Surface, SurfaceHover, 22, () =>
                {
                    e.Value = Next(e.Value, steps);
                    Rebuild();
                }).Label = label;
                y += 58;
            }
            Row("Flashes suaves", Settings.SoftFlashes.Value, () => Settings.SoftFlashes.Value = !Settings.SoftFlashes.Value);
            Row("Sem efeitos de movimento", Settings.NoMovement.Value, () => Settings.NoMovement.Value = !Settings.NoMovement.Value);
            Row("Sanidade no pulso", Settings.Wrist.Value, () => Settings.Wrist.Value = !Settings.Wrist.Value);
            Row("Pausar com o menu aberto", Settings.PauseInMenu.Value, () => Settings.PauseInMenu.Value = !Settings.PauseInMenu.Value);
            Step("Volume dos sustos", Settings.ScareVolume, new[] { 0.25f, 0.5f, 0.75f, 1f });
            Step("Volume geral", Settings.Volume, new[] { 0.25f, 0.5f, 0.75f, 1f });
            Step("Escuridão máxima", Settings.MaxDarkness, new[] { 0.5f, 0.7f, 0.85f, 0.95f });
            Step("Sensibilidade do microfone", Settings.MicSensitivity, new[] { 0.5f, 0.75f, 1f, 1.5f, 2f });
            Label(root.transform, 40, y + 10, 920, 80, "Flashes suaves também enfraquecem os bugs de tela. Sem efeitos de movimento desliga a gravidade falha e os puxões. Microfone: " +
                (Hearing.Available ? "ele está ouvindo." : "desligado ou sem permissão no Gorilla Tag (ele não ouve).") ,
                20, TextDim, TextAlignmentOptions.TopLeft, false).textWrappingMode = TextWrappingModes.Normal;
        }

        // ---------- building blocks (as in MonkeCraft / MonkePortal) ----------

        private void Build()
        {
            rounded = Rounded();
            root = new GameObject("TLW menu", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(root);
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)root.transform).sizeDelta = new Vector2(W, H);
            root.transform.localScale = Vector3.one * (Width / W);
            root.SetActive(false);

            laserGo = new GameObject("TLW menu pointer");
            UnityEngine.Object.DontDestroyOnLoad(laserGo);
            laser = laserGo.AddComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.startWidth = 0.005f;
            laser.endWidth = 0.003f;
            var lm = OnTop(new Material(Shader.Find("UI/Default")));
            lm.color = new Color(0.9f, 0.1f, 0.08f, 1f);
            laser.sharedMaterial = lm;
            var dg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(dg.GetComponent<Collider>());
            dg.GetComponent<MeshRenderer>().sharedMaterial = lm;
            dg.transform.SetParent(laserGo.transform, false);
            dg.transform.localScale = Vector3.one * 0.012f;
            dot = dg.transform;
            laserGo.SetActive(false);

            countGo = FloatingText("TLW countdown", 0.05f, new Color(1f, 1f, 0.33f), out countdown);

            // Wrist bar: 400 x 90 canvas units, 0.13 m wide.
            wristGo = new GameObject("TLW wrist", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(wristGo);
            wristGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)wristGo.transform).sizeDelta = new Vector2(400, 90);
            wristGo.transform.localScale = Vector3.one * (0.13f / 400f);
            Box("bg", wristGo.transform, 0, 0, 400, 90, new Color(0.03f, 0.03f, 0.03f, 0.85f));
            wristText = Label(wristGo.transform, 10, 6, 380, 40, "", 30, Text, TextAlignmentOptions.Left, true);
            Box("bar", wristGo.transform, 10, 52, 380, 28, Off);
            wristFillImg = Box("fill", wristGo.transform, 10, 52, 380, 28, Text);
            wristFill = wristFillImg.rectTransform;
            wristNoise = Box("noise", wristGo.transform, 10, 82, 380, 6, new Color(0.9f, 0.2f, 0.1f, 1f)).rectTransform;
            wristGo.SetActive(false);
        }

        private GameObject FloatingText(string name, float height, Color color, out TextMeshProUGUI text)
        {
            var go = new GameObject(name, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            ((RectTransform)go.transform).sizeDelta = new Vector2(2600, 100);
            go.transform.localScale = Vector3.one * (height / 60f);
            text = Label(go.transform, 0, 0, 2600, 100, "", 60, color, TextAlignmentOptions.Center, true);
            text.enableAutoSizing = false;
            text.fontSize = 60;
            go.SetActive(false);
            return go;
        }

        /// <summary>UI drawn over the world and over the horror overlays (queue 4900 &gt; 4004).</summary>
        private static Material OnTop(Material m)
        {
            m.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)CompareFunction.Always);
            m.renderQueue = 4900;
            return m;
        }

        private Material uiMat;

        private Image Box(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            PlaceRect(go, parent, x, y, w, h);
            var img = go.AddComponent<Image>();
            img.sprite = rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
            img.raycastTarget = false;
            img.material = uiMat ?? (uiMat = OnTop(new Material(Shader.Find("UI/Default"))));
            return img;
        }

        private TextMeshProUGUI Label(Transform parent, float x, float y, float w, float h, string text, float size, Color color, TextAlignmentOptions align, bool bold)
        {
            var go = new GameObject("text", typeof(RectTransform));
            PlaceRect(go, parent, x, y, w, h);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (Fx.Font != null) t.font = Fx.Font;
            t.text = Fx.Safe(text);
            // Gorilla Tag's font is wide: start smaller and shrink to fit.
            t.enableAutoSizing = true;
            t.fontSizeMax = size * 0.85f;
            t.fontSizeMin = size * 0.4f;
            t.fontSize = size * 0.85f;
            t.color = color;
            t.alignment = align;
            t.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            OnTop(t.fontMaterial);
            return t;
        }

        private Clickable Button(Transform parent, float x, float y, float w, float h, string label, Color normal, Color hover, float size, Action onClick, Color? textColor = null)
        {
            var bg = Box("button", parent, x, y, w, h, normal);
            if (!string.IsNullOrEmpty(label)) Label(bg.transform, 0, 0, w, h, label, size, textColor ?? Text, TextAlignmentOptions.Center, true);
            var c = new Clickable { Rt = bg.rectTransform, Bg = bg, Normal = normal, Hover = hover, OnClick = onClick, Label = label };
            clickables.Add(c);
            return c;
        }

        private static void PlaceRect(GameObject go, Transform parent, float x, float y, float w, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private RawImage RawImg(string name, Transform parent, float x, float y, float w, float h, Texture tex, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            PlaceRect(go, parent, x, y, w, h);
            var r = go.AddComponent<RawImage>();
            r.texture = tex;
            r.color = color;
            r.raycastTarget = false;
            r.material = uiMat ?? (uiMat = OnTop(new Material(Shader.Find("UI/Default"))));
            return r;
        }

        private static Texture2D scanTex, edgeTex, eyeTex, pupilTex;

        private static Texture2D Make(int w, int h, Func<int, int, Color32> px, FilterMode filter = FilterMode.Point, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = wrap };
            var c = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) c[y * w + x] = px(x, y);
            t.SetPixels32(c);
            t.Apply();
            return t;
        }

        private static Texture2D Scanlines() => scanTex ?? (scanTex = Make(1, 4, (x, y) => y == 0 ? new Color32(120, 0, 0, 90) : new Color32(0, 0, 0, 0), FilterMode.Point, TextureWrapMode.Repeat));

        private static Texture2D Edges() => edgeTex ?? (edgeTex = Make(64, 64, (x, y) =>
        {
            float dx = Mathf.Abs(x + 0.5f - 32f) / 32f, dy = Mathf.Abs(y + 0.5f - 32f) / 32f;
            float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1f, Mathf.Max(dx, dy)));
            return new Color32(0, 0, 0, (byte)(a * 230));
        }, FilterMode.Bilinear));

        /// <summary>A bloodshot eye (no pupil: that is a separate image that moves).</summary>
        private static Texture2D EyeTex() => eyeTex ?? (eyeTex = Make(46, 30, (x, y) =>
        {
            float u = (x + 0.5f) / 46f * 2f - 1f, v = (y + 0.5f) / 30f * 2f - 1f;
            float lid = 1f - u * u; // almond outline
            if (Mathf.Abs(v) > lid) return new Color32(0, 0, 0, 0);
            float r = Mathf.Sqrt(u * u * 2.2f + v * v);
            if (r < 0.55f) return new Color32((byte)(200 + 55 * (0.55f - r)), (byte)(20 + 60 * (0.55f - r)), 10, 255); // iris
            bool vein = ((x * 7 + y * 13) % 11) == 0;
            return vein ? new Color32(150, 10, 10, 255) : new Color32(205, 190, 175, 255);
        }));

        private static Texture2D PupilTex() => pupilTex ?? (pupilTex = Make(7, 22, (x, y) =>
        {
            float u = (x + 0.5f) / 7f * 2f - 1f, v = (y + 0.5f) / 22f * 2f - 1f;
            return u * u + v * v * 0.15f < 0.9f ? new Color32(0, 0, 0, 255) : new Color32(0, 0, 0, 0);
        }));

        private static Sprite Rounded()
        {
            const int S = 64, R = 16;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(R - x - 0.5f, x + 0.5f - (S - R))), dy = Mathf.Max(0, Mathf.Max(R - y - 0.5f, y + 0.5f - (S - R)));
                    float a = Mathf.Clamp01(R - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(R, R, R, R));
        }
    }
}
