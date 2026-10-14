using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace DualPulse
{
    public class StatusPill : DControl
    {
        public bool Good; public string Device, Detail;
        public StatusPill() { BackColor = Theme.Bg; Height = 40; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; Theme.Smooth(g); g.Clear(BackColor);
            float s = S;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (var p = Theme.Round(r, Height / 2f)) { using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p); using (var pen = new Pen(Theme.Border)) g.DrawPath(pen, p); }
            using (var b = new SolidBrush(Good ? Theme.Ok : Theme.Bad)) g.FillEllipse(b, 14 * s, Height / 2f - 4 * s, 8 * s, 8 * s);
            var tf = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(g, Device, Theme.Bold, new Rectangle((int)(30 * s), (int)(3 * s), Width - (int)(42 * s), (int)(18 * s)), Theme.Text, tf);
            TextRenderer.DrawText(g, Detail, Theme.Small, new Rectangle((int)(30 * s), (int)(20 * s), Width - (int)(42 * s), (int)(16 * s)), Theme.Muted, tf);
        }
    }

    public class MainForm : Form
    {
        readonly Settings S;
        readonly Engine E;
        readonly NotifyIcon tray;
        readonly List<NavButton> nav = new List<NavButton>();
        readonly List<Panel> pages = new List<Panel>();
        readonly Label title, subtitle;
        readonly StatusPill kbPill, mousePill;
        readonly DevicePreview preview;
        readonly List<SchemeTile> tiles = new List<SchemeTile>();
        Segmented effectSeg, mouseMode, mouseRate, kbPoll;
        Swatch sw1, sw2;
        Slider speed, bright, dpi, kbBright;
        ComboBox mouseCombo, kbCombo;
        Panel deviceList;
        Card trouble;
        Toggle altFormat;
        Label dpiNow, devCount, mouseNote, kbNote;
        readonly List<Control> sensorControls = new List<Control>(), hwControls = new List<Control>();
        bool reallyExit, syncing;
        readonly Timer timer;
        event Action readWin;

        static readonly string[] PageTitles = { "Lighting", "Devices", "Mouse", "Keyboard", "App" };
        static readonly string[] PageSubs = {
            "One color scheme across every device, in sync.",
            "Everything DualPulse found on this PC.",
            "Sensor, polling and Windows pointer settings.",
            "Hardware and typing settings.",
            "Startup behaviour and how DualPulse talks to your hardware." };

        public MainForm(Settings s, Engine e, bool startHidden)
        {
            S = s; E = e;
            Text = "DualPulse";
            Icon = Brand.AppIcon(64);
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(1040, 690);
            MinimumSize = new Size(1056, 729);
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            SuspendLayout();

            var side = new Panel { BackColor = Theme.Side, Location = new Point(0, 0), Size = new Size(210, 690), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom };
            Controls.Add(side);
            side.Controls.Add(new PictureBox { Image = Brand.Logo(40), Location = new Point(20, 22), Size = new Size(40, 40), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Theme.Side });
            side.Controls.Add(new Label { Text = "DualPulse", Font = Theme.F(14f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Theme.Side, Location = new Point(68, 22), AutoSize = true });
            side.Controls.Add(new Label { Text = "keyboard + mouse", Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Side, Location = new Point(70, 46), AutoSize = true });
            string[] glyphs = { "✦", "☰", "◉", "⌨", "⚙" };
            for (int i = 0; i < PageTitles.Length; i++)
            {
                var b = new NavButton(glyphs[i], PageTitles[i]) { Location = new Point(0, 96 + i * 48), Width = 210 };
                int idx = i; b.Click += (o, a) => ShowPage(idx);
                nav.Add(b); side.Controls.Add(b);
            }
            devCount = new Label { Font = Theme.Small, ForeColor = Color.FromArgb(90, 95, 112), BackColor = Theme.Side, Location = new Point(20, 656), AutoSize = true, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            side.Controls.Add(devCount);

            title = new Label { Font = Theme.H1, ForeColor = Theme.Text, BackColor = Theme.Bg, Location = new Point(234, 18), AutoSize = true };
            subtitle = new Label { Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Bg, Location = new Point(236, 48), AutoSize = true };
            Controls.Add(title); Controls.Add(subtitle);
            kbPill = new StatusPill { Location = new Point(626, 20), Size = new Size(196, 40) };
            mousePill = new StatusPill { Location = new Point(830, 20), Size = new Size(186, 40) };
            Controls.Add(kbPill); Controls.Add(mousePill);

            preview = new DevicePreview { Engine = E };
            pages.Add(BuildLighting());
            pages.Add(BuildDevices());
            pages.Add(BuildMouse());
            pages.Add(BuildKeyboard());
            pages.Add(BuildApp());
            foreach (var p in pages) { p.Location = new Point(210, 74); p.Size = new Size(830, 616); p.BackColor = Theme.Bg; Controls.Add(p); }
            ResumeLayout(false);
            ShowPage(0);

            tray = new NotifyIcon { Icon = Brand.AppIcon(32), Text = "DualPulse", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Open DualPulse", null, (o, a) => ShowFromTray());
            var schemes = new ToolStripMenuItem("Color scheme");
            foreach (var sc in Scheme.All) { var c = sc; schemes.DropDownItems.Add(sc.Name, null, (o, a) => ApplyScheme(c)); }
            menu.Items.Add(schemes);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit (hand control back to devices)", null, (o, a) => { reallyExit = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += (o, a) => ShowFromTray();

            E.DevicesChanged += () => { try { BeginInvoke(new Action(DevicesChanged)); } catch { } };
            E.ReadingsChanged += d => { try { BeginInvoke(new Action(() => { if (d == SelMouse) LoadMouse(); if (d == SelKb) LoadKb(); RefreshStatus(); })); } catch { } };
            HandleCreated += (o, a) => DevicesChanged();

            timer = new Timer { Interval = 33 };
            int tick = 0;
            timer.Tick += (o, a) =>
            {
                if (Visible && WindowState != FormWindowState.Minimized && pages[0].Visible) preview.Invalidate();
                if (tick++ % 15 == 0) RefreshStatus();
            };
            timer.Start();

            if (startHidden) { WindowState = FormWindowState.Minimized; ShowInTaskbar = false; Load += (o, a) => BeginInvoke(new Action(Hide)); }
        }

        public void ExitApp() { reallyExit = true; Close(); }

        public void ShowFromTray()
        {
            ShowInTaskbar = true; Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ShowPage(int i)
        {
            for (int k = 0; k < pages.Count; k++) { pages[k].Visible = k == i; nav[k].Active = k == i; nav[k].Invalidate(); }
            title.Text = PageTitles[i]; subtitle.Text = PageSubs[i];
            if (i == 2 || i == 3) { if (readWin != null) try { readWin(); } catch { } }
        }

        Device[] Sensors { get { return E.Devices.Where(d => d.Sensor != null).ToArray(); } }
        Device[] HwDevices { get { return E.Devices.Where(d => d.Hw != null).ToArray(); } }
        Device SelMouse { get { var a = Sensors; return mouseCombo.SelectedIndex >= 0 && mouseCombo.SelectedIndex < a.Length ? a[mouseCombo.SelectedIndex] : null; } }
        Device SelKb { get { var a = HwDevices; return kbCombo.SelectedIndex >= 0 && kbCombo.SelectedIndex < a.Length ? a[kbCombo.SelectedIndex] : null; } }

        static int Cached(Dictionary<Device, int> d, Device k) { if (k == null) return -1; lock (d) { int v; return d.TryGetValue(k, out v) ? v : -1; } }

        void RefreshStatus()
        {
            var devs = E.Devices;
            var kb = devs.FirstOrDefault(d => d.Kind == DeviceKind.Keyboard);
            var ms = devs.FirstOrDefault(d => d.Kind == DeviceKind.Mouse);
            kbPill.Good = kb != null; mousePill.Good = ms != null;
            kbPill.Device = kb != null ? kb.Name : "Keyboard";
            mousePill.Device = ms != null ? ms.Name : "Mouse";
            kbPill.Detail = kb == null ? "Not detected" : (E.Lit(kb) ? "Lighting: DualPulse" : kb.HasLighting ? "Lighting: onboard" : "Connected")
                + (Cached(E.PollNow, kb) > 0 ? "  •  " + Cached(E.PollNow, kb) + " Hz" : "");
            mousePill.Detail = ms == null ? "Not detected" : (ms.Sensor != null && ms.Sensor.HasOnboardMode ? (E.Host(ms) ? "Live" : "Onboard") : "Connected")
                + (Cached(E.DpiNow, ms) > 0 ? "  •  " + Cached(E.DpiNow, ms) + " DPI" : "");
            kbPill.Invalidate(); mousePill.Invalidate();
            devCount.Text = "v1.0  •  " + devs.Length + (devs.Length == 1 ? " device" : " devices");
            var m = SelMouse;
            if (dpiNow != null) dpiNow.Text = m == null ? "" : "Device reports " + Cached(E.DpiNow, m) + " DPI" + (Cached(E.RateNow, m) > 0 ? "  •  " + Cached(E.RateNow, m) + " Hz" : "");
        }

        // ---------- helpers ----------
        static Card AddCard(Panel page, string t, int x, int y, int w, int h)
        {
            var c = new Card(t) { Location = new Point(x, y), Size = new Size(w, h) };
            page.Controls.Add(c); return c;
        }
        static T Put<T>(Control c, T ctl, int x, int y, int w) where T : Control
        {
            ctl.Location = new Point(x, y); ctl.Width = w; ctl.BackColor = Theme.Card; c.Controls.Add(ctl); return ctl;
        }
        static Label Note(Control c, string text, int x, int y, int w, int h = 36)
        {
            var l = new Label { Text = text, Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Card, Location = new Point(x, y), Size = new Size(w, h) };
            c.Controls.Add(l); return l;
        }
        static ComboBox Combo(Control c, int x, int y, int w)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.Body, Location = new Point(x, y), Width = w };
            c.Controls.Add(cb); return cb;
        }

        // ---------- pages ----------
        Panel BuildLighting()
        {
            var p = new Panel();
            var pc = AddCard(p, "Live preview", 24, 0, 782, 238);
            Put(pc, preview, 16, 36, 750).Height = 192;

            var sc = AddCard(p, "Color schemes", 24, 250, 782, 128);
            for (int i = 0; i < Scheme.All.Length; i++)
            {
                var t = Put(sc, new SchemeTile(Scheme.All[i]), 16 + i * 63, 36, 58);
                t.Height = 80;
                var s = Scheme.All[i]; t.Click += (o, a) => ApplyScheme(s);
                tiles.Add(t);
            }

            var ec = AddCard(p, "Effect", 24, 390, 782, 90);
            effectSeg = Put(ec, new Segmented("Static", "Breathing", "Spectrum", "Rainbow wave", "Gradient wave", "Starlight", "Off"), 16, 38, 750);
            effectSeg.Selected = (int)S.Effect;
            effectSeg.Changed += (o, a) => { S.Effect = (Effect)effectSeg.Selected; MarkScheme(); S.Save(); };

            var cc = AddCard(p, "Colors & motion", 24, 492, 782, 108);
            sw1 = Put(cc, new Swatch("Primary"), 16, 44, 150); sw1.Value = S.C1;
            sw2 = Put(cc, new Swatch("Secondary"), 176, 44, 150); sw2.Value = S.C2;
            sw1.Changed += (o, a) => { S.C1 = sw1.Value; MarkScheme(); S.Save(); };
            sw2.Changed += (o, a) => { S.C2 = sw2.Value; MarkScheme(); S.Save(); };
            speed = Put(cc, new Slider { Caption = "Speed", Format = v => v + "%" }, 352, 40, 196);
            speed.Value = S.Speed;
            speed.ValueChanged += (o, a) => S.Speed = speed.Value;
            speed.Committed += (o, a) => S.Save();
            bright = Put(cc, new Slider { Caption = "Brightness", Format = v => v + "%" }, 566, 40, 200);
            bright.Value = S.Brightness;
            bright.ValueChanged += (o, a) => S.Brightness = bright.Value;
            bright.Committed += (o, a) => S.Save();
            MarkScheme();
            return p;
        }

        Panel BuildDevices()
        {
            var p = new Panel();
            var c = AddCard(p, "Detected devices", 24, 0, 782, 470);
            deviceList = new Panel { Location = new Point(12, 36), Size = new Size(758, 424), BackColor = Theme.Card, AutoScroll = true };
            c.Controls.Add(deviceList);
            var hc = AddCard(p, "", 24, 482, 782, 118);
            var scan = Put(hc, new FlatButton("Rescan devices"), 16, 18, 170);
            scan.Click += (o, a) => E.Rescan();
            Note(hc, "DualPulse supports any device with Windows Dynamic Lighting (HID LampArray), Logitech G devices over HID++ 2.0, " +
                "and Corsair v2 devices. Devices appear here automatically when you plug them in.", 202, 16, 560, 40);
            Note(hc, "If a device's lights fight with DualPulse, turn off Settings › Personalization › Dynamic Lighting › \"Use Dynamic Lighting on my devices\", or close the vendor app.", 202, 62, 560, 40);
            return p;
        }

        Panel BuildMouse()
        {
            var p = new Panel();
            var mc = AddCard(p, "Device", 24, 0, 782, 110);
            mouseCombo = Combo(mc, 18, 40, 360);
            mouseMode = Put(mc, new Segmented("Live  •  DualPulse", "Onboard  •  mouse memory"), 392, 36, 374);
            mouseNote = Note(mc, "Live mode applies the settings below in real time. Onboard mode hands the mouse back to its stored profile (DPI buttons work again).", 18, 74, 750);
            mouseCombo.SelectedIndexChanged += (o, a) => LoadMouse();
            mouseMode.Changed += (o, a) =>
            {
                var d = SelMouse; if (d == null || syncing) return;
                S.SetDev(d, "host", mouseMode.Selected == 0);
                if (mouseMode.Selected == 1 && d.HasLighting) S.SetDev(d, "light", false);
                E.Apply(d); S.Save(); RebuildDeviceList();
            };

            var sc = AddCard(p, "Sensor", 24, 122, 384, 300);
            dpi = Put(sc, new Slider { Caption = "DPI", Minimum = 100, Maximum = 25600, Step = 50, Format = v => v.ToString("N0") }, 16, 38, 352);
            dpi.Committed += (o, a) => SetDpi(dpi.Value);
            sensorControls.Add(dpi);
            var presets = S.DpiPresets.Split(',').Select(x => { int v; return int.TryParse(x.Trim(), out v) ? v : 0; }).Where(v => v > 0).Take(5).ToArray();
            for (int i = 0; i < presets.Length; i++)
            {
                int v = presets[i];
                var b = Put(sc, new FlatButton(v.ToString()), 16 + i * 71, 98, 66);
                b.Click += (o, a) => { dpi.Value = v; SetDpi(dpi.Value); };
                sensorControls.Add(b);
            }
            sc.Controls.Add(new Label { Text = "Polling rate", Font = Theme.Body, ForeColor = Theme.Text, BackColor = Theme.Card, Location = new Point(20, 150), AutoSize = true });
            mouseRate = Put(sc, new Segmented("1000 Hz"), 16, 176, 352);
            mouseRate.Changed += (o, a) =>
            {
                var d = SelMouse; if (d == null || syncing || mouseRate.Selected < 0) return;
                S.SetDev(d, "rate", d.Sensor.Rates[mouseRate.Selected]); EnsureLive(d); E.Apply(d); S.Save();
            };
            sensorControls.Add(mouseRate);
            dpiNow = Note(sc, "", 18, 232, 350);
            dpiNow.ForeColor = Theme.A2;

            var wc = AddCard(p, "Windows pointer (all mice)", 422, 122, 384, 300);
            var ps = Put(wc, new Slider { Caption = "Pointer speed", Minimum = 1, Maximum = 20, Format = v => v + " / 20" }, 16, 36, 352);
            var dc = Put(wc, new Slider { Caption = "Double-click time", Minimum = 200, Maximum = 900, Step = 10, Format = v => v + " ms" }, 16, 92, 352);
            var sl = Put(wc, new Slider { Caption = "Scroll lines per notch", Minimum = 1, Maximum = 20 }, 16, 148, 352);
            var pr = Put(wc, new Toggle("Enhance pointer precision") { Hint = "Windows mouse acceleration (off is best for gaming)", Height = 48 }, 16, 208, 352);
            ps.Committed += (o, a) => Win.PointerSpeed = ps.Value;
            dc.Committed += (o, a) => Win.DoubleClick = dc.Value;
            sl.Committed += (o, a) => Win.ScrollLines = sl.Value;
            pr.Changed += (o, a) => { if (!syncing) Win.Precision = pr.On; };
            readWin += () => { ps.Value = Win.PointerSpeed; dc.Value = Win.DoubleClick; sl.Value = Math.Max(1, Win.ScrollLines); syncing = true; pr.On = Win.Precision; syncing = false; };
            return p;
        }

        Panel BuildKeyboard()
        {
            var p = new Panel();
            var dc = AddCard(p, "Device", 24, 0, 782, 96);
            kbCombo = Combo(dc, 18, 40, 360);
            kbNote = Note(dc, "", 392, 40, 374, 40);
            kbCombo.SelectedIndexChanged += (o, a) => LoadKb();

            var hc = AddCard(p, "Hardware", 24, 108, 384, 200);
            kbBright = Put(hc, new Slider { Caption = "Hardware LED brightness", Format = v => v + "%" }, 16, 36, 352);
            kbBright.Committed += (o, a) => { var d = SelKb; if (d == null) return; S.SetDev(d, "bright", kbBright.Value); E.Apply(d); S.Save(); };
            hwControls.Add(kbBright);
            hc.Controls.Add(new Label { Text = "Polling rate", Font = Theme.Body, ForeColor = Theme.Text, BackColor = Theme.Card, Location = new Point(20, 104), AutoSize = true });
            kbPoll = Put(hc, new Segmented("1000 Hz"), 16, 130, 352);
            kbPoll.Changed += (o, a) =>
            {
                var d = SelKb; if (d == null || syncing || kbPoll.Selected < 0) return;
                S.SetDev(d, "poll", d.Hw.PollRates[kbPoll.Selected]); E.Apply(d); S.Save();
            };
            hwControls.Add(kbPoll);

            var tc = AddCard(p, "Typing (Windows, all keyboards)", 422, 108, 384, 200);
            var rd = Put(tc, new Slider { Caption = "Key repeat delay", Minimum = 0, Maximum = 3, Format = v => (250 + v * 250) + " ms" }, 16, 36, 352);
            var rr = Put(tc, new Slider { Caption = "Key repeat rate", Minimum = 0, Maximum = 31, Format = v => (v == 31 ? "fastest" : v == 0 ? "slowest" : v.ToString()) }, 16, 96, 352);
            rd.Committed += (o, a) => Win.RepeatDelay = rd.Value;
            rr.Committed += (o, a) => Win.RepeatRate = rr.Value;
            readWin += () => { rd.Value = Win.RepeatDelay; rr.Value = Win.RepeatRate; };
            var test = new TextBox { Location = new Point(20, 156), Width = 344, BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Text = "Hold a key here to test repeat..." };
            test.GotFocus += (o, a) => { if (test.Text.StartsWith("Hold")) test.Text = ""; };
            tc.Controls.Add(test);

            trouble = AddCard(p, "Troubleshooting", 24, 320, 782, 100);
            altFormat = Put(trouble, new Toggle("Alternative color data format") { Hint = "Turn this on only if key colors look scrambled or shifted on this keyboard", Height = 48 }, 16, 36, 750);
            altFormat.Changed += (o, a) => { var d = SelKb; if (d == null || syncing) return; S.SetDev(d, "alt", altFormat.On); E.Apply(d); S.Save(); };
            return p;
        }

        Panel BuildApp()
        {
            var p = new Panel();
            var sc = AddCard(p, "Startup", 24, 0, 782, 150);
            var au = Put(sc, new Toggle("Start with Windows") { Hint = "Starts minimized in the tray and re-applies your settings", Height = 48 }, 16, 36, 750);
            var ct = Put(sc, new Toggle("Closing the window keeps DualPulse running in the tray") { Hint = "Needed for animated lighting and live mouse settings", Height = 48 }, 16, 90, 750);
            au.On = Win.Autostart; ct.On = S.CloseToTray;
            au.Changed += (o, a) => { try { Win.Autostart = au.On; } catch (Exception ex) { MessageBox.Show(this, ex.Message, "DualPulse"); } };
            ct.Changed += (o, a) => { S.CloseToTray = ct.On; S.Save(); };

            var ac = AddCard(p, "How it works", 24, 162, 782, 300);
            ac.Controls.Add(new Label
            {
                Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Card, Location = new Point(18, 38), Size = new Size(746, 210),
                Text =
                    "DualPulse talks to your devices directly over USB HID. No vendor software has to run alongside it.\r\n\r\n" +
                    "•  Windows Dynamic Lighting (HID LampArray): any compliant keyboard, mouse, headset or accessory, from any brand.\r\n" +
                    "•  Logitech G (HID++ 2.0): lighting zones, DPI, polling rate and onboard/host mode, wired or via Lightspeed receiver.\r\n" +
                    "•  Corsair (protocol v2): brightness and polling on any v2 device, plus per-key lighting on supported models.\r\n" +
                    "•  Windows: pointer speed, acceleration, scroll lines, double-click time and key repeat for every mouse and keyboard.\r\n\r\n" +
                    "Choosing Exit hands every device back to its own onboard memory."
            });
            var openDir = Put(ac, new FlatButton("Open settings folder"), 600, 250, 164);
            openDir.Click += (o, a) => { Directory.CreateDirectory(Settings.Dir); System.Diagnostics.Process.Start("explorer.exe", "\"" + Settings.Dir + "\""); };

            var xc = AddCard(p, "", 24, 474, 782, 70);
            var quit = Put(xc, new FlatButton("Exit DualPulse"), 16, 18, 180);
            quit.Click += (o, a) => { reallyExit = true; Close(); };
            Note(xc, "Hands lighting back to every device and puts mice back on their onboard profiles.", 212, 26, 540);
            return p;
        }

        // ---------- dynamic device state ----------
        void DevicesChanged()
        {
            RebuildDeviceList();
            var ms = SelMouse; var kb = SelKb;
            Fill(mouseCombo, Sensors, ms, "No mouse with adjustable DPI detected");
            Fill(kbCombo, HwDevices, kb, "No keyboard with hardware settings detected");
            LoadMouse(); LoadKb(); RefreshStatus();
        }

        static void Fill(ComboBox cb, Device[] devs, Device keep, string empty)
        {
            cb.BeginUpdate();
            cb.Items.Clear();
            foreach (var d in devs) cb.Items.Add(d.Name);
            if (devs.Length == 0) { cb.Items.Add(empty); cb.SelectedIndex = 0; cb.Enabled = false; }
            else { cb.Enabled = true; int i = Array.IndexOf(devs, keep); cb.SelectedIndex = i >= 0 ? i : 0; }
            cb.EndUpdate();
        }

        void RebuildDeviceList()
        {
            deviceList.SuspendLayout();
            foreach (Control c in deviceList.Controls.Cast<Control>().ToArray()) { deviceList.Controls.Remove(c); c.Dispose(); }
            var devs = E.Devices.OrderBy(d => d.Kind).ThenBy(d => d.Name).ToArray();
            int y = 0;
            foreach (var d in devs)
            {
                var row = new DeviceRow(d, E.Lit(d)) { Location = new Point(0, y), Width = deviceList.Width - 20 };
                if (row.Sync != null)
                {
                    var dev = d;
                    row.Sync.Changed += (o, a) => { S.SetDev(dev, "light", row.Sync.On); if (row.Sync.On && dev.Sensor != null) S.SetDev(dev, "host", true); E.Apply(dev); S.Save(); LoadMouse(); };
                }
                deviceList.Controls.Add(row);
                y += row.Height;
            }
            if (devs.Length == 0)
                deviceList.Controls.Add(new Label { Text = "Searching for devices...", Font = Theme.Body, ForeColor = Theme.Muted, BackColor = Theme.Card, Location = new Point(8, 8), AutoSize = true });
            deviceList.ResumeLayout();
        }

        void LoadMouse()
        {
            var d = SelMouse;
            foreach (var c in sensorControls) c.Enabled = d != null;
            mouseMode.Enabled = d != null && d.Sensor.HasOnboardMode;
            if (d == null) return;
            var sn = d.Sensor;
            syncing = true;
            mouseMode.Selected = E.Host(d) ? 0 : 1;
            dpi.Minimum = sn.DpiMin; dpi.Maximum = sn.DpiMax; dpi.Step = sn.DpiStep;
            int cur = Cached(E.DpiNow, d); if (cur <= 0) cur = S.DevInt(d, "dpi", 0);
            dpi.Value = cur > 0 ? cur : 1600;
            mouseRate.Items = sn.Rates.Length > 0 ? sn.Rates.Select(r => r + " Hz").ToArray() : new[] { "n/a" };
            mouseRate.Enabled = sn.Rates.Length > 0;
            int rate = Cached(E.RateNow, d); if (rate <= 0) rate = S.DevInt(d, "rate", 0);
            mouseRate.Selected = Array.IndexOf(sn.Rates, rate);
            mouseRate.Invalidate();
            mouseNote.Text = sn.HasOnboardMode
                ? "Live mode applies the settings below in real time. Onboard mode hands the mouse back to its stored profile (DPI buttons work again)."
                : "This mouse applies settings directly.";
            syncing = false;
        }

        void LoadKb()
        {
            var d = SelKb;
            foreach (var c in hwControls) c.Enabled = d != null;
            var cd = d as CorsairDevice;
            trouble.Visible = cd != null && cd.HasLighting;
            if (d == null) { kbNote.Text = "Typing settings below still apply to every keyboard."; return; }
            syncing = true;
            kbNote.Text = d.Driver;
            kbBright.Enabled = d.Hw.HasBrightness;
            int b = Cached(E.BrightNow, d); if (b < 0) b = S.DevInt(d, "bright", -1);
            kbBright.Value = b >= 0 ? b : 100;
            kbPoll.Items = d.Hw.PollRates.Select(r => r + " Hz").ToArray();
            int poll = Cached(E.PollNow, d); if (poll <= 0) poll = S.DevInt(d, "poll", 0);
            kbPoll.Selected = Array.IndexOf(d.Hw.PollRates, poll);
            kbPoll.Invalidate();
            altFormat.On = S.DevBool(d, "alt", false);
            syncing = false;
        }

        // ---------- actions ----------
        void EnsureLive(Device d) { if (d.Sensor.HasOnboardMode && !S.DevBool(d, "host", true)) { S.SetDev(d, "host", true); syncing = true; mouseMode.Selected = 0; syncing = false; } }

        void SetDpi(int v) { var d = SelMouse; if (d == null) return; S.SetDev(d, "dpi", v); EnsureLive(d); E.Apply(d); S.Save(); }

        void ApplyScheme(Scheme sc)
        {
            S.Effect = sc.Effect;
            if (sc.Effect != Effect.RainbowWave && sc.Effect != Effect.Spectrum) { S.C1 = sc.C1; S.C2 = sc.C2; }
            effectSeg.Selected = (int)S.Effect; sw1.Value = S.C1; sw2.Value = S.C2;
            MarkScheme(); S.Save();
        }

        void MarkScheme()
        {
            foreach (var t in tiles)
            {
                var sc = t.Scheme;
                bool colorless = sc.Effect == Effect.RainbowWave || sc.Effect == Effect.Spectrum || sc.Effect == Effect.Off;
                t.Active = sc.Effect == S.Effect && (colorless || (sc.C1.ToArgb() == S.C1.ToArgb() && sc.C2.ToArgb() == S.C2.ToArgb()));
                t.Invalidate();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && S.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; Hide();
                tray.ShowBalloonTip(2500, "DualPulse is still running", "Lighting and mouse settings stay active. Right-click the tray icon to exit.", ToolTipIcon.Info);
                return;
            }
            timer.Stop();
            tray.Visible = false;
            S.Save();
            E.Stop();
            base.OnFormClosing(e);
        }
    }
}
