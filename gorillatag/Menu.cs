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
    /// of you again. Pages: main (sanity, start/pause, intensity), events (each one on/off), comfort and diary.
    /// A small black panel with hairline blood-red edges that beat with the tension, a red glow breathing behind it,
    /// text-only options underlined when chosen. Also draws the sanity line on the left wrist.
    /// Desktop test mode: Tab toggles, the view aims, left click.
    /// </summary>
    public class Menu
    {
        private const float W = 600f, H = 440f, Width = 0.62f, Distance = 0.65f, Pad = 32f;

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
        private RawImage scan, glow;
        private readonly List<Image> frame = new List<Image>();
        private Vector2 hitLocal;
        private float nextFace, nextLine, blinkUntil, nextBlink;
        private const string TitleText = "THE LAST WITNESS";
        private GameObject root, laserGo, countGo, wristGo;
        private Transform dot;
        private LineRenderer laser;
        private TextMeshProUGUI countdown, status, percent, wristText;
        private RectTransform fill, wristFill, wristNoise;
        private Image fillImg, wristFillImg;
        private readonly List<Clickable> clickables = new List<Clickable>();
        private Page page;
        private bool byHeld, holdDone, lastTrigger;
        private float lastPress = -10f, holdStart = -1f;

        private static readonly Color Panel = new Color(0.02f, 0.015f, 0.015f, 1f), Bone = new Color(0.88f, 0.84f, 0.8f, 1f),
            Dim = new Color(0.48f, 0.44f, 0.42f, 1f), Blood = new Color(0.86f, 0.07f, 0.05f, 1f), BloodDim = new Color(0.45f, 0.05f, 0.04f, 1f),
            Hair = new Color(0.55f, 0.04f, 0.03f, 0.35f), Track = new Color(0.12f, 0.1f, 0.1f, 1f),
            Clear = new Color(0.6f, 0.03f, 0.03f, 0f), Hover = new Color(0.6f, 0.03f, 0.03f, 0.2f), Deep = new Color(0.32f, 0.02f, 0.02f, 1f);

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
            // A little below the eyes and tilted back towards them, like a sheet held up to read.
            Vector3 p = head.position + fwd * Distance - Vector3.up * 0.08f;
            root.transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position, Vector3.up));
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

        private static Color SanityColor(float s) => Color.Lerp(Blood, Bone, s / 100f);

        private string StatusText()
        {
            if (!allowed()) return "Desligado em salas públicas: jogue sozinho ou numa sala privada.";
            if (Events.Ended) return Events.Ending + " Comece outra noite.";
            if (!Settings.Running) return "Parado. A floresta espera você.";
            string s = "Ativo · " + Settings.DiffName(Settings.Diff.Value);
            if (Director.Paused) s += " · tudo parado com o menu aberto";
            return s;
        }

        private void Live()
        {
            if (page != Page.Main || fill == null) return;
            float s = Director.Sanity;
            fill.sizeDelta = new Vector2((W - 2 * Pad) * Mathf.Max(0f, s) / 100f, fill.sizeDelta.y);
            fillImg.color = SanityColor(s);
            percent.text = $"SANIDADE  {s:0}%";
            status.text = Fx.Safe(StatusText());
            if (actText != null)
                actText.text = Fx.Safe("ATO " + Roman[Mathf.Clamp(Director.Act - 1, 0, Roman.Length - 1)] +
                    (Settings.Mode.Value == GameMode.Fitas ? $"  ·  FITAS {Events.Found.Count}/{Events.TapeCount}" : ""));
        }

        private void Wrist(Transform head)
        {
            bool show = Settings.Wrist.Value && Settings.Running && allowed() && GorillaTagger.Instance != null;
            wristGo.SetActive(show);
            if (!show) return;
            var hand = GorillaTagger.Instance.leftHandTransform;
            if (hand == null) return;
            Vector3 toHead = (head.position - hand.position).normalized;
            Vector3 p = hand.position + toHead * 0.06f + Vector3.up * 0.06f;
            wristGo.transform.SetPositionAndRotation(p, Quaternion.LookRotation(p - head.position, Vector3.up));
            float s = Director.Sanity;
            bool broken = Fx.Glitch > 0f;
            wristFill.sizeDelta = new Vector2(280f * (broken ? Director.Rf(0f, 1f) : Mathf.Max(0f, s) / 100f), wristFill.sizeDelta.y);
            wristFillImg.color = SanityColor(s);
            wristNoise.sizeDelta = new Vector2(280f * Hearing.Noise, 2f); // how loud you have been: it is listening
            string tapes = Settings.Mode.Value == GameMode.Fitas ? $"   {Events.Found.Count}/{Events.TapeCount}" : "";
            wristText.text = broken ? "??%" : $"{s:0}%{tapes}";
        }

        // ---------- layout ----------

        private void Rebuild()
        {
            foreach (Transform child in root.transform) UnityEngine.Object.Destroy(child.gameObject);
            clickables.Clear();
            frame.Clear();
            fill = null;
            actText = null;
            // A red glow breathing behind the panel, the panel, faint red scanlines, a hairline frame.
            glow = RawImg("glow", root.transform, -70, -70, W + 140, H + 140, GlowTex(), new Color(1f, 1f, 1f, 0.4f));
            Box("panel", root.transform, 0, 0, W, H, Panel);
            scan = RawImg("scanlines", root.transform, 0, 0, W, H, Scanlines(), new Color(1f, 1f, 1f, 0.16f));
            frame.Add(Box("frame", root.transform, 0, 0, W, 1.5f, Blood));
            frame.Add(Box("frame", root.transform, 0, H - 1.5f, W, 1.5f, Blood));
            frame.Add(Box("frame", root.transform, 0, 0, 1.5f, H, Blood));
            frame.Add(Box("frame", root.transform, W - 1.5f, 0, 1.5f, H, Blood));

            // An eye beside the title that follows where you point, and blinks.
            var e = RawImg("eye", root.transform, Pad, 30, 40, 26, EyeTex(), Color.white);
            eye = e.rectTransform;
            var pu = RawImg("pupil", eye, 0, 0, 6, 19, PupilTex(), Color.white);
            pupil = pu.rectTransform;
            pupil.anchorMin = pupil.anchorMax = pupil.pivot = new Vector2(0.5f, 0.5f);
            pupil.anchoredPosition = Vector2.zero;
            title = Label(root.transform, Pad + 54, 24, 420, 40, TitleText, 30, Blood, TextAlignmentOptions.Left, true);
            title.characterSpacing = 12f;
            subtitle = Label(root.transform, Pad + 56, 62, 420, 22, "", 15, BloodDim, TextAlignmentOptions.Left, false);
            subtitle.fontStyle = FontStyles.Italic;
            nextLine = 0f;
            TextButton(root.transform, W - Pad - 30, 26, 34, 34, "X", 22, Dim, Close, "X");
            Box("rule", root.transform, Pad, 98, W - 2 * Pad, 1f, Hair);

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

        /// <summary>The menu is part of the horror: the title breaks, lines change, the eye watches, the frame beats, and late at night it is behind the panel.</summary>
        private void Animate()
        {
            float ins = Director.Insanity;
            if (scan != null) scan.uvRect = new Rect(0f, -Time.time * 0.15f, 1f, 40f);
            // A heartbeat (lub-dub) that quickens with the tension, on the frame and the glow.
            float bpm = Mathf.Lerp(55f, 120f, Mathf.Clamp01(Director.Tension + ins * 0.5f));
            float ph = Time.time * bpm / 60f % 1f;
            float beat = Mathf.Exp(-ph * 14f) + 0.6f * Mathf.Exp(-Mathf.Max(0f, ph - 0.18f) * 16f) * (ph > 0.18f ? 1f : 0f);
            foreach (var f in frame) f.color = new Color(Blood.r, Blood.g, Blood.b, 0.35f + 0.65f * Mathf.Clamp01(beat));
            if (glow != null) glow.color = new Color(1f, 1f, 1f, 0.22f + 0.35f * Mathf.Clamp01(beat) * (0.5f + ins));
            if (title != null)
            {
                string t = TitleText;
                if (ins > 0.45f && Director.Rng.NextDouble() < 0.004) t = "VOCÊ É A TESTEMUNHA";
                else if (Director.Rng.NextDouble() < 0.05)
                {
                    var c = t.ToCharArray();
                    c[Director.Rng.Next(c.Length)] = "#%&@?!/"[Director.Rng.Next(7)];
                    t = new string(c);
                }
                title.text = Fx.Safe(t);
                title.color = Director.Rng.NextDouble() < 0.03 ? new Color(1f, 0.9f, 0.9f) : Blood;
            }
            if (subtitle != null && Time.time >= nextLine)
            {
                nextLine = Time.time + Director.Rf(5f, 9f);
                subtitle.text = Fx.Safe(Director.Pick(Lines));
            }
            if (eye != null)
            {
                // The eye's centre in the panel's own coordinates (origin in the middle, y up).
                var center = new Vector2(Pad + 20 - W / 2, H / 2 - (30 + 13));
                pupil.anchoredPosition = Vector2.ClampMagnitude((hitLocal - center) * 0.05f, 11f) * new Vector2(1f, 0.25f);
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
            w.transform.position = t.position + t.forward * 0.6f - Vector3.up * h * 0.85f;
            Entity.Face(w.transform, Head.position);
            Fx.Play(Synth.Static(0.12f), t.position, 0.5f, false);
            yield return null;
            yield return null;
            UnityEngine.Object.Destroy(w);
        }

        private void BuildMain()
        {
            bool story = Settings.Mode.Value == GameMode.Fitas;
            percent = Label(root.transform, Pad, 116, 260, 20, "", 14, Dim, TextAlignmentOptions.Left, true);
            percent.characterSpacing = 6f;
            actText = Label(root.transform, W - Pad - 280, 116, 280, 20, "", 14, Blood, TextAlignmentOptions.Right, true);
            actText.characterSpacing = 6f;
            Box("track", root.transform, Pad, 142, W - 2 * Pad, 3f, Track);
            fillImg = Box("fill", root.transform, Pad, 142, W - 2 * Pad, 3f, SanityColor(Director.Sanity));
            fill = fillImg.rectTransform;
            status = Label(root.transform, Pad, 154, W - 2 * Pad, 20, "", 13, Dim, TextAlignmentOptions.Left, false);

            // Mode: changes only while the night is stopped (and starts a new one).
            Caption(196, "MODO");
            Option(150, 188, 190, "AS FITAS", "As Fitas", story, () => SetMode(GameMode.Fitas));
            Option(345, 188, 190, "PESADELO", "Pesadelo sem fim", !story, () => SetMode(GameMode.Pesadelo));
            Label(root.transform, 150, 226, W - Pad - 150, 18, story ? "ache as 8 fitas. sobreviva ao fim." : "sem objetivo. sem fim.", 12, BloodDim, TextAlignmentOptions.Left, false).fontStyle = FontStyles.Italic;

            Caption(262, "INTENSIDADE");
            float x = 150, ow = (W - Pad - 150) / 4f;
            foreach (Difficulty d in Enum.GetValues(typeof(Difficulty)))
            {
                var dd = d;
                string shortName = d == Difficulty.TerrorAbsoluto ? "TERROR" : Settings.DiffName(d).ToUpperInvariant();
                Option(x, 254, ow, shortName, Settings.DiffName(d), Settings.Diff.Value == d, () => { Settings.Diff.Value = dd; Rebuild(); });
                x += ow;
            }

            bool on = Settings.Running;
            string go = on ? "Pausar" : Events.NightStarted && !Events.Ended ? "Continuar a noite" : "Começar a noite";
            Primary(Pad, 306, W - 2 * Pad, 54, go, on, () =>
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

            float lw = (W - 2 * Pad) / 4f;
            TextButton(root.transform, Pad, 374, lw, 30, "EVENTOS", 14, Dim, () => Go(Page.Events), "Eventos");
            TextButton(root.transform, Pad + lw, 374, lw, 30, "CONFORTO", 14, Dim, () => Go(Page.Comfort), "Conforto");
            TextButton(root.transform, Pad + 2 * lw, 374, lw, 30, "DIÁRIO", 14, Dim, () => Go(Page.Diary), "Diário");
            TextButton(root.transform, Pad + 3 * lw, 374, lw, 30, "NOVA NOITE", 14, BloodDim, () => { newNight(); Rebuild(); }, "Nova noite");
            Label(root.transform, Pad, 412, W - 2 * Pad, 16, "B/Y 2x abrir · segurar: trazer · gatilho: escolher" +
                (Entity.HasModel ? " · horror rig: IdkmaneGTH / wiz (CC BY)" : ""), 11, new Color(0.32f, 0.29f, 0.28f), TextAlignmentOptions.Center, false);
            Live();
        }

        private void SetMode(GameMode m)
        {
            if (Settings.Running || Settings.Mode.Value == m) return;
            Settings.Mode.Value = m;
            newNight();
            Rebuild();
        }

        private static readonly string[] Roman = { "I", "II", "III", "IV", "V", "VI", "VII" };

        /// <summary>A sub-page: its name under the title and a way back.</summary>
        private void SubPage(string name)
        {
            var t = Label(root.transform, Pad, 108, 300, 22, name, 15, Bone, TextAlignmentOptions.Left, true);
            t.characterSpacing = 8f;
            TextButton(root.transform, W - Pad - 110, 104, 110, 28, "< VOLTAR", 13, Dim, () => Go(Page.Main), "Voltar");
        }

        /// <summary>The tapes found so far, in full; the rest are blacked out.</summary>
        private void BuildDiary()
        {
            SubPage($"DIÁRIO  {Events.Found.Count}/{Events.TapeCount}");
            for (int i = 0; i < Events.TapeCount; i++)
            {
                bool got = Events.Found.Contains(i);
                var t = Label(root.transform, Pad, 140 + i * 36, W - 2 * Pad, 34, got ? Events.Tapes[i] : $"FITA {i + 1}. ██████ ███ ████████ ██ ███████", 12,
                    got ? Bone : new Color(0.26f, 0.23f, 0.22f), TextAlignmentOptions.TopLeft, false);
                t.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        private void BuildEvents()
        {
            SubPage("EVENTOS");
            var items = new List<(string id, string name)>();
            foreach (var e in director.Events) items.Add((e.Id, e.Name));
            items.AddRange(Director.Layers);
            int rows = Mathf.CeilToInt(items.Count / 3f);
            float cw = (W - 2 * Pad) / 3f, rh = Mathf.Min(24f, (H - 150f - 16f) / rows);
            for (int i = 0; i < items.Count; i++)
            {
                var (id, name) = items[i];
                Check(Pad + (i / rows) * cw, 144 + (i % rows) * rh, cw - 6, rh, name, Settings.IsOn(id), () => Settings.Toggle(id));
            }
        }

        private static float Next(float v, float[] steps)
        {
            for (int i = 0; i < steps.Length; i++) if (v < steps[i] - 0.001f) return steps[i];
            return steps[0];
        }

        private void BuildComfort()
        {
            SubPage("CONFORTO");
            float y = 144;
            void Row(string label, string value, bool lit, Action act)
            {
                var c = TextButton(root.transform, Pad, y, W - 2 * Pad, 28, "", 13, Dim, () => { act(); Rebuild(); }, label);
                Label(c.Rt, 10, 0, 360, 28, label, 13, Bone, TextAlignmentOptions.Left, false);
                Label(c.Rt, W - 2 * Pad - 170, 0, 160, 28, value, 13, lit ? Blood : Dim, TextAlignmentOptions.Right, true).characterSpacing = 4f;
                y += 29;
            }
            void Flag(string label, BepInEx.Configuration.ConfigEntry<bool> e) => Row(label, e.Value ? "LIGADO" : "DESLIGADO", e.Value, () => e.Value = !e.Value);
            void Step(string label, BepInEx.Configuration.ConfigEntry<float> e, float[] steps) => Row(label, $"{e.Value * 100f:0}%", true, () => e.Value = Next(e.Value, steps));
            Row("Forma da entidade", Settings.FormName(Settings.Form.Value).ToUpperInvariant(), true, () => Settings.Form.Value = (EntityForm)(((int)Settings.Form.Value + 1) % 3));
            Flag("Flashes suaves", Settings.SoftFlashes);
            Flag("Sem efeitos de movimento", Settings.NoMovement);
            Flag("Sanidade no pulso", Settings.Wrist);
            Flag("Pausar com o menu aberto", Settings.PauseInMenu);
            Step("Volume dos sustos", Settings.ScareVolume, new[] { 0.25f, 0.5f, 0.75f, 1f });
            Step("Volume geral", Settings.Volume, new[] { 0.25f, 0.5f, 0.75f, 1f });
            Step("Escuridão máxima", Settings.MaxDarkness, new[] { 0.5f, 0.7f, 0.85f, 0.95f });
            Step("Sensibilidade do microfone", Settings.MicSensitivity, new[] { 0.5f, 0.75f, 1f, 1.5f, 2f });
            Label(root.transform, Pad, y + 2, W - 2 * Pad, 26, "Microfone: " + (Hearing.Available ? "ele está ouvindo." : "desligado ou sem permissão no Gorilla Tag (ele não ouve)."),
                12, BloodDim, TextAlignmentOptions.TopLeft, false).textWrappingMode = TextWrappingModes.Normal;
        }

        // ---------- building blocks ----------

        private void Caption(float y, string text)
        {
            var t = Label(root.transform, Pad, y, 120, 20, text, 12, Dim, TextAlignmentOptions.Left, true);
            t.characterSpacing = 6f;
        }

        /// <summary>One choice of a set: plain text, blood red and underlined when chosen.</summary>
        private void Option(float x, float y, float w, string text, string label, bool sel, Action onClick)
        {
            var c = TextButton(root.transform, x, y, w, 34, text, 15, sel ? Blood : Dim, onClick, label);
            if (sel) Box("underline", c.Rt, 14, 31, w - 28, 2f, Blood);
        }

        /// <summary>The main action: a blood-red outline that fills dark red under the pointer (solid when it pauses).</summary>
        private void Primary(float x, float y, float w, float h, string label, bool running, Action onClick)
        {
            Box("outline", root.transform, x, y, w, h, Blood);
            var bg = Box("button", root.transform, x + 1.5f, y + 1.5f, w - 3f, h - 3f, running ? Deep : Panel);
            var t = Label(bg.transform, 0, 0, w - 3f, h - 3f, label.ToUpperInvariant(), 20, running ? Bone : Blood, TextAlignmentOptions.Center, true);
            t.characterSpacing = 14f;
            clickables.Add(new Clickable { Rt = bg.rectTransform, Bg = bg, Normal = running ? Deep : Panel, Hover = new Color(0.45f, 0.03f, 0.03f, 1f), OnClick = onClick, Label = label });
        }

        /// <summary>An event switch: a small square (filled red when on) and its name.</summary>
        private void Check(float x, float y, float w, float h, string name, bool on, Action flip)
        {
            var c = TextButton(root.transform, x, y, w, h, "", 12, Dim, () => { flip(); Rebuild(); }, name);
            Box("box", c.Rt, 6, h / 2 - 4, 8, 8, on ? Blood : Track);
            Label(c.Rt, 22, 0, w - 24, h, name, 12, on ? Bone : Dim, TextAlignmentOptions.Left, false);
        }

        /// <summary>Clickable text with no box: a faint red wash under the pointer.</summary>
        private Clickable TextButton(Transform parent, float x, float y, float w, float h, string text, float size, Color color, Action onClick, string label)
        {
            var bg = Box("button", parent, x, y, w, h, Clear);
            if (!string.IsNullOrEmpty(text))
            {
                var t = Label(bg.transform, 0, 0, w, h, text, size, color, TextAlignmentOptions.Center, true);
                t.characterSpacing = 4f;
            }
            var c = new Clickable { Rt = bg.rectTransform, Bg = bg, Normal = Clear, Hover = Hover, OnClick = onClick, Label = label };
            clickables.Add(c);
            return c;
        }

        private void Build()
        {
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
            laser.startWidth = 0.003f;
            laser.endWidth = 0.0015f;
            var lm = OnTop(new Material(Shader.Find("UI/Default")));
            lm.color = new Color(0.9f, 0.08f, 0.06f, 0.8f);
            laser.sharedMaterial = lm;
            var dg = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(dg.GetComponent<Collider>());
            dg.GetComponent<MeshRenderer>().sharedMaterial = lm;
            dg.transform.SetParent(laserGo.transform, false);
            dg.transform.localScale = Vector3.one * 0.007f;
            dot = dg.transform;
            laserGo.SetActive(false);

            countGo = FloatingText("TLW countdown", 0.04f, Blood, out countdown);

            // Wrist: 300 x 50 canvas units, 0.085 m wide. The number, the tapes, a thin line, and how loud you are.
            wristGo = new GameObject("TLW wrist", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(wristGo);
            wristGo.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)wristGo.transform).sizeDelta = new Vector2(300, 50);
            wristGo.transform.localScale = Vector3.one * (0.085f / 300f);
            Box("bg", wristGo.transform, 0, 0, 300, 50, new Color(0.02f, 0.015f, 0.015f, 0.8f));
            wristText = Label(wristGo.transform, 10, 4, 280, 30, "", 26, Bone, TextAlignmentOptions.Left, true);
            wristText.characterSpacing = 6f;
            Box("track", wristGo.transform, 10, 38, 280, 3, Track);
            wristFillImg = Box("fill", wristGo.transform, 10, 38, 280, 3, Bone);
            wristFill = wristFillImg.rectTransform;
            wristNoise = Box("noise", wristGo.transform, 10, 44, 280, 2, Blood).rectTransform;
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

        /// <summary>UI drawn over the world, the horror overlays and Gorilla Tag's own boards (the last queue; nearest drawn last).</summary>
        private static Material OnTop(Material m)
        {
            m.SetFloat("unity_GUIZTestMode", (float)CompareFunction.Always);
            if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)CompareFunction.Always);
            m.renderQueue = 5000;
            return m;
        }

        private Material uiMat;

        /// <summary>A flat rectangle with sharp corners.</summary>
        private Image Box(string name, Transform parent, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            PlaceRect(go, parent, x, y, w, h);
            var img = go.AddComponent<Image>();
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

        private static Texture2D scanTex, glowTex, eyeTex, pupilTex;

        private static Texture2D Make(int w, int h, Func<int, int, Color32> px, FilterMode filter = FilterMode.Point, TextureWrapMode wrap = TextureWrapMode.Clamp)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = filter, wrapMode = wrap };
            var c = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) c[y * w + x] = px(x, y);
            t.SetPixels32(c);
            t.Apply();
            return t;
        }

        private static Texture2D Scanlines() => scanTex ?? (scanTex = Make(1, 4, (x, y) => y == 0 ? new Color32(120, 0, 0, 60) : new Color32(0, 0, 0, 0), FilterMode.Point, TextureWrapMode.Repeat));

        /// <summary>Red light fading out from a rounded rectangle: the glow around the panel.</summary>
        private static Texture2D GlowTex() => glowTex ?? (glowTex = Make(64, 48, (x, y) =>
        {
            // distance outside the panel's rectangle (which covers the inner 600/740 x 440/580 of the texture)
            float u = Mathf.Abs((x + 0.5f) / 64f * 2f - 1f), v = Mathf.Abs((y + 0.5f) / 48f * 2f - 1f);
            float dx = Mathf.Max(0f, u - 600f / 740f) / (1f - 600f / 740f), dy = Mathf.Max(0f, v - 440f / 580f) / (1f - 440f / 580f);
            float a = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 2.2f);
            return new Color32(200, 8, 4, (byte)(a * 255));
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
    }
}
