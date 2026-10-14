using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace DualPulse
{
    public enum Effect { Static, Breathing, Spectrum, RainbowWave, GradientWave, Starlight, Off }

    public class Scheme
    {
        public string Name; public Effect Effect; public Color C1, C2;
        public Scheme(string n, Effect e, Color a, Color b) { Name = n; Effect = e; C1 = a; C2 = b; }
        public static readonly Scheme[] All = {
            new Scheme("Rainbow", Effect.RainbowWave, Color.Red, Color.Blue),
            new Scheme("Ocean", Effect.GradientWave, Color.FromArgb(0, 90, 255), Color.FromArgb(0, 230, 200)),
            new Scheme("Inferno", Effect.GradientWave, Color.FromArgb(255, 30, 0), Color.FromArgb(255, 170, 0)),
            new Scheme("Neon", Effect.GradientWave, Color.FromArgb(255, 0, 200), Color.FromArgb(0, 220, 255)),
            new Scheme("Toxic", Effect.Breathing, Color.FromArgb(90, 255, 0), Color.FromArgb(0, 0, 0)),
            new Scheme("Sunset", Effect.GradientWave, Color.FromArgb(255, 60, 120), Color.FromArgb(255, 150, 0)),
            new Scheme("Galaxy", Effect.Starlight, Color.FromArgb(255, 255, 255), Color.FromArgb(40, 0, 110)),
            new Scheme("Arctic", Effect.Static, Color.FromArgb(150, 220, 255), Color.Black),
            new Scheme("Prism", Effect.Spectrum, Color.Red, Color.Blue),
            new Scheme("Blood", Effect.Static, Color.FromArgb(255, 0, 0), Color.Black),
            new Scheme("Stealth", Effect.Static, Color.FromArgb(255, 255, 255), Color.Black),
            new Scheme("Dark", Effect.Off, Color.Black, Color.Black),
        };
    }

    // Plain key=value file in %APPDATA%\DualPulse\settings.ini. Per-device values use "dev.<id>.<key>".
    public class Settings
    {
        public Effect Effect = Effect.RainbowWave;
        public Color C1 = Color.FromArgb(124, 92, 255), C2 = Color.FromArgb(0, 212, 255);
        public int Speed = 50, Brightness = 100;
        public bool CloseToTray = true;
        public string DpiPresets = "400,800,1600,3200,6400";
        readonly Dictionary<string, string> dev = new Dictionary<string, string>();

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DualPulse"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public int DevInt(Device d, string key, int def)
        {
            string v; int r;
            lock (dev) return dev.TryGetValue(d.Id + "." + key, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }
        public bool DevBool(Device d, string key, bool def) { return DevInt(d, key, def ? 1 : 0) == 1; }
        public void SetDev(Device d, string key, int v) { lock (dev) dev[d.Id + "." + key] = v.ToString(CultureInfo.InvariantCulture); }
        public void SetDev(Device d, string key, bool v) { SetDev(d, key, v ? 1 : 0); }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var d = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k.StartsWith("dev.")) s.dev[k.Substring(4)] = v; else d[k] = v;
                }
                string x;
                if (d.TryGetValue("Effect", out x)) s.Effect = (Effect)Enum.Parse(typeof(Effect), x);
                if (d.TryGetValue("C1", out x)) s.C1 = ColorTranslator.FromHtml(x);
                if (d.TryGetValue("C2", out x)) s.C2 = ColorTranslator.FromHtml(x);
                s.Speed = Int(d, "Speed", s.Speed); s.Brightness = Int(d, "Brightness", s.Brightness);
                s.CloseToTray = Int(d, "CloseToTray", 1) == 1;
                if (d.TryGetValue("DpiPresets", out x)) s.DpiPresets = x;
            }
            catch { }
            return s;
        }

        static int Int(Dictionary<string, string> d, string k, int def)
        {
            string v; int r;
            return d.TryGetValue(k, out v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : def;
        }

        static string Hex(Color c) { return ColorTranslator.ToHtml(Color.FromArgb(c.R, c.G, c.B)); }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string> {
                    "Effect=" + Effect, "C1=" + Hex(C1), "C2=" + Hex(C2), "Speed=" + Speed, "Brightness=" + Brightness,
                    "CloseToTray=" + (CloseToTray ? 1 : 0), "DpiPresets=" + DpiPresets };
                lock (dev) lines.AddRange(dev.OrderBy(k => k.Key).Select(k => "dev." + k.Key + "=" + k.Value));
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }

    public static class Fx
    {
        public static Color Hsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
            double r = 0, g = 0, b = 0;
            if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
            else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        public static Color Lerp(Color a, Color b, double k)
        {
            k = Math.Max(0, Math.Min(1, k));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * k), (int)(a.G + (b.G - a.G) * k), (int)(a.B + (b.B - a.B) * k));
        }

        public static Color Scale(Color c, double k) { return Lerp(Color.Black, c, k); }

        static double Hash(int n) { double s = Math.Sin(n * 12.9898 + 78.233) * 43758.5453; return s - Math.Floor(s); }

        // x, y: position in the shared layout of all devices (0..1)
        public static Color Eval(Effect e, Color c1, Color c2, int speed, double x, double y, double t, int seed)
        {
            double sp = 0.15 + speed / 100.0 * 1.85;
            switch (e)
            {
                case Effect.Static: return c1;
                case Effect.Breathing:
                    {
                        double ph = t * sp * 0.5;
                        double k = (1 - Math.Cos(2 * Math.PI * (ph - Math.Floor(ph)))) / 2;
                        bool alt = (c2.R | c2.G | c2.B) != 0 && ((long)Math.Floor(ph)) % 2 == 1;
                        return Scale(alt ? c2 : c1, k);
                    }
                case Effect.Spectrum: return Hsv(t * sp * 60, 1, 1);
                case Effect.RainbowWave: return Hsv(x * 300 - t * sp * 140 + y * 30, 1, 1);
                case Effect.GradientWave: return Lerp(c1, c2, (Math.Sin(2 * Math.PI * (x * 0.9 - t * sp * 0.35)) + 1) / 2);
                case Effect.Starlight:
                    {
                        double ph = t * sp * 0.3 + Hash(seed) * 7;
                        double f = ph - Math.Floor(ph);
                        double k = Hash(seed * 31 + (int)Math.Floor(ph)) < 0.35 && f < 0.3 ? Math.Sin(Math.PI * f / 0.3) : 0;
                        return Lerp(c2, c1, k);
                    }
                default: return Color.Black;
            }
        }
    }

    // Where each lit device sits in the shared effect space (keyboards, then mice, then the rest).
    public class Placement { public Device Dev; public float X0, W; }

    public class Engine
    {
        public Settings S;
        public double T;
        public event Action DevicesChanged;
        public event Action<Device> ReadingsChanged;   // fresh DPI / polling / brightness values

        static readonly IDriver[] Drivers = { new LogitechDriver(), new CorsairDriver(), new LampArrayDriver() };
        readonly List<Device> devices = new List<Device>();
        readonly HashSet<Device> controlled = new HashSet<Device>();
        readonly Queue<Action> jobs = new Queue<Action>();
        readonly AutoResetEvent rescan = new AutoResetEvent(true);
        Thread loop, scanner;
        volatile bool running;
        public const float Gap = 0.35f;

        // cached readings for the UI (written on the engine thread)
        public readonly Dictionary<Device, int> DpiNow = new Dictionary<Device, int>(), RateNow = new Dictionary<Device, int>();
        public readonly Dictionary<Device, int> PollNow = new Dictionary<Device, int>(), BrightNow = new Dictionary<Device, int>();

        public Engine(Settings s) { S = s; }

        public Device[] Devices { get { lock (devices) return devices.ToArray(); } }

        public bool Lit(Device d) { return d.HasLighting && S.DevBool(d, "light", true); }
        public bool Host(Device d) { return d.Sensor != null && (S.DevBool(d, "host", true) || (d.HasLighting && Lit(d) && d.Sensor.HasOnboardMode)); }

        public List<Placement> Layout()
        {
            var list = new List<Placement>();
            float x = 0;
            foreach (var d in Devices.Where(Lit).OrderBy(d => d.Kind == DeviceKind.Keyboard ? 0 : d.Kind == DeviceKind.Mouse ? 1 : 2).ThenBy(d => d.Name))
            {
                list.Add(new Placement { Dev = d, X0 = x, W = d.Aspect });
                x += d.Aspect + Gap;
            }
            return list;
        }

        public void Start()
        {
            running = true;
            loop = new Thread(Loop) { IsBackground = true, Name = "DualPulse engine" };
            scanner = new Thread(Scan) { IsBackground = true, Name = "DualPulse scanner" };
            loop.Start(); scanner.Start();
        }

        public void Rescan() { rescan.Set(); }

        public void Post(Action a) { lock (jobs) jobs.Enqueue(a); }

        // Hand every device back to its own memory (called on exit).
        public void Stop()
        {
            running = false; rescan.Set();
            if (loop != null) loop.Join(1500);
            foreach (var d in Devices)
            {
                try { d.ReleaseControl(); } catch { }
                try { if (d.Sensor != null && d.Sensor.HasOnboardMode) d.Sensor.SetHostMode(false); } catch { }
                try { d.Dispose(); } catch { }
            }
        }

        // Re-apply everything DualPulse controls on a device.
        public void Apply(Device d)
        {
            Post(() =>
            {
                if (!d.Alive) return;
                var c = d as CorsairDevice;
                if (c != null) c.Interleaved = S.DevBool(d, "alt", false);
                if (d.HasLighting)
                {
                    if (Lit(d) && !controlled.Contains(d)) { d.TakeControl(); controlled.Add(d); }
                    else if (!Lit(d) && controlled.Contains(d)) { d.ReleaseControl(); controlled.Remove(d); }
                    else if (Lit(d)) d.TakeControl();
                }
                var sn = d.Sensor;
                if (sn != null)
                {
                    bool host = Host(d);
                    if (sn.HasOnboardMode) sn.SetHostMode(host);
                    if (host || !sn.HasOnboardMode)
                    {
                        int dpi = S.DevInt(d, "dpi", 0); if (dpi > 0) sn.SetDpi(dpi);
                        int rate = S.DevInt(d, "rate", 0); if (rate > 0) sn.SetRate(rate);
                    }
                    lock (DpiNow) { DpiNow[d] = sn.GetDpi(); RateNow[d] = sn.GetRate(); }
                }
                var hw = d.Hw;
                if (hw != null)
                {
                    int b = S.DevInt(d, "bright", -1); if (b >= 0 && hw.HasBrightness) hw.SetBrightness(b);
                    int p = S.DevInt(d, "poll", 0); if (p > 0 && hw.GetPoll() != p) hw.SetPoll(p);
                    lock (DpiNow) { PollNow[d] = hw.GetPoll(); BrightNow[d] = hw.HasBrightness ? hw.GetBrightness() : -1; }
                }
                if (ReadingsChanged != null) ReadingsChanged(d);
            });
        }

        void Scan()
        {
            string lastSet = null;
            bool forced = true;
            while (running)
            {
                if (rescan.WaitOne(3000)) forced = true;
                if (!running) break;
                try
                {
                    var all = HidDevice.Enumerate();
                    // only probe when something was plugged / unplugged (probing pings devices)
                    string set = string.Join("|", all.Select(i => i.Path).OrderBy(p => p));
                    if (!forced && set == lastSet) continue;
                    lastSet = set; forced = false;

                    var claimed = new HashSet<string>();
                    foreach (var d in Devices)
                    {
                        foreach (var p in d.Paths) claimed.Add(p);
                        if (d.HasLighting) claimed.Add(string.Format("lit:{0:X4}:{1:X4}", d.Vid, d.Pid));
                    }
                    var added = new List<Device>();
                    foreach (var drv in Drivers)
                        foreach (var d in drv.Probe(all, claimed)) added.Add(d);
                    if (added.Count > 0)
                    {
                        lock (devices) devices.AddRange(added);
                        foreach (var d in added) Apply(d);
                        if (DevicesChanged != null) DevicesChanged();
                    }
                }
                catch { }
            }
        }

        void Loop()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var lastStatic = new Dictionary<Device, DateTime>();
            while (running)
            {
                long t0 = sw.ElapsedMilliseconds;
                T = t0 / 1000.0;
                while (true)
                {
                    Action a = null;
                    lock (jobs) if (jobs.Count > 0) a = jobs.Dequeue();
                    if (a == null) break;
                    try { a(); } catch { }
                }

                // drop unplugged devices; the scanner re-adds them when they come back
                var dead = Devices.Where(d => !d.Alive).ToList();
                if (dead.Count > 0)
                {
                    lock (devices) devices.RemoveAll(dead.Contains);
                    foreach (var d in dead) { controlled.Remove(d); lastStatic.Remove(d); try { d.Dispose(); } catch { } }
                    if (DevicesChanged != null) DevicesChanged();
                    rescan.Set();
                }

                var layout = Layout();
                float total = layout.Count == 0 ? 1 : layout.Last().X0 + layout.Last().W;
                Effect e = S.Effect;
                bool animated = e != Effect.Static && e != Effect.Off;
                double k = S.Brightness / 100.0;
                int seed = 0;
                foreach (var pl in layout)
                {
                    var d = pl.Dev;
                    var frame = new Color[d.LampCount];
                    for (int i = 0; i < d.LampCount; i++)
                    {
                        var p = i < d.LampPos.Length ? d.LampPos[i] : new PointF(0.5f, 0.5f);
                        double gx = (pl.X0 + p.X * pl.W) / total;
                        frame[i] = Fx.Eval(e, S.C1, S.C2, S.Speed, gx, p.Y, T, seed + i);
                    }
                    seed += 1000;
                    d.Frame = frame;
                    if (!controlled.Contains(d) || (d.Sensor != null && d.Sensor.HasOnboardMode && !Host(d))) continue;
                    DateTime last;
                    if (!animated && lastStatic.TryGetValue(d, out last) && (DateTime.Now - last).TotalMilliseconds < 1000) continue;
                    var outF = frame.Select(c => Fx.Scale(c, k)).ToArray();
                    if (d.WriteLamps(outF)) lastStatic[d] = DateTime.Now;
                }

                int wait = 33 - (int)(sw.ElapsedMilliseconds - t0);
                if (wait > 0) Thread.Sleep(wait);
            }
        }
    }
}
