// DFGT Control - DirectInput calibration of the wheel's axes. Games that read the wheel through DirectInput
// (most racing sims) see axis values through this calibration, so it is how deadzones reach those games.
// DirectInput is called through its function tables directly (the automatic COM wrappers crash in EnumDevices).
using System;
using System.Runtime.InteropServices;

namespace DFGTControl
{
    static class DiCalibration
    {
        [DllImport("dinput8.dll")] static extern int DirectInput8Create(IntPtr hinst, int version, ref Guid riid, out IntPtr di, IntPtr outer);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateDeviceFn(IntPtr self, ref Guid guid, out IntPtr dev, IntPtr outer);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumDevicesFn(IntPtr self, int type, IntPtr cb, IntPtr pvRef, int flags);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PropertyFn(IntPtr self, IntPtr prop, IntPtr pdiph);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int EnumCallback(IntPtr deviceInstance, IntPtr pvRef);

        const int DIPROP_CALIBRATION = 11;
        const int DIPH_BYUSAGE = 3;
        public const ushort AxisX = 0x30, AxisZ = 0x32, AxisRz = 0x35;
        public const int WheelMax = 16383, PedalMax = 255;

        /// The DFGT's product GUID in DirectInput: Data1 = PID << 16 | VID.
        const uint ProductData1 = 0xC29A046D;

        static T Slot<T>(IntPtr obj, int index) where T : class
        {
            IntPtr vt = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, index * IntPtr.Size), typeof(T)) as T;
        }

        static void Release(IntPtr obj) { if (obj != IntPtr.Zero) Slot<ReleaseFn>(obj, 2)(obj); }

        /// Opens the wheel as a DirectInput device. Caller releases both returned pointers.
        static IntPtr Open(out IntPtr di, out string error)
        {
            error = null;
            Guid iid = new Guid("BF798031-483A-4DA2-AA99-5D64ED369700"); // IID_IDirectInput8W
            int hr = DirectInput8Create(GetModuleHandle(null), 0x0800, ref iid, out di, IntPtr.Zero);
            if (hr < 0 || di == IntPtr.Zero) { error = "DirectInput is not available (0x" + hr.ToString("X8") + ")."; return IntPtr.Zero; }

            Guid found = Guid.Empty;
            EnumCallback cb = delegate(IntPtr inst, IntPtr _)
            {
                Guid product = (Guid)Marshal.PtrToStructure(new IntPtr(inst.ToInt64() + 20), typeof(Guid));
                if ((uint)BitConverter.ToInt32(product.ToByteArray(), 0) != ProductData1) return 1; // DIENUM_CONTINUE
                found = (Guid)Marshal.PtrToStructure(new IntPtr(inst.ToInt64() + 4), typeof(Guid));
                return 0; // DIENUM_STOP
            };
            Slot<EnumDevicesFn>(di, 4)(di, 4 /*DI8DEVCLASS_GAMECTRL*/, Marshal.GetFunctionPointerForDelegate(cb), IntPtr.Zero, 1 /*ATTACHEDONLY*/);
            GC.KeepAlive(cb);
            if (found == Guid.Empty) { error = "The wheel was not found in DirectInput."; return IntPtr.Zero; }

            IntPtr dev;
            hr = Slot<CreateDeviceFn>(di, 3)(di, ref found, out dev, IntPtr.Zero);
            if (hr < 0 || dev == IntPtr.Zero) { error = "Could not open the wheel in DirectInput (0x" + hr.ToString("X8") + ")."; return IntPtr.Zero; }
            return dev;
        }

        static IntPtr CalBuffer(ushort usage, int min, int center, int max)
        {
            IntPtr p = Marshal.AllocHGlobal(28);
            Marshal.WriteInt32(p, 0, 28);                 // dwSize = sizeof(DIPROPCAL)
            Marshal.WriteInt32(p, 4, 16);                 // dwHeaderSize
            Marshal.WriteInt32(p, 8, (1 << 16) | usage);  // DIMAKEUSAGEDWORD(Generic Desktop, usage)
            Marshal.WriteInt32(p, 12, DIPH_BYUSAGE);
            Marshal.WriteInt32(p, 16, min);
            Marshal.WriteInt32(p, 20, center);
            Marshal.WriteInt32(p, 24, max);
            return p;
        }

        /// Returns {min, center, max} or null.
        public static int[] Read(ushort usage, out string error)
        {
            IntPtr di;
            IntPtr dev = Open(out di, out error);
            try
            {
                if (dev == IntPtr.Zero) return null;
                IntPtr p = CalBuffer(usage, 0, 0, 0);
                try
                {
                    int hr = Slot<PropertyFn>(dev, 5)(dev, new IntPtr(DIPROP_CALIBRATION), p);
                    if (hr < 0) { error = "Could not read calibration (0x" + hr.ToString("X8") + ")."; return null; }
                    return new[] { Marshal.ReadInt32(p, 16), Marshal.ReadInt32(p, 20), Marshal.ReadInt32(p, 24) };
                }
                finally { Marshal.FreeHGlobal(p); }
            }
            finally { Release(dev); Release(di); }
        }

        /// Writes calibration points for several axes. Returns null on success.
        public static string Write(ushort[] usages, int[][] points)
        {
            IntPtr di; string error;
            IntPtr dev = Open(out di, out error);
            try
            {
                if (dev == IntPtr.Zero) return error;
                for (int i = 0; i < usages.Length; i++)
                {
                    IntPtr p = CalBuffer(usages[i], points[i][0], points[i][1], points[i][2]);
                    try
                    {
                        int hr = Slot<PropertyFn>(dev, 6)(dev, new IntPtr(DIPROP_CALIBRATION), p);
                        if (hr < 0) return "Could not save calibration (0x" + hr.ToString("X8") + ").";
                    }
                    finally { Marshal.FreeHGlobal(p); }
                }
                return null;
            }
            finally { Release(dev); Release(di); }
        }
    }
}
