// Self-updater: checks GitHub Releases, downloads the new exe, verifies it, swaps it in and restarts.
// Publishing an update = pushing a tag like v2.2.0 (the GitHub Action builds and releases the exe).

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace MathSnip
{
    static class Updater
    {
        public const string Owner = "ZInuke21";
        public const string Repo = "math-snip-board";
        public const string AssetName = "MathSnipBoard.exe";
        public static string ReleasesPage { get { return "https://github.com/" + Owner + "/" + Repo + "/releases/latest"; } }

        public class Info
        {
            public Version Version;
            public string Tag, Url, Notes;
            public long Size;
        }

        public static Info Available;
        public static event EventHandler Found;
        static Control ui;
        static System.Windows.Forms.Timer timer;
        static bool checking;

        public static Version Current
        {
            get { return Norm(Assembly.GetExecutingAssembly().GetName().Version); }
        }

        public static string CurrentText { get { var v = Current; return v.Major + "." + v.Minor + "." + v.Build; } }

        static Version Norm(Version v)
        {
            return new Version(Math.Max(0, v.Major), Math.Max(0, v.Minor), Math.Max(0, v.Build));
        }

        static void EnableTls()
        {
            // .NET 4.x apps default to old TLS; GitHub needs TLS 1.2.
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        }

        // ---------------------------------------------------------------- checking
        public static void Start(Control uiThread)
        {
            ui = uiThread;
            CleanupOld();
            var first = new System.Windows.Forms.Timer { Interval = 8000 };
            first.Tick += delegate { first.Stop(); first.Dispose(); Check(false, null); };
            first.Start();
            timer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
            timer.Tick += delegate { Check(false, null); };
            timer.Start();
        }

        // manual = the teacher clicked "Check for updates", so tell them the result either way.
        public static void Check(bool manual, IWin32Window owner)
        {
            if (checking || ui == null) return;
            checking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Info info = null;
                Exception err = null;
                try { info = Fetch(); } catch (Exception ex) { err = ex; }
                try
                {
                    ui.BeginInvoke(new Action(() =>
                    {
                        checking = false;
                        OnResult(info, err, manual, owner);
                    }));
                }
                catch { checking = false; }
            });
        }

        static void OnResult(Info info, Exception err, bool manual, IWin32Window owner)
        {
            if (info != null && info.Version > Current)
            {
                bool isNew = Available == null || Available.Version != info.Version;
                Available = info;
                if (isNew && Found != null) Found(null, EventArgs.Empty);
                if (manual) Install(owner);
                return;
            }
            if (!manual) return;
            if (err != null)
                MessageBox.Show(owner, "Couldn't check for updates right now.\n\n" + err.Message, Program.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(owner, "You're on the latest version (v" + CurrentText + ").", Program.Name,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static Info Fetch()
        {
            EnableTls();
            string api = Environment.GetEnvironmentVariable("MATHSNIP_UPDATE_API");   // only for testing the updater locally
            if (string.IsNullOrEmpty(api)) api = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";
            var req = (HttpWebRequest)WebRequest.Create(api);
            req.UserAgent = "MathSnipBoard/" + CurrentText;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            string json;
            try
            {
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    json = sr.ReadToEnd();
            }
            catch (WebException wex)
            {
                var hr = wex.Response as HttpWebResponse;
                if (hr != null && hr.StatusCode == HttpStatusCode.NotFound) return null;   // no releases yet
                throw;
            }
            return Parse(json);
        }

        public static Info Parse(string json)
        {
            string tag = JsonString(json, "tag_name");
            if (tag == null) return null;
            Version v;
            if (!Version.TryParse(tag.Trim().TrimStart('v', 'V'), out v)) return null;

            var m = Regex.Match(json, "\"browser_download_url\"\\s*:\\s*\"([^\"]*/" + Regex.Escape(AssetName) + ")\"");
            if (!m.Success) return null;
            var info = new Info { Version = Norm(v), Tag = tag, Url = m.Groups[1].Value, Notes = JsonString(json, "body") ?? "" };

            int at = json.IndexOf("\"name\":\"" + AssetName + "\"", StringComparison.Ordinal);
            if (at < 0) at = json.IndexOf("\"name\": \"" + AssetName + "\"", StringComparison.Ordinal);
            if (at >= 0)
            {
                var sm = new Regex("\"size\"\\s*:\\s*(\\d+)").Match(json, at);
                long size;
                if (sm.Success && sm.Index - at < 3000 && long.TryParse(sm.Groups[1].Value, out size)) info.Size = size;
            }
            return info;
        }

        static string JsonString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            if (!m.Success) return null;
            string s = m.Groups[1].Value;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': break;
                    case 't': sb.Append('\t'); break;
                    case 'b': case 'f': break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            int code;
                            if (int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code)) sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- installing
        public static void Install(IWin32Window owner)
        {
            var info = Available;
            if (info == null) return;

            string notes = CleanNotes(info.Notes);
            string msg = "A new version of Math Snip Board is ready: v" + info.Version.Major + "." + info.Version.Minor + "." + info.Version.Build +
                         "  (you have v" + CurrentText + ")." +
                         (notes.Length > 0 ? "\n\nWhat's new:\n" + notes : "") +
                         "\n\nUpdate now? It takes a few seconds and the app restarts by itself.";
            int dirty = 0;
            foreach (var ed in Program.Editors()) if (ed.IsDirty) dirty++;
            if (dirty > 0) msg += "\n\nNote: " + (dirty == 1 ? "a board has" : dirty + " boards have") + " unsaved work – save it first if you need it.";
            if (MessageBox.Show(owner, msg, "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;

            using (var dlg = new UpdateProgressForm(info)) dlg.ShowDialog(owner);
        }

        static string CleanNotes(string notes)
        {
            if (string.IsNullOrEmpty(notes)) return "";
            var lines = notes.Replace("\r", "").Split('\n');
            var sb = new StringBuilder();
            foreach (var raw in lines)
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#") || l.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase) || l.StartsWith("<!--")) continue;
                if (l.StartsWith("* ") || l.StartsWith("- ")) l = "• " + l.Substring(2);
                l = l.Replace("**", "");
                if (sb.Length + l.Length > 700) { sb.Append("…"); break; }
                sb.AppendLine(l);
            }
            return sb.ToString().TrimEnd();
        }

        // Called once the new exe is fully downloaded to newPath. Returns an error message, or null when it is restarting.
        public static string SwapAndRestart(Info info, string newPath)
        {
            // 1. Make sure what we downloaded really is the new Math Snip Board.
            var fi = new FileInfo(newPath);
            if (!fi.Exists || fi.Length < 40000) return "The download looks incomplete.";
            if (info.Size > 0 && fi.Length != info.Size) return "The download size doesn't match.";
            using (var fs = File.OpenRead(newPath))
                if (fs.ReadByte() != 'M' || fs.ReadByte() != 'Z') return "The download isn't a Windows program.";
            try
            {
                var an = AssemblyName.GetAssemblyName(newPath);
                if (an.Name != "MathSnipBoard") return "The download isn't Math Snip Board.";
                if (Norm(an.Version) != info.Version) return "The download has the wrong version.";
            }
            catch (Exception ex) { return "Couldn't read the download: " + ex.Message; }

            // 2. Windows lets a running exe be renamed, so: current -> .old, new -> current name.
            string exe = Application.ExecutablePath;
            string old = exe + ".old";
            try { if (File.Exists(old)) File.Delete(old); } catch { }
            try { File.Move(exe, old); }
            catch (Exception ex) { return "Couldn't replace the program file: " + ex.Message; }
            try { File.Move(newPath, exe); }
            catch (Exception ex)
            {
                try { File.Move(old, exe); } catch { }
                return "Couldn't install the new version: " + ex.Message;
            }

            // 3. Start the new version (it waits for this one to close) and quit.
            Program.ReleaseSingleInstance();
            try { Process.Start(new ProcessStartInfo(exe, "--updated " + Process.GetCurrentProcess().Id) { UseShellExecute = false }); }
            catch (Exception ex)
            {
                try { File.Move(exe, newPath); File.Move(old, exe); } catch { }
                return "Couldn't start the new version: " + ex.Message;
            }
            Program.Exiting = true;
            Program.Settings.Save();
            if (Program.Launcher != null) Program.Launcher.RemoveTrayIcon();
            var killer = new Thread(() => { Thread.Sleep(2000); Environment.Exit(0); }) { IsBackground = true };
            killer.Start();   // guarantee the old copy is gone even if a window refuses to close
            Application.Exit();
            return null;
        }

        public static void CleanupOld()
        {
            string old = Application.ExecutablePath + ".old";
            string partial = Application.ExecutablePath + ".download";
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < 20; i++)
                {
                    try
                    {
                        if (File.Exists(old)) File.Delete(old);
                        if (File.Exists(partial)) File.Delete(partial);
                        return;
                    }
                    catch { Thread.Sleep(500); }
                }
            });
        }

        public static void OpenReleasesPage()
        {
            try { Process.Start(ReleasesPage); } catch { }
        }

        public static int CommandLineParse(string jsonFile, string outFile)
        {
            var i = Parse(File.ReadAllText(jsonFile, Encoding.UTF8));
            File.WriteAllText(outFile, i == null ? "null" :
                "tag=" + i.Tag + "\nversion=" + i.Version + "\nurl=" + i.Url + "\nsize=" + i.Size + "\nnewer=" + (i.Version > Current) +
                "\nnotes<<\n" + CleanNotes(i.Notes) + "\n>>", Encoding.UTF8);
            return 0;
        }

        // `--update-check out.txt`: used by the release pipeline to prove the updater works on real Windows.
        public static int CommandLineCheck(string outFile)
        {
            string result;
            try
            {
                var info = Fetch();
                result = info == null ? "latest=none" : "latest=" + info.Tag + "\nversion=" + info.Version + "\nurl=" + info.Url + "\nsize=" + info.Size;
                result += "\ncurrent=" + CurrentText;
            }
            catch (Exception ex) { result = "error=" + ex.Message; }
            File.WriteAllText(outFile, result);
            return result.StartsWith("error=") ? 2 : 0;
        }
    }

    // Small "Downloading update..." window with a progress bar.
    class UpdateProgressForm : Form
    {
        readonly Updater.Info info;
        readonly Label label;
        readonly ProgressBar bar;
        readonly WebClient client = new WebClient();
        readonly string tempPath;
        bool done;

        public UpdateProgressForm(Updater.Info info)
        {
            this.info = info;
            Text = "Updating Math Snip Board";
            if (Program.AppIcon != null) Icon = Program.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Surface;
            Font = Theme.Body;
            ClientSize = new Size(Program.S(380), Program.S(118));

            label = new Label
            {
                Text = "Downloading v" + info.Version.Major + "." + info.Version.Minor + "." + info.Version.Build + "…",
                AutoSize = false,
                Location = new Point(Program.S(20), Program.S(18)),
                Size = new Size(Program.S(340), Program.S(22)),
                ForeColor = Theme.Text
            };
            bar = new ProgressBar
            {
                Location = new Point(Program.S(20), Program.S(46)),
                Size = new Size(Program.S(340), Program.S(16)),
                Minimum = 0,
                Maximum = 100
            };
            var cancel = new ToolButton(null, "Cancel") { Outline = true, Center = true, FixedWidth = Program.S(96), FixedHeight = Program.S(32) };
            cancel.AutoFit();
            cancel.Location = new Point(ClientSize.Width - cancel.Width - Program.S(20), Program.S(74));
            cancel.Click += delegate { Close(); };
            Controls.Add(label);
            Controls.Add(bar);
            Controls.Add(cancel);

            tempPath = Application.ExecutablePath + ".download";
            client.Headers[HttpRequestHeader.UserAgent] = "MathSnipBoard/" + Updater.CurrentText;
            client.DownloadProgressChanged += (s, e) => { bar.Value = Math.Max(0, Math.Min(100, e.ProgressPercentage)); };
            client.DownloadFileCompleted += OnDownloaded;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                if (File.Exists(tempPath)) File.Delete(tempPath);
                client.DownloadFileAsync(new Uri(info.Url), tempPath);
            }
            catch (Exception ex) { Fail("Couldn't start the download: " + ex.Message); }
        }

        void OnDownloaded(object sender, System.ComponentModel.AsyncCompletedEventArgs e)
        {
            if (IsDisposed || done) return;
            if (e.Cancelled) { TryDelete(); return; }
            if (e.Error != null) { Fail("The download failed: " + e.Error.Message); return; }
            label.Text = "Installing…";
            bar.Value = 100;
            string err = Updater.SwapAndRestart(info, tempPath);
            if (err != null) Fail(err);
            else { done = true; Close(); }
        }

        void Fail(string why)
        {
            done = true;
            TryDelete();
            var r = MessageBox.Show(Owner, why + "\n\nOpen the download page instead? You can download the new MathSnipBoard.exe there and replace the old one.",
                Program.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r == DialogResult.Yes) Updater.OpenReleasesPage();
            Close();
        }

        void TryDelete()
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!done && client.IsBusy) client.CancelAsync();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) client.Dispose();
            base.Dispose(disposing);
        }
    }
}
