using System;
using System.Collections.Generic;
using System.Drawing;

namespace DualPulse
{
    public enum DeviceKind { Keyboard, Mouse, Headset, Other }

    // Anything with lamps that DualPulse can paint. Drivers fill in the geometry so effects
    // can be laid out spatially across all devices.
    public abstract class Device : IDisposable
    {
        public string Id;            // stable across sessions, used for per-device settings
        public string Name;
        public string Driver;        // shown in the UI, e.g. "Logitech HID++ 2.0"
        public DeviceKind Kind = DeviceKind.Other;
        public ushort Vid, Pid;
        public string[] Paths = new string[0];   // HID collections this device owns
        public int LampCount;
        public PointF[] LampPos = new PointF[0];   // 0..1 inside the device box
        public float Aspect = 1f;                  // box width / height
        public KbLayout Layout;                    // optional key drawing for keyboards
        public volatile bool Alive = true;
        public string Problem;                     // set when the device is present but not usable right now
        public Color[] Frame = new Color[0];       // last frame computed by the engine

        public virtual ISensor Sensor { get { return null; } }
        public virtual IHwSettings Hw { get { return null; } }
        public virtual bool HasLighting { get { return LampCount > 0; } }
        public virtual bool AnimatesWell { get { return true; } }

        public abstract bool WriteLamps(Color[] colors);
        public virtual void TakeControl() { }      // switch the device to host-driven lighting
        public virtual void ReleaseControl() { }   // hand lighting back to the device / vendor app
        public abstract void Dispose();

        protected void SetEvenLamps(int n, bool horizontal)
        {
            LampCount = n; LampPos = new PointF[n]; Frame = new Color[n];
            for (int i = 0; i < n; i++)
            {
                float k = n == 1 ? 0.5f : (i + 0.5f) / n;
                LampPos[i] = horizontal ? new PointF(k, 0.5f) : new PointF(0.5f, k);
            }
        }
    }

    // A device that is plugged in but does not answer its control protocol. It is listed so the user
    // knows what is wrong, and re-probed on every scan so it recovers on its own once it responds.
    public class UnresponsiveDevice : Device
    {
        public UnresponsiveDevice(HidInfo info, DeviceKind kind, string name, string driver, string problem)
        {
            Vid = info.Vid; Pid = info.Pid; Kind = kind; Name = name; Driver = driver; Problem = problem;
            Id = string.Format("problem:{0:X4}:{1:X4}", Vid, Pid);
        }
        public override bool WriteLamps(Color[] colors) { return true; }
        public override void Dispose() { }
    }

    // DPI / polling / onboard-profile control, typically a mouse.
    public interface ISensor
    {
        int DpiMin { get; }
        int DpiMax { get; }
        int DpiStep { get; }
        int[] Rates { get; }
        bool HasOnboardMode { get; }
        bool SetHostMode(bool host);
        int GetDpi();
        bool SetDpi(int dpi);
        int GetRate();
        bool SetRate(int hz);
    }

    // Hardware brightness / polling, typically a keyboard.
    public interface IHwSettings
    {
        bool HasBrightness { get; }
        int GetBrightness();          // percent
        bool SetBrightness(int pct);
        int[] PollRates { get; }
        int GetPoll();
        bool SetPoll(int hz);
    }

    public interface IDriver
    {
        // Return devices for the given HID collections that are not already claimed.
        IEnumerable<Device> Probe(List<HidInfo> all, HashSet<string> claimedPaths);
    }
}
