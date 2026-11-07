// DFGT Control - light / dark theme, plus the themed tab control and meters.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DFGTControl
{
    enum ThemeMode { System = 0, Light = 1, Dark = 2 }

    static class Theme
    {
        public static bool Dark;

        public static Color Chrome { get { return Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243); } }   // window background
        public static Color Surface { get { return Dark ? Color.FromArgb(40, 40, 40) : Color.White; } }                    // tab pages
        public static Color Text { get { return Dark ? Color.FromArgb(235, 235, 235) : Color.FromArgb(25, 25, 25); } }
        public static Color SubText { get { return Dark ? Color.FromArgb(158, 158, 158) : Color.FromArgb(112, 112, 112); } }
        public static Color Faint { get { return Dark ? Color.FromArgb(125, 125, 125) : Color.FromArgb(150, 150, 150); } }
        public static Color Border { get { return Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(210, 210, 210); } }
        public static Color Input { get { return Dark ? Color.FromArgb(52, 52, 52) : Color.White; } }
        public static Color ButtonBg { get { return Dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(251, 251, 251); } }
        public static Color ButtonHover { get { return Dark ? Color.FromArgb(68, 68, 68) : Color.FromArgb(229, 241, 251); } }
        public static Color Header { get { return Dark ? Color.FromArgb(48, 48, 48) : Color.FromArgb(240, 240, 240); } }
        public static Color GridLine { get { return Dark ? Color.FromArgb(55, 55, 55) : Color.FromArgb(230, 230, 230); } }
        public static Color Selection { get { return Dark ? Color.FromArgb(48, 72, 102) : Color.FromArgb(204, 228, 247); } }
        public static Color Accent { get { return Dark ? Color.FromArgb(96, 168, 255) : Color.FromArgb(0, 120, 215); } }
        public static Color Pressed { get { return Dark ? Color.FromArgb(40, 105, 55) : Color.FromArgb(150, 230, 150); } }
        public static Color Good { get { return Dark ? Color.FromArgb(110, 210, 130) : Color.FromArgb(16, 120, 50); } }
        public static Color Bad { get { return Dark ? Color.FromArgb(255, 145, 105) : Color.FromArgb(190, 60, 20); } }
        public static Color Link { get { return Dark ? Color.FromArgb(120, 175, 255) : Color.FromArgb(0, 102, 204); } }
        public static Color MeterBg { get { return Dark ? Color.FromArgb(28, 28, 28) : Color.White; } }
        public static Color Deadzone { get { return Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(222, 222, 222); } }

        public const string NoteTag = "note";

        public static bool SystemUsesDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return false; }
        }

        public static void SetMode(ThemeMode mode)
        {
            Dark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemUsesDark());
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// Dark or light title bar (Windows 10 2004+ / 11).
        public static void TitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            int on = Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref on, 4);
            }
            catch { }
        }

        static void ScrollbarTheme(Control c)
        {
            try { if (c.IsHandleCreated) SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        /// Colours a control and everything inside it.
        public static void Apply(Control c)
        {
            Color parentBg = c.Parent != null ? c.Parent.BackColor : Chrome;

            if (c is Form) { c.BackColor = Chrome; c.ForeColor = Text; TitleBar((Form)c); }
            else if (c is TabPage) { c.BackColor = Surface; c.ForeColor = Text; ((TabPage)c).UseVisualStyleBackColor = false; }
            else if (c is ThemedTabs) { c.BackColor = Chrome; c.Invalidate(); }
            else if (c is LinkLabel)
            {
                LinkLabel l = (LinkLabel)c; l.BackColor = parentBg;
                l.LinkColor = Link; l.ActiveLinkColor = Accent; l.VisitedLinkColor = Link;
            }
            else if (c is Label) { c.BackColor = parentBg; c.ForeColor = (c.Tag as string) == NoteTag ? SubText : Text; }
            else if (c is CheckBox || c is RadioButton)
            {
                c.BackColor = parentBg; c.ForeColor = Text;
                ButtonBase b = (ButtonBase)c;
                b.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
                if (Dark) { b.FlatAppearance.BorderColor = Faint; b.FlatAppearance.CheckedBackColor = Input; }
            }
            else if (c is Button)
            {
                Button b = (Button)c;
                if (Dark)
                {
                    b.FlatStyle = FlatStyle.Flat; b.BackColor = ButtonBg; b.ForeColor = Text;
                    b.FlatAppearance.BorderColor = Border; b.FlatAppearance.MouseOverBackColor = ButtonHover;
                    b.FlatAppearance.MouseDownBackColor = Selection;
                }
                else { b.FlatStyle = FlatStyle.Standard; b.UseVisualStyleBackColor = true; b.ForeColor = SystemColors.ControlText; }
            }
            else if (c is NumericUpDown || c is TextBox)
            {
                c.BackColor = Input; c.ForeColor = Text;
                if (c is NumericUpDown) ((NumericUpDown)c).BorderStyle = Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
                if (c is TextBox) ((TextBox)c).BorderStyle = Dark ? BorderStyle.FixedSingle : BorderStyle.Fixed3D;
            }
            else if (c is ComboBox)
            {
                ComboBox cb = (ComboBox)c; cb.BackColor = Input; cb.ForeColor = Text; cb.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
            }
            else if (c is TrackBar) { c.BackColor = parentBg; }
            else if (c is DataGridView) { StyleGrid((DataGridView)c); }
            else if (c is Meter) { c.BackColor = parentBg; c.ForeColor = Text; c.Invalidate(); }
            else { c.BackColor = parentBg; c.ForeColor = Text; c.Invalidate(); }

            if (c is ScrollableControl && ((ScrollableControl)c).AutoScroll) ScrollbarTheme(c);
            foreach (Control child in c.Controls) Apply(child);
        }

        static void StyleGrid(DataGridView g)
        {
            g.BackgroundColor = Surface;
            g.GridColor = GridLine;
            g.EnableHeadersVisualStyles = false;
            g.ColumnHeadersDefaultCellStyle.BackColor = Header;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Header;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Text;
            g.DefaultCellStyle.BackColor = Surface;
            g.DefaultCellStyle.ForeColor = Text;
            g.DefaultCellStyle.SelectionBackColor = Selection;
            g.DefaultCellStyle.SelectionForeColor = Text;
            g.BorderStyle = BorderStyle.FixedSingle;
            foreach (DataGridViewColumn col in g.Columns)
            {
                DataGridViewComboBoxColumn cc = col as DataGridViewComboBoxColumn;
                if (cc != null) { cc.FlatStyle = FlatStyle.Flat; cc.DefaultCellStyle.BackColor = Surface; cc.DefaultCellStyle.ForeColor = Text; }
            }
            foreach (Control sb in g.Controls) ScrollbarTheme(sb);
            g.Invalidate();
        }

        /// Dark-aware colours for the tray menu.
        public static void StyleMenu(ToolStrip m)
        {
            m.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            m.BackColor = Dark ? Color.FromArgb(43, 43, 43) : SystemColors.Menu;
            m.ForeColor = Text;
            foreach (ToolStripItem it in m.Items) it.ForeColor = Text;
        }

        class MenuColors : ProfessionalColorTable
        {
            Color Bg { get { return Dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249); } }
            public override Color ToolStripDropDownBackground { get { return Bg; } }
            public override Color ImageMarginGradientBegin { get { return Bg; } }
            public override Color ImageMarginGradientMiddle { get { return Bg; } }
            public override Color ImageMarginGradientEnd { get { return Bg; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Selection; } }
            public override Color MenuItemSelected { get { return Selection; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Border; } }
            public override Color CheckBackground { get { return Selection; } }
            public override Color CheckSelectedBackground { get { return Selection; } }
            public override Color CheckPressedBackground { get { return Selection; } }
        }
    }

    /// A tab control that paints its own tab strip, so it can follow the dark theme.
    class ThemedTabs : TabControl
    {
        public ThemedTabs()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Chrome);
            Rectangle page = DisplayRectangle; page.Inflate(1, 1);
            using (Brush sb = new SolidBrush(Theme.Surface)) g.FillRectangle(sb, page);
            using (Pen bp = new Pen(Theme.Border)) g.DrawRectangle(bp, page.X - 1, page.Y - 1, page.Width + 1, page.Height + 1);
            for (int i = 0; i < TabCount; i++)
            {
                Rectangle r = GetTabRect(i);
                bool sel = i == SelectedIndex;
                if (sel) { r.Y -= 2; r.Height += 3; }
                using (Brush b = new SolidBrush(sel ? Theme.Surface : Theme.Chrome)) g.FillRectangle(b, r);
                using (Pen bp = new Pen(Theme.Border))
                {
                    g.DrawLine(bp, r.Left, r.Top, r.Right - 1, r.Top);
                    g.DrawLine(bp, r.Left, r.Top, r.Left, r.Bottom - 1);
                    g.DrawLine(bp, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
                }
                if (sel) using (Brush ab = new SolidBrush(Theme.Accent)) g.FillRectangle(ab, r.X + 1, r.Y + 1, r.Width - 2, 2);
                TextRenderer.DrawText(g, TabPages[i].Text, Font, r, sel ? Theme.Text : Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// A horizontal meter. The bar shows the raw position; grey zones show deadzones; the text shows the output.
    class Meter : Control
    {
        public double Value;          // raw position: -1..1 (Centered) or 0..1
        public bool Centered;
        public double DzStart, DzEnd; // fractions; for Centered, DzStart = centre deadzone, DzEnd = at each end
        public Color BarColor = Color.FromArgb(0, 120, 215);

        public Meter() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = ClientRectangle; r.Width -= 1; r.Height -= 1;
            using (Brush bg = new SolidBrush(Theme.MeterBg)) g.FillRectangle(bg, r);
            using (Brush dz = new SolidBrush(Theme.Deadzone))
            {
                if (Centered)
                {
                    int half = r.Width / 2;
                    int c = (int)(DzStart * half), en = (int)(DzEnd * half);
                    if (c > 0) g.FillRectangle(dz, half - c, 0, c * 2, r.Height);
                    if (en > 0) { g.FillRectangle(dz, 0, 0, en, r.Height); g.FillRectangle(dz, r.Width - en, 0, en, r.Height); }
                }
                else
                {
                    int s = (int)(DzStart * r.Width), en = (int)(DzEnd * r.Width);
                    if (s > 0) g.FillRectangle(dz, 0, 0, s, r.Height);
                    if (en > 0) g.FillRectangle(dz, r.Width - en, 0, en, r.Height);
                }
            }
            using (Brush b = new SolidBrush(Color.FromArgb(Theme.Dark ? 210 : 255, BarColor)))
            {
                if (Centered)
                {
                    int mid = r.Width / 2;
                    int x = (int)(mid + Value * mid);
                    g.FillRectangle(b, Math.Min(mid, x), 0, Math.Abs(x - mid), r.Height);
                }
                else g.FillRectangle(b, 0, 0, (int)(Value * r.Width), r.Height);
            }
            using (Pen p = new Pen(Theme.Border))
            {
                if (Centered) g.DrawLine(p, r.Width / 2, 0, r.Width / 2, r.Height);
                g.DrawRectangle(p, r);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
