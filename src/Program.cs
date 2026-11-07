// Math Snip Board v2 - snip any part of the screen and teach on it.
// Single-file WinForms app for .NET Framework 4.x (built into Windows 10/11).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;


namespace MathSnip
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        public const string Name = "Math Snip Board";
        public static float Dpi = 1f;
        public static int S(float v) { return (int)Math.Round(v * Dpi); }
        public static Icon AppIcon;
        public static Settings Settings = new Settings();
        public static LauncherForm Launcher;
        static bool snipping, showingError;
        static Mutex instanceMutex;
        public static bool Exiting;
        static int boardCount;

        [STAThread]
        static int Main(string[] args)
        {
            bool selfTest = args.Length > 0 && args[0] == "--selftest";
            if (args.Length >= 2 && args[0] == "--update-check") return Updater.CommandLineCheck(args[1]);
            if (args.Length >= 3 && args[0] == "--parse-release") return Updater.CommandLineParse(args[1], args[2]);
            if (args.Length >= 2 && args[0] == "--gdi-probe") return GdiProbe.Run(args[1]);
            bool justUpdated = args.Length >= 1 && args[0] == "--updated";
            if (justUpdated && args.Length >= 2)
            {
                // Wait for the old version to close so it releases the single-instance lock.
                int pid;
                if (int.TryParse(args[1], out pid))
                    try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(15000); } catch { }
            }
            EnableDpiAwareness();

            Mutex mutex = null;
            EventWaitHandle wake = null;
            if (!selfTest)
            {
                try
                {
                    bool created;
                    mutex = new Mutex(true, "MathSnipBoard.SingleInstance", out created);
                    if (created) instanceMutex = mutex;
                    wake = new EventWaitHandle(false, EventResetMode.AutoReset, "MathSnipBoard.Wake");
                    if (!created) { wake.Set(); return 0; }   // already running: bring it forward
                }
                catch { mutex = null; wake = null; }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

            try { using (var g = Graphics.FromHwnd(IntPtr.Zero)) Dpi = Math.Max(1f, g.DpiX / 96f); } catch { }
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                    if (s != null) AppIcon = new Icon(s);
            }
            catch { }
            if (AppIcon == null) try { AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Settings = Settings.Load();

            if (selfTest) return SelfTest.Run(args.Length > 1 ? args[1] : ".");

            Launcher = new LauncherForm();
            if (wake != null)
            {
                var t = new Thread(() =>
                {
                    while (true)
                    {
                        try
                        {
                            wake.WaitOne();
                            if (Launcher.IsHandleCreated) Launcher.BeginInvoke(new Action(Launcher.Summon));
                        }
                        catch (ObjectDisposedException) { return; }
                        catch { }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            Launcher.Shown += delegate
            {
                Updater.Start(Launcher);
                if (justUpdated) Launcher.ShowUpdatedNotice();
            };

            if (args.Length >= 2 && args[0] == "--open")
            {
                string path = args[1];
                Launcher.Shown += delegate { OpenBoard(LoadImage(path)); };
            }
            else if (args.Length >= 1 && args[0] == "--blank")
                Launcher.Shown += delegate { OpenBoard(null); };

            Application.Run(Launcher);
            Settings.Save();
            GC.KeepAlive(mutex);
            Environment.Exit(0);   // never linger in the background (matters when updating)
            return 0;
        }

        // Lets the freshly updated copy start immediately.
        public static void ReleaseSingleInstance()
        {
            try { if (instanceMutex != null) { instanceMutex.ReleaseMutex(); instanceMutex.Close(); instanceMutex = null; } }
            catch { }
        }

        // Per-monitor DPI so snips are pixel-perfect on laptops + projectors with different scaling.
        static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        static readonly Dictionary<string, bool> shownErrors = new Dictionary<string, bool>();

        public static void ShowError(Exception ex)
        {
            if (ex == null || showingError) return;
            // Show each distinct problem once per session - never a stream of pop-ups in the middle of a lesson.
            string[] frames = (ex.StackTrace ?? "").Split('\n');
            string key = ex.GetType().FullName + "|" + (frames.Length > 0 ? frames[frames.Length > 2 ? 2 : 0].Trim() : "");
            if (shownErrors.ContainsKey(key)) return;
            shownErrors[key] = true;
            showingError = true;
            try
            {
                string[] lines = (ex.StackTrace ?? "").Split('\n');
                string top = string.Join("\n", lines, 0, Math.Min(5, lines.Length));
                MessageBox.Show("Oops, something went wrong - but your boards are still open.\n\n" +
                                ex.GetType().Name + ": " + ex.Message + "\n\n" + top,
                                Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch { }
            showingError = false;
        }

        public static Bitmap LoadImage(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var img = Image.FromStream(fs))
                return new Bitmap(img);
        }

        public static void OpenBoard(Bitmap snip)
        {
            boardCount++;
            var ed = new EditorForm(snip, boardCount);
            ed.Show();
            ed.Activate();
            // The board has its own Snip button, so the launcher steps aside (tray + shortcut still work).
            if (Launcher != null && Launcher.Visible) Launcher.Hide();
        }

        public static void BoardClosed()
        {
            if (Exiting || Launcher == null || Launcher.IsDisposed || Launcher.Disposing) return;
            foreach (var e in Editors()) if (e.Visible) return;
            if (!snipping) Launcher.Summon();
        }

        public static IEnumerable<EditorForm> Editors()
        {
            var list = new List<EditorForm>();
            foreach (Form f in Application.OpenForms)
            {
                var e = f as EditorForm;
                if (e != null && !e.IsDisposed) list.Add(e);
            }
            return list;
        }

        // deliver == null: open the snip on a new board.
        public static void StartSnip(Action<Bitmap> deliver)
        {
            if (snipping) return;
            snipping = true;

            var hidden = new List<Form>();
            foreach (Form f in Application.OpenForms) if (f.Visible) hidden.Add(f);
            foreach (var f in hidden) f.Hide();

            var timer = new System.Windows.Forms.Timer { Interval = hidden.Count > 0 ? 280 : 60 };
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();
                Rectangle vs;
                Bitmap shot;
                try { shot = CaptureScreen(out vs); }
                catch (Exception ex)
                {
                    foreach (var f in hidden) if (!f.IsDisposed) f.Show();
                    snipping = false;
                    MessageBox.Show("Couldn't capture the screen: " + ex.Message, Name);
                    return;
                }
                var ov = new SnipOverlay(shot, vs);
                ov.FormClosed += delegate
                {
                    foreach (var f in hidden) if (!f.IsDisposed) f.Show();
                    snipping = false;
                    shot.Dispose();
                    if (ov.Result == null) return;
                    if (deliver != null) deliver(ov.Result);
                    else OpenBoard(ov.Result);
                };
                ov.Show();
                ov.Activate();
            };
            timer.Start();
        }

        static Bitmap CaptureScreen(out Rectangle vs)
        {
            vs = SystemInformation.VirtualScreen;
            var bmp = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(vs.Left, vs.Top, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);
            return bmp;
        }
    }

    // Remembers the teacher's choices between sessions (%APPDATA%\MathSnipBoard\settings.ini).
    class Settings
    {
        public int PenColor = Color.FromArgb(24, 24, 27).ToArgb();
        public int HlColor = Color.FromArgb(255, 221, 0).ToArgb();
        public int CustomColor = 0;
        public int Level = 1;
        public int Backdrop = 0;
        public bool Pressure = true;
        public bool KeepOnTop = true;
        public int LauncherX = int.MinValue, LauncherY = int.MinValue;
        public bool TipShown, TrayTipShown;
        public string LastFolder = "";
        public string NotifiedVersion = "";

        static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MathSnipBoard"); } }
        static string File_ { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(File_)) return s;
                foreach (var line in File.ReadAllLines(File_))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    s.Set(line.Substring(0, i).Trim(), line.Substring(i + 1).Trim());
                }
            }
            catch { }
            return s;
        }

        void Set(string k, string v)
        {
            int n; bool b;
            bool isInt = int.TryParse(v, out n), isBool = bool.TryParse(v, out b);
            switch (k)
            {
                case "PenColor": if (isInt) PenColor = n; break;
                case "HlColor": if (isInt) HlColor = n; break;
                case "CustomColor": if (isInt) CustomColor = n; break;
                case "Level": if (isInt && n >= 0 && n <= 2) Level = n; break;
                case "Backdrop": if (isInt && n >= 0 && n <= 3) Backdrop = n; break;
                case "Pressure": if (isBool) Pressure = b; break;
                case "KeepOnTop": if (isBool) KeepOnTop = b; break;
                case "LauncherX": if (isInt) LauncherX = n; break;
                case "LauncherY": if (isInt) LauncherY = n; break;
                case "TipShown": if (isBool) TipShown = b; break;
                case "TrayTipShown": if (isBool) TrayTipShown = b; break;
                case "LastFolder": LastFolder = v; break;
                case "NotifiedVersion": NotifiedVersion = v; break;
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(File_, new[]
                {
                    "PenColor=" + PenColor, "HlColor=" + HlColor, "CustomColor=" + CustomColor,
                    "Level=" + Level, "Backdrop=" + Backdrop, "Pressure=" + Pressure,
                    "KeepOnTop=" + KeepOnTop, "LauncherX=" + LauncherX, "LauncherY=" + LauncherY,
                    "TipShown=" + TipShown, "TrayTipShown=" + TrayTipShown, "LastFolder=" + LastFolder,
                    "NotifiedVersion=" + NotifiedVersion
                });
            }
            catch { }
        }
    }
}
