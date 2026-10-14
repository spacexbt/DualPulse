using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace DualPulse
{
    // Logitech HID++ 2.0 (G-series mice and keyboards, wired or via a Lightspeed / Unifying receiver).
    // Every capability is discovered from the device's own feature table:
    //   0x0005 device name/type   0x8070 color LED effects   0x2201 adjustable DPI
    //   0x8060 report rate        0x8100 onboard profiles (host / onboard mode)
    public class LogitechDriver : IDriver
    {
        public IEnumerable<Device> Probe(List<HidInfo> all, HashSet<string> claimed)
        {
            var found = new List<Device>();
            foreach (var info in all.Where(i => i.Vid == 0x046D && i.UsagePage == 0xFF00 && i.Usage == 0x0002 && i.InLen == 20))
            {
                if (claimed.Contains(info.Path)) continue;
                HidppChannel ch;
                try { ch = new HidppChannel(info); } catch { continue; }
                var devs = new List<LogitechDevice>();
                // 0xFF = wired device; 1..6 = devices paired to a receiver
                foreach (byte idx in new byte[] { 0xFF, 1, 2, 3, 4, 5, 6 })
                {
                    var d = LogitechDevice.TryCreate(ch, idx);
                    if (d != null) devs.Add(d);
                    if (idx == 0xFF && d != null) break;
                }
                if (devs.Count == 0) { ch.Dispose(); continue; }
                claimed.Add(info.Path);
                foreach (var d in devs)
                {
                    d.Paths = new[] { info.Path };
                    if (d.HasLighting) claimed.Add(string.Format("lit:{0:X4}:{1:X4}", d.Vid, d.Pid));
                    found.Add(d);
                }
            }
            return found;
        }
    }

    // One vendor HID collection, possibly shared by several receiver-paired devices.
    public class HidppChannel : IDisposable
    {
        public readonly HidDevice Dev;
        public readonly object Lock = new object();
        int refs;
        public HidppChannel(HidInfo info) { Dev = new HidDevice(info); }
        public void AddRef() { lock (Lock) refs++; }
        public void Release() { lock (Lock) { if (--refs <= 0) Dev.Dispose(); } }
        public void Dispose() { Dev.Dispose(); }
    }

    public class LogitechDevice : Device, ISensor
    {
        public const byte EffOff = 0, EffStatic = 1;
        readonly HidppChannel ch;
        readonly byte index;
        byte fLed, fDpi, fRate, fOnboard;
        int dpiMin = 100, dpiMax = 25600, dpiStep = 50;
        int[] rates = new int[0];
        int[] zones = new int[0];
        readonly Color[] sent = new Color[8];
        readonly DateTime[] sentAt = new DateTime[8];
        public string LastError = "";

        LogitechDevice(HidppChannel ch, byte index) { this.ch = ch; this.index = index; }

        public static LogitechDevice TryCreate(HidppChannel ch, byte index)
        {
            var d = new LogitechDevice(ch, index);
            var ping = d.Call(0, 1, 0, 0, 0x5A);
            if (ping == null || ping[4] < 2) return null;     // not a HID++ 2.0 device at this index
            d.Init();
            if (!d.HasLighting && d.fDpi == 0) return null;   // nothing DualPulse can control
            ch.AddRef();
            return d;
        }

        void Init()
        {
            Vid = ch.Dev.Info.Vid; Pid = ch.Dev.Info.Pid;
            byte fName = Feature(0x0005);
            fLed = Feature(0x8070); fDpi = Feature(0x2201); fRate = Feature(0x8060); fOnboard = Feature(0x8100);

            string name = null; int type = -1;
            if (fName != 0)
            {
                var r = Call(fName, 0);
                if (r != null)
                {
                    int len = r[4]; var sb = new StringBuilder();
                    while (sb.Length < len)
                    {
                        var c = Call(fName, 1, (byte)sb.Length);
                        if (c == null) break;
                        for (int i = 4; i < 20 && sb.Length < len; i++) { if (c[i] == 0) { len = sb.Length; break; } sb.Append((char)c[i]); }
                    }
                    name = sb.ToString();
                }
                var t = Call(fName, 2);
                if (t != null) type = t[4];
            }
            Kind = type == 0 || type == 2 ? DeviceKind.Keyboard : type == 3 || type == 4 || type == 5 ? DeviceKind.Mouse
                : type == 8 ? DeviceKind.Headset : fDpi != 0 ? DeviceKind.Mouse : DeviceKind.Other;
            Name = string.IsNullOrEmpty(name) ? "Logitech " + Kind.ToString().ToLowerInvariant() : (name.StartsWith("Logitech") ? name : "Logitech " + name);
            Name = Names.Friendly(Name, Vid, Kind);
            Driver = "Logitech HID++ 2.0" + (index == 0xFF ? "" : " (receiver)");
            Id = string.Format("logitech:{0:X4}:{1}", Pid, Name);

            if (fLed != 0)
            {
                var r = Call(fLed, 0);
                int n = r == null ? 0 : Math.Min(8, (int)r[4]);
                zones = new int[n];
                for (int z = 0; z < n; z++)
                {
                    var zi = Call(fLed, 1, (byte)z);
                    zones[z] = zi == null ? 0 : (zi[5] << 8) | zi[6];
                }
                LampCount = n; Frame = new Color[n];
                LampPos = zones.Select((loc, i) => ZonePos(loc, i, n)).ToArray();
            }
            Aspect = Kind == DeviceKind.Mouse ? 0.56f : Kind == DeviceKind.Keyboard ? 3.4f : 1f;

            if (fDpi != 0)
            {
                var r = Call(fDpi, 1, 0);
                if (r != null && (r[7] & 0xE0) == 0xE0)
                {
                    dpiMin = (r[5] << 8) | r[6];
                    dpiStep = ((r[7] & 0x1F) << 8) | r[8];
                    dpiMax = (r[9] << 8) | r[10];
                }
                else if (r != null)
                {
                    // explicit list of DPI values
                    var vals = new List<int>();
                    for (int i = 5; i + 1 < 20; i += 2) { int v = (r[i] << 8) | r[i + 1]; if (v == 0) break; vals.Add(v); }
                    if (vals.Count > 0) { dpiMin = vals.Min(); dpiMax = vals.Max(); dpiStep = 50; }
                }
            }
            if (fRate != 0)
            {
                var r = Call(fRate, 0);
                var l = new List<int>();
                if (r != null) for (int b = 7; b >= 0; b--) if ((r[4] & (1 << b)) != 0) l.Add(1000 / (b + 1));
                rates = l.ToArray();
            }
        }

        // HID++ zone locations: 1 primary, 2 logo, 3 left side, 4 right side
        PointF ZonePos(int loc, int i, int n)
        {
            if (Kind == DeviceKind.Mouse)
            {
                if (loc == 2) return new PointF(0.5f, 0.72f);
                if (loc == 3) return new PointF(0.08f, 0.55f);
                if (loc == 4) return new PointF(0.92f, 0.55f);
                return new PointF(0.22f, 0.3f);
            }
            return new PointF(n == 1 ? 0.5f : (i + 0.5f) / n, 0.5f);
        }

        byte Feature(ushort id)
        {
            var r = Call(0, 0, (byte)(id >> 8), (byte)id);
            return r == null ? (byte)0 : r[4];
        }

        byte[] Call(byte feat, int func, params byte[] p)
        {
            lock (ch.Lock)
            {
                var msg = new byte[20];
                msg[0] = 0x11; msg[1] = index; msg[2] = feat; msg[3] = (byte)((func << 4) | 0x0D);
                Array.Copy(p, 0, msg, 4, Math.Min(p.Length, 16));
                try
                {
                    ch.Dev.Write(msg);
                    for (int i = 0; i < 24; i++)
                    {
                        var r = ch.Dev.Read(250);
                        if (r == null) return null;
                        if (r[1] != index) continue;
                        if (r[2] == feat && r[3] == msg[3]) return r;
                        if (r[2] == 0xFF && r[3] == feat && r[4] == msg[3]) { LastError = "HID++ error " + r[5]; return null; }
                        if (r[2] == 0x8F) return null;   // HID++ 1.0 error from a receiver: no device here
                    }
                }
                catch (Exception e) { LastError = e.Message; Alive = false; }
                return null;
            }
        }

        public override ISensor Sensor { get { return fDpi != 0 ? this : null; } }
        public override bool HasLighting { get { return fLed != 0 && LampCount > 0; } }

        public override void TakeControl() { for (int z = 0; z < sentAt.Length; z++) sentAt[z] = DateTime.MinValue; }

        public override bool WriteLamps(Color[] colors)
        {
            var now = DateTime.Now;
            for (int z = 0; z < LampCount; z++)
            {
                Color c = colors[z], o = sent[z];
                int diff = Math.Abs(c.R - o.R) + Math.Abs(c.G - o.G) + Math.Abs(c.B - o.B);
                if (diff <= 3 && (now - sentAt[z]).TotalMilliseconds < 2000) continue;
                var p = new byte[13];
                p[0] = (byte)z;
                if (c.R + c.G + c.B > 0) { p[1] = EffStatic; p[2] = c.R; p[3] = c.G; p[4] = c.B; }
                p[12] = 0x00;   // not persistent: RAM only, safe to stream
                if (Call(fLed, 3, p) == null) { if (!Alive) return false; continue; }
                sent[z] = c; sentAt[z] = now;
            }
            return true;
        }

        // ISensor
        public int DpiMin { get { return dpiMin; } }
        public int DpiMax { get { return dpiMax; } }
        public int DpiStep { get { return dpiStep; } }
        public int[] Rates { get { return rates; } }
        public bool HasOnboardMode { get { return fOnboard != 0; } }
        public bool SetHostMode(bool host) { return fOnboard != 0 && Call(fOnboard, 1, (byte)(host ? 2 : 1)) != null; }
        public int GetDpi() { var r = Call(fDpi, 2, 0); return r == null ? 0 : (r[5] << 8) | r[6]; }
        public bool SetDpi(int dpi)
        {
            dpi = Math.Max(dpiMin, Math.Min(dpiMax, (int)Math.Round(dpi / (double)dpiStep) * dpiStep));
            return Call(fDpi, 3, 0, (byte)(dpi >> 8), (byte)dpi) != null;
        }
        public int GetRate() { var r = fRate == 0 ? null : Call(fRate, 1); return r == null || r[4] == 0 ? 0 : 1000 / r[4]; }
        public bool SetRate(int hz) { return fRate != 0 && Call(fRate, 2, (byte)Math.Max(1, 1000 / hz)) != null; }

        public override void Dispose() { ch.Release(); }
    }
}
