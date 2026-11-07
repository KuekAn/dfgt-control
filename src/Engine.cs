// DFGT Control - engine: finds the wheel, keeps settings applied, turns button presses into keys.
using System;
using System.Threading;

namespace DFGTControl
{
    class Engine : IDisposable
    {
        readonly Settings s;
        readonly Thread thread;
        readonly Timer applyTimer;
        readonly KeySender keys = new KeySender();
        readonly object devLock = new object();
        readonly object stateLock = new object();
        volatile bool stop;
        WheelDevice dev;
        InputState current = new InputState();

        readonly bool[] prev = new bool[Wheel.SlotCount];
        readonly bool[] toggled = new bool[Wheel.SlotCount];
        readonly int[][] held = new int[Wheel.SlotCount][];

        bool applyRequested;
        DateTime applyNotBefore = DateTime.MinValue, lastApply = DateTime.MinValue;

        public volatile string Status = "Looking for the Driving Force GT…";
        public volatile bool Connected;
        public volatile string LastApplyInfo = "";
        public volatile string Ds4Status = "Off.";
        public volatile bool Ds4Connected;
        VirtualDs4 ds4;
        readonly object ds4Lock = new object();
        int ds4Busy;
        DateTime ds4FailSince = DateTime.MinValue;

        public Engine(Settings settings)
        {
            s = settings;
            keys.TapMs = s.TapMs;
            thread = new Thread(Run);
            thread.IsBackground = true;
            thread.Name = "WheelReader";
            thread.Start();
            applyTimer = new Timer(ApplyTick, null, 200, 200);
            if (s.Ds4Enabled) Ds4Refresh();
        }

        /// Plugs the virtual PS4 controller in or out to match the settings, then sends the current state.
        public void Ds4Refresh()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (Interlocked.Exchange(ref ds4Busy, 1) == 1)
                {
                    // Another refresh is running; check again shortly so the final state always wins.
                    ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(300); Ds4Refresh(); });
                    return;
                }
                try
                {
                    bool have; lock (ds4Lock) have = ds4 != null;
                    if (s.Ds4Enabled && !have && !stop)
                    {
                        Ds4Status = "Connecting the virtual PS4 controller…";
                        VirtualDs4 v = new VirtualDs4();
                        string err;
                        try { err = v.Connect(); } catch (Exception ex) { err = ex.Message; }
                        if (err == null) { lock (ds4Lock) ds4 = v; Ds4Connected = true; Ds4Status = "Virtual PS4 controller is connected."; }
                        else { v.Dispose(); Ds4Connected = false; Ds4Status = err; }
                    }
                    else if (!s.Ds4Enabled && have)
                    {
                        lock (ds4Lock) { ds4.Dispose(); ds4 = null; }
                        Ds4Connected = false;
                        Ds4Status = "Off.";
                    }
                    else if (!s.Ds4Enabled) Ds4Status = "Off.";
                    UpdateDs4(Snapshot());
                }
                finally { Interlocked.Exchange(ref ds4Busy, 0); }
            });
        }

        /// Re-sends the current wheel state (call after the button mapping changes).
        public void Ds4MapChanged() { UpdateDs4(Snapshot()); }

        void UpdateDs4(InputState st)
        {
            lock (ds4Lock)
            {
                if (ds4 == null) return;
                if (ds4.Submit(VirtualDs4.Build(st, s.Ds4Map, s.Ds4Axes))) { ds4FailSince = DateTime.MinValue; return; }
                // Tolerate short hiccups; only give up after it has failed for a few seconds.
                if (ds4FailSince == DateTime.MinValue) { ds4FailSince = DateTime.Now; return; }
                if ((DateTime.Now - ds4FailSince).TotalSeconds < 3) return;
                ds4.Dispose(); ds4 = null;
                ds4FailSince = DateTime.MinValue;
                Ds4Connected = false;
                Ds4Status = "The virtual PS4 controller stopped responding. Untick and tick the box to try again.";
            }
        }

        public InputState Snapshot() { lock (stateLock) return current.Clone(); }

        /// Call after any wheel setting changes; it is sent to the wheel within ~0.2 s.
        public void RequestApply()
        {
            lock (devLock) { applyRequested = true; if (applyNotBefore < DateTime.Now) applyNotBefore = DateTime.Now; }
        }

        public void SetTapMs(int ms) { keys.TapMs = ms; }

        public void BindingsChanged()
        {
            // Let go of anything we are holding so a removed/changed binding can't leave a key stuck down.
            keys.ReleaseAll();
            for (int i = 0; i < Wheel.SlotCount; i++) { toggled[i] = false; held[i] = null; }
        }

        void ApplyTick(object _)
        {
            try
            {
                lock (devLock)
                {
                    if (dev == null) return;
                    DateTime now = DateTime.Now;
                    bool due = applyRequested && now >= applyNotBefore;
                    bool enforce = s.Enforce && (now - lastApply).TotalSeconds >= s.EnforceSeconds;
                    if (!due && !enforce) return;
                    bool ok = dev.SetRange(s.Range);
                    ok &= dev.SetAutocenter(s.Spring ? Math.Max(1, s.SpringStrength) : 0);
                    lastApply = now;
                    applyRequested = false;
                    LastApplyInfo = (ok ? "Sent to wheel at " : "Could not send to wheel at ") + now.ToString("HH:mm:ss") +
                                    ": " + s.Range + "°, spring " + (s.Spring ? s.SpringStrength + "%" : "off");
                }
            }
            catch { }
        }

        void Sleep(int ms) { for (int t = 0; t < ms && !stop; t += 100) Thread.Sleep(100); }

        void Run()
        {
            while (!stop)
            {
                WheelDevice d = null;
                string err = null;
                try { d = WheelDevice.FindAndOpen(out err); } catch (Exception ex) { err = ex.Message; }
                if (d == null)
                {
                    Connected = false;
                    Status = err ?? "Driving Force GT not found. Plug it in (and turn on its power supply).";
                    Sleep(2000);
                    continue;
                }
                if (d.IsCompatMode)
                {
                    Status = "Switching the wheel to native mode (needed for 900°)…";
                    try { d.SwitchToNative(); } catch { }
                    d.Dispose();
                    Sleep(3000);
                    continue;
                }

                lock (devLock)
                {
                    dev = d;
                    applyRequested = true;
                    applyNotBefore = DateTime.Now.AddMilliseconds(800); // let the Logitech driver finish its own init first
                }
                Connected = true;
                Status = "Connected: " + d.Product;

                byte[] buf = new byte[Math.Max(1, d.InputLength)];
                InputState st = new InputState();
                if (d.ReadCurrent(buf))
                {
                    try
                    {
                        d.Parse(buf, st);
                        ApplyPedalInvert(st);
                        lock (stateLock) current = st.Clone();
                        for (int i = 0; i < Wheel.SlotCount; i++) prev[i] = st.Buttons[i]; // don't fire bindings for buttons already held
                    }
                    catch { }
                }
                while (!stop)
                {
                    bool ok;
                    try { ok = d.Read(buf); } catch { ok = false; }
                    if (!ok) break;
                    try
                    {
                        d.Parse(buf, st);
                        ApplyPedalInvert(st);
                        lock (stateLock) current = st.Clone();
                        HandleButtons(st);
                        UpdateDs4(st);
                    }
                    catch { }
                }

                lock (devLock) dev = null;
                Connected = false;
                d.Dispose();
                keys.ReleaseAll();
                for (int i = 0; i < Wheel.SlotCount; i++) { prev[i] = false; toggled[i] = false; held[i] = null; }
                lock (stateLock) current = new InputState();
                UpdateDs4(new InputState());
                if (!stop) { Status = "Wheel disconnected - waiting for it to come back…"; Sleep(1000); }
            }
        }

        void ApplyPedalInvert(InputState st)
        {
            st.AccelRaw = st.Accel; st.BrakeRaw = st.Brake;
            Reinvert(st);
        }

        void Reinvert(InputState st)
        {
            st.Accel = s.InvertAccel ? 1 - st.AccelRaw : st.AccelRaw;
            st.Brake = s.InvertBrake ? 1 - st.BrakeRaw : st.BrakeRaw;
            st.WheelOut = Deadzone.Steer(st.Wheel, s.SteerDzCenter, s.SteerDzEnd);
            st.AccelOut = Deadzone.Pedal(st.Accel, s.AccelDzStart, s.AccelDzEnd);
            st.BrakeOut = Deadzone.Pedal(st.Brake, s.BrakeDzStart, s.BrakeDzEnd);
        }

        /// Call after an invert or deadzone option changes, so it shows (and reaches the PS4 controller) without waiting for movement.
        public void AxisSettingsChanged() { PedalInvertChanged(); }

        public void PedalInvertChanged()
        {
            lock (stateLock) Reinvert(current);
            UpdateDs4(Snapshot());
        }

        void HandleButtons(InputState st)
        {
            for (int i = 0; i < Wheel.SlotCount; i++)
            {
                bool p = st.Buttons[i];
                if (p == prev[i]) continue;
                prev[i] = p;
                try { OnButton(i, p); } catch { }
            }
        }

        void OnButton(int i, bool pressed)
        {
            if (!pressed && held[i] != null && !toggled[i]) { keys.Up(held[i]); held[i] = null; return; }
            if (!s.BindingsEnabled) return;
            Binding b = s.Bindings[i];
            if (b == null || !b.Active || !pressed) return;
            int last = b.Steps.Count - 1;
            switch (b.Mode)
            {
                case BindMode.Tap:
                    foreach (int[] step in b.Steps) keys.Tap(step);
                    break;
                case BindMode.Hold:
                    for (int k = 0; k < last; k++) keys.Tap(b.Steps[k]);
                    held[i] = b.Steps[last];
                    keys.Down(held[i]);
                    break;
                case BindMode.Toggle:
                    if (!toggled[i])
                    {
                        for (int k = 0; k < last; k++) keys.Tap(b.Steps[k]);
                        held[i] = b.Steps[last];
                        keys.Down(held[i]);
                        toggled[i] = true;
                    }
                    else
                    {
                        if (held[i] != null) keys.Up(held[i]);
                        held[i] = null;
                        toggled[i] = false;
                    }
                    break;
            }
        }

        public void Dispose()
        {
            stop = true;
            applyTimer.Dispose();
            lock (devLock) { if (dev != null) dev.CancelRead(); }
            thread.Join(1500);
            keys.Dispose();
            lock (ds4Lock) { if (ds4 != null) { ds4.Dispose(); ds4 = null; } }
        }
    }
}
