// DFGT Control - user interface.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("DFGT Control")]
[assembly: System.Reflection.AssemblyProduct("DFGT Control")]
[assembly: System.Reflection.AssemblyDescription("Settings and key bindings for the Logitech Driving Force GT")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace DFGTControl
{
    static class Program
    {
        public const string AppName = "DFGT Control";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 2 && args[0] == "--write-names-hklm")
            {
                // Elevated helper: writes button names for all users, then exits.
                try { NameStore.WriteNames(NameStore.ReadNamesFile(args[1]), true); return 0; }
                catch (Exception ex) { MessageBox.Show("Could not write button names:\n" + ex.Message, AppName); return 1; }
            }

            bool startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            bool created;
            using (Mutex mutex = new Mutex(true, "DFGTControl_SingleInstance_v1", out created))
            {
                if (!created)
                {
                    try { using (EventWaitHandle ev = EventWaitHandle.OpenExisting("DFGTControl_Show_v1")) ev.Set(); } catch { }
                    return 0;
                }
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Settings settings = Settings.Load();
                Theme.SetMode(settings.Theme);
                using (Engine engine = new Engine(settings))
                using (EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "DFGTControl_Show_v1"))
                {
                    MainForm form = new MainForm(engine, settings, startHidden, showEvent);
                    Application.Run(form);
                }
                settings.Save();
            }
            return 0;
        }

        /// The app icon at the given size: assets\app.ico when it was built in (see build.cmd),
        /// otherwise a simple drawn wheel.
        public static Icon AppIcon(int size)
        {
            try
            {
                using (Stream st = typeof(Program).Assembly.GetManifestResourceStream("app.ico"))
                    if (st != null) return new Icon(st, size, size);
            }
            catch { }
            return MakeIcon(size);
        }

        static Icon MakeIcon(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float w = size / 9f;
                    using (Pen rim = new Pen(Color.FromArgb(30, 30, 30), w * 1.6f))
                    using (Pen spoke = new Pen(Color.FromArgb(30, 30, 30), w * 1.3f))
                    using (Brush red = new SolidBrush(Color.FromArgb(220, 40, 40)))
                    {
                        float m = w * 1.0f;
                        g.FillEllipse(Brushes.White, m, m, size - 2 * m, size - 2 * m);
                        g.DrawEllipse(rim, m, m, size - 2 * m, size - 2 * m);
                        float c = size / 2f;
                        g.DrawLine(spoke, m, c, size - m, c);
                        g.DrawLine(spoke, c, c, c, size - m);
                        g.FillEllipse(red, c - w * 1.4f, c - w * 1.4f, w * 2.8f, w * 2.8f);
                    }
                }
                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }
    }

}
