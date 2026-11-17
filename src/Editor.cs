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
        ToolButton eraserBtn, graphBtn;
        Tool eraserChoice = Tool.Eraser;
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
            board.ToolChanged += delegate { RefreshUi(); };
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

            AddTool(Tool.Select, "select", "Select – move or resize anything you drew or pasted, drag empty space to scroll (V)");
            AddTool(Tool.Pen, "pen", "Pen (P)");
            AddTool(Tool.Highlighter, "highlighter", "Highlighter (H)");
            AddTool(Tool.Line, "line", "Straight line (L) – hold Shift for 45° angles");
            AddTool(Tool.Arrow, "arrow", "Arrow (A)");
            AddTool(Tool.Rect, "rect", "Rectangle (R) – hold Shift for a square");
            AddTool(Tool.Ellipse, "ellipse", "Circle / ellipse (O) – hold Shift for a perfect circle");
            AddTool(Tool.Text, "text", "Text (T) – click to type, Enter to finish, Shift+Enter for a new line");

            graphBtn = new ToolButton("graph") { DropArrow = true };
            graphBtn.AutoFit();
            graphBtn.Click += delegate { DropOrRun(graphBtn, ShowGraphMenu, delegate { SelectTool(Tool.Graph); }); };
            Add(graphBtn, "Graph box (B) – drag out a square of graph paper, lines or dots to draw in. Arrow: choose the paper");

            eraserBtn = new ToolButton("eraser") { DropArrow = true };
            eraserBtn.AutoFit();
            eraserBtn.Click += delegate { DropOrRun(eraserBtn, ShowEraserMenu, delegate { SelectTool(eraserChoice); }); };
            Add(eraserBtn, "Eraser (E) – arrow: rub out a bit, or wipe a whole line, shape or word with one touch (Shift+E)");
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
            if (board.CurrentTool == Tool.Select || Board.IsEraser(board.CurrentTool) || board.CurrentTool == Tool.Graph || board.CurrentTool == Tool.Laser)
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
            if (Board.IsEraser(board.CurrentTool)) eraserChoice = board.CurrentTool;
            eraserBtn.Checked = Board.IsEraser(board.CurrentTool);
            eraserBtn.SetIcon(eraserChoice == Tool.StrokeEraser ? "eraser2" : "eraser");
            graphBtn.Checked = board.CurrentTool == Tool.Graph;
            bool hl = board.CurrentTool == Tool.Highlighter;
            var pal = hl ? HlColors : PenColors;
            var active = board.ActiveColor.ToArgb();
            bool showSel = board.CurrentTool != Tool.Select && !Board.IsEraser(board.CurrentTool) && board.CurrentTool != Tool.Graph && board.CurrentTool != Tool.Laser;
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

        // A split button: the arrow opens the menu, the rest of the button just uses the current choice.
        void DropOrRun(ToolButton b, Action<ToolButton> menu, Action run)
        {
            if (b.PointToClient(Cursor.Position).X >= b.Width - Program.S(16)) menu(b);
            else run();
        }

        void ShowEraserMenu(ToolButton anchor)
        {
            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false, ShowCheckMargin = true };
            var rub = new ToolStripMenuItem("Rub out  (E)") { Checked = eraserChoice == Tool.Eraser, ToolTipText = "Erases just the part you drag over" };
            rub.Click += delegate { SelectTool(Tool.Eraser); };
            var whole = new ToolStripMenuItem("Whole line or shape  (Shift+E)") { Checked = eraserChoice == Tool.StrokeEraser };
            whole.Click += delegate { SelectTool(Tool.StrokeEraser); };
            menu.Items.Add(rub);
            menu.Items.Add(whole);
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        void ShowGraphMenu(ToolButton anchor)
        {
            var menu = new ContextMenuStrip { Font = Theme.Body, ShowImageMargin = false, ShowCheckMargin = true };
            string[] labels = { "Graph paper", "Graph paper with axes", "Lines", "Dots", "Blank" };
            for (int i = 0; i < labels.Length; i++)
            {
                var st = (GraphStyle)i;
                var item = new ToolStripMenuItem(labels[i]) { Checked = board.GraphStyle == st };
                item.Click += delegate { board.GraphStyle = st; SelectTool(Tool.Graph); };
                menu.Items.Add(item);
            }
            menu.Closed += delegate { BeginInvoke(new Action(menu.Dispose)); };
            menu.Show(anchor, new Point(0, anchor.Height));
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
                case Keys.Shift | Keys.E: SelectTool(Tool.StrokeEraser); return true;
                case Keys.B: SelectTool(Tool.Graph); return true;
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
            Action<bool, string> check = (ok, what) => { log.Add((ok ? "PASS " : "FAIL ") + what); if (!ok) { try { Directory.CreateDirectory(dir); File.WriteAllLines(Path.Combine(dir, "selftest.log"), log.ToArray()); } catch { } throw new Exception("self-test failed: " + what); } };

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

            // A drawn line shows a dot at each end; dragging a dot moves only that end (like the Snipping Tool).
            float z = board.Zoom;
            board.SetTool(Tool.Line);
            board.TestStroke(new[] { P(300, 960), P(450, 960), P(600, 960) }, false);
            var drawn = board.Items[board.Items.Count - 1] as ShapeItem;
            check(drawn != null && drawn.Kind == ShapeKind.Line && board.ActiveLine == drawn, "a drawn line shows its end dots");
            int lines = board.Items.Count;
            board.TestStroke(new[] { P(603, 958), P(650, 1000), P(700, 1060) }, false);
            var moved = board.Items[board.Items.Count - 1] as ShapeItem;
            var wantB = P(drawn.B.X + 97 / z, drawn.B.Y + 102 / z);
            check(board.Items.Count == lines && moved != null && moved != drawn && moved.A == drawn.A &&
                  Geo.Dist(moved.B, wantB) < 0.5f && board.ActiveLine == moved, "dragging an end dot moves only that end");
            board.Undo();
            check(board.Items[board.Items.Count - 1] == drawn, "undo puts the line end back");
            board.TestStroke(new[] { P(300, 1100), P(500, 1100) }, false);
            check(board.Items.Count == lines + 1 && board.Items[lines - 1] == drawn, "drawing away from the dots draws a new line");
            board.Undo();
            board.SetTool(Tool.Select);
            board.TestStroke(new[] { P(450, 961), P(460, 971), P(470, 981) }, false);
            var shifted = board.Items[board.Items.Count - 1] as ShapeItem;
            check(shifted != null && shifted != drawn && Geo.Dist(shifted.A, P(drawn.A.X + 20 / z, drawn.A.Y + 20 / z)) < 0.5f &&
                  Geo.Dist(shifted.B, P(drawn.B.X + 20 / z, drawn.B.Y + 20 / z)) < 0.5f, "Select tool drags a whole line");
            board.DeleteSelected();
            check(board.Items.Count == lines - 1 && board.ActiveLine == null, "Delete removes the selected line");
            board.Undo();
            board.Undo();
            check(board.Items[board.Items.Count - 1] == drawn, "undo restores the line");
            board.SetTool(Tool.Pen);
            check(board.ActiveLine == null, "switching to the pen hides the dots");

            // A wobbly, held square snaps to a clean square; a held circle is still a circle, not a box.
            board.SetTool(Tool.Pen);
            pts.Clear();
            var rn = new Random(3);
            Func<float, float> jit = v => v + (float)(rn.NextDouble() * 6 - 3);
            float qx = 1000, qy = 1250, qs = 140;
            for (int i = 0; i <= 20; i++) pts.Add(P(jit(qx + qs * i / 20f), jit(qy)));
            for (int i = 1; i <= 20; i++) pts.Add(P(jit(qx + qs), jit(qy + qs * i / 20f)));
            for (int i = 1; i <= 20; i++) pts.Add(P(jit(qx + qs - qs * i / 20f), jit(qy + qs)));
            for (int i = 1; i < 20; i++) pts.Add(P(jit(qx), jit(qy + qs - qs * i / 20f)));
            board.TestStroke(pts.ToArray(), true);
            var sq = board.Items[board.Items.Count - 1] as ShapeItem;
            check(sq != null && sq.Kind == ShapeKind.Rect, "held wobbly square snaps to a rectangle");
            var sqr = Geo.Norm(sq.A, sq.B);
            check(Math.Abs(sqr.Width - sqr.Height) < 0.5f, "a nearly square box becomes an exact square");
            pts.Clear();
            for (int i = 0; i <= 60; i++)
            {
                double t = i * Math.PI * 2 / 60;
                pts.Add(P(1300 + 70 * (float)Math.Cos(t), 1250 + 70 * (float)Math.Sin(t)));
            }
            board.TestStroke(pts.ToArray(), true);
            var rc = board.Items[board.Items.Count - 1] as ShapeItem;
            check(rc != null && rc.Kind == ShapeKind.Ellipse, "a held circle still becomes a circle, not a box");

            // A sloppy square (sides overshoot each other, big wobble) still snaps.
            pts.Clear();
            var rs = new Random(11);
            Func<float, float> wob = v => v + (float)(rs.NextDouble() * 14 - 7);
            float ox = 1700, oy = 1250, os = 150;
            for (int i = 0; i <= 20; i++) pts.Add(P(wob(ox - 20 + (os + 40) * i / 20f), wob(oy)));          // top, starts and ends 20 px out
            for (int i = 1; i <= 20; i++) pts.Add(P(wob(ox + os + 10), wob(oy - 15 + (os + 25) * i / 20f)));   // right, leans out
            for (int i = 1; i <= 20; i++) pts.Add(P(wob(ox + os + 15 - (os + 40) * i / 20f), wob(oy + os + 12)));
            for (int i = 1; i <= 24; i++) pts.Add(P(wob(ox - 12), wob(oy + os + 12 - (os + 40) * i / 24f)));   // left, overshoots past the start
            board.TestStroke(pts.ToArray(), true);
            var sloppy = board.Items[board.Items.Count - 1] as ShapeItem;
            check(sloppy != null && sloppy.Kind == ShapeKind.Rect, "a sloppy, overshooting square still snaps to a box");

            // A square drawn with bowed (curved) sides and uneven corners still snaps.
            pts.Clear();
            float bx = 1950, by = 1550, bs = 140, bow = 16;
            for (int i = 0; i <= 20; i++) { float t = i / 20f; pts.Add(P(bx + bs * t, by - bow * (float)Math.Sin(t * Math.PI))); }
            for (int i = 1; i <= 20; i++) { float t = i / 20f; pts.Add(P(bx + bs + bow * (float)Math.Sin(t * Math.PI), by + bs * t)); }
            for (int i = 1; i <= 20; i++) { float t = i / 20f; pts.Add(P(bx + bs - bs * t, by + bs + bow * (float)Math.Sin(t * Math.PI))); }
            for (int i = 1; i <= 20; i++) { float t = i / 20f; pts.Add(P(bx - bow * (float)Math.Sin(t * Math.PI), by + bs - bs * t)); }
            board.TestStroke(pts.ToArray(), true);
            var bowed = board.Items[board.Items.Count - 1] as ShapeItem;
            check(bowed != null && bowed.Kind == ShapeKind.Rect, "a square with bowed, curved sides still snaps");

            // A rectangle drawn a bit slanted (lopsided corners) still snaps.
            pts.Clear();
            var slant = new[] { P(2300, 1560), P(2490, 1585), P(2480, 1680), P(2290, 1650) };
            for (int e = 0; e < 4; e++)
            {
                var a = slant[e]; var b = slant[(e + 1) % 4];
                for (int i = 0; i < 20; i++) pts.Add(P(a.X + (b.X - a.X) * i / 20f + (float)Math.Sin(i * 0.9) * 5, a.Y + (b.Y - a.Y) * i / 20f));
            }
            pts.Add(slant[0]);
            board.TestStroke(pts.ToArray(), true);
            var lop = board.Items[board.Items.Count - 1];
            check(lop is ShapeItem && ((ShapeItem)lop).Kind == ShapeKind.Rect, "a lopsided, slightly slanted rectangle still snaps");

            // A held triangle becomes a clean triangle.
            pts.Clear();
            var tv = new[] { P(2000, 1400), P(2160, 1400), P(2080, 1260) };
            var tr = new Random(5);
            for (int e = 0; e < 3; e++)
            {
                var a = tv[e]; var b = tv[(e + 1) % 3];
                for (int i = 0; i < 20; i++)
                {
                    float t = i / 20f;
                    pts.Add(P(a.X + (b.X - a.X) * t + (float)(tr.NextDouble() * 8 - 4), a.Y + (b.Y - a.Y) * t + (float)(tr.NextDouble() * 8 - 4)));
                }
            }
            pts.Add(tv[0]);
            board.TestStroke(pts.ToArray(), true);
            var tri = board.Items[board.Items.Count - 1] as PolyItem;
            check(tri != null && tri.Pts.Length == 3, "a held triangle snaps to a triangle");

            // After snapping, the pen can stay down and drag the shape bigger; it locks in when lifted.
            pts.Clear();
            for (int i = 0; i <= 60; i++)
            {
                double t = i * Math.PI * 2 / 60;
                pts.Add(P(2400 + 70 * (float)Math.Cos(t), 1300 + 70 * (float)Math.Sin(t)));
            }
            int nBig = board.Items.Count;
            board.TestHoldThenDrag(pts.ToArray(), new[] { P(2400 + 100, 1300), P(2400 + 140, 1300) });
            var big = board.Items[board.Items.Count - 1] as ShapeItem;
            check(board.Items.Count == nBig + 1 && big != null && big.Kind == ShapeKind.Ellipse, "snapped circle is committed once, on release");
            var bigR = Geo.Norm(big.A, big.B);
            check(Math.Abs(bigR.Width - 280 / z) < 4f / z && Math.Abs(bigR.Width - bigR.Height) < 0.5f, "dragging after the snap made the circle twice as big (" + bigR.Width + ")");

            // Select moves rectangles and pen strokes (not just pictures and lines).
            board.SetTool(Tool.Rect);
            board.TestStroke(new[] { P(1000, 1600), P(1120, 1680) }, false);
            var rectBefore = board.Items[board.Items.Count - 1] as ShapeItem;
            int nItems = board.Items.Count;
            board.TestSelectDrag(P(1060, 1600), P(1110, 1630));
            var rectAfter = board.Items[board.Items.Count - 1] as ShapeItem;
            check(board.Items.Count == nItems && rectAfter != null && rectAfter != rectBefore && rectAfter.Kind == ShapeKind.Rect &&
                  Geo.Dist(rectAfter.A, P(rectBefore.A.X + 50 / z, rectBefore.A.Y + 30 / z)) < 0.5f, "Select tool moves a rectangle");
            board.Undo();
            check(board.Items[board.Items.Count - 1] == rectBefore, "undo puts the rectangle back");
            board.SetTool(Tool.Pen);
            pts.Clear();
            for (int i = 0; i < 40; i++) pts.Add(P(1000 + i * 5, 1750 + 20 * (float)Math.Sin(i / 4.0)));
            board.TestStroke(pts.ToArray(), false);
            var penStroke = board.Items[board.Items.Count - 1] as StrokeItem;
            var sp0 = penStroke.Pts[0];
            board.TestSelectDrag(pts[10], P(pts[10].X, pts[10].Y + 60));
            var penMoved = board.Items[board.Items.Count - 1] as StrokeItem;
            check(penMoved != null && penMoved != penStroke && Math.Abs(penMoved.Pts[0].Y - (sp0.Y + 60 / z)) < 0.5f && Math.Abs(penMoved.Pts[0].X - sp0.X) < 0.5f,
                  "Select tool moves a pen stroke");
            board.Undo();

            // The whole-stroke eraser takes one touch to remove a whole stroke (the normal eraser only splits it).
            int beforeWhole = board.Items.Count;
            board.TestStrokeErase(new[] { pts[20] });
            check(board.Items.Count == beforeWhole - 1, "whole-stroke eraser removes the stroke with one tap");
            board.Undo();
            check(board.Items.Count == beforeWhole, "undo brings the stroke back");

            // Graph box: drag it out, write on it, and it carries the writing when moved.
            int beforeGraph = board.Items.Count;
            board.TestDrawGraph(P(1400, 1250), P(1600, 1400), GraphStyle.Axes);
            var gbox = board.Items[board.Items.Count - 1] as GraphItem;
            check(board.Items.Count == beforeGraph + 1 && gbox != null && gbox.Style == GraphStyle.Axes, "graph box is added");
            check(board.CurrentTool == Tool.Pen, "after drawing a graph box the pen is ready");
            pts.Clear();
            for (int i = 0; i < 12; i++) pts.Add(P(1440 + i * 10, 1330 - i * i * 0.6f));
            board.TestStroke(pts.ToArray(), false);
            var onGraph = board.Items[board.Items.Count - 1] as StrokeItem;
            var og0 = onGraph.Pts[0];
            board.SetTool(Tool.Select);
            board.TestSelectDrag(P(1420, 1270), P(1460, 1290));
            GraphItem gMoved = null; StrokeItem sMoved = null;
            foreach (var it in board.Items) { if (it is GraphItem) gMoved = (GraphItem)it; }
            sMoved = board.Items[board.Items.Count - 1] as StrokeItem;
            check(gMoved != null && gMoved != gbox && Math.Abs(gMoved.Rect.X - (gbox.Rect.X + 40 / z)) < 1f && Math.Abs(gMoved.Rect.Y - (gbox.Rect.Y + 20 / z)) < 1f,
                  "Select tool moves the graph box");
            check(sMoved != null && Math.Abs(sMoved.Pts[0].X - (og0.X + 40 / z)) < 1.5f, "writing on the graph box moves with it");
            board.DeleteSelected();
            check(board.Items.Count == beforeGraph + 1 && !(board.Items.Exists(i => i is GraphItem)), "Delete removes the selected graph box (writing stays)");
            board.SetTool(Tool.Pen);

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
