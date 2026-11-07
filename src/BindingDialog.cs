// DFGT Control - key binding editor.
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

namespace DFGTControl
{
    // ------------------------------------------------------------ binding editor
    class BindingDialog : Form, IMessageFilter
    {
        public Binding Result;
        public string ResultName;
        readonly TextBox nameBox, keysBox;
        readonly ComboBox modeBox;
        readonly Button recordBtn;
        readonly Label recordInfo, error;
        bool recording;
        readonly List<int> heldKeys = new List<int>(), combo = new List<int>();

        public BindingDialog(int index, string name, Binding b, float k)
        {
            Func<int, int> Px = v => (int)Math.Round(v * k);
            Text = "Bind keys to: " + name;
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel t = new TableLayoutPanel(); t.AutoSize = true; t.ColumnCount = 2; t.Padding = new Padding(Px(12));
            Controls.Add(t);

            t.Controls.Add(Lbl("Button name"), 0, 0);
            nameBox = new TextBox(); nameBox.Text = name; nameBox.Width = Px(340);
            t.Controls.Add(nameBox, 1, 0);

            t.Controls.Add(Lbl("Mode"), 0, 1);
            modeBox = new ComboBox(); modeBox.DropDownStyle = ComboBoxStyle.DropDownList; modeBox.Width = Px(340);
            modeBox.Items.AddRange(new object[] {
                "Off",
                "Hold - keys stay down while the button is held",
                "Tap - press and release the keys once",
                "Toggle - first press holds the keys, next press releases" });
            modeBox.SelectedIndex = b.Active ? (int)b.Mode : (int)BindMode.Hold;
            t.Controls.Add(modeBox, 1, 1);

            t.Controls.Add(Lbl("Keys"), 0, 2);
            keysBox = new TextBox(); keysBox.Text = b.Text; keysBox.Width = Px(340);
            t.Controls.Add(keysBox, 1, 2);

            FlowLayoutPanel rr = new FlowLayoutPanel(); rr.AutoSize = true; rr.WrapContents = false; rr.Margin = new Padding(0);
            recordBtn = new Button(); recordBtn.Text = "Record keys"; recordBtn.AutoSize = true;
            recordBtn.Click += delegate { if (recording) StopRecording(); else StartRecording(); };
            Button addStep = new Button(); addStep.Text = "Clear"; addStep.AutoSize = true; addStep.Click += delegate { keysBox.Text = ""; };
            rr.Controls.Add(recordBtn); rr.Controls.Add(addStep);
            t.Controls.Add(rr, 1, 3);
            recordInfo = new Label(); recordInfo.AutoSize = true; recordInfo.ForeColor = Color.FromArgb(0, 90, 180);
            t.Controls.Add(recordInfo, 1, 4);

            Label help = new Label(); help.AutoSize = true; help.MaximumSize = new Size(Px(340), 0); help.ForeColor = SystemColors.GrayText;
            help.Text = "Press several keys together while recording to make a combination (Ctrl+Shift+F5). " +
                        "Record again to add another step, which makes a sequence (Ctrl+C, Ctrl+V). " +
                        "You can also type: + joins keys, a comma separates steps. Key names: A-Z, 0-9, F1-F24, Ctrl, Shift, Alt, Win, " +
                        "Enter, Esc, Space, Tab, Up/Down/Left/Right, Num0-Num9, NumPlus, Comma, Minus…";
            t.Controls.Add(help, 1, 5);
            error = new Label(); error.AutoSize = true; error.ForeColor = Color.Firebrick; error.MaximumSize = new Size(Px(340), 0);
            t.Controls.Add(error, 1, 6);

            FlowLayoutPanel ok = new FlowLayoutPanel(); ok.AutoSize = true; ok.FlowDirection = FlowDirection.RightToLeft; ok.Dock = DockStyle.Fill; ok.Margin = new Padding(0, Px(8), 0, 0);
            Button cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.AutoSize = true;
            Button okBtn = new Button(); okBtn.Text = "OK"; okBtn.AutoSize = true;
            okBtn.Click += delegate { Accept(); };
            ok.Controls.Add(cancel); ok.Controls.Add(okBtn);
            t.Controls.Add(ok, 0, 7); t.SetColumnSpan(ok, 2);
            CancelButton = cancel;

            help.Tag = Theme.NoteTag;
            Theme.Apply(this);
            recordInfo.ForeColor = Theme.Accent;
            error.ForeColor = Theme.Bad;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(this);
        }

        static Label Lbl(string s) { Label l = new Label(); l.Text = s; l.AutoSize = true; l.Anchor = AnchorStyles.Left; return l; }

        void Accept()
        {
            if (recording) StopRecording();
            BindMode mode = (BindMode)modeBox.SelectedIndex;
            try
            {
                List<int[]> steps = KeyText.ParseSteps(keysBox.Text);
                if (mode != BindMode.Off && steps.Count == 0) { error.Text = "Record or type at least one key, or set the mode to Off."; return; }
                Result = Binding.Create(mode, keysBox.Text);
                if (steps.Count == 0) Result.Mode = BindMode.Off;
                ResultName = nameBox.Text;
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (FormatException ex) { error.Text = ex.Message; }
        }

        void StartRecording()
        {
            recording = true; heldKeys.Clear(); combo.Clear();
            recordBtn.Text = "Stop recording";
            recordInfo.Text = "Press the key or keys now…";
            Application.AddMessageFilter(this);
        }

        void StopRecording()
        {
            recording = false;
            Application.RemoveMessageFilter(this);
            recordBtn.Text = "Record keys";
            if (combo.Count == 0) recordInfo.Text = "";
        }

        void FinishCombo()
        {
            string c = KeyText.Format(combo);
            keysBox.Text = keysBox.Text.Trim().Length == 0 ? c : keysBox.Text.Trim().TrimEnd(',') + ", " + c;
            recordInfo.Text = "Recorded " + c + ". Press Record keys again to add a step.";
            combo.Clear();
            StopRecording();
            recordInfo.Text = "Recorded " + c + ". Press Record keys again to add a step.";
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (!recording) return false;
            const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
            if (m.Msg != WM_KEYDOWN && m.Msg != WM_KEYUP && m.Msg != WM_SYSKEYDOWN && m.Msg != WM_SYSKEYUP) return false;
            int vk = (int)m.WParam.ToInt64() & 0xFF;
            bool downMsg = m.Msg == WM_KEYDOWN || m.Msg == WM_SYSKEYDOWN;
            if (downMsg)
            {
                if (!heldKeys.Contains(vk)) heldKeys.Add(vk);
                if (!combo.Contains(vk)) combo.Add(vk);
                recordInfo.Text = "Holding: " + KeyText.Format(combo);
            }
            else
            {
                heldKeys.Remove(vk);
                if (!combo.Contains(vk)) combo.Add(vk); // keys like PrintScreen only send key-up
                if (heldKeys.Count == 0 && combo.Count > 0) FinishCombo();
            }
            return true; // swallow so Alt/Tab/Enter don't act on the dialog
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (recording) Application.RemoveMessageFilter(this);
            base.OnFormClosed(e);
        }
    }
}
