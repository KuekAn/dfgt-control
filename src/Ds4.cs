// DFGT Control - virtual PlayStation 4 controller (DualShock 4) through the ViGEmBus driver.
// Talks to the driver directly; the request layouts follow ViGEmClient's BusShared.h / Common.h (MIT, Nefarius Software Solutions).
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DFGTControl
{
    enum Ds4Btn
    {
        None, Cross, Square, Circle, Triangle, L1, R1, L2, R2, Share, Options, L3, R3, PS, Touchpad,
        DpadUp, DpadRight, DpadDown, DpadLeft
    }

    static class Ds4Map
    {
        public static readonly Ds4Btn[] Defaults = {
            Ds4Btn.Cross, Ds4Btn.Square, Ds4Btn.Circle, Ds4Btn.Triangle, Ds4Btn.R1, Ds4Btn.L1, Ds4Btn.R2, Ds4Btn.L2,
            Ds4Btn.Share, Ds4Btn.Options, Ds4Btn.R3, Ds4Btn.L3,
            Ds4Btn.None, Ds4Btn.None, Ds4Btn.None, Ds4Btn.None, Ds4Btn.None, Ds4Btn.None, Ds4Btn.None, Ds4Btn.None, // shifter, dial, rocker, horn
            Ds4Btn.PS,
            Ds4Btn.DpadUp, Ds4Btn.DpadRight, Ds4Btn.DpadDown, Ds4Btn.DpadLeft
        };

        public static string Label(Ds4Btn b)
        {
            switch (b)
            {
                case Ds4Btn.None: return "Wheel only";
                case Ds4Btn.Cross: return "Cross ✕";
                case Ds4Btn.Square: return "Square □";
                case Ds4Btn.Circle: return "Circle ○";
                case Ds4Btn.Triangle: return "Triangle △";
                case Ds4Btn.PS: return "PS button";
                case Ds4Btn.DpadUp: return "D-pad Up";
                case Ds4Btn.DpadRight: return "D-pad Right";
                case Ds4Btn.DpadDown: return "D-pad Down";
                case Ds4Btn.DpadLeft: return "D-pad Left";
                default: return b.ToString();
            }
        }
    }

    /// One virtual DualShock 4 plugged into the ViGEm bus. It disappears when this object is disposed
    /// (or the app exits - the driver unplugs it when the handle closes).
    class VirtualDs4 : IDisposable
    {
        public const string DriverUrl = "https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0";
        static readonly Guid BusGuid = new Guid("96E42B22-F5E9-42F8-B043-ED0F932F014F");

        // CTL_CODE(FILE_DEVICE_BUS_EXTENDER = 0x2A, function, METHOD_BUFFERED, FILE_WRITE_DATA)
        const uint IOCTL_PLUGIN_TARGET = 0x2AA004;       // 0x801
        const uint IOCTL_UNPLUG_TARGET = 0x2AA008;       // 0x802
        const uint IOCTL_CHECK_VERSION = 0x2AA00C;       // 0x803
        const uint IOCTL_WAIT_DEVICE_READY = 0x2AA010;   // 0x804
        const uint IOCTL_DS4_SUBMIT_REPORT = 0x2AA80C;   // 0xA03
        const int TARGET_DS4 = 2;

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, int inLen, byte[] outBuf, int outLen, out int returned, IntPtr ov);

        SafeFileHandle bus;
        uint serial;
        readonly byte[] submit = new byte[20];   // sizeof(DS4_SUBMIT_REPORT): 4 + 4 + DS4_REPORT(10), padded to 20
        readonly byte[] last = new byte[10];
        bool haveLast;

        public static bool DriverInstalled()
        {
            try { return WheelDevice.InterfacePaths(BusGuid).Count > 0; } catch { return false; }
        }

        bool Ioctl(uint code, byte[] data)
        {
            int ret;
            return DeviceIoControl(bus, code, data, data.Length, data, data.Length, out ret, IntPtr.Zero);
        }

        static byte[] Pair(uint a, uint b)
        {
            byte[] d = new byte[8];
            BitConverter.GetBytes(a).CopyTo(d, 0);
            BitConverter.GetBytes(b).CopyTo(d, 4);
            return d;
        }

        /// Plugs in the controller. Returns null on success, otherwise a message for the user.
        public string Connect()
        {
            var paths = WheelDevice.InterfacePaths(BusGuid);
            if (paths.Count == 0) return "The ViGEmBus driver is not installed.";
            string lastErr = "Could not open the ViGEmBus driver.";
            foreach (string path in paths)
            {
                bus = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
                if (bus.IsInvalid) { lastErr = "Could not open the ViGEmBus driver (error " + Marshal.GetLastWin32Error() + ")."; continue; }
                if (!Ioctl(IOCTL_CHECK_VERSION, Pair(8, 1)))
                {
                    lastErr = "The installed ViGEmBus driver version is not supported (error " + Marshal.GetLastWin32Error() + ").";
                    bus.Dispose(); continue;
                }
                for (uint sn = 1; sn <= 16; sn++)
                {
                    byte[] plug = new byte[16];
                    BitConverter.GetBytes(16u).CopyTo(plug, 0);
                    BitConverter.GetBytes(sn).CopyTo(plug, 4);
                    BitConverter.GetBytes(TARGET_DS4).CopyTo(plug, 8);
                    // VendorId / ProductId left 0 = the driver's default (Sony DualShock 4)
                    if (!Ioctl(IOCTL_PLUGIN_TARGET, plug)) continue;
                    serial = sn;
                    Ioctl(IOCTL_WAIT_DEVICE_READY, Pair(8, sn)); // not supported by old driver versions; harmless if it fails
                    BitConverter.GetBytes(20u).CopyTo(submit, 0);
                    BitConverter.GetBytes(sn).CopyTo(submit, 4);
                    // The new controller takes a moment to finish installing; wait until it accepts a report.
                    int code = 0;
                    for (int t = 0; t < 100; t++)
                    {
                        if (Submit(Neutral())) return null;
                        code = Marshal.GetLastWin32Error();
                        System.Threading.Thread.Sleep(100);
                    }
                    Ioctl(IOCTL_UNPLUG_TARGET, Pair(8, sn));
                    bus.Dispose(); bus = null;
                    return "The virtual PS4 controller was created but did not become ready (error " + code + ").";
                }
                lastErr = "ViGEmBus refused to create the controller (error " + Marshal.GetLastWin32Error() + ").";
                bus.Dispose();
            }
            bus = null;
            return lastErr;
        }

        public static byte[] Neutral()
        {
            return new byte[] { 0x80, 0x80, 0x80, 0x80, 0x08, 0x00, 0, 0, 0, 0 };
        }

        /// Builds a DS4_REPORT (10 bytes) from the wheel state.
        public static byte[] Build(InputState st, Ds4Btn[] map, bool axes)
        {
            byte[] r = Neutral();
            ushort buttons = 0; byte special = 0;
            bool up = false, down = false, left = false, right = false;
            bool l2 = false, r2 = false;
            for (int i = 0; i < Wheel.SlotCount && i < map.Length; i++)
            {
                if (!st.Buttons[i]) continue;
                switch (map[i])
                {
                    case Ds4Btn.Square: buttons |= 1 << 4; break;
                    case Ds4Btn.Cross: buttons |= 1 << 5; break;
                    case Ds4Btn.Circle: buttons |= 1 << 6; break;
                    case Ds4Btn.Triangle: buttons |= 1 << 7; break;
                    case Ds4Btn.L1: buttons |= 1 << 8; break;
                    case Ds4Btn.R1: buttons |= 1 << 9; break;
                    case Ds4Btn.L2: buttons |= 1 << 10; l2 = true; break;
                    case Ds4Btn.R2: buttons |= 1 << 11; r2 = true; break;
                    case Ds4Btn.Share: buttons |= 1 << 12; break;
                    case Ds4Btn.Options: buttons |= 1 << 13; break;
                    case Ds4Btn.L3: buttons |= 1 << 14; break;
                    case Ds4Btn.R3: buttons |= 1 << 15; break;
                    case Ds4Btn.PS: special |= 1; break;
                    case Ds4Btn.Touchpad: special |= 2; break;
                    case Ds4Btn.DpadUp: up = true; break;
                    case Ds4Btn.DpadRight: right = true; break;
                    case Ds4Btn.DpadDown: down = true; break;
                    case Ds4Btn.DpadLeft: left = true; break;
                }
            }
            int dpad = 8;
            if (up && right) dpad = 1; else if (down && right) dpad = 3; else if (down && left) dpad = 5; else if (up && left) dpad = 7;
            else if (up) dpad = 0; else if (right) dpad = 2; else if (down) dpad = 4; else if (left) dpad = 6;
            buttons |= (ushort)dpad;

            int lt = l2 ? 255 : 0, rt = r2 ? 255 : 0;
            if (axes)
            {
                if (st.HasWheel) r[0] = (byte)Math.Max(0, Math.Min(255, Math.Round((st.WheelOut + 1) * 127.5)));
                if (st.HasBrake) { lt = Math.Max(lt, (int)Math.Round(st.BrakeOut * 255)); if (lt > 30) buttons |= 1 << 10; }
                if (st.HasAccel) { rt = Math.Max(rt, (int)Math.Round(st.AccelOut * 255)); if (rt > 30) buttons |= 1 << 11; }
            }
            r[4] = (byte)(buttons & 0xFF); r[5] = (byte)(buttons >> 8);
            r[6] = special; r[7] = (byte)lt; r[8] = (byte)rt;
            return r;
        }

        /// Sends the report if it differs from the last one. Returns false if the controller is gone.
        public bool Submit(byte[] report)
        {
            if (bus == null || bus.IsInvalid) return false;
            if (haveLast && Equal(report, last)) return true;
            Array.Copy(report, 0, submit, 8, 10);
            if (!Ioctl(IOCTL_DS4_SUBMIT_REPORT, submit)) return false;
            Array.Copy(report, last, 10); haveLast = true;
            return true;
        }

        static bool Equal(byte[] a, byte[] b) { for (int i = 0; i < 10; i++) if (a[i] != b[i]) return false; return true; }

        public void Dispose()
        {
            if (bus == null) return;
            try { if (!bus.IsInvalid && serial != 0) Ioctl(IOCTL_UNPLUG_TARGET, Pair(8, serial)); } catch { }
            bus.Dispose();
            bus = null;
        }
    }
}
