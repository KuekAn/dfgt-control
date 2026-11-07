// DFGT Control - main window.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DFGTControl
{
    partial class MainForm : Form
    {
        readonly Engine engine;
        readonly Settings s;
        bool startHidden;
        bool reallyExit;
        bool loading = true;
        readonly float k;   // DPI scale
        int Px(int v) { return (int)Math.Round(v * k); }

        ThemedTabs tabs;
        RadioButton themeSystem, themeLight, themeDark;
        NumericUpDown dzSteerC, dzSteerE, dzAccS, dzAccE, dzBrkS, dzBrkE;
        CheckBox dzGamesChk;
        Label dzGamesStatus;
        System.Windows.Forms.Timer dzGamesTimer;
        int dzGamesVersion;

        Label status, applyInfo;
        Meter wheelMeter, accelMeter, brakeMeter;
        TrackBar rangeBar, springBar;
        NumericUpDown rangeNum, springNum, enforceNum, tapNum;
        CheckBox springChk, enforceChk, bindChk, startupChk, trayChk, invAccelChk, invBrakeChk;
        CheckBox ds4Chk, ds4AxesChk, showOrigChk;
        RadioButton namesSymbols, namesOriginal;
        Label ds4Status;
        LinkLabel ds4DriverLink;
        DataGridView grid;
        NotifyIcon tray;
        ToolStripMenuItem trayBindItem;
        System.Windows.Forms.Timer uiTimer;
        readonly bool[] shownPressed = new bool[Wheel.SlotCount];
        readonly EventWaitHandle showEvent;
        RegisteredWaitHandle showWait;
        int driverCheckTick;
        bool driverInstalled = true;
        static readonly string[] ds4Labels = Enum.GetValues(typeof(Ds4Btn)).Cast<Ds4Btn>().Select(Ds4Map.Label).ToArray();

        const int ColName = 0, ColKeys = 1, ColPs4 = 2;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";


        public MainForm(Engine engine, Settings settings, bool startHidden, EventWaitHandle showEvent)
        {
            this.engine = engine; s = settings; this.startHidden = startHidden; this.showEvent = showEvent;
            using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;

            Text = Program.AppName + " - Logitech Driving Force GT";
            Icon = Program.AppIcon(Px(32));
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(Px(860), Px(700));
            MinimumSize = new Size(Px(680), Px(560));
            StartPosition = FormStartPosition.CenterScreen;

            BuildTop();
            BuildTabs();
            BuildTray();
            LoadValues();
            loading = false;

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 40;
            uiTimer.Tick += delegate { RefreshLive(); };
            uiTimer.Start();

            if (!IsHandleCreated) CreateHandle();
            showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate
            {
                try { BeginInvoke(new Action(ShowFromTray)); } catch { }
            }, null, -1, false);

            ApplyTheme();
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }

        // ------------------------------------------------------------ theme
        void ApplyTheme()
        {
            Theme.SetMode(s.Theme);
            SuspendLayout();
            Theme.Apply(this);
            Theme.StyleMenu(tray.ContextMenuStrip);
            ResumeLayout();
            for (int i = 0; i < Wheel.SlotCount; i++)   // re-colour pressed rows on the next tick
            {
                shownPressed[i] = false;
                grid.Rows[i].DefaultCellStyle.BackColor = Color.Empty;
                grid.Rows[i].DefaultCellStyle.SelectionBackColor = Color.Empty;
            }
            dzGamesStatus.ForeColor = Theme.SubText;
            Invalidate(true);
        }

        void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (s.Theme != ThemeMode.System) return;
            bool dark = Theme.SystemUsesDark();
            if (dark == Theme.Dark) return;
            try { BeginInvoke(new Action(ApplyTheme)); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden) { startHidden = false; value = false; if (!IsHandleCreated) CreateHandle(); }
            base.SetVisibleCore(value);
        }

        // ------------------------------------------------------------ names
        /// The name shown for a button: its symbol name (Cross…) or, with "original names", Button N.
        public string DisplayName(int i) { return s.OriginalNames ? Wheel.OriginalName(i) : s.Names[i]; }
        /// The faint reference label next to the name (the other naming style).
        public string ReferenceName(int i) { return s.OriginalNames ? s.Names[i] : Wheel.OriginalName(i); }

        public string KeysText(int i)
        {
            Binding b = s.Bindings[i];
            return b.Active ? b.Text + "  (" + b.Mode + ")" : "";
        }

        // ------------------------------------------------------------ layout
        void BuildTop()
        {
            TableLayoutPanel top = new TableLayoutPanel();
            top.Dock = DockStyle.Top; top.AutoSize = true; top.ColumnCount = 2; top.Padding = new Padding(Px(10), Px(8), Px(10), Px(4));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            status = new Label(); status.AutoSize = true; status.Font = new Font(Font, FontStyle.Bold); status.Margin = new Padding(0, 0, 0, Px(6));
            top.Controls.Add(status, 0, 0); top.SetColumnSpan(status, 2);

            wheelMeter = AddMeter(top, "Wheel", 1, true);
            accelMeter = AddMeter(top, "Accelerator", 2, false);
            brakeMeter = AddMeter(top, "Brake", 3, false);
            accelMeter.BarColor = Color.FromArgb(16, 140, 60);
            brakeMeter.BarColor = Color.FromArgb(200, 50, 40);
            Controls.Add(top);
        }

        Meter AddMeter(TableLayoutPanel p, string label, int row, bool centered)
        {
            Label l = new Label(); l.Text = label; l.AutoSize = true; l.Anchor = AnchorStyles.Left; l.Margin = new Padding(0, Px(2), Px(10), Px(2));
            Meter m = new Meter(); m.Centered = centered; m.Height = Px(18); m.Dock = DockStyle.Fill; m.Margin = new Padding(0, Px(2), 0, Px(2));
            p.Controls.Add(l, 0, row); p.Controls.Add(m, 1, row);
            return m;
        }

        Label Note(string text)
        {
            Label l = new Label(); l.Text = text; l.AutoSize = true; l.Tag = Theme.NoteTag;
            l.MaximumSize = new Size(Px(800), 0); l.Margin = new Padding(0, Px(4), 0, Px(8));
            return l;
        }

        Label Heading(string text, int topMargin)
        {
            Label h = new Label(); h.Text = text; h.AutoSize = true; h.Font = new Font(Font, FontStyle.Bold); h.Margin = new Padding(0, Px(topMargin), 0, Px(2));
            return h;
        }

        FlowLayoutPanel Row()
        {
            FlowLayoutPanel p = new FlowLayoutPanel(); p.AutoSize = true; p.WrapContents = false; p.Margin = new Padding(0, Px(2), 0, Px(2));
            return p;
        }

        TabPage Page(string title)
        {
            TabPage t = new TabPage(title); t.Padding = new Padding(Px(12)); t.UseVisualStyleBackColor = false; t.BackColor = SystemColors.Window;
            return t;
        }

        CheckBox Check(string text)
        {
            CheckBox c = new CheckBox(); c.Text = text; c.AutoSize = true; return c;
        }

        void BuildTabs()
        {
            tabs = new ThemedTabs(); tabs.Dock = DockStyle.Fill; tabs.Padding = new Point(Px(12), Px(4));
            Controls.Add(tabs);
            tabs.BringToFront();
            BuildWheelTab(tabs);
            BuildDeadzoneTab(tabs);
            BuildButtonsTab(tabs);
            BuildOptionsTab(tabs);
        }

        void BuildWheelTab(TabControl tabs)
        {
            TabPage tp = Page("Wheel settings");
            FlowLayoutPanel f = new FlowLayoutPanel(); f.Dock = DockStyle.Fill; f.FlowDirection = FlowDirection.TopDown; f.WrapContents = false; f.AutoScroll = true;
            tp.Controls.Add(f);

            f.Controls.Add(Heading("Operating range (degrees of rotation)", 0));
            FlowLayoutPanel r1 = Row();
            rangeBar = new TrackBar(); rangeBar.Minimum = 40; rangeBar.Maximum = 900; rangeBar.TickFrequency = 50; rangeBar.SmallChange = 10; rangeBar.LargeChange = 90; rangeBar.Width = Px(420); rangeBar.BackColor = SystemColors.Window;
            rangeNum = new NumericUpDown(); rangeNum.Minimum = 40; rangeNum.Maximum = 900; rangeNum.Increment = 10; rangeNum.Width = Px(70); rangeNum.Margin = new Padding(Px(6), Px(8), 0, 0);
            r1.Controls.Add(rangeBar); r1.Controls.Add(rangeNum);
            f.Controls.Add(r1);
            FlowLayoutPanel presets = Row();
            foreach (int deg in new int[] { 180, 270, 360, 540, 720, 900 })
            {
                Button b = new Button(); b.Text = deg + "°"; b.AutoSize = true; b.Tag = deg;
                b.Click += delegate(object o, EventArgs e) { rangeNum.Value = (int)((Button)o).Tag; };
                presets.Controls.Add(b);
            }
            f.Controls.Add(presets);
            rangeBar.ValueChanged += delegate { if (rangeNum.Value != rangeBar.Value) rangeNum.Value = rangeBar.Value; };
            rangeNum.ValueChanged += delegate { if (rangeBar.Value != (int)rangeNum.Value) rangeBar.Value = (int)rangeNum.Value; WheelChanged(); };

            f.Controls.Add(Heading("Centering spring", 14));
            springChk = Check("Enable centering spring (pulls the wheel back to the middle)");
            f.Controls.Add(springChk);
            FlowLayoutPanel r2 = Row();
            springBar = new TrackBar(); springBar.Minimum = 0; springBar.Maximum = 100; springBar.TickFrequency = 10; springBar.Width = Px(420); springBar.BackColor = SystemColors.Window;
            springNum = new NumericUpDown(); springNum.Minimum = 0; springNum.Maximum = 100; springNum.Width = Px(70); springNum.Margin = new Padding(Px(6), Px(8), 0, 0);
            Label pct = new Label(); pct.Text = "% strength"; pct.AutoSize = true; pct.Margin = new Padding(Px(4), Px(11), 0, 0);
            r2.Controls.Add(springBar); r2.Controls.Add(springNum); r2.Controls.Add(pct);
            f.Controls.Add(r2);
            springChk.CheckedChanged += delegate { springBar.Enabled = springNum.Enabled = springChk.Checked; WheelChanged(); };
            springBar.ValueChanged += delegate { if (springNum.Value != springBar.Value) springNum.Value = springBar.Value; };
            springNum.ValueChanged += delegate { if (springBar.Value != (int)springNum.Value) springBar.Value = (int)springNum.Value; WheelChanged(); };

            f.Controls.Add(Heading("Keeping settings applied", 14));
            FlowLayoutPanel r3 = Row();
            enforceChk = Check("Re-send these settings every"); enforceChk.Margin = new Padding(0, Px(5), 0, 0);
            enforceNum = new NumericUpDown(); enforceNum.Minimum = 1; enforceNum.Maximum = 60; enforceNum.Width = Px(55);
            Label sec = new Label(); sec.Text = "seconds (puts them back if a game or driver changes them)"; sec.AutoSize = true; sec.Margin = new Padding(Px(4), Px(6), 0, 0);
            r3.Controls.Add(enforceChk); r3.Controls.Add(enforceNum); r3.Controls.Add(sec);
            f.Controls.Add(r3);
            enforceChk.CheckedChanged += delegate { enforceNum.Enabled = enforceChk.Checked; if (!loading) { s.Enforce = enforceChk.Checked; s.Save(); } };
            enforceNum.ValueChanged += delegate { if (!loading) { s.EnforceSeconds = (int)enforceNum.Value; s.Save(); } };

            FlowLayoutPanel r4 = Row();
            Button applyNow = new Button(); applyNow.Text = "Send to wheel now"; applyNow.AutoSize = true;
            applyNow.Click += delegate { engine.RequestApply(); };
            applyInfo = new Label(); applyInfo.AutoSize = true; applyInfo.Margin = new Padding(Px(8), Px(8), 0, 0); applyInfo.Tag = Theme.NoteTag;
            r4.Controls.Add(applyNow); r4.Controls.Add(applyInfo);
            f.Controls.Add(r4);

            f.Controls.Add(Note("Changes go to the wheel straight away, also while a game is running, and are applied whenever the wheel is plugged in or Windows starts. " +
                "You don't need to launch games from this app. Force-feedback strength and other effects are still set inside each game."));
            tabs.TabPages.Add(tp);
        }

        NumericUpDown Pct(int max)
        {
            NumericUpDown n = new NumericUpDown(); n.Minimum = 0; n.Maximum = max; n.Width = Px(58); n.Margin = new Padding(0, Px(3), Px(4), Px(3));
            n.ValueChanged += delegate { DeadzonesChanged(); };
            return n;
        }

        Label Cell(string text, bool bold)
        {
            Label l = new Label(); l.Text = text; l.AutoSize = true; l.Anchor = AnchorStyles.Left; l.Margin = new Padding(0, Px(6), Px(16), Px(6));
            if (bold) l.Font = new Font(Font, FontStyle.Bold);
            return l;
        }

        Control PctBox(NumericUpDown n, string after)
        {
            FlowLayoutPanel p = Row(); p.Margin = new Padding(0, 0, Px(16), 0);
            Label l = new Label(); l.Text = after; l.AutoSize = true; l.Margin = new Padding(0, Px(6), 0, 0);
            p.Controls.Add(n); p.Controls.Add(l);
            return p;
        }

        void BuildDeadzoneTab(TabControl tabs)
        {
            TabPage tp = Page("Pedals & deadzones");
            FlowLayoutPanel f = new FlowLayoutPanel(); f.Dock = DockStyle.Fill; f.FlowDirection = FlowDirection.TopDown; f.WrapContents = false; f.AutoScroll = true;
            tp.Controls.Add(f);

            f.Controls.Add(Heading("Deadzones", 0));
            f.Controls.Add(Note("A start deadzone ignores the first part of the travel; an end deadzone gives full output before the physical end. " +
                                "The grey zones on the meters at the top show them, and the percentage shows the result."));

            TableLayoutPanel t = new TableLayoutPanel(); t.AutoSize = true; t.ColumnCount = 4; t.RowCount = 4; t.Margin = new Padding(0, Px(4), 0, Px(4));
            t.Controls.Add(Cell("", false), 0, 0);
            t.Controls.Add(Cell("Start / centre", true), 1, 0);
            t.Controls.Add(Cell("End", true), 2, 0);
            t.Controls.Add(Cell("", false), 3, 0);

            dzSteerC = Pct(45); dzSteerE = Pct(45); dzAccS = Pct(45); dzAccE = Pct(45); dzBrkS = Pct(45); dzBrkE = Pct(45);
            t.Controls.Add(Cell("Steering", true), 0, 1);
            t.Controls.Add(PctBox(dzSteerC, "% around the centre"), 1, 1);
            t.Controls.Add(PctBox(dzSteerE, "% at each end"), 2, 1);

            invAccelChk = Check("Invert"); invAccelChk.Anchor = AnchorStyles.Left;
            invBrakeChk = Check("Invert"); invBrakeChk.Anchor = AnchorStyles.Left;
            t.Controls.Add(Cell("Accelerator", true), 0, 2);
            t.Controls.Add(PctBox(dzAccS, "% at the start"), 1, 2);
            t.Controls.Add(PctBox(dzAccE, "% at the end"), 2, 2);
            t.Controls.Add(invAccelChk, 3, 2);
            t.Controls.Add(Cell("Brake", true), 0, 3);
            t.Controls.Add(PctBox(dzBrkS, "% at the start"), 1, 3);
            t.Controls.Add(PctBox(dzBrkE, "% at the end"), 2, 3);
            t.Controls.Add(invBrakeChk, 3, 3);
            f.Controls.Add(t);
            invAccelChk.CheckedChanged += delegate { if (loading) return; s.InvertAccel = invAccelChk.Checked; s.Save(); engine.AxisSettingsChanged(); if (s.DzInGames) QueueGameDeadzones(); };
            invBrakeChk.CheckedChanged += delegate { if (loading) return; s.InvertBrake = invBrakeChk.Checked; s.Save(); engine.AxisSettingsChanged(); if (s.DzInGames) QueueGameDeadzones(); };
            f.Controls.Add(Note("Invert: tick it if that pedal shows 100% when you're not pressing it."));

            f.Controls.Add(Heading("Where the deadzones apply", 14));
            f.Controls.Add(Note("Always: the meters above and the virtual PS4 controller."));
            dzGamesChk = Check("Also apply them in games that read the wheel through DirectInput (most racing sims)");
            f.Controls.Add(dzGamesChk);
            dzGamesStatus = new Label(); dzGamesStatus.AutoSize = true; dzGamesStatus.Margin = new Padding(Px(18), Px(2), 0, Px(2)); dzGamesStatus.Tag = Theme.NoteTag;
            f.Controls.Add(dzGamesStatus);
            f.Controls.Add(Note("This saves them in the wheel's Windows calibration, so it works without this app running. Restart a game to pick up changes. " +
                                "Games get the pedal start/end and the steering end deadzones; for a steering centre deadzone, use the game's own setting. " +
                                "Games that read the wheel another way (not DirectInput) are not affected. Unticking puts the calibration back to normal."));
            dzGamesChk.CheckedChanged += delegate { if (loading) return; s.DzInGames = dzGamesChk.Checked; s.Save(); QueueGameDeadzones(); };

            dzGamesTimer = new System.Windows.Forms.Timer(); dzGamesTimer.Interval = 600;
            dzGamesTimer.Tick += delegate { dzGamesTimer.Stop(); WriteGameDeadzones(); };
            tabs.TabPages.Add(tp);
        }

        void DeadzonesChanged()
        {
            if (loading) return;
            s.SteerDzCenter = (int)dzSteerC.Value; s.SteerDzEnd = (int)dzSteerE.Value;
            s.AccelDzStart = (int)dzAccS.Value; s.AccelDzEnd = (int)dzAccE.Value;
            s.BrakeDzStart = (int)dzBrkS.Value; s.BrakeDzEnd = (int)dzBrkE.Value;
            s.Save();
            engine.AxisSettingsChanged();
            if (s.DzInGames) QueueGameDeadzones();
        }

        /// Waits until the values stop changing, then saves the calibration once.
        void QueueGameDeadzones()
        {
            dzGamesStatus.ForeColor = Theme.SubText;
            dzGamesStatus.Text = "Saving…";
            dzGamesTimer.Stop(); dzGamesTimer.Start();
        }

        void WriteGameDeadzones()
        {
            bool on = s.DzInGames;
            ushort[] axes = { DiCalibration.AxisX, DiCalibration.AxisZ, DiCalibration.AxisRz };
            int[][] pts = on
                ? new[] { Deadzone.SteerCalibration(s.SteerDzEnd), Deadzone.PedalCalibration(s.AccelDzStart, s.AccelDzEnd, s.InvertAccel), Deadzone.PedalCalibration(s.BrakeDzStart, s.BrakeDzEnd, s.InvertBrake) }
                : new[] { Deadzone.SteerCalibration(0), Deadzone.PedalCalibration(0, 0, false), Deadzone.PedalCalibration(0, 0, false) };
            int version = ++dzGamesVersion;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err;
                try { err = DiCalibration.Write(axes, pts); } catch (Exception ex) { err = ex.Message; }
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (version != dzGamesVersion) return;
                        dzGamesStatus.ForeColor = err == null ? Theme.Good : Theme.Bad;
                        dzGamesStatus.Text = err != null ? err + " (Is the wheel plugged in?)"
                            : on ? "Saved for games at " + DateTime.Now.ToString("HH:mm:ss") + ". Restart a running game to use it."
                                 : "Game calibration is back to normal.";
                    }));
                }
                catch { }
            });
        }

        void BuildButtonsTab(TabControl tabs)
        {
            TabPage tb = Page("Buttons");
            TableLayoutPanel bt = new TableLayoutPanel(); bt.Dock = DockStyle.Fill; bt.ColumnCount = 1; bt.RowCount = 6;
            for (int r = 0; r < 6; r++) bt.RowStyles.Add(r == 4 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            tb.Controls.Add(bt);

            // Row 0: keyboard
            FlowLayoutPanel b1 = Row();
            bindChk = Check("Keyboard keys"); bindChk.Font = new Font(Font, FontStyle.Bold); bindChk.Margin = new Padding(0, Px(5), Px(24), 0);
            Label tl = new Label(); tl.Text = "Key press length for Tap:"; tl.AutoSize = true; tl.Margin = new Padding(0, Px(6), Px(4), 0);
            tapNum = new NumericUpDown(); tapNum.Minimum = 10; tapNum.Maximum = 500; tapNum.Increment = 10; tapNum.Width = Px(60);
            Label ms = new Label(); ms.Text = "ms"; ms.AutoSize = true; ms.Margin = new Padding(Px(2), Px(6), 0, 0);
            b1.Controls.Add(bindChk); b1.Controls.Add(tl); b1.Controls.Add(tapNum); b1.Controls.Add(ms);
            bt.Controls.Add(b1, 0, 0);
            bindChk.CheckedChanged += delegate { if (loading) return; s.BindingsEnabled = bindChk.Checked; trayBindItem.Checked = bindChk.Checked; engine.BindingsChanged(); s.Save(); };
            tapNum.ValueChanged += delegate { if (loading) return; s.TapMs = (int)tapNum.Value; engine.SetTapMs(s.TapMs); s.Save(); };

            // Row 1: PS4 controller
            FlowLayoutPanel b2 = Row();
            ds4Chk = Check("PS4 controller (virtual DualShock 4)"); ds4Chk.Font = new Font(Font, FontStyle.Bold); ds4Chk.Margin = new Padding(0, Px(5), Px(12), 0);
            ds4Status = new Label(); ds4Status.AutoSize = true; ds4Status.Margin = new Padding(0, Px(6), Px(8), 0);
            ds4DriverLink = new LinkLabel(); ds4DriverLink.Text = "Get the ViGEmBus driver"; ds4DriverLink.AutoSize = true; ds4DriverLink.Margin = new Padding(0, Px(6), 0, 0);
            ds4DriverLink.LinkClicked += delegate { try { Process.Start(VirtualDs4.DriverUrl); } catch { } };
            b2.Controls.Add(ds4Chk); b2.Controls.Add(ds4Status); b2.Controls.Add(ds4DriverLink);
            bt.Controls.Add(b2, 0, 1);
            ds4Chk.CheckedChanged += delegate { if (loading) return; s.Ds4Enabled = ds4Chk.Checked; s.Save(); engine.Ds4Refresh(); };

            FlowLayoutPanel b3 = Row();
            ds4AxesChk = Check("Also send steering (left stick) and pedals (L2 = brake, R2 = accelerator) to the PS4 controller");
            ds4AxesChk.Margin = new Padding(Px(18), 0, 0, Px(4));
            b3.Controls.Add(ds4AxesChk);
            bt.Controls.Add(b3, 0, 2);
            ds4AxesChk.CheckedChanged += delegate { if (loading) return; s.Ds4Axes = ds4AxesChk.Checked; s.Save(); engine.Ds4MapChanged(); };

            // Row 3: naming
            FlowLayoutPanel b4 = Row(); b4.Margin = new Padding(0, Px(4), 0, Px(4));
            Label nl = new Label(); nl.Text = "Button names:"; nl.AutoSize = true; nl.Margin = new Padding(0, Px(5), Px(6), 0);
            namesSymbols = new RadioButton(); namesSymbols.Text = "Symbols (Cross, Square…)"; namesSymbols.AutoSize = true; namesSymbols.Margin = new Padding(0, Px(3), Px(8), 0);
            namesOriginal = new RadioButton(); namesOriginal.Text = "Original (Button 1, Button 2…)"; namesOriginal.AutoSize = true; namesOriginal.Margin = new Padding(0, Px(3), Px(24), 0);
            showOrigChk = Check("Show the other name in grey for reference"); showOrigChk.Margin = new Padding(0, Px(4), 0, 0);
            b4.Controls.Add(nl); b4.Controls.Add(namesSymbols); b4.Controls.Add(namesOriginal); b4.Controls.Add(showOrigChk);
            bt.Controls.Add(b4, 0, 3);
            namesOriginal.CheckedChanged += delegate { if (loading) return; s.OriginalNames = namesOriginal.Checked; s.Save(); RefreshNames(); };
            showOrigChk.CheckedChanged += delegate { if (loading) return; s.ShowOriginal = showOrigChk.Checked; s.Save(); grid.Invalidate(); };

            // Row 4: the table
            grid = new DataGridView(); grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.BackgroundColor = SystemColors.Window; grid.BorderStyle = BorderStyle.FixedSingle;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect; grid.MultiSelect = false;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.GridColor = Color.FromArgb(230, 230, 230);
            grid.RowTemplate.Height = Px(26);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersHeight = Px(28);
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(Font, FontStyle.Bold);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(204, 228, 247);
            grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
            grid.DefaultCellStyle.Padding = new Padding(Px(4), 0, Px(4), 0);

            DataGridViewTextBoxColumn cName = new DataGridViewTextBoxColumn(); cName.HeaderText = "Wheel button"; cName.ReadOnly = true;
            cName.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; cName.FillWeight = 34;
            DataGridViewTextBoxColumn cKeys = new DataGridViewTextBoxColumn(); cKeys.HeaderText = "Keyboard keys  (double-click to set)"; cKeys.ReadOnly = true;
            cKeys.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; cKeys.FillWeight = 40;
            DataGridViewComboBoxColumn cPs4 = new DataGridViewComboBoxColumn(); cPs4.HeaderText = "PS4 controller button";
            cPs4.DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox; cPs4.DisplayStyleForCurrentCellOnly = true; cPs4.FlatStyle = FlatStyle.Flat;
            cPs4.Items.AddRange(ds4Labels.Cast<object>().ToArray());
            cPs4.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; cPs4.FillWeight = 26;
            grid.Columns.Add(cName); grid.Columns.Add(cKeys); grid.Columns.Add(cPs4);
            foreach (DataGridViewColumn c in grid.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;

            grid.CellPainting += GridCellPainting;
            grid.CellDoubleClick += delegate(object o, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex != ColPs4) EditBinding(e.RowIndex); };
            grid.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (grid.CurrentCell == null || grid.CurrentCell.ColumnIndex == ColPs4) return;
                if (e.KeyCode == Keys.Enter) { EditBinding(grid.CurrentCell.RowIndex); e.Handled = true; }
                else if (e.KeyCode == Keys.Delete) { ClearBinding(grid.CurrentCell.RowIndex); e.Handled = true; }
            };
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate(object o, DataGridViewCellEventArgs e)
            {
                if (loading || e.RowIndex < 0 || e.ColumnIndex != ColPs4) return;
                int idx = Array.IndexOf(ds4Labels, grid.Rows[e.RowIndex].Cells[ColPs4].Value as string);
                if (idx < 0) return;
                s.Ds4Map[e.RowIndex] = (Ds4Btn)idx;
                s.Save();
                engine.Ds4MapChanged();
            };
            grid.DataError += delegate(object o, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            bt.Controls.Add(grid, 0, 4);

            // Row 5: actions + help
            FlowLayoutPanel b5 = Row();
            Button edit = new Button(); edit.Text = "Set keys / rename…"; edit.AutoSize = true; edit.Click += delegate { EditBinding(SelectedRow()); };
            Button clear = new Button(); clear.Text = "Clear keys"; clear.AutoSize = true; clear.Click += delegate { ClearBinding(SelectedRow()); };
            Button resetPs4 = new Button(); resetPs4.Text = "Reset PS4 buttons to default"; resetPs4.AutoSize = true; resetPs4.Click += delegate { ResetPs4(); };
            b5.Controls.Add(edit); b5.Controls.Add(clear); b5.Controls.Add(resetPs4);
            TableLayoutPanel foot = new TableLayoutPanel(); foot.AutoSize = true; foot.ColumnCount = 1; foot.Dock = DockStyle.Fill; foot.Margin = new Padding(0);
            foot.Controls.Add(b5, 0, 0);
            foot.Controls.Add(Note("Press a wheel button and its row lights up. Games always see the wheel's own button too (\"Button 5\"), " +
                "so in a game assign each action to only one of: the wheel button, its keyboard keys, or its PS4 controller button. " +
                "\"Wheel only\" means the button is not sent to the PS4 controller."), 0, 1);
            bt.Controls.Add(foot, 0, 5);
            tabs.TabPages.Add(tb);
        }

        void BuildOptionsTab(TabControl tabs)
        {
            TabPage to = Page("Options");
            FlowLayoutPanel of = new FlowLayoutPanel(); of.Dock = DockStyle.Fill; of.FlowDirection = FlowDirection.TopDown; of.WrapContents = false; of.AutoScroll = true;
            to.Controls.Add(of);
            startupChk = Check("Start with Windows (runs quietly in the tray and applies your settings)");
            trayChk = Check("Closing the window keeps the app running in the tray");
            of.Controls.Add(startupChk); of.Controls.Add(trayChk);
            startupChk.CheckedChanged += delegate { if (!loading) SetStartup(startupChk.Checked); };
            trayChk.CheckedChanged += delegate { if (!loading) { s.CloseToTray = trayChk.Checked; s.Save(); } };

            of.Controls.Add(Heading("Appearance", 18));
            FlowLayoutPanel ar = Row();
            themeSystem = new RadioButton(); themeSystem.Text = "Match Windows"; themeSystem.AutoSize = true; themeSystem.Margin = new Padding(0, Px(2), Px(16), 0);
            themeLight = new RadioButton(); themeLight.Text = "Light"; themeLight.AutoSize = true; themeLight.Margin = new Padding(0, Px(2), Px(16), 0);
            themeDark = new RadioButton(); themeDark.Text = "Dark"; themeDark.AutoSize = true; themeDark.Margin = new Padding(0, Px(2), Px(16), 0);
            ar.Controls.Add(themeSystem); ar.Controls.Add(themeLight); ar.Controls.Add(themeDark);
            of.Controls.Add(ar);
            EventHandler themeChanged = delegate(object o, EventArgs e)
            {
                if (loading || !((RadioButton)o).Checked) return;
                s.Theme = themeDark.Checked ? ThemeMode.Dark : themeLight.Checked ? ThemeMode.Light : ThemeMode.System;
                s.Save();
                ApplyTheme();
            };
            themeSystem.CheckedChanged += themeChanged; themeLight.CheckedChanged += themeChanged; themeDark.CheckedChanged += themeChanged;

            of.Controls.Add(Heading("Button names in other programs", 18));
            of.Controls.Add(Note("Windows keeps a name for each wheel button. Programs that ask Windows for button names show these; many games ignore them and show \"Button 5\" anyway. " +
                "This saves the names as they currently appear on the Buttons tab: symbols, or Button 1, Button 2… if you picked Original there."));
            FlowLayoutPanel nr = Row();
            Button writeNames = new Button(); writeNames.Text = "Save button names to Windows…"; writeNames.AutoSize = true; writeNames.Click += delegate { WriteNamesToWindows(null); };
            Button restore = new Button(); restore.Text = "Restore the names Windows had before"; restore.AutoSize = true; restore.Click += delegate { RestoreNames(); };
            nr.Controls.Add(writeNames); nr.Controls.Add(restore);
            of.Controls.Add(nr);

            of.Controls.Add(Heading("Settings file", 18));
            LinkLabel link = new LinkLabel(); link.Text = Settings.FilePath; link.AutoSize = true;
            link.LinkClicked += delegate { try { Directory.CreateDirectory(Settings.Folder); Process.Start("explorer.exe", "\"" + Settings.Folder + "\""); } catch { } };
            of.Controls.Add(link);
            tabs.TabPages.Add(to);
        }

        void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = Program.AppIcon(Px(16) < 16 ? 16 : Px(16));
            tray.Text = Program.AppName;
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Open " + Program.AppName, null, delegate { ShowFromTray(); });
            m.Items.Add("Send settings to wheel now", null, delegate { engine.RequestApply(); });
            trayBindItem = new ToolStripMenuItem("Keyboard keys enabled");
            trayBindItem.Click += delegate { bindChk.Checked = !bindChk.Checked; };
            m.Items.Add(trayBindItem);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Exit", null, delegate { reallyExit = true; Close(); });
            tray.ContextMenuStrip = m;
            tray.DoubleClick += delegate { ShowFromTray(); };
            tray.Visible = true;
        }

        void LoadValues()
        {
            rangeNum.Value = s.Range; rangeBar.Value = s.Range;
            springChk.Checked = s.Spring; springNum.Value = s.SpringStrength; springBar.Value = s.SpringStrength;
            springBar.Enabled = springNum.Enabled = s.Spring;
            invAccelChk.Checked = s.InvertAccel; invBrakeChk.Checked = s.InvertBrake;
            enforceChk.Checked = s.Enforce; enforceNum.Value = s.EnforceSeconds; enforceNum.Enabled = s.Enforce;
            bindChk.Checked = s.BindingsEnabled; trayBindItem.Checked = s.BindingsEnabled; tapNum.Value = s.TapMs;
            ds4Chk.Checked = s.Ds4Enabled; ds4AxesChk.Checked = s.Ds4Axes;
            namesOriginal.Checked = s.OriginalNames; namesSymbols.Checked = !s.OriginalNames; showOrigChk.Checked = s.ShowOriginal;
            trayChk.Checked = s.CloseToTray;
            themeSystem.Checked = s.Theme == ThemeMode.System; themeLight.Checked = s.Theme == ThemeMode.Light; themeDark.Checked = s.Theme == ThemeMode.Dark;
            dzSteerC.Value = s.SteerDzCenter; dzSteerE.Value = s.SteerDzEnd;
            dzAccS.Value = s.AccelDzStart; dzAccE.Value = s.AccelDzEnd;
            dzBrkS.Value = s.BrakeDzStart; dzBrkE.Value = s.BrakeDzEnd;
            dzGamesChk.Checked = s.DzInGames;
            startupChk.Checked = IsStartupEnabled();
            if (startupChk.Checked) SetStartup(true); // refresh the path in case the exe was moved

            grid.Rows.Clear();
            for (int i = 0; i < Wheel.SlotCount; i++)
            {
                grid.Rows.Add(DisplayName(i), KeysText(i), Ds4Map.Label(s.Ds4Map[i]));
            }
        }

        void UpdateRow(int i)
        {
            grid.Rows[i].Cells[ColName].Value = DisplayName(i);
            grid.Rows[i].Cells[ColKeys].Value = KeysText(i);
        }

        void RefreshNames()
        {
            for (int i = 0; i < Wheel.SlotCount; i++) grid.Rows[i].Cells[ColName].Value = DisplayName(i);
            grid.Invalidate();
        }

        /// Name column: the name, then (optionally) the reference name in grey.
        void GridCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != ColName) return;
            bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
            e.PaintBackground(e.CellBounds, selected);
            Rectangle r = e.CellBounds; r.X += Px(6); r.Width -= Px(8);
            string main = DisplayName(e.RowIndex);
            TextFormatFlags fl = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics, main, grid.Font, r, Theme.Text, fl);
            if (s.ShowOriginal)
            {
                int w = TextRenderer.MeasureText(e.Graphics, main, grid.Font, r.Size, fl).Width;
                Rectangle r2 = r; r2.X += w + Px(4); r2.Width -= w + Px(4);
                if (r2.Width > 10) TextRenderer.DrawText(e.Graphics, ReferenceName(e.RowIndex), grid.Font, r2, Theme.Faint, fl);
            }
            e.Handled = true;
        }

        int SelectedRow() { return grid.CurrentCell == null ? -1 : grid.CurrentCell.RowIndex; }

        // ------------------------------------------------------------ behaviour
        void WheelChanged()
        {
            if (loading) return;
            s.Range = (int)rangeNum.Value;
            s.Spring = springChk.Checked;
            s.SpringStrength = (int)springNum.Value;
            engine.RequestApply();
            s.Save();
        }

        void RefreshLive()
        {
            if (!Visible) return;
            status.Text = engine.Status;
            status.ForeColor = engine.Connected ? Theme.Good : Theme.Bad;
            applyInfo.Text = engine.LastApplyInfo;
            InputState st = engine.Snapshot();
            double deg = st.WheelOut * s.Range / 2.0;
            SetMeter(wheelMeter, st.HasWheel ? st.Wheel : 0, st.HasWheel ? string.Format("{0:+0;-0;0}°", deg) : "", s.SteerDzCenter, s.SteerDzEnd);
            SetMeter(accelMeter, st.HasAccel ? st.Accel : 0, st.HasAccel ? string.Format("{0:0}%", st.AccelOut * 100) : "", s.AccelDzStart, s.AccelDzEnd);
            SetMeter(brakeMeter, st.HasBrake ? st.Brake : 0, st.HasBrake ? string.Format("{0:0}%", st.BrakeOut * 100) : "", s.BrakeDzStart, s.BrakeDzEnd);
            for (int i = 0; i < Wheel.SlotCount; i++)
            {
                if (st.Buttons[i] == shownPressed[i]) continue;
                shownPressed[i] = st.Buttons[i];
                DataGridViewCellStyle cs = grid.Rows[i].DefaultCellStyle;
                cs.BackColor = st.Buttons[i] ? Theme.Pressed : Color.Empty;
                cs.SelectionBackColor = st.Buttons[i] ? Theme.Pressed : Color.Empty;
            }

            if (driverCheckTick-- <= 0)
            {
                driverCheckTick = 50; // about every 2 seconds
                bool was = driverInstalled;
                driverInstalled = VirtualDs4.DriverInstalled();
                if (driverInstalled && !was && s.Ds4Enabled && !engine.Ds4Connected) engine.Ds4Refresh(); // driver just got installed
            }
            ds4DriverLink.Visible = !driverInstalled;
            string ds4Text;
            if (!driverInstalled) ds4Text = s.Ds4Enabled ? "Driver not installed yet - connects by itself once it is." : "Needs the free ViGEmBus driver.";
            else if (engine.Ds4Connected) ds4Text = "Connected";
            else ds4Text = s.Ds4Enabled ? engine.Ds4Status : "Off";
            if (ds4Status.Text != ds4Text) ds4Status.Text = ds4Text;
            ds4Status.ForeColor = engine.Ds4Connected ? Theme.Good : (s.Ds4Enabled ? Theme.Bad : Theme.SubText);
        }

        static void SetMeter(Meter m, double v, string text, int dzStartPct, int dzEndPct)
        {
            double a = dzStartPct / 100.0, b = dzEndPct / 100.0;
            if (Math.Abs(m.Value - v) < 0.002 && m.Text == text && m.DzStart == a && m.DzEnd == b) return;
            m.Value = v; m.Text = text; m.DzStart = a; m.DzEnd = b; m.Invalidate();
        }

        /// Opens the key / rename dialog for one button.
        public void EditBinding(int i)
        {
            if (i < 0) { MessageBox.Show(this, "Select a button in the table first (or press it on the wheel and double-click its row).", Program.AppName); return; }
            using (BindingDialog d = new BindingDialog(i, s.Names[i], s.Bindings[i], k))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                s.Names[i] = string.IsNullOrWhiteSpace(d.ResultName) ? Wheel.DefaultNames[i] : d.ResultName.Trim();
                s.Bindings[i] = d.Result;
                engine.BindingsChanged();
                UpdateRow(i);
                s.Save();
            }
        }

        void ClearBinding(int i)
        {
            if (i < 0) return;
            s.Bindings[i] = new Binding();
            engine.BindingsChanged();
            UpdateRow(i);
            s.Save();
        }

        void ResetPs4()
        {
            loading = true;
            for (int i = 0; i < Wheel.SlotCount; i++)
            {
                s.Ds4Map[i] = Ds4Map.Defaults[i];
                grid.Rows[i].Cells[ColPs4].Value = Ds4Map.Label(s.Ds4Map[i]);
            }
            loading = false;
            s.Save();
            engine.Ds4MapChanged();
        }

        bool IsStartupEnabled()
        {
            try { using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(RunKey)) return rk != null && rk.GetValue("DFGTControl") != null; }
            catch { return false; }
        }

        void SetStartup(bool on)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) rk.SetValue("DFGTControl", "\"" + Application.ExecutablePath + "\" --tray");
                    else rk.DeleteValue("DFGTControl", false);
                }
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not change the startup setting:\n" + ex.Message, Program.AppName); }
        }

        void WriteNamesToWindows(string[] names)
        {
            if (names == null) names = Enumerable.Range(0, Wheel.ButtonCount).Select(DisplayName).ToArray();
            try
            {
                NameStore.BackupOnce();
                NameStore.WriteNames(names, false); // current user, no admin needed
                string tmp = Path.Combine(Path.GetTempPath(), "dfgt_names_" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllLines(tmp, names, Encoding.UTF8);
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--write-names-hklm \"" + tmp + "\"");
                psi.Verb = "runas"; psi.UseShellExecute = true;
                try
                {
                    using (Process p = Process.Start(psi)) { p.WaitForExit(); }
                    MessageBox.Show(this, "Button names saved. Restart any game that is open so it picks them up.", Program.AppName);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    MessageBox.Show(this, "Saved for your user account only (administrator permission was declined). Most programs will still see the names.", Program.AppName);
                }
                finally { try { File.Delete(tmp); } catch { } }
            }
            catch (Exception ex) { MessageBox.Show(this, "Could not save the names:\n" + ex.Message, Program.AppName); }
        }

        void RestoreNames()
        {
            string[] backup = NameStore.ReadBackup();
            string[] names = backup != null && backup.Length >= Wheel.ButtonCount && backup.Any(n => n.Length > 0)
                ? backup : Wheel.DefaultNames.Take(Wheel.ButtonCount).ToArray();
            WriteNamesToWindows(names);
        }

        void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && s.CloseToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            uiTimer.Stop();
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            if (showWait != null) showWait.Unregister(null);
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosed(e);
        }
    }
}
