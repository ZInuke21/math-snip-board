// The board window: toolbar + scrollable board.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace MathSnip
{
    class EditorForm : Form
    {
        static readonly Color[] PenColors =
        {
            Color.FromArgb(24, 24, 27),    // black
            Color.FromArgb(37, 99, 235),   // blue
            Color.FromArgb(220, 38, 38),   // red
            Color.FromArgb(22, 163, 74),   // green
            Color.FromArgb(234, 88, 12),   // orange
            Color.FromArgb(147, 51, 234),  // purple
        };
        static readonly Color[] HlColors =
        {
            Color.FromArgb(255, 221, 0),   // yellow
            Color.FromArgb(110, 231, 120), // green
            Color.FromArgb(255, 120, 190), // pink
            Color.FromArgb(90, 200, 255),  // blue
            Color.FromArgb(255, 170, 60),  // orange
            Color.FromArgb(190, 150, 255), // violet
        };

        readonly Board board;
        readonly ScrollPanel scroller;
        readonly FlowLayoutPanel bar;
        readonly ToolTip tips = new ToolTip { InitialDelay = 400, ReshowDelay = 100 };
        readonly Toast toast = new Toast();
        readonly Dictionary<Tool, ToolButton> toolBtns = new Dictionary<Tool, ToolButton>();
        readonly List<SwatchButton> swatches = new List<SwatchButton>();
        SwatchButton customSwatch;
        readonly ToolButton[] sizeBtns = new ToolButton[3];
        ToolButton undoBtn, redoBtn, clearBtn, pressureBtn, bgBtn, zoomBtn, fullBtn, updateBtn;
        Bitmap initial;
        bool fullscreen;
        FormWindowState stateBeforeFull = FormWindowState.Maximized;
        readonly int number;

        public EditorForm(Bitmap snip, int n)
        {
            initial = snip;
            number = n;
            Text = "Board " + n + " – " + Program.Name;
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            Font = Theme.Body;
            BackColor = Theme.Surface;
            KeyPreview = true;
            AllowDrop = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Program.S(1200), Program.S(800));
            MinimumSize = new Size(Program.S(560), Program.S(400));
            WindowState = FormWindowState.Maximized;

            scroller = new ScrollPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
            board = new Board { Location = Point.Empty, Size = new Size(10, 10), Margin = Padding.Empty };
            scroller.Controls.Add(board);

            bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = true,
                Padding = new Padding(Program.S(8), Program.S(5), Program.S(8), Program.S(5)),
                BackColor = Theme.Surface
            };

            var s = Program.Settings;
            board.PenColor = Color.FromArgb(s.PenColor);
            board.HlColor = Color.FromArgb(s.HlColor);
            board.SetLevel(s.Level);
            board.UsePressure = s.Pressure;
            board.Backdrop = (Backdrop)s.Backdrop;

            BuildToolbar();

            var line = new Panel { Dock = DockStyle.Top, Height = Math.Max(1, Program.S(1)), BackColor = Theme.Border };
            Controls.Add(toast);
            Controls.Add(scroller);
            Controls.Add(line);
            Controls.Add(bar);

            board.HistoryChanged += delegate { RefreshUi(); };
            board.ZoomChanged += delegate { RefreshZoom(); };
            scroller.Resize += delegate { board.ApplySize(); toast.Reposition(); };
            scroller.ClientSizeChanged += delegate { board.ApplySize(); };   // scrollbars appearing/disappearing
            Resize += delegate { toast.Reposition(); };
            Shown += OnFirstShown;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            RefreshUi();
        }

        public bool IsDirty { get { return board.Dirty && board.Items.Count > 0; } }
        public Board Board { get { return board; } }

        void OnFirstShown(object sender, EventArgs e)
        {
            board.ApplySize();
            if (initial != null) board.AddImage(initial, false);
            initial = null;
            board.Dirty = false;
            board.Focus();
            RefreshUi();
            if (!Program.Settings.TipShown)
            {
                toast.Show("Tip: draw a line or circle and keep the pen still for a moment – it snaps perfect", 7000);
                Program.Settings.TipShown = true;
                Program.Settings.Save();
            }
        }

        // ---------------------------------------------------------------- toolbar
        void Add(Control c, string tip = null)
        {
            if (tip != null) tips.SetToolTip(c, tip);
            bar.Controls.Add(c);
        }

        ToolButton Btn(string icon, string tip, EventHandler click, string caption = null)
        {
            var b = new ToolButton(icon, caption);
            b.Click += click;
            Add(b, tip);
            return b;
        }

        void AddTool(Tool t, string icon, string tip)
        {
            var b = new ToolButton(icon);
            b.Click += delegate { SelectTool(t); };
            toolBtns[t] = b;
            Add(b, tip);
        }

        void BuildToolbar()
        {
            var snip = new ToolButton("snip", "Snip") { Primary = true };
            snip.Font = Theme.Bold;
            snip.AutoFit();
            snip.Click += delegate { SnipIntoBoard(); };
            Add(snip, "Snip another part of the screen onto this board (Ctrl+N)");
            Add(new Divider());

            AddTool(Tool.Select, "select", "Select – move or resize pictures, drag empty space to scroll (V)");
            AddTool(Tool.Pen, "pen", "Pen (P)");
            AddTool(Tool.Highlighter, "highlighter", "Highlighter (H)");
            AddTool(Tool.Line, "line", "Straight line (L) – hold Shift for 45° angles");
            AddTool(Tool.Arrow, "arrow", "Arrow (A)");
            AddTool(Tool.Rect, "rect", "Rectangle (R) – hold Shift for a square");
            AddTool(Tool.Ellipse, "ellipse", "Circle / ellipse (O) – hold Shift for a perfect circle");
            AddTool(Tool.Text, "text", "Text (T) – click to type, Enter to finish, Shift+Enter for a new line");
            AddTool(Tool.Eraser, "eraser", "Eraser (E) – rubs out just the bit you touch");
            AddTool(Tool.Laser, "laser", "Laser pointer (K) – point at things without drawing");
            Add(new Divider());

            for (int i = 0; i < 6; i++)
            {
                var sw = new SwatchButton(PenColors[i]);
                sw.Click += delegate { PickColor(sw.Color); };
                swatches.Add(sw);
                Add(sw);
            }
            customSwatch = new SwatchButton(Program.Settings.CustomColor == 0 ? Color.Transparent : Color.FromArgb(Program.Settings.CustomColor)) { IsCustom = true };
            customSwatch.Click += delegate { PickCustomColor(); };
            Add(customSwatch, "More colours");
            Add(new Divider());

            string[] names = { "Thin", "Medium", "Thick" };
            float[] dots = { 4f, 7.5f, 12f };
            for (int i = 0; i < 3; i++)
            {
                int lvl = i;
                var b = new ToolButton(null) { DotSize = dots[i] };
                b.Click += delegate { board.SetLevel(lvl); Program.Settings.Level = lvl; RefreshUi(); board.Focus(); };
                sizeBtns[i] = b;
                Add(b, names[i] + " (" + (i + 1) + ")");
            }
            pressureBtn = Btn("pressure", "Pen pressure – press harder for thicker lines (pen tablets)", delegate
            {
                board.UsePressure = !board.UsePressure;
                Program.Settings.Pressure = board.UsePressure;
                RefreshUi();
                toast.Show(board.UsePressure ? "Pen pressure on" : "Pen pressure off – every line is the same thickness", 1800);
            });
            Add(new Divider());

            undoBtn = Btn("undo", "Undo (Ctrl+Z)", delegate { board.Undo(); board.Focus(); });
            redoBtn = Btn("redo", "Redo (Ctrl+Y)", delegate { board.Redo(); board.Focus(); });
            clearBtn = Btn("trash", "Clear all writing – pictures stay (Undo brings it back)", delegate
            {
                board.ClearInk();
                toast.Show("Writing cleared – press Ctrl+Z to bring it back", 2600);
                board.Focus();
            });
            Add(new Divider());

            bgBtn = new ToolButton("plain") { DropArrow = true };
            bgBtn.AutoFit();
            bgBtn.Click += delegate { ShowBackdropMenu(); };
            Add(bgBtn, "Page background: plain, graph paper, lines or dots (G)");
            Add(new Divider());

            Btn("zoomout", "Zoom out (Ctrl + −)", delegate { board.ZoomStep(false); board.Focus(); });
            zoomBtn = new ToolButton(null, "100%") { FixedWidth = Program.S(54), Center = true };
            zoomBtn.AutoFit();
            zoomBtn.Click += delegate { ShowZoomMenu(); };
            Add(zoomBtn, "Zoom level – Ctrl + mouse wheel also zooms");
            Btn("zoomin", "Zoom in (Ctrl + +)", delegate { board.ZoomStep(true); board.Focus(); });
            Add(new Divider());

            Btn("copy", "Copy the board as a picture (Ctrl+C)", delegate { CopyImage(); });
            Btn("save", "Save as PNG, JPG or PDF (Ctrl+S)", delegate { SaveImage(); });
            fullBtn = Btn("fullscreen", "Full screen (F11)", delegate { ToggleFullscreen(); });
            Btn("help", "Shortcuts & tips (F1)", delegate { ShowHelp(); });

            updateBtn = new ToolButton("save", "Update") { Primary = true, Visible = false };
            updateBtn.Font = Theme.Bold;
            updateBtn.AutoFit();
            updateBtn.Margin = new Padding(Program.S(8), Program.S(2), Program.S(1), Program.S(2));
            updateBtn.Click += delegate { board.CommitText(); Updater.Install(this); };
            Add(updateBtn, "A new version of Math Snip Board is ready \u2013 click to update");
            Updater.Found += OnUpdateFound;
            OnUpdateFound(null, EventArgs.Empty);
        }

        void OnUpdateFound(object sender, EventArgs e)
        {
            if (IsDisposed || Updater.Available == null) return;
            var v = Updater.Available.Version;
            updateBtn.Visible = true;
            tips.SetToolTip(updateBtn, "Math Snip Board v" + v.Major + "." + v.Minor + "." + v.Build + " is ready \u2013 click to update");
        }

        void SelectTool(Tool t)
        {
            board.SetTool(t);
            RefreshUi();
            board.Focus();
        }

        void PickColor(Color c)
        {
            if (board.CurrentTool == Tool.Select || board.CurrentTool == Tool.Eraser || board.CurrentTool == Tool.Laser)
                board.SetTool(Tool.Pen);
            board.SetColor(c);
            Program.Settings.PenColor = board.PenColor.ToArgb();
            Program.Settings.HlColor = board.HlColor.ToArgb();
            RefreshUi();
            if (!board.IsTyping) board.Focus();
        }

        void PickCustomColor()
        {
            using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = board.ActiveColor })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                var c = Color.FromArgb(255, dlg.Color);
                Program.Settings.CustomColor = c.ToArgb();
                customSwatch.Color = c;
                PickColor(c);
            }
        }

        void RefreshUi()
        {
            foreach (var kv in toolBtns) kv.Value.Checked = kv.Key == board.CurrentTool;
            bool hl = board.CurrentTool == Tool.Highlighter;
            var pal = hl ? HlColors : PenColors;
            var active = board.ActiveColor.ToArgb();
            bool showSel = board.CurrentTool != Tool.Select && board.CurrentTool != Tool.Eraser && board.CurrentTool != Tool.Laser;
            bool matched = false;
            for (int i = 0; i < swatches.Count; i++)
            {
                swatches[i].Color = pal[i];
                bool sel = showSel && pal[i].ToArgb() == active;
                swatches[i].Selected = sel;
                matched |= sel;
            }
            customSwatch.Selected = showSel && !matched && customSwatch.Color.A > 0 && customSwatch.Color.ToArgb() == active;
            for (int i = 0; i < 3; i++) sizeBtns[i].Checked = i == board.Level;
            pressureBtn.Checked = board.UsePressure;
            undoBtn.Enabled = board.CanUndo;
            redoBtn.Enabled = board.CanRedo;
            clearBtn.Enabled = board.HasInk;
            string[] bgIcons = { "plain", "grid", "lines", "dots" };
            bgBtn.SetIcon(bgIcons[(int)board.Backdrop]);
            RefreshZoom();
        }

        void RefreshZoom()
        {
            zoomBtn.Text = (int)Math.Round(board.Zoom * 100) + "%";
        }

        void ShowBackdropMenu()
        {
            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false, ShowCheckMargin = true };
            string[] labels = { "Plain", "Graph paper", "Lines", "Dots" };
            for (int i = 0; i < 4; i++)
            {
                var b = (Backdrop)i;
                var item = new ToolStripMenuItem(labels[i]) { Checked = board.Backdrop == b };
                item.Click += delegate { SetBackdrop(b); };
                menu.Items.Add(item);
            }
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(bgBtn, new Point(0, bgBtn.Height));
        }

        void SetBackdrop(Backdrop b)
        {
            board.Backdrop = b;
            Program.Settings.Backdrop = (int)b;
            RefreshUi();
            board.Focus();
        }

        void ShowZoomMenu()
        {
            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false };
            var fit = new ToolStripMenuItem("Fit to width");
            fit.Click += delegate { board.FitWidth(); };
            menu.Items.Add(fit);
            menu.Items.Add(new ToolStripSeparator());
            foreach (var z in new[] { 50, 75, 100, 125, 150, 200, 300 })
            {
                int zz = z;
                var item = new ToolStripMenuItem(z + "%");
                item.Click += delegate { board.ZoomTo(zz / 100f); };
                menu.Items.Add(item);
            }
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(zoomBtn, new Point(0, zoomBtn.Height));
        }

        // ---------------------------------------------------------------- actions
        void SnipIntoBoard()
        {
            board.CommitText();
            Program.StartSnip(img =>
            {
                if (IsDisposed) { Program.OpenBoard(img); return; }
                board.AddImage(img, true);
                Activate();
                board.Focus();
            });
        }

        void CopyImage()
        {
            try
            {
                var img = board.Export();
                Clipboard.SetImage(img);
                toast.Show("Copied! Paste it anywhere with Ctrl+V");
            }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't copy: " + ex.Message, Program.Name); }
        }

        bool SaveImage()
        {
            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "PNG picture (*.png)|*.png|JPEG picture (*.jpg)|*.jpg|PDF document (*.pdf)|*.pdf";
                dlg.FileName = "Math board " + DateTime.Now.ToString("yyyy-MM-dd HH.mm");
                dlg.AddExtension = true;
                try
                {
                    string dir = Program.Settings.LastFolder;
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                        dir = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    dlg.InitialDirectory = dir;
                }
                catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                try
                {
                    using (var img = board.Export())
                    {
                        string path = dlg.FileName;
                        string ext = Path.GetExtension(path).ToLowerInvariant();
                        if (dlg.FilterIndex == 3 || ext == ".pdf") Pdf.Save(img, path);
                        else if (dlg.FilterIndex == 2 || ext == ".jpg" || ext == ".jpeg") SaveJpeg(img, path);
                        else img.Save(path, ImageFormat.Png);
                        Program.Settings.LastFolder = Path.GetDirectoryName(path);
                        Program.Settings.Save();
                        board.Dirty = false;
                        toast.Show("Saved " + Path.GetFileName(path));
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't save: " + ex.Message, Program.Name);
                    return false;
                }
            }
        }

        public static void SaveJpeg(Bitmap img, string path)
        {
            using (var rgb = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb))
            {
                using (var g = Graphics.FromImage(rgb)) { g.Clear(Color.White); g.DrawImageUnscaled(img, 0, 0); }
                var enc = Pdf.JpegCodec();
                using (var ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
                    rgb.Save(path, enc, ep);
                }
            }
        }

        void PasteImage()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    using (var img = Clipboard.GetImage()) board.AddImage(new Bitmap(img), true);
                    return;
                }
                if (Clipboard.ContainsFileDropList())
                    foreach (var f in Clipboard.GetFileDropList()) TryAddFile(f);
            }
            catch (Exception ex) { toast.Show("Couldn't paste that: " + ex.Message); }
        }

        static bool IsImageFile(string f)
        {
            string e = Path.GetExtension(f).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".bmp" || e == ".gif" || e == ".tif" || e == ".tiff";
        }

        void TryAddFile(string f)
        {
            if (!IsImageFile(f)) return;
            try { board.AddImage(Program.LoadImage(f), true); }
            catch { toast.Show("Couldn't open " + Path.GetFileName(f)); }
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return;
            foreach (var f in files) TryAddFile(f);
            Activate();
        }

        void ToggleFullscreen()
        {
            board.CommitText();
            fullscreen = !fullscreen;
            SuspendLayout();
            if (fullscreen)
            {
                stateBeforeFull = WindowState;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
                fullBtn.SetIcon("exitfull");
                tips.SetToolTip(fullBtn, "Exit full screen (F11 or Esc)");
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                WindowState = FormWindowState.Normal;
                WindowState = stateBeforeFull == FormWindowState.Minimized ? FormWindowState.Maximized : stateBeforeFull;
                fullBtn.SetIcon("fullscreen");
                tips.SetToolTip(fullBtn, "Full screen (F11)");
            }
            ResumeLayout();
            board.Focus();
        }

        void ShowHelp()
        {
            using (var h = new HelpForm()) h.ShowDialog(this);
            board.Focus();
        }

        // ---------------------------------------------------------------- keyboard
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (board.IsTyping || !ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
            switch (keyData)
            {
                case Keys.Control | Keys.Z: board.Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: board.Redo(); return true;
                case Keys.Control | Keys.S: SaveImage(); return true;
                case Keys.Control | Keys.C: CopyImage(); return true;
                case Keys.Control | Keys.V: PasteImage(); return true;
                case Keys.Control | Keys.N: SnipIntoBoard(); return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add: board.ZoomStep(true); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: board.ZoomStep(false); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: board.ZoomTo(1f); return true;
                case Keys.V: SelectTool(Tool.Select); return true;
                case Keys.P: SelectTool(Tool.Pen); return true;
                case Keys.H: SelectTool(Tool.Highlighter); return true;
                case Keys.L: SelectTool(Tool.Line); return true;
                case Keys.A: SelectTool(Tool.Arrow); return true;
                case Keys.R: SelectTool(Tool.Rect); return true;
                case Keys.O: SelectTool(Tool.Ellipse); return true;
                case Keys.T: SelectTool(Tool.Text); return true;
                case Keys.E: SelectTool(Tool.Eraser); return true;
                case Keys.K: SelectTool(Tool.Laser); return true;
                case Keys.G: SetBackdrop((Backdrop)(((int)board.Backdrop + 1) % 4)); return true;
                case Keys.D1: case Keys.NumPad1: board.SetLevel(0); Program.Settings.Level = 0; RefreshUi(); return true;
                case Keys.D2: case Keys.NumPad2: board.SetLevel(1); Program.Settings.Level = 1; RefreshUi(); return true;
                case Keys.D3: case Keys.NumPad3: board.SetLevel(2); Program.Settings.Level = 2; RefreshUi(); return true;
                case Keys.Delete:
                case Keys.Back: board.DeleteSelected(); return true;
                case Keys.F11: ToggleFullscreen(); return true;
                case Keys.F1: ShowHelp(); return true;
                case Keys.Escape:
                    if (fullscreen) ToggleFullscreen();
                    else board.ClearSelection();
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && !board.IsTyping) { board.SpacePan = true; e.Handled = true; e.SuppressKeyPress = true; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) board.SpacePan = false;
            base.OnKeyUp(e);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            board.SpacePan = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            board.CommitText();
            if (IsDirty && e.CloseReason == CloseReason.UserClosing)
            {
                var r = MessageBox.Show(this, "Save Board " + number + " before closing?", Program.Name,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel || (r == DialogResult.Yes && !SaveImage())) { e.Cancel = true; return; }
            }
            Program.Settings.Save();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Updater.Found -= OnUpdateFound;
            base.OnFormClosed(e);
            Program.BoardClosed();
        }

        // ---------------------------------------------------------------- self-test
        public void RunSelfTest(string dir)
        {
            Func<float, float, PointF> P = (x, y) => new PointF(x, y);
            var log = new List<string>();
            Action<bool, string> check = (ok, what) => { log.Add((ok ? "PASS " : "FAIL ") + what); if (!ok) throw new Exception("self-test failed: " + what); };

            int start = board.Items.Count;
            check(start == 1, "initial snip is on the board");

            // a wobbly handwriting-like stroke stays a stroke
            var pts = new List<PointF>();
            for (int i = 0; i < 80; i++) pts.Add(P(200 + i * 4, 420 + 25 * (float)Math.Sin(i / 5.0)));
            board.TestStroke(pts.ToArray(), false);
            check(board.Items[board.Items.Count - 1] is StrokeItem, "freehand stroke stays freehand");

            // a straight-ish stroke held at the end snaps to a perfect horizontal line
            pts.Clear();
            for (int i = 0; i < 60; i++) pts.Add(P(200 + i * 6, 520 + (i % 3) - 1));
            board.TestStroke(pts.ToArray(), true);
            var line = board.Items[board.Items.Count - 1] as ShapeItem;
            check(line != null && line.Kind == ShapeKind.Line && Math.Abs(line.A.Y - line.B.Y) < 0.01f, "held straight stroke snaps to a level line");

            // a rough circle held at the end snaps to a perfect circle
            pts.Clear();
            var rnd = new Random(7);
            for (int i = 0; i <= 72; i++)
            {
                double t = i * Math.PI * 2 / 70;
                float r = 80 + (float)(rnd.NextDouble() * 6 - 3);
                pts.Add(P(800 + r * (float)Math.Cos(t), 560 + r * 0.95f * (float)Math.Sin(t)));
            }
            board.TestStroke(pts.ToArray(), true);
            var circ = board.Items[board.Items.Count - 1] as ShapeItem;
            check(circ != null && circ.Kind == ShapeKind.Ellipse, "held circle snaps to a circle");
            var cr = Geo.Norm(circ.A, circ.B);
            check(Math.Abs(cr.Width - cr.Height) < 0.5f, "snapped circle is perfectly round");

            // a zig-zag held at the end does not snap
            pts.Clear();
            for (int i = 0; i < 40; i++) pts.Add(P(200 + i * 8, 700 + (i % 2 == 0 ? 0 : 30)));
            board.TestStroke(pts.ToArray(), true);
            check(board.Items[board.Items.Count - 1] is StrokeItem, "zig-zag is not snapped");

            // erasing through the middle of the first stroke splits it
            int before = board.Items.Count;
            board.TestErase(new[] { P(360, 380), P(360, 470) });
            check(board.Items.Count == before + 1, "eraser splits a stroke into two pieces (" + before + " -> " + board.Items.Count + ")");
            board.Undo();
            check(board.Items.Count == before, "undo restores the erased stroke");

            // Pen-tablet stress: dense, jittery, pressure-varying samples (what crashed v2.1.0 on Windows).
            int beforeStress = board.Items.Count;
            StrokeItem.Fallbacks = 0;
            var jr = new Random(99);
            for (int s = 0; s < 30; s++)
            {
                var sp = new List<PointF>();
                float x = 150 + s * 25, y = 820;
                for (int i = 0; i < 800; i++)
                {
                    x += (float)(jr.NextDouble() - 0.3) * 1.3f;
                    y += (float)(jr.NextDouble() - 0.5) * 1.3f;
                    sp.Add(P(x, y));
                }
                board.TestStroke(sp.ToArray(), false, true);
            }
            check(board.Items.Count == beforeStress + 30,
                "30 dense jittery pen strokes drawn with no errors (GDI+ fallbacks used: " + StrokeItem.Fallbacks + ")");

            board.SetTool(Tool.Pen);
            Directory.CreateDirectory(dir);
            using (var img = board.Export())
            {
                img.Save(Path.Combine(dir, "selftest.png"), ImageFormat.Png);
                SaveJpeg(img, Path.Combine(dir, "selftest.jpg"));
                Pdf.Save(img, Path.Combine(dir, "selftest.pdf"));
                check(img.Width > 100 && img.Height > 100, "export renders " + img.Width + "x" + img.Height);
            }
            var head = File.ReadAllBytes(Path.Combine(dir, "selftest.pdf"));
            check(head.Length > 1000 && head[0] == '%' && head[1] == 'P', "PDF written");

            board.ClearInk();
            check(board.Items.Count == 1 && !board.HasInk, "clear keeps the picture, removes the writing");
            board.Undo();
            check(board.HasInk, "undo brings writing back");

            board.Dirty = false;
            File.WriteAllLines(Path.Combine(dir, "selftest.log"), log.ToArray());
            foreach (var l in log) Console.WriteLine(l);
        }
    }
}
