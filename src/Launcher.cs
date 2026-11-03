// The small start window, tray icon, global hotkey, and the help dialog.

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MathSnip
{
    class LauncherForm : Form
    {
        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        const int WM_HOTKEY = 0x0312, HOTKEY_ID = 0x4D53;
        const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;

        readonly Label hotkeyLabel;
        readonly CheckBox onTop;
        readonly ToolButton updateBtn;
        readonly LinkLabel versionLink;
        NotifyIcon tray;
        string hotkeyText;
        bool hotkeyOk;

        public LauncherForm()
        {
            Text = Program.Name;
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Surface;
            Font = Theme.Body;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            TopMost = Program.Settings.KeepOnTop;

            int w = Program.S(250);
            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(Program.S(18), Program.S(16), Program.S(18), Program.S(14)),
                BackColor = Theme.Surface
            };

            var header = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Program.S(14)),
                BackColor = Theme.Surface
            };
            header.Controls.Add(new LogoMark { Margin = new Padding(0, 0, Program.S(10), 0) });
            var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            titles.Controls.Add(new Label { Text = Program.Name, Font = Theme.Title, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, 0) });
            titles.Controls.Add(new Label { Text = "Snip it. Teach it.", Font = Theme.Body, ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(Program.S(1), 0, 0, 0) });
            header.Controls.Add(titles);

            var snip = new ToolButton("snip", "New Snip")
            {
                Primary = true, Center = true, FixedWidth = w, FixedHeight = Program.S(54), IconSize = 22,
                Margin = new Padding(0, 0, 0, Program.S(8))
            };
            snip.Font = Theme.BigBold;
            snip.AutoFit();
            snip.Click += delegate { Program.StartSnip(null); };

            var blank = new ToolButton("board", "Blank Board")
            {
                Outline = true, Center = true, FixedWidth = w, FixedHeight = Program.S(44),
                Margin = new Padding(0, 0, 0, Program.S(12))
            };
            blank.Font = Theme.Bold;
            blank.AutoFit();
            blank.Click += delegate { Program.OpenBoard(null); };

            hotkeyLabel = new Label
            {
                Text = "Shortcut: Ctrl + Alt + S",
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, Program.S(6))
            };

            var row = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
            onTop = new CheckBox { Text = "Keep on top", Checked = Program.Settings.KeepOnTop, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, Program.S(3), Program.S(16), 0) };
            onTop.CheckedChanged += delegate { TopMost = onTop.Checked; Program.Settings.KeepOnTop = onTop.Checked; Program.Settings.Save(); };
            var help = new LinkLabel { Text = "How to use", AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.AccentDark, Margin = new Padding(0, Program.S(4), 0, 0) };
            help.LinkBehavior = LinkBehavior.HoverUnderline;
            help.LinkClicked += delegate { using (var h = new HelpForm()) h.ShowDialog(this); };
            row.Controls.Add(onTop);
            row.Controls.Add(help);

            updateBtn = new ToolButton("save", "Update available")
            {
                Outline = true, Center = true, FixedWidth = w, FixedHeight = Program.S(38), Visible = false,
                Margin = new Padding(0, 0, 0, Program.S(10))
            };
            updateBtn.Font = Theme.Bold;
            updateBtn.Checked = true;   // soft blue highlight so it stands out
            updateBtn.AutoFit();
            updateBtn.Click += delegate { Updater.Install(this); };

            versionLink = new LinkLabel
            {
                Text = "v" + Updater.CurrentText + "  \u00B7  Check for updates",
                AutoSize = true,
                LinkColor = Theme.Muted,
                ActiveLinkColor = Theme.Accent,
                ForeColor = Theme.Muted,
                Font = Theme.Small,
                LinkBehavior = LinkBehavior.HoverUnderline,
                Margin = new Padding(0, Program.S(8), 0, 0)
            };
            versionLink.LinkArea = new LinkArea(versionLink.Text.IndexOf("Check"), "Check for updates".Length);
            versionLink.LinkClicked += delegate { Updater.Check(true, this); };

            Updater.Found += OnUpdateFound;

            flow.Controls.Add(header);
            flow.Controls.Add(updateBtn);
            flow.Controls.Add(snip);
            flow.Controls.Add(blank);
            flow.Controls.Add(hotkeyLabel);
            flow.Controls.Add(row);
            flow.Controls.Add(versionLink);
            Controls.Add(flow);

            SetupTray();
        }

        void SetupTray()
        {
            try
            {
                var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
                menu.Items.Add("New Snip", null, delegate { Program.StartSnip(null); });
                menu.Items.Add("Blank Board", null, delegate { Program.OpenBoard(null); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Show window", null, delegate { Summon(); });
                menu.Items.Add("Exit", null, delegate { Close(); });
                tray = new NotifyIcon
                {
                    Icon = Program.AppIcon ?? SystemIcons.Application,
                    Text = Program.Name,
                    ContextMenuStrip = menu,
                    Visible = true
                };
                tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) Summon(); };
                tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) Program.StartSnip(null); };
            }
            catch { tray = null; }
        }

        void OnUpdateFound(object sender, EventArgs e)
        {
            var info = Updater.Available;
            if (info == null) return;
            string v = info.Version.Major + "." + info.Version.Minor + "." + info.Version.Build;
            updateBtn.Text = "Update to v" + v;
            updateBtn.Visible = true;
            if (tray != null && Program.Settings.NotifiedVersion != v)
            {
                Program.Settings.NotifiedVersion = v;
                Program.Settings.Save();
                try { tray.ShowBalloonTip(5000, "Update available", "Math Snip Board v" + v + " is ready \u2013 click Update on a board or in this window.", ToolTipIcon.Info); }
                catch { }
            }
        }

        public void RemoveTrayIcon()
        {
            try { if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; } } catch { }
        }

        public void ShowUpdatedNotice()
        {
            try { if (tray != null) tray.ShowBalloonTip(4000, Program.Name, "Updated to v" + Updater.CurrentText + ". You're all set!", ToolTipIcon.Info); } catch { }
        }

        public void Summon()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            bool top = TopMost;
            TopMost = true;
            Activate();
            TopMost = top;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            var s = Program.Settings;
            var wa = Screen.PrimaryScreen.WorkingArea;
            var pos = new Point(wa.Right - Width - Program.S(24), wa.Top + Program.S(24));
            if (s.LauncherX != int.MinValue)
            {
                var p = new Point(s.LauncherX, s.LauncherY);
                foreach (var scr in Screen.AllScreens)
                    if (scr.WorkingArea.Contains(new Rectangle(p, new Size(Program.S(60), Program.S(40))))) { pos = p; break; }
            }
            Location = pos;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && tray != null)
            {
                Hide();
                WindowState = FormWindowState.Normal;
                if (!Program.Settings.TrayTipShown)
                {
                    try
                    {
                        tray.ShowBalloonTip(4000, Program.Name,
                            "Still here in the tray." + (hotkeyOk ? " Press " + hotkeyText + " to snip." : " Double-click this icon to snip."),
                            ToolTipIcon.Info);
                    }
                    catch { }
                    Program.Settings.TrayTipShown = true;
                    Program.Settings.Save();
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hotkeyOk = false;
            try
            {
                if (RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)Keys.S))
                { hotkeyOk = true; hotkeyText = "Ctrl + Alt + S"; }
                else if (RegisterHotKey(Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.S))
                { hotkeyOk = true; hotkeyText = "Ctrl + Alt + Shift + S"; }
            }
            catch { }
            if (hotkeyLabel != null)
                hotkeyLabel.Text = hotkeyOk ? "Shortcut from anywhere:  " + hotkeyText : "Tip: double-click the tray icon to snip";
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { UnregisterHotKey(Handle, HOTKEY_ID); } catch { }
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID) { Program.StartSnip(null); return; }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                int dirty = 0;
                foreach (var ed in Program.Editors()) if (ed.IsDirty) dirty++;
                if (dirty > 0)
                {
                    Summon();
                    var r = MessageBox.Show(this,
                        (dirty == 1 ? "A board has" : dirty + " boards have") + " unsaved work.\nQuit Math Snip Board anyway?",
                        Program.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (r == DialogResult.No) { e.Cancel = true; return; }
                }
            }
            Program.Exiting = true;
            Updater.Found -= OnUpdateFound;
            if (WindowState == FormWindowState.Normal && Visible)
            {
                Program.Settings.LauncherX = Left;
                Program.Settings.LauncherY = Top;
            }
            Program.Settings.Save();
            if (tray != null) { tray.Visible = false; tray.Dispose(); tray = null; }
            base.OnFormClosing(e);
        }
    }

    class HelpForm : Form
    {
        public HelpForm()
        {
            Text = "How to use " + Program.Name;
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface;
            Font = Theme.Body;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;

            var flow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(Program.S(22), Program.S(18), Program.S(22), Program.S(18))
            };

            flow.Controls.Add(Heading("Quick start"));
            flow.Controls.Add(Para("1.  Press New Snip (or the shortcut) and drag a box around the question.\n" +
                                   "2.  It opens on a board with space around it – write, draw and explain.\n" +
                                   "3.  Snip again from the board to add the next question below.\n" +
                                   "4.  Save as PNG, JPG or PDF to share the notes with the class."));

            flow.Controls.Add(Heading("Pen tricks"));
            flow.Controls.Add(Para("•  Draw a line or circle and keep the pen still for a moment – it snaps perfect.\n" +
                                   "•  Flip the pen (or hold its side button) to erase.\n" +
                                   "•  Press harder for thicker lines (toggle with the pressure button).\n" +
                                   "•  Tablet driver: tick “Windows Ink” for the smoothest writing."));

            flow.Controls.Add(Heading("Keyboard shortcuts"));
            var table = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, Program.S(8)) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            string[,] rows =
            {
                { "Ctrl + Alt + S", "New snip from anywhere" },
                { "Ctrl + N", "Snip onto the current board" },
                { "P  H  L  A  R  O", "Pen, highlighter, line, arrow, rectangle, circle" },
                { "T  E  K  V", "Text, eraser, laser pointer, select" },
                { "1  2  3", "Thin, medium, thick" },
                { "Shift (while drawing)", "45° lines, squares, perfect circles" },
                { "Space + drag", "Scroll around the board (middle mouse works too)" },
                { "Ctrl + scroll", "Zoom in and out" },
                { "Ctrl + Z  /  Ctrl + Y", "Undo / redo" },
                { "Ctrl + C  /  Ctrl + S", "Copy board / save board" },
                { "Ctrl + V", "Paste a picture (or drag one in)" },
                { "Delete", "Remove the selected picture" },
                { "G", "Change page background" },
                { "F11", "Full screen" },
            };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                table.Controls.Add(new Label { Text = rows[i, 0], Font = Theme.Bold, ForeColor = Theme.Accent, AutoSize = true, Margin = new Padding(0, Program.S(3), Program.S(18), Program.S(3)) }, 0, i);
                table.Controls.Add(new Label { Text = rows[i, 1], ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, Program.S(3), 0, Program.S(3)) }, 1, i);
            }
            flow.Controls.Add(table);

            var footer = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, WrapContents = false, Margin = new Padding(0, Program.S(6), 0, 0) };
            var ok = new ToolButton(null, "Got it") { Primary = true, Center = true, FixedWidth = Program.S(110), FixedHeight = Program.S(38) };
            ok.Font = Theme.Bold;
            ok.AutoFit();
            ok.Click += delegate { Close(); };
            footer.Controls.Add(ok);
            footer.Controls.Add(new Label { Text = "Made by Mohaneesh", ForeColor = Theme.Muted, AutoSize = true, Margin = new Padding(Program.S(16), Program.S(11), 0, 0) });
            flow.Controls.Add(footer);
            Controls.Add(flow);
        }

        static Label Heading(string t)
        {
            return new Label { Text = t, Font = Theme.BigBold, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, Program.S(4), 0, Program.S(6)) };
        }

        static Label Para(string t)
        {
            return new Label { Text = t, ForeColor = Theme.Text, AutoSize = true, Margin = new Padding(0, 0, 0, Program.S(12)) };
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close();
            base.OnKeyDown(e);
        }
    }
}
