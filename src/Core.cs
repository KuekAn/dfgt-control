// DFGT Control - core: HID access to the Logitech Driving Force GT, settings, key output.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DFGTControl
{
    // ------------------------------------------------------------------ Win32
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr p);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] public static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder buf, int bytes);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] buf, int len);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr p, out HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages, ref uint length, IntPtr preparsed, byte[] report, uint reportLength);
        [DllImport("hid.dll")] public static extern int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage, out uint value, IntPtr preparsed, byte[] report, uint reportLength);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int reportType, [Out] byte[] caps, ref ushort length, IntPtr preparsed);

        [DllImport("setupapi.dll", SetLastError = true)] public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool ReadFile(SafeFileHandle h, byte[] buf, int len, out int read, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool WriteFile(SafeFileHandle h, byte[] buf, int len, out int written, IntPtr ov);
        [DllImport("kernel32.dll")] public static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT { public uint type; public InputUnion u; }

        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);

        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_RW = 3, OPEN_EXISTING = 3;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;
    }

    // ------------------------------------------------------------------ Wheel layout / names
    static class Wheel
    {
        public const ushort VID = 0x046D;
        public const ushort PID_NATIVE = 0xC29A;   // Driving Force GT native mode (900 degrees)
        public const ushort PID_COMPAT = 0xC294;   // Driving Force EX compatibility mode
        public const int ButtonCount = 21;          // real HID buttons
        public const int SlotCount = 25;            // + 4 D-pad directions
        public const int DpadUp = 21, DpadRight = 22, DpadDown = 23, DpadLeft = 24;

        public static readonly string[] DefaultNames = {
            "Cross", "Square", "Circle", "Triangle", "Right Paddle (R1)", "Left Paddle (L1)", "R2", "L2",
            "Select", "Start", "R3", "L3", "Shifter Backwards", "Shifter Forwards", "Dial Center (Enter)",
            "Rocker Plus", "Dial CW", "Dial CCW", "Rocker Minus", "Horn", "PS Button",
            "D-Pad Up", "D-Pad Right", "D-Pad Down", "D-Pad Left"
        };

        /// What Windows / games call the input when they don't use names: "Button 5", "POV Up"…
        public static string OriginalName(int i)
        {
            if (i < ButtonCount) return "Button " + (i + 1);
            return "POV " + new[] { "Up", "Right", "Down", "Left" }[i - ButtonCount];
        }
    }

    class InputState
    {
        public bool[] Buttons = new bool[global::DFGTControl.Wheel.SlotCount];
        public double Wheel;      // -1 .. 1
        public double Accel;      // 0 .. 1 (pressed = 1)
        public double Brake;      // 0 .. 1
        public double AccelRaw, BrakeRaw;   // before the user's invert options
        public double WheelOut, AccelOut, BrakeOut;   // after deadzones (what the PS4 controller gets)
        public bool HasWheel, HasAccel, HasBrake;
        public InputState Clone()
        {
            InputState c = (InputState)MemberwiseClone();
            c.Buttons = (bool[])Buttons.Clone();
            return c;
        }
    }

    static class Deadzone
    {
        static double Clamp(double v) { return v < 0 ? 0 : (v > 1 ? 1 : v); }

        /// Pedal 0..1: nothing until "start" %, full output from (100 - "end") %.
        public static double Pedal(double v, int startPct, int endPct)
        {
            double s = startPct / 100.0, e = endPct / 100.0, span = 1 - s - e;
            if (span <= 0.01) return v >= s ? 1 : 0;
            return Clamp((v - s) / span);
        }

        /// Steering -1..1: nothing inside the centre zone, full lock from (100 - "end") % of each side.
        public static double Steer(double v, int centerPct, int endPct)
        {
            double c = centerPct / 100.0, e = endPct / 100.0, a = Math.Abs(v), span = 1 - c - e;
            double o = span <= 0.01 ? (a > c ? 1 : 0) : Clamp((a - c) / span);
            return v < 0 ? -o : o;
        }

        /// DirectInput calibration points {min, centre, max} for games. Pedals are calibrated on the raw axis,
        /// where released = 0 unless the pedal is set to inverted.
        public static int[] PedalCalibration(int startPct, int endPct, bool inverted)
        {
            int lo = (int)Math.Round((inverted ? endPct : startPct) / 100.0 * DiCalibration.PedalMax);
            int hi = DiCalibration.PedalMax - (int)Math.Round((inverted ? startPct : endPct) / 100.0 * DiCalibration.PedalMax);
            if (hi <= lo + 2) hi = lo + 2;
            return new[] { lo, (lo + hi + 1) / 2, hi };
        }

        public static int[] SteerCalibration(int endPct)
        {
            int lo = (int)Math.Round(endPct / 100.0 * DiCalibration.WheelMax);
            return new[] { lo, 8192, DiCalibration.WheelMax - lo };
        }
    }

    // ------------------------------------------------------------------ HID device
    class AxisInfo { public ushort Usage; public int Min, Max; }

    class WheelDevice : IDisposable
    {
        public string Path;
        public ushort Pid, Version;
        public string Product = "Logitech Driving Force GT";
        public int InputLength, OutputLength;
        SafeFileHandle rd, wr;
        IntPtr pp = IntPtr.Zero;
        readonly Dictionary<ushort, AxisInfo> axes = new Dictionary<ushort, AxisInfo>();
        readonly ushort[] usageBuf = new ushort[64];
        readonly object writeLock = new object();

        public bool IsCompatMode { get { return Pid == Wheel.PID_COMPAT; } }

        static List<string> HidPaths()
        {
            Guid g; Native.HidD_GetHidGuid(out g);
            return InterfacePaths(g);
        }

        /// Device paths of all present devices exposing the given interface class.
        public static List<string> InterfacePaths(Guid g)
        {
            List<string> list = new List<string>();
            IntPtr set = Native.SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12 /*PRESENT|DEVICEINTERFACE*/);
            if (set == new IntPtr(-1)) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    Native.SP_DEVICE_INTERFACE_DATA d = new Native.SP_DEVICE_INTERFACE_DATA();
                    d.cbSize = Marshal.SizeOf(d);
                    if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    int req;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (Native.SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                            list.Add(Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)));
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        /// Finds the DFGT (native or compatibility mode). Returns null if not present.
        public static WheelDevice FindAndOpen(out string error)
        {
            error = null;
            foreach (string path in HidPaths())
            {
                string lp = path.ToLowerInvariant();
                if (!lp.Contains("vid_046d")) continue;
                if (!lp.Contains("pid_c29a") && !lp.Contains("pid_c294")) continue;
                using (SafeFileHandle h = Native.CreateFile(path, 0, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
                {
                    if (h.IsInvalid) continue;
                    Native.HIDD_ATTRIBUTES a = new Native.HIDD_ATTRIBUTES();
                    a.Size = Marshal.SizeOf(a);
                    if (!Native.HidD_GetAttributes(h, ref a)) continue;
                    if (a.VendorID != Wheel.VID) continue;
                    // C294 is also used by the original Driving Force / DF EX: only accept a DFGT (firmware 0x13xx).
                    if (a.ProductID == Wheel.PID_COMPAT && (a.VersionNumber & 0xFF00) != 0x1300) continue;
                    if (a.ProductID != Wheel.PID_NATIVE && a.ProductID != Wheel.PID_COMPAT) continue;
                    WheelDevice dev = new WheelDevice();
                    dev.Path = path; dev.Pid = a.ProductID; dev.Version = a.VersionNumber;
                    StringBuilder sb = new StringBuilder(128);
                    if (Native.HidD_GetProductString(h, sb, 256) && sb.Length > 0) dev.Product = sb.ToString();
                    string err;
                    if (dev.Open(out err)) return dev;
                    error = err;
                    dev.Dispose();
                }
            }
            return null;
        }

        bool Open(out string error)
        {
            error = null;
            rd = Native.CreateFile(Path, Native.GENERIC_READ, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            wr = Native.CreateFile(Path, Native.GENERIC_READ | Native.GENERIC_WRITE, Native.FILE_SHARE_RW, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (rd.IsInvalid) { error = "Could not open the wheel for reading (error " + Marshal.GetLastWin32Error() + ")."; return false; }
            if (wr.IsInvalid) { error = "Could not open the wheel for writing (error " + Marshal.GetLastWin32Error() + ")."; return false; }
            if (!Native.HidD_GetPreparsedData(rd, out pp)) { error = "Could not read the wheel's HID description."; return false; }
            Native.HIDP_CAPS caps;
            if (Native.HidP_GetCaps(pp, out caps) != Native.HIDP_STATUS_SUCCESS) { error = "Could not read the wheel's capabilities."; return false; }
            InputLength = caps.InputReportByteLength;
            OutputLength = caps.OutputReportByteLength;

            ushort n = caps.NumberInputValueCaps;
            if (n > 0)
            {
                const int SZ = 72; // sizeof(HIDP_VALUE_CAPS)
                byte[] vc = new byte[SZ * n];
                if (Native.HidP_GetValueCaps(0, vc, ref n, pp) == Native.HIDP_STATUS_SUCCESS)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int o = i * SZ;
                        ushort page = BitConverter.ToUInt16(vc, o);
                        if (page != 1) continue;
                        ushort bits = BitConverter.ToUInt16(vc, o + 18);
                        int min = BitConverter.ToInt32(vc, o + 40), max = BitConverter.ToInt32(vc, o + 44);
                        if (max <= min && bits > 0 && bits < 32) { min = 0; max = (1 << bits) - 1; }
                        ushort usage = BitConverter.ToUInt16(vc, o + 56);
                        AxisInfo ai = new AxisInfo(); ai.Usage = usage; ai.Min = min; ai.Max = max;
                        axes[usage] = ai;
                    }
                }
            }
            return true;
        }

        public bool Read(byte[] buf)
        {
            int read;
            return Native.ReadFile(rd, buf, buf.Length, out read, IntPtr.Zero) && read > 0;
        }

        /// Asks the wheel for its current state (it otherwise only reports when something changes).
        public bool ReadCurrent(byte[] buf)
        {
            Array.Clear(buf, 0, buf.Length);
            try { return Native.HidD_GetInputReport(rd, buf, buf.Length); } catch { return false; }
        }

        public void CancelRead() { try { if (rd != null && !rd.IsInvalid) Native.CancelIoEx(rd, IntPtr.Zero); } catch { } }

        double Norm(byte[] report, ushort usage, out bool has)
        {
            has = false;
            AxisInfo ai;
            if (!axes.TryGetValue(usage, out ai)) return 0;
            uint v;
            if (Native.HidP_GetUsageValue(0, 1, 0, usage, out v, pp, report, (uint)report.Length) != Native.HIDP_STATUS_SUCCESS) return 0;
            has = true;
            double span = ai.Max - ai.Min;
            return span <= 0 ? 0 : Math.Max(0, Math.Min(1, ((double)v - ai.Min) / span));
        }

        public void Parse(byte[] report, InputState st)
        {
            for (int i = 0; i < st.Buttons.Length; i++) st.Buttons[i] = false;
            uint len = (uint)usageBuf.Length;
            if (Native.HidP_GetUsages(0, 9, 0, usageBuf, ref len, pp, report, (uint)report.Length) == Native.HIDP_STATUS_SUCCESS)
            {
                for (int i = 0; i < len; i++)
                {
                    int idx = usageBuf[i] - 1;
                    if (idx >= 0 && idx < Wheel.ButtonCount) st.Buttons[idx] = true;
                }
            }
            uint hat;
            if (axes.ContainsKey(0x39) && Native.HidP_GetUsageValue(0, 1, 0, 0x39, out hat, pp, report, (uint)report.Length) == Native.HIDP_STATUS_SUCCESS)
            {
                AxisInfo ai = axes[0x39];
                int h = (int)hat - ai.Min;
                if (h >= 0 && h <= 7)
                {
                    st.Buttons[Wheel.DpadUp] = h == 7 || h == 0 || h == 1;
                    st.Buttons[Wheel.DpadRight] = h >= 1 && h <= 3;
                    st.Buttons[Wheel.DpadDown] = h >= 3 && h <= 5;
                    st.Buttons[Wheel.DpadLeft] = h >= 5 && h <= 7;
                }
            }
            bool has;
            st.Wheel = Norm(report, 0x30, out has) * 2 - 1; st.HasWheel = has;
            // With the Logitech driver a released pedal reads 0. The per-pedal "invert" options are applied by the Engine.
            st.Accel = Norm(report, 0x32, out has); st.HasAccel = has;   // Z  = accelerator
            st.Brake = Norm(report, 0x35, out has); st.HasBrake = has;   // Rz = brake
        }

        // ---- commands (same byte sequences the Linux hid-lg4ff driver uses for this wheel)
        public bool Send(params byte[] cmd)
        {
            if (wr == null || wr.IsInvalid || OutputLength < 8) return false;
            byte[] buf = new byte[OutputLength];
            buf[0] = 0; // no report ID
            Array.Copy(cmd, 0, buf, 1, Math.Min(cmd.Length, OutputLength - 1));
            lock (writeLock)
            {
                int written;
                return Native.WriteFile(wr, buf, buf.Length, out written, IntPtr.Zero);
            }
        }

        public bool SetRange(int degrees)
        {
            degrees = Math.Max(40, Math.Min(900, degrees));
            return Send(0xF8, 0x81, (byte)(degrees & 0xFF), (byte)(degrees >> 8), 0, 0, 0);
        }

        /// percent 0 = centering spring off.
        public bool SetAutocenter(int percent)
        {
            if (percent <= 0) return Send(0xF5, 0, 0, 0, 0, 0, 0);
            long m = (long)Math.Round(Math.Min(100, percent) / 100.0 * 0xFFFF);
            long a, b;
            if (m <= 0xAAAA) { a = 0x0C * m; b = 0x80 * m; }
            else { a = 0x0C * 0xAAAA + 0x06 * (m - 0xAAAA); b = 0x80 * 0xAAAA + 0xFF * (m - 0xAAAA); }
            byte ea = (byte)Math.Min(255, a / 0xAAAA), eb = (byte)Math.Min(255, b / 0xAAAA);
            bool ok = Send(0xFE, 0x0D, ea, ea, eb, 0, 0);
            return Send(0x14, 0, 0, 0, 0, 0, 0) && ok;
        }

        public void SwitchToNative()
        {
            Send(0xF8, 0x0A, 0, 0, 0, 0, 0);
            Send(0xF8, 0x09, 0x03, 0x01, 0, 0, 0);
        }

        public void Dispose()
        {
            if (pp != IntPtr.Zero) { Native.HidD_FreePreparsedData(pp); pp = IntPtr.Zero; }
            if (rd != null) rd.Dispose();
            if (wr != null) wr.Dispose();
        }
    }

    // ------------------------------------------------------------------ key names / parsing
    static class KeyText
    {
        static readonly Dictionary<string, Keys> alias = new Dictionary<string, Keys>(StringComparer.OrdinalIgnoreCase)
        {
            {"Ctrl", Keys.ControlKey}, {"Control", Keys.ControlKey}, {"Shift", Keys.ShiftKey}, {"Alt", Keys.Menu},
            {"Win", Keys.LWin}, {"Windows", Keys.LWin}, {"LCtrl", Keys.LControlKey}, {"RCtrl", Keys.RControlKey},
            {"LShift", Keys.LShiftKey}, {"RShift", Keys.RShiftKey}, {"LAlt", Keys.LMenu}, {"RAlt", Keys.RMenu},
            {"LWin", Keys.LWin}, {"RWin", Keys.RWin}, {"Menu", Keys.Apps}, {"Apps", Keys.Apps},
            {"Enter", Keys.Return}, {"Return", Keys.Return}, {"Esc", Keys.Escape}, {"Escape", Keys.Escape},
            {"Space", Keys.Space}, {"Tab", Keys.Tab}, {"Backspace", Keys.Back}, {"Back", Keys.Back},
            {"PageUp", Keys.PageUp}, {"PgUp", Keys.PageUp}, {"PageDown", Keys.PageDown}, {"PgDn", Keys.PageDown},
            {"Del", Keys.Delete}, {"Delete", Keys.Delete}, {"Ins", Keys.Insert}, {"Insert", Keys.Insert},
            {"Home", Keys.Home}, {"End", Keys.End}, {"Up", Keys.Up}, {"Down", Keys.Down}, {"Left", Keys.Left}, {"Right", Keys.Right},
            {"CapsLock", Keys.CapsLock}, {"NumLock", Keys.NumLock}, {"ScrollLock", Keys.Scroll}, {"PrintScreen", Keys.PrintScreen},
            {"PrtSc", Keys.PrintScreen}, {"Pause", Keys.Pause},
            {"Minus", Keys.OemMinus}, {"Equals", Keys.Oemplus}, {"Comma", Keys.Oemcomma}, {"Period", Keys.OemPeriod},
            {"Slash", Keys.OemQuestion}, {"Backslash", Keys.OemPipe}, {"Semicolon", Keys.OemSemicolon},
            {"Quote", Keys.OemQuotes}, {"LBracket", Keys.OemOpenBrackets}, {"RBracket", Keys.OemCloseBrackets},
            {"Backtick", Keys.Oemtilde}, {"Tilde", Keys.Oemtilde},
            {"NumPlus", Keys.Add}, {"NumMinus", Keys.Subtract}, {"NumMultiply", Keys.Multiply}, {"NumDivide", Keys.Divide},
            {"NumPeriod", Keys.Decimal},
            {"VolumeUp", Keys.VolumeUp}, {"VolumeDown", Keys.VolumeDown}, {"Mute", Keys.VolumeMute},
            {"PlayPause", Keys.MediaPlayPause}, {"NextTrack", Keys.MediaNextTrack}, {"PrevTrack", Keys.MediaPreviousTrack},
        };

        public static string Name(int vk)
        {
            Keys k = (Keys)vk;
            switch (k)
            {
                case Keys.ControlKey: return "Ctrl";
                case Keys.ShiftKey: return "Shift";
                case Keys.Menu: return "Alt";
                case Keys.LControlKey: return "LCtrl";
                case Keys.RControlKey: return "RCtrl";
                case Keys.LShiftKey: return "LShift";
                case Keys.RShiftKey: return "RShift";
                case Keys.LMenu: return "LAlt";
                case Keys.RMenu: return "RAlt";
                case Keys.LWin: return "Win";
                case Keys.RWin: return "RWin";
                case Keys.Return: return "Enter";
                case Keys.Escape: return "Esc";
                case Keys.Back: return "Backspace";
                case Keys.PageUp: return "PageUp";
                case Keys.PageDown: return "PageDown";
                case Keys.CapsLock: return "CapsLock";
                case Keys.Scroll: return "ScrollLock";
                case Keys.PrintScreen: return "PrintScreen";
                case Keys.Apps: return "Menu";
                case Keys.OemMinus: return "Minus";
                case Keys.Oemplus: return "Equals";
                case Keys.Oemcomma: return "Comma";
                case Keys.OemPeriod: return "Period";
                case Keys.OemQuestion: return "Slash";
                case Keys.OemPipe: return "Backslash";
                case Keys.OemSemicolon: return "Semicolon";
                case Keys.OemQuotes: return "Quote";
                case Keys.OemOpenBrackets: return "LBracket";
                case Keys.OemCloseBrackets: return "RBracket";
                case Keys.Oemtilde: return "Backtick";
                case Keys.Add: return "NumPlus";
                case Keys.Subtract: return "NumMinus";
                case Keys.Multiply: return "NumMultiply";
                case Keys.Divide: return "NumDivide";
                case Keys.Decimal: return "NumPeriod";
                case Keys.VolumeMute: return "Mute";
                case Keys.MediaPlayPause: return "PlayPause";
                case Keys.MediaNextTrack: return "NextTrack";
                case Keys.MediaPreviousTrack: return "PrevTrack";
            }
            if (k >= Keys.D0 && k <= Keys.D9) return ((int)(k - Keys.D0)).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return "Num" + (int)(k - Keys.NumPad0);
            return k.ToString();
        }

        public static int Parse(string token)
        {
            string t = token.Trim();
            if (t.Length == 0) return 0;
            Keys k;
            if (alias.TryGetValue(t, out k)) return (int)k;
            if (t.Length == 1)
            {
                char c = char.ToUpperInvariant(t[0]);
                if (c >= 'A' && c <= 'Z') return (int)Keys.A + (c - 'A');
                if (c >= '0' && c <= '9') return (int)Keys.D0 + (c - '0');
                switch (c) { case '-': return (int)Keys.OemMinus; case '=': return (int)Keys.Oemplus; case '.': return (int)Keys.OemPeriod;
                             case '/': return (int)Keys.OemQuestion; case '\\': return (int)Keys.OemPipe; case ';': return (int)Keys.OemSemicolon;
                             case '\'': return (int)Keys.OemQuotes; case '[': return (int)Keys.OemOpenBrackets; case ']': return (int)Keys.OemCloseBrackets;
                             case '`': return (int)Keys.Oemtilde; }
            }
            if (t.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && t.Length == 4 && char.IsDigit(t[3]))
                return (int)Keys.NumPad0 + (t[3] - '0');
            if (Enum.TryParse<Keys>(t, true, out k) && (int)k > 0 && (int)k < 256) return (int)k;
            return 0;
        }

        /// "Ctrl+Shift+A, Enter" -> [[Ctrl,Shift,A],[Enter]]. Throws FormatException on unknown keys.
        public static List<int[]> ParseSteps(string text)
        {
            List<int[]> steps = new List<int[]>();
            if (string.IsNullOrWhiteSpace(text)) return steps;
            foreach (string stepText in text.Split(','))
            {
                if (stepText.Trim().Length == 0) continue;
                List<int> combo = new List<int>();
                foreach (string tok in stepText.Split('+'))
                {
                    if (tok.Trim().Length == 0) throw new FormatException("Empty key in \"" + stepText.Trim() + "\". (Use \"NumPlus\" for the + key.)");
                    int vk = Parse(tok);
                    if (vk == 0) throw new FormatException("Unknown key \"" + tok.Trim() + "\".");
                    if (!combo.Contains(vk)) combo.Add(vk);
                }
                steps.Add(combo.ToArray());
            }
            return steps;
        }

        public static bool IsModifier(int vk)
        {
            Keys k = (Keys)vk;
            return k == Keys.ControlKey || k == Keys.ShiftKey || k == Keys.Menu || k == Keys.LWin || k == Keys.RWin ||
                   k == Keys.LControlKey || k == Keys.RControlKey || k == Keys.LShiftKey || k == Keys.RShiftKey || k == Keys.LMenu || k == Keys.RMenu;
        }

        public static string Format(IEnumerable<int> combo)
        {
            List<int> c = combo.ToList();
            List<int> ordered = c.Where(IsModifier).Concat(c.Where(v => !IsModifier(v))).ToList();
            return string.Join("+", ordered.Select(Name).ToArray());
        }
    }

    // ------------------------------------------------------------------ key output
    class KeySender : IDisposable
    {
        readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();
        readonly Thread worker;
        readonly int[] down = new int[256];   // reference counts, only touched by worker thread
        public volatile int TapMs = 40;

        public KeySender()
        {
            worker = new Thread(Work);
            worker.IsBackground = true;
            worker.Name = "KeySender";
            worker.Start();
        }

        void Work()
        {
            foreach (Action a in queue.GetConsumingEnumerable())
            {
                try { a(); } catch { }
            }
        }

        static void SendRaw(int vk, bool up)
        {
            uint sc = Native.MapVirtualKey((uint)vk, 4 /*MAPVK_VK_TO_VSC_EX*/);
            Native.INPUT inp = new Native.INPUT();
            inp.type = 1;
            if (sc != 0)
            {
                inp.u.ki.wScan = (ushort)(sc & 0xFF);
                inp.u.ki.dwFlags = 0x0008; // KEYEVENTF_SCANCODE - games reading DirectInput/raw keyboard need this
                if ((sc & 0xFF00) == 0xE000 || (sc & 0xFF00) == 0xE100) inp.u.ki.dwFlags |= 0x0001; // EXTENDEDKEY
            }
            else inp.u.ki.wVk = (ushort)vk; // media keys etc.
            if (up) inp.u.ki.dwFlags |= 0x0002;
            Native.SendInput(1, new Native.INPUT[] { inp }, Marshal.SizeOf(typeof(Native.INPUT)));
        }

        void DownNow(int[] combo) { foreach (int vk in combo) { if (down[vk & 0xFF]++ == 0) SendRaw(vk, false); } }
        void UpNow(int[] combo)
        {
            for (int i = combo.Length - 1; i >= 0; i--)
            {
                int vk = combo[i] & 0xFF;
                if (down[vk] > 0 && --down[vk] == 0) SendRaw(vk, true);
            }
        }

        public void Down(int[] combo) { queue.Add(delegate { DownNow(combo); }); }
        public void Up(int[] combo) { queue.Add(delegate { UpNow(combo); }); }
        public void Tap(int[] combo)
        {
            queue.Add(delegate
            {
                DownNow(combo);
                Thread.Sleep(Math.Max(5, TapMs));
                UpNow(combo);
                Thread.Sleep(Math.Max(5, TapMs / 2));
            });
        }
        public void ReleaseAll()
        {
            queue.Add(delegate
            {
                for (int vk = 0; vk < 256; vk++) if (down[vk] > 0) { down[vk] = 0; SendRaw(vk, true); }
            });
        }

        public void Dispose()
        {
            ReleaseAll();
            queue.CompleteAdding();
            worker.Join(1000);
        }
    }

    // ------------------------------------------------------------------ settings
    enum BindMode { Off = 0, Hold = 1, Tap = 2, Toggle = 3 }

    class Binding
    {
        public BindMode Mode = BindMode.Off;
        public string Text = "";
        public List<int[]> Steps = new List<int[]>();
        public bool Active { get { return Mode != BindMode.Off && Steps.Count > 0; } }

        public static Binding Create(BindMode mode, string text)
        {
            Binding b = new Binding();
            b.Mode = mode;
            try { b.Steps = KeyText.ParseSteps(text); }
            catch (FormatException) { b.Steps = new List<int[]>(); }
            b.Text = string.Join(", ", b.Steps.Select(s => KeyText.Format(s)).ToArray());
            if (b.Steps.Count == 0) b.Text = "";
            return b;
        }
    }

    class Settings
    {
        public int Range = 900;
        public bool Spring = false;
        public int SpringStrength = 20;
        public bool Enforce = true;
        public int EnforceSeconds = 5;
        public bool BindingsEnabled = true;
        public int TapMs = 40;
        public bool CloseToTray = true;
        public bool Ds4Enabled = false;
        public bool Ds4Axes = false;
        public bool InvertAccel = false;
        public bool InvertBrake = false;
        public bool OriginalNames = false;   // show / save "Button 1…" instead of Cross, Square…
        public bool ShowOriginal = true;     // faint "Button N" next to each name in the table
        public ThemeMode Theme = ThemeMode.System;
        // Deadzones in percent. Steering: centre and both ends. Pedals: start (released side) and end (fully pressed side).
        public int SteerDzCenter, SteerDzEnd, AccelDzStart, AccelDzEnd, BrakeDzStart, BrakeDzEnd;
        public bool DzInGames = false;       // also write them into the DirectInput calibration (games)
        public Ds4Btn[] Ds4Map = (Ds4Btn[])DFGTControl.Ds4Map.Defaults.Clone();
        public string[] Names = (string[])Wheel.DefaultNames.Clone();
        public Binding[] Bindings = new Binding[Wheel.SlotCount];

        public static string Folder
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DFGTControl"); }
        }
        public static string FilePath { get { return System.IO.Path.Combine(Folder, "settings.ini"); } }

        public Settings() { for (int i = 0; i < Bindings.Length; i++) Bindings[i] = new Binding(); }

        static int Pct(int n) { return Math.Max(0, Math.Min(45, n)); }

        static string Esc(string s) { return (s ?? "").Replace("\r", " ").Replace("\n", " "); }

        public static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0 || line.StartsWith("#")) continue;
                    string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                    int n; int.TryParse(v, out n);
                    switch (k)
                    {
                        case "range": s.Range = Math.Max(40, Math.Min(900, n)); break;
                        case "spring": s.Spring = n != 0; break;
                        case "springStrength": s.SpringStrength = Math.Max(0, Math.Min(100, n)); break;
                        case "enforce": s.Enforce = n != 0; break;
                        case "enforceSeconds": s.EnforceSeconds = Math.Max(1, Math.Min(60, n)); break;
                        case "bindingsEnabled": s.BindingsEnabled = n != 0; break;
                        case "tapMs": s.TapMs = Math.Max(10, Math.Min(500, n)); break;
                        case "closeToTray": s.CloseToTray = n != 0; break;
                        case "ds4": s.Ds4Enabled = n != 0; break;
                        case "ds4Axes": s.Ds4Axes = n != 0; break;
                        case "invertAccel": s.InvertAccel = n != 0; break;
                        case "invertBrake": s.InvertBrake = n != 0; break;
                        case "originalNames": s.OriginalNames = n != 0; break;
                        case "showOriginal": s.ShowOriginal = n != 0; break;
                        case "theme": s.Theme = (ThemeMode)Math.Max(0, Math.Min(2, n)); break;
                        case "steerDzCenter": s.SteerDzCenter = Pct(n); break;
                        case "steerDzEnd": s.SteerDzEnd = Pct(n); break;
                        case "accelDzStart": s.AccelDzStart = Pct(n); break;
                        case "accelDzEnd": s.AccelDzEnd = Pct(n); break;
                        case "brakeDzStart": s.BrakeDzStart = Pct(n); break;
                        case "brakeDzEnd": s.BrakeDzEnd = Pct(n); break;
                        case "dzInGames": s.DzInGames = n != 0; break;
                        default:
                            int idx;
                            if (k.StartsWith("name.") && int.TryParse(k.Substring(5), out idx) && idx >= 0 && idx < Wheel.SlotCount && v.Length > 0)
                                s.Names[idx] = v;
                            else if (k.StartsWith("ds4map.") && int.TryParse(k.Substring(7), out idx) && idx >= 0 && idx < Wheel.SlotCount)
                            {
                                Ds4Btn db;
                                if (Enum.TryParse<Ds4Btn>(v, true, out db)) s.Ds4Map[idx] = db;
                            }
                            else if (k.StartsWith("bind.") && int.TryParse(k.Substring(5), out idx) && idx >= 0 && idx < Wheel.SlotCount)
                            {
                                int bar = v.IndexOf('|');
                                if (bar > 0)
                                {
                                    BindMode m;
                                    if (!Enum.TryParse<BindMode>(v.Substring(0, bar), true, out m)) m = BindMode.Off;
                                    s.Bindings[idx] = Binding.Create(m, v.Substring(bar + 1));
                                }
                            }
                            break;
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# DFGT Control settings");
                sb.AppendLine("range=" + Range);
                sb.AppendLine("spring=" + (Spring ? 1 : 0));
                sb.AppendLine("springStrength=" + SpringStrength);
                sb.AppendLine("enforce=" + (Enforce ? 1 : 0));
                sb.AppendLine("enforceSeconds=" + EnforceSeconds);
                sb.AppendLine("bindingsEnabled=" + (BindingsEnabled ? 1 : 0));
                sb.AppendLine("tapMs=" + TapMs);
                sb.AppendLine("closeToTray=" + (CloseToTray ? 1 : 0));
                sb.AppendLine("ds4=" + (Ds4Enabled ? 1 : 0));
                sb.AppendLine("ds4Axes=" + (Ds4Axes ? 1 : 0));
                sb.AppendLine("invertAccel=" + (InvertAccel ? 1 : 0));
                sb.AppendLine("invertBrake=" + (InvertBrake ? 1 : 0));
                sb.AppendLine("originalNames=" + (OriginalNames ? 1 : 0));
                sb.AppendLine("showOriginal=" + (ShowOriginal ? 1 : 0));
                sb.AppendLine("theme=" + (int)Theme);
                sb.AppendLine("steerDzCenter=" + SteerDzCenter);
                sb.AppendLine("steerDzEnd=" + SteerDzEnd);
                sb.AppendLine("accelDzStart=" + AccelDzStart);
                sb.AppendLine("accelDzEnd=" + AccelDzEnd);
                sb.AppendLine("brakeDzStart=" + BrakeDzStart);
                sb.AppendLine("brakeDzEnd=" + BrakeDzEnd);
                sb.AppendLine("dzInGames=" + (DzInGames ? 1 : 0));
                for (int i = 0; i < Wheel.SlotCount; i++)
                {
                    sb.AppendLine("name." + i + "=" + Esc(Names[i]));
                    sb.AppendLine("ds4map." + i + "=" + Ds4Map[i]);
                    Binding b = Bindings[i];
                    if (b != null && (b.Mode != BindMode.Off || b.Text.Length > 0))
                        sb.AppendLine("bind." + i + "=" + b.Mode + "|" + Esc(b.Text));
                }
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ Windows button names (DirectInput OEM registry)
    static class NameStore
    {
        const string Sub = @"\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM\VID_046D&PID_C29A\Buttons";
        static string BackupPath { get { return System.IO.Path.Combine(Settings.Folder, "button_names_backup.txt"); } }

        public static string[] ReadWindowsNames()
        {
            string[] names = new string[Wheel.ButtonCount];
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey("SYSTEM" + Sub))
                {
                    if (k == null) return null;
                    for (int i = 0; i < Wheel.ButtonCount; i++)
                        using (RegistryKey b = k.OpenSubKey(i.ToString()))
                            names[i] = b == null ? null : b.GetValue("") as string;
                }
            }
            catch { return null; }
            return names;
        }

        public static void BackupOnce()
        {
            if (File.Exists(BackupPath)) return;
            string[] cur = ReadWindowsNames();
            if (cur == null) return;
            Directory.CreateDirectory(Settings.Folder);
            File.WriteAllLines(BackupPath, cur.Select(n => n ?? "").ToArray(), Encoding.UTF8);
        }

        public static string[] ReadBackup()
        {
            if (!File.Exists(BackupPath)) return null;
            return File.ReadAllLines(BackupPath, Encoding.UTF8);
        }

        public static string[] ReadNamesFile(string path) { return File.ReadAllLines(path, Encoding.UTF8); }

        public static void WriteNames(string[] names, bool machine)
        {
            RegistryKey root = machine ? Registry.LocalMachine : Registry.CurrentUser;
            string basePath = (machine ? "SYSTEM" : "System") + Sub;
            for (int i = 0; i < Wheel.ButtonCount && i < names.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i])) continue;
                using (RegistryKey k = root.CreateSubKey(basePath + "\\" + i))
                {
                    k.SetValue("", names[i].Trim());
                    if (k.GetValue("Attributes") == null) k.SetValue("Attributes", new byte[] { 2, 128, 0, 0 }, RegistryValueKind.Binary);
                }
            }
        }
    }
}
