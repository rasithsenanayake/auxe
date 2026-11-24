using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].StartsWith("chrome-extension://", StringComparison.Ordinal))
        {
            ChromeBridge.RunHost();
            return;
        }
        using (var mutex = new Mutex(false, @"Local\Auxe.Tray"))
        {
            if (!mutex.WaitOne(0)) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(true);
            using (var tray = new Tray()) Application.Run(tray);
        }
    }
}

sealed class Tray : ApplicationContext
{
    readonly Audio audio = new Audio();
    readonly ChromeBridge bridge = new ChromeBridge();
    readonly NotifyIcon tray = new NotifyIcon();
    readonly Timer animation = new Timer { Interval = 750 };
    readonly Timer refresh = new Timer { Interval = 1200 };
    readonly Icon[] frames = Enumerable.Range(0, 5).Select(SpeakerArt.Draw).ToArray();
    readonly Form panel = new Form();
    readonly Label heading = new CrispLabel();
    readonly PictureBox mark = new PictureBox();
    readonly HeaderButton settings = new HeaderButton(true);
    readonly HeaderButton appearance = new HeaderButton(false);
    readonly ToolTip hints = new ToolTip();
    readonly Timer panelFade = new Timer { Interval = 15 };
    readonly Stopwatch panelClock = new Stopwatch();
    readonly Timer appearanceTimer = new Timer { Interval = 15 };
    readonly Stopwatch appearanceClock = new Stopwatch();
    PictureBox oldAppearance;
    Point revealOrigin;
    double fadeStart;
    bool panelClosing;
    readonly float displayScale = DisplayScale();
    readonly Timer updateTimer = new Timer { Interval = 30000 };
    readonly ToolStripMenuItem updatesItem = new ToolStripMenuItem("Automatic updates");
    readonly ToolStripMenuItem downloadItem = new ToolStripMenuItem("Check for updates");
    readonly ToolStripMenuItem companionItem = new ToolStripMenuItem("Chrome companion setup");
    bool checkingUpdate;
    string readyUpdate;
    readonly Font rowFont = Typography.Heading(10);
    readonly Font detailFont = Typography.Text(9.25f);
    readonly Label status = new CrispLabel();
    readonly FlowLayoutPanel rows = new FlowLayoutPanel();
    readonly Dictionary<string, Tuple<string, long>> requested = new Dictionary<string, Tuple<string, long>>();
    readonly Dictionary<string, Bitmap> appIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    readonly object audioLock = new object();
    Audio routingAudio;
    bool refreshing;
    bool routing;
    bool disposed;
    bool nativeAudioUnavailable;
    readonly FadingMenu settingsMenu = new FadingMenu();
    readonly ToolStripMenuItem startupItem = new ToolStripMenuItem("Start with Windows");
    readonly Font bodyFont = Typography.Text(10);
    readonly Font headingFont = Typography.Heading(20);
    bool expanded;
    int frame;
    int quietTicks;
    string signature;
    Point anchor;

    public Tray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Auxe", null, (s, e) => ShowPanel());
        menu.Items.Add("Exit", null, (s, e) => ExitThread());
        tray.Icon = frames[0];
        tray.Text = "Auxe";
        tray.ContextMenuStrip = menu;
        tray.MouseClick += (s, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (panel.Visible) HidePanel(); else ShowPanel();
        };
        tray.Visible = true;
        animation.Tick += (s, e) => Animate();
        animation.Start();
        refresh.Tick += (s, e) => RefreshRows();

        panel.Text = "Auxe";
        panel.ShowInTaskbar = false;
        panel.FormBorderStyle = FormBorderStyle.None;
        panel.StartPosition = FormStartPosition.Manual;
        panel.TopMost = true;
        panel.KeyPreview = true;
        panel.Font = bodyFont;
        panel.BackColor = Color.FromArgb(246, 246, 244);
        panel.ForeColor = Color.FromArgb(35, 37, 42);
        panel.AutoScaleMode = AutoScaleMode.None;
        panel.Deactivate += (s, e) =>
        {
            if (PickerOpen(false) || settingsMenu.Visible) return;
            HidePanel();
        };
        panel.VisibleChanged += (s, e) => { if (panel.Visible) refresh.Start(); else refresh.Stop(); };
        panel.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) HidePanel(); };
        heading.Text = "Auxe";
        heading.Font = headingFont;
        mark.Bounds = new Rectangle(20, 16, 36, 36);
        mark.BackColor = Color.FromArgb(228, 234, 246);
        mark.SizeMode = PictureBoxSizeMode.CenterImage;
        mark.Image = frames[0].ToBitmap();
        heading.SetBounds(62, 13, 252, 44);
        rows.SetBounds(20, 66, 426, 50);
        rows.FlowDirection = FlowDirection.TopDown;
        rows.WrapContents = false;
        rows.AutoScroll = true;
        status.ForeColor = Color.FromArgb(92, 98, 110);
        status.AutoEllipsis = true;
        settings.AccessibleName = "Settings";
        settings.Bounds = new Rectangle(414, 19, 32, 30);
        hints.SetToolTip(settings, "Settings");
        settingsMenu.Font = bodyFont;
        settingsMenu.Items.Add(startupItem);
        appearance.Bounds = new Rectangle(374, 19, 32, 30);
        settingsMenu.Items.Add(updatesItem);
        settingsMenu.Items.Add(downloadItem);
        settingsMenu.Items.Add(companionItem);
        settingsMenu.Renderer = new OutputPicker.PickerRenderer();
        settingsMenu.ShowImageMargin = false;
        settingsMenu.ShowCheckMargin = true;
        startupItem.Padding = updatesItem.Padding = downloadItem.Padding = companionItem.Padding = new Padding(7, 4, 12, 4);
        companionItem.Click += (sender, args) =>
        {
            var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Startup.Executable), "chrome");
            MessageBox.Show(panel,
                "1. Open chrome://extensions and turn on Developer mode.\n" +
                "2. Click Load unpacked and select this folder:\n\n" + folder +
                "\n\nIf Auxe Companion is already listed, click Reload. Then press Ctrl+Shift+U in the tab or YouTube Music window you want to route.",
                "Chrome companion setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        settingsMenu.Opening += (sender, args) =>
        {
            startupItem.Checked = Startup.Enabled;

            updatesItem.Checked = Updates.Enabled;
            downloadItem.Text = readyUpdate == null ? "Check for updates" : "Install update";
            downloadItem.Enabled = !checkingUpdate;
        };
        updatesItem.Click += (sender, args) =>
        {
            Updates.Enabled = !Updates.Enabled;
            updateTimer.Stop();
            StartUpdateTimer();
        };
        downloadItem.Click += (sender, args) =>
        {
            if (!string.Equals(Application.ExecutablePath, Startup.Executable, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Run Auxe Setup to enable updates.");
                return;
            }
            if (readyUpdate != null) ApplyUpdate(true);
            else CheckForUpdate(true);
        };
        appearance.Click += (sender, args) => ToggleAppearance();
        startupItem.Click += (sender, args) =>
        {
            try { Startup.Set(!Startup.Enabled); startupItem.Checked = Startup.Enabled; }
            catch (InvalidOperationException error)
            {
                MessageBox.Show(panel, error.Message, "Auxe settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        settings.Click += (sender, args) =>
            settingsMenu.ShowAnimated(settings);
        panel.Controls.AddRange(new Control[] { mark, heading, rows, status, settings, appearance });
        if (displayScale != 1) panel.Scale(new SizeF(displayScale, displayScale));
        using (var shape = SpeakerArt.Rounded(mark.ClientRectangle, Px(18))) mark.Region = new Region(shape);
        using (var shape = SpeakerArt.Rounded(settings.ClientRectangle, Px(16))) settings.Region = new Region(shape);
        SizePanel(1);
        panelFade.Tick += (sender, args) => AdvancePanelFade();
        appearanceTimer.Tick += (sender, args) => AdvanceAppearance();
        ApplyTheme();
        updateTimer.Tick += (sender, args) => CheckForUpdate(false);
        StartUpdateTimer();
    }

    void StartUpdateTimer()
    {
        if (!Updates.Enabled || !string.Equals(Application.ExecutablePath, Startup.Executable, StringComparison.OrdinalIgnoreCase)) return;
        updateTimer.Interval = 30000;
        updateTimer.Start();
    }

    async void CheckForUpdate(bool manual)
    {
        if (disposed || checkingUpdate || (!manual && !Updates.Enabled)) return;
        if (readyUpdate == null)
        {
            checkingUpdate = true;
            if (manual) SetStatus("Checking for updates…");
            updateTimer.Interval = 12 * 60 * 60 * 1000;
            try
            {
                var version = typeof(Tray).Assembly.GetName().Version;
                var installer = await Task.Run(() => Updates.Download(version));
                if (disposed) return;
                readyUpdate = installer;
                if (manual) SetStatus(installer == null ? "You’re up to date." : "Update ready. Open Settings to install.");
            }
            catch (Exception error)
            {
                if (manual && !disposed) SetStatus("Could not download an update. Try again later.");
                Debug.WriteLine(error);
            }
            finally { checkingUpdate = false; }
        }
        if (!manual && readyUpdate != null && Updates.Enabled) ApplyUpdate(false);
    }

    void SetStatus(string text)
    {
        status.Text = text;
    }

    void BeginRefresh()
    {
        if (panel.IsHandleCreated && !disposed) panel.BeginInvoke((Action)RefreshRows);
    }

    void ApplyUpdate(bool manual)
    {
        if (bridge.Sources().Any(source => source.Enabled) || (!manual && panel.Visible))
        {
            if (manual) SetStatus("Pause Chrome tab routing to install the update.");
            else updateTimer.Interval = 30000;
            return;
        }
        try
        {
            using (var self = Process.GetCurrentProcess())
            using (var setup = Process.Start(new ProcessStartInfo(readyUpdate, "--update " + self.Id) { UseShellExecute = false }))
            {
                if (setup == null) return;
            }
            readyUpdate = null;
            ExitThread();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            if (manual) SetStatus("Couldn’t open the installer. Try again.");
            else updateTimer.Interval = 60 * 60 * 1000;
        }
    }

    void ToggleAppearance()
    {
        EndAppearance();
        if (SystemInformation.IsMenuAnimationEnabled && panel.Visible && !panelClosing)
        {
            var snapshot = new Bitmap(panel.ClientSize.Width, panel.ClientSize.Height);
            panel.DrawToBitmap(snapshot, panel.ClientRectangle);
            oldAppearance = new PictureBox { Image = snapshot, Bounds = panel.ClientRectangle, TabStop = false };
            revealOrigin = new Point(appearance.Left + appearance.Width / 2, appearance.Top + appearance.Height / 2);
            panel.Controls.Add(oldAppearance);
            oldAppearance.BringToFront();
        }
        Theme.Toggle();
        ApplyTheme();
        if (oldAppearance == null) return;
        appearanceClock.Restart();
        appearanceTimer.Start();
    }

    void AdvanceAppearance()
    {
        double progress = Math.Min(1, appearanceClock.Elapsed.TotalMilliseconds / 210);
        if (progress >= 1 || !panel.Visible || panel.ClientSize != oldAppearance.Size)
        {
            EndAppearance();
            return;
        }
        int dx = Math.Max(revealOrigin.X, oldAppearance.Width - revealOrigin.X);
        int dy = Math.Max(revealOrigin.Y, oldAppearance.Height - revealOrigin.Y);
        float radius = (float)(Math.Sqrt(dx * dx + dy * dy) * (1 - Math.Pow(1 - progress, 3)));
        if (radius < 1) return;
        using (var hole = new GraphicsPath())
        {
            hole.AddEllipse(revealOrigin.X - radius, revealOrigin.Y - radius, 2 * radius, 2 * radius);
            var next = new Region(oldAppearance.ClientRectangle);
            next.Exclude(hole);
            var previous = oldAppearance.Region;
            oldAppearance.Region = next;
            if (previous != null) previous.Dispose();
        }
    }

    void EndAppearance()
    {
        appearanceTimer.Stop();
        if (oldAppearance == null) return;
        var image = oldAppearance.Image;
        oldAppearance.Dispose();
        image.Dispose();
        oldAppearance = null;
    }

    void ApplyTheme()
    {
        panel.BackColor = Theme.Canvas;
        panel.ForeColor = Theme.Ink;
        mark.BackColor = Theme.Mark;
        status.ForeColor = Theme.Muted;
        settings.BackColor = Theme.Card;
        settings.Invalidate();
        startupItem.ForeColor = updatesItem.ForeColor = downloadItem.ForeColor = companionItem.ForeColor = Theme.Ink;
        appearance.AccessibleName = Theme.Dark ? "Switch to Light appearance" : "Switch to Dark appearance";
        hints.SetToolTip(appearance, appearance.AccessibleName);
        appearance.Invalidate();
        foreach (var row in rows.Controls.OfType<Panel>())
        {
            row.BackColor = Theme.Card;
            foreach (var detail in row.Controls.OfType<Label>().Where(label => label.Top == 30))
                detail.ForeColor = Theme.Muted;
            row.Invalidate(true);
        }
    }

    void ShowPanel()
    {
        anchor = Cursor.Position;
        SizePanel(Math.Max(1, rows.Controls.Count));
        panel.Opacity = SystemInformation.IsMenuAnimationEnabled ? 0.12 : 1;
        panel.Show();
        AnimatePanel(false);
        RefreshRows();
        panel.Activate();
    }

    void HidePanel()
    {
        if (panel.Visible && !panelClosing)
        {
            EndAppearance();
            AnimatePanel(true);
        }
    }

    void AnimatePanel(bool close)
    {
        panelClosing = close;
        if (!SystemInformation.IsMenuAnimationEnabled)
        {
            panelFade.Stop();
            if (close) panel.Hide();
            panel.Opacity = 1;
            panelClosing = false;
            return;
        }
        fadeStart = panel.Opacity;
        panelClock.Restart();
        panelFade.Start();
    }

    void AdvancePanelFade()
    {
        double progress = Math.Min(1, panelClock.Elapsed.TotalMilliseconds / (panelClosing ? 95 : 145));
        double ease = 1 - Math.Pow(1 - progress, 3);
        panel.Opacity = fadeStart + ((panelClosing ? 0 : 1) - fadeStart) * ease;
        if (progress < 1) return;
        panelFade.Stop();
        if (panelClosing) panel.Hide();
        panel.Opacity = 1;
        panelClosing = false;
    }

    static float DisplayScale()
    {
        using (var desktop = Graphics.FromHwnd(IntPtr.Zero)) return desktop.DpiX / 96f;
    }

    int Px(int value) { return (int)Math.Round(value * displayScale); }

    void SizePanel(int rowCount)
    {
        var area = Screen.FromPoint(anchor).WorkingArea;
        int contentHeight = Math.Min(rowCount * Px(66), Math.Min(Px(396), Math.Max(Px(66), area.Height - Px(122))));
        var size = new Size(Px(466), contentHeight + Px(100));
        if (panel.ClientSize != size)
        {
            panel.ClientSize = size;
            using (var clip = SpeakerArt.Rounded(panel.ClientRectangle, Px(38)))
            {
                var old = panel.Region;
                panel.Region = new Region(clip);
                if (old != null) old.Dispose();
            }
        }
        rows.Height = contentHeight;
        status.SetBounds(Px(21), Px(68) + contentHeight, Px(423), Px(25));
        panel.Location = new Point(Math.Max(area.Left, Math.Min(anchor.X - Px(205), area.Right - panel.Width)),
            Math.Max(area.Top, Math.Min(anchor.Y, area.Bottom) - panel.Height - Px(9)));
    }

    void Animate()
    {
        bool animationsEnabled = SystemInformation.IsMenuAnimationEnabled;
        bool playing = animationsEnabled && audio.Playing();
        quietTicks = playing ? 0 : Math.Min(quietTicks + 1, 3);
        frame = animationsEnabled && quietTicks < 3 && (playing || frame != 0) ? frame % 4 + 1 : 0;
        animation.Interval = frame == 0 ? 750 : 250;
        if (tray.Icon != frames[frame]) tray.Icon = frames[frame];
    }

    bool PickerOpen(bool includeBusy = true)
    {
        return rows.Controls.OfType<Panel>().SelectMany(row => row.Controls.OfType<OutputPicker>())
            .Any(picker => picker.MenuOpen || (includeBusy && picker.Busy));
    }

    async void RefreshRows()
    {
        if (disposed || panelClosing || refreshing || routing || PickerOpen() || settingsMenu.Visible) return;
        refreshing = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                lock (audioLock)
                {
                    if (disposed) return null;
                    var core = routingAudio ?? (routingAudio = new Audio());
                    List<AudioApp> apps;
                    var outputs = core.Read(out apps, true);
                    var preferences = new Dictionary<string, string>();
                    foreach (var app in apps)
                    {
                        try { preferences[app.Key] = core.Get(app.Processes.First(), 1); }
                        catch (COMException) { }
                    }
                    return Tuple.Create(apps, outputs, preferences);
                }
            });
            if (snapshot == null || disposed || !panel.Visible || panelClosing || routing || PickerOpen() || settingsMenu.Visible) return;
            if (nativeAudioUnavailable) { nativeAudioUnavailable = false; status.Text = ""; }
            RenderRows(snapshot.Item1, snapshot.Item2, snapshot.Item3);
        }
        catch (COMException)
        {
            if (!disposed) { status.Text = "Can’t connect to Windows audio."; nativeAudioUnavailable = true; }
        }
        finally { refreshing = false; }
    }

    void RenderRows(List<AudioApp> apps, List<Output> outputs, Dictionary<string, string> preferences)
    {
        var chrome = bridge.Sources();
        foreach (var app in apps)
        {
            Tuple<string, long> pending;
            if (requested.TryGetValue(app.Key, out pending) && app.Active && app.ActiveOutputs.Count == 1 &&
                app.ActiveOutputs.Contains(pending.Item1)) requested.Remove(app.Key);
        }
        apps = apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var next = string.Join("|", apps.Select(app => app.Key + ":" + string.Join(",", app.Outputs.OrderBy(x => x)) + "/" + string.Join(",", app.ActiveOutputs.OrderBy(x => x)))) +
            string.Join("|", outputs.Select(output => output.Id + output.Name)) +
            string.Join("|", chrome.Select(source => source.Signature())) + expanded +
            string.Join("|", preferences.Select(item => item.Key + ":" + item.Value)) +
            string.Join("|", requested.Select(item => item.Key + ":" + item.Value.Item1 + ":" + (Stopwatch.GetTimestamp() >= item.Value.Item2)));
        if (next == signature) return;
        signature = next;
        var scroll = -rows.AutoScrollPosition.Y;
        var focusName = rows.Controls.OfType<Panel>().SelectMany(row => row.Controls.OfType<OutputPicker>())
            .Where(picker => picker.Focused).Select(picker => picker.AccessibleName).FirstOrDefault();
        rows.SuspendLayout();
        try
        {
            while (rows.Controls.Count != 0) rows.Controls[0].Dispose();
            var activeKeys = new HashSet<string>(apps.Select(app => app.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var old in appIcons.Keys.Where(key => !activeKeys.Contains(key)).ToArray())
            {
                if (appIcons[old] != null) appIcons[old].Dispose();
                appIcons.Remove(old);
            }
            outputs.Insert(0, new Output { Name = "System default" });
            bool chromeShown = false;
            foreach (var app in apps)
            {
                bool isChrome = app.Name == "Chrome";
                chromeShown |= isChrome;
                var outputsInUse = app.Active ? app.ActiveOutputs : app.Outputs;
                string id = outputsInUse.Count == 1 ? outputsInUse.First() : null;
                var actual = outputs.FirstOrDefault(output => output.Id == id);
                string subtitle = actual != null && id != null ? actual.Name : "Multiple outputs";
                Tuple<string, long> pending;
                if (requested.TryGetValue(app.Key, out pending))
                {
                    subtitle = Stopwatch.GetTimestamp() < pending.Item2
                            ? "Switching output…" : "Restart playback to apply";
                }
                string preference;
                if (!preferences.TryGetValue(app.Key, out preference)) preference = id;
                bool chromeOverrides = isChrome && chrome.Any(source => source.Enabled);
                AddRow(app.Name, subtitle, outputs, preference, selected => ChangeWindowsOutput(app.Key, selected.Id, chromeOverrides),
                    isChrome && chrome.Any(source => !source.Standalone), AppIcon(app.Key));
                if (isChrome && expanded) AddChromeRows(chrome.Where(source => !source.Standalone), true);
            }
            if (!chromeShown && chrome.Any(source => !source.Standalone))
            {
                AddRow("Chrome", "Browser tabs", null, null, null, true);
                if (expanded) AddChromeRows(chrome.Where(source => !source.Standalone), true);
            }
            AddChromeRows(chrome.Where(source => source.Standalone), false);
            if (rows.Controls.Count == 0)
            {
                AddRow("No audio yet", "Play audio in an app to see it here.", null, null, null, false);
            }
            SizePanel(rows.Controls.Count);
        }
        finally { rows.ResumeLayout(true); }
        if (focusName != null && (panel.ContainsFocus || Form.ActiveForm == panel))
        {
            var samePicker = rows.Controls.OfType<Panel>().SelectMany(row => row.Controls.OfType<OutputPicker>())
                .FirstOrDefault(picker => picker.AccessibleName == focusName);
            if (samePicker != null) samePicker.Focus();
        }
        rows.AutoScrollPosition = new Point(0, scroll);

    }

    async Task<bool> ChangeWindowsOutput(string key, string output, bool chromeOverrides)
    {
        if (routing) return false;
        routing = true;
        try
        {
            await Task.Run(() =>
            {
                lock (audioLock)
                {
                    if (disposed) return;
                    var core = routingAudio ?? (routingAudio = new Audio());
                    List<AudioApp> apps;
                    var available = core.Read(out apps, true);
                    if (output != null && !available.Any(device => device.Id == output))
                        throw new InvalidOperationException("That output is no longer connected.");
                    var current = apps.FirstOrDefault(app => app.Key == key);
                    if (current == null) throw new InvalidOperationException("This app closed before its output could change.");
                    core.Set(current, output);
                }
            });
            if (disposed) return false;
            if (output != null && !chromeOverrides) requested[key] = Tuple.Create(output, Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3);
            else requested.Remove(key);
            status.Text = chromeOverrides ? "Chrome output saved. Tab routes stay active."
                : output == null ? "New playback will use the Windows default." : "";
            return true;
        }
        finally { routing = false; }
    }

    void AddChromeRows(IEnumerable<ChromeSource> sources, bool child)
    {
        foreach (var source in sources)
        {
            AddRow(source.Name,
                source.Status ?? (source.Enabled ? "Chrome playback" : "Press Ctrl+Shift+U to enable routing"),
                source.Enabled ? source.Outputs : null, source.Selected,
                                async selected =>
                {
                    await bridge.Route(source, selected.Id);
                    return false; // The Chrome snapshot confirms selection.
                }, false, null, source.Standalone);
        }
    }

    Bitmap AppIcon(string path)
    {
        Bitmap result;
        if (appIcons.TryGetValue(path, out result)) return result;
        if (path.Length > 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] == '\\' &&
            string.Equals(System.IO.Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) &&
            System.IO.File.Exists(path))
        {
            try
            {
                result = PackageIcon(path);
                if (result == null)
                    using (var icon = Icon.ExtractAssociatedIcon(path))
                        if (icon != null) result = icon.ToBitmap();
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        appIcons[path] = result;
        return result;
    }

    static Bitmap PackageIcon(string executable)
    {
        string directory = Path.GetDirectoryName(executable);
        string manifest = Path.Combine(directory, "AppxManifest.xml");
        if (!File.Exists(manifest)) return null;
        try
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            using (var reader = System.Xml.XmlReader.Create(manifest,
                new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit }))
                document.Load(reader);
            var application = document.GetElementsByTagName("Application").OfType<System.Xml.XmlElement>()
                .FirstOrDefault(element => string.Equals(element.GetAttribute("Executable"),
                    Path.GetFileName(executable), StringComparison.OrdinalIgnoreCase));
            if (application == null) return null;
            var visual = application.ChildNodes.OfType<System.Xml.XmlElement>()
                .FirstOrDefault(element => element.LocalName == "VisualElements");
            if (visual == null) return null;
            string logo = visual.GetAttribute("Square44x44Logo");
            if (string.IsNullOrWhiteSpace(logo) || Path.IsPathRooted(logo)) return null;
            string image = Path.GetFullPath(Path.Combine(directory, logo));
            if (!image.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(image), ".png", StringComparison.OrdinalIgnoreCase)) return null;
            string stem = image.Substring(0, image.Length - 4);
            string file = new[] { stem + ".targetsize-32_altform-unplated.png", stem + ".targetsize-32.png",
                image, stem + ".scale-200.png" }.FirstOrDefault(File.Exists);
            if (file == null) return null;
            using (var input = File.OpenRead(file))
            using (var bitmap = new Bitmap(input)) return new Bitmap(bitmap);
        }
        catch (System.Xml.XmlException) { return null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (ArgumentException) { return null; }
    }

    void AddRow(string name, string subtitle, List<Output> outputs, string selected, Func<Output, Task<bool>> change,
        bool expander, Bitmap appIcon = null, bool music = false)
    {
        var row = new Panel { Width = 409, Height = 60, Margin = new Padding(0, 0, 0, 6), BackColor = Theme.Card };
        const int textLeft = 46;
        int textWidth = (outputs == null ? 373 : 199) - textLeft;
        var title = new CrispLabel { Text = name, UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Font = rowFont, Bounds = new Rectangle(textLeft, 9, textWidth, 22) };
        var detail = new CrispLabel { Text = subtitle, UseMnemonic = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
            Font = detailFont, ForeColor = Theme.Muted, Bounds = new Rectangle(textLeft, 30, textWidth, 20) };
        if (outputs != null || appIcon != null || music) row.Controls.Add(new AppBadge(appIcon, music) { Bounds = new Rectangle(13, 18, 24, 24) });
        hints.SetToolTip(title, name.Trim());
        hints.SetToolTip(detail, subtitle);
        row.Controls.AddRange(new Control[] { title, detail });
        if (outputs != null)
        {
            var picker = new OutputPicker(outputs, selected, async output =>
            {
                try { return await change(output); }
                catch (COMException) { status.Text = "Windows could not change the output."; signature = null; }
                catch (InvalidOperationException error) { status.Text = error.Message; signature = null; }
                catch (System.IO.IOException) { status.Text = "Chrome disconnected. Re-enable the tab."; signature = null; }
                return false;
            })
            {
                AccessibleName = name + " output",
                Bounds = new Rectangle(205, 12, expander ? 155 : 190, 36)
            };
            hints.SetToolTip(picker, picker.Text);
            picker.TextChanged += (sender, args) => hints.SetToolTip(picker, picker.Text);
            row.Controls.Add(picker);
        }
        if (expander)
        {
            var button = new Button { Text = expanded ? "⌃" : "⌄", AccessibleName = expanded ? "Collapse Chrome tabs" : "Expand Chrome tabs",
                Bounds = new Rectangle(367, 15, 30, 28), FlatStyle = FlatStyle.Flat };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) => { expanded = !expanded; RefreshRows(); };
            row.Controls.Add(button);
        }
        if (displayScale != 1) row.Scale(new SizeF(displayScale, displayScale));
        using (var clip = SpeakerArt.Rounded(row.ClientRectangle, Px(20))) row.Region = new Region(clip);
        rows.Controls.Add(row);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            EndAppearance(); appearanceTimer.Dispose();
            refresh.Dispose(); animation.Dispose(); panelFade.Dispose(); updateTimer.Dispose(); tray.Visible = false;
            tray.ContextMenuStrip.Dispose(); tray.Dispose(); mark.Image.Dispose(); panel.Dispose();
            disposed = true;
            lock (audioLock) { if (routingAudio != null) routingAudio.Dispose(); }
            hints.Dispose(); settingsMenu.Dispose(); bridge.Dispose(); audio.Dispose();
            rowFont.Dispose(); detailFont.Dispose(); bodyFont.Dispose(); headingFont.Dispose();
            foreach (var image in appIcons.Values) if (image != null) image.Dispose();
            foreach (var icon in frames) icon.Dispose();
        }
        base.Dispose(disposing);
    }

}

sealed class OutputPicker : Button
{
    static Color Surface { get { return Theme.Surface; } }
    static Color Highlight { get { return Theme.Highlight; } }
    static Color Ink { get { return Theme.Ink; } }
    static Color Accent { get { return Theme.Accent; } }
    readonly FadingMenu menu;
    readonly Font menuFont = Typography.Text(10);
    bool hover;
    bool opening;
    bool busy;
ToolStripMenuItem current;

    public bool MenuOpen { get { return opening || menu.Visible; } }
    public bool Busy { get { return busy; } }

    public OutputPicker(List<Output> outputs, string selected, Func<Output, Task<bool>> commit)
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        UseMnemonic = false;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ButtonMenu;
        menu = new FadingMenu
        {
            Renderer = new PickerRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            Padding = new Padding(5),
            Font = menuFont,
            BackColor = Surface,
            ForeColor = Ink
        };

        foreach (var output in outputs)
        {
            var item = new ToolStripMenuItem(output.Name.Replace("&", "&&"))
            {
                ForeColor = Ink,
                Padding = new Padding(9, 5, 12, 5)
            };
            item.Click += async (sender, args) =>
            {
                if (busy) return;
                busy = true;
                Invalidate();
                bool changed;
                try { changed = await commit(output); }
                finally { busy = false; if (!IsDisposed) Invalidate(); }
                if (IsDisposed || !changed) return;
                current.Checked = false;
                current = item;
                current.Checked = true;
                Text = output.Name;
                Invalidate();
            };
            menu.Items.Add(item);
            if (output.Id == selected) current = item;
        }
        current = current ?? (ToolStripMenuItem)menu.Items[0];
        current.Checked = true;
        Text = outputs[menu.Items.IndexOf(current)].Name;
        menu.Closed += (sender, args) => Invalidate();
    }

    protected override void OnClick(EventArgs args)
    {
        base.OnClick(args);
        if (busy) return;
        if (menu.Visible) { menu.Close(); return; }
        opening = true;
        try
        {
            menu.ShowAnimated(this);
            current.Select();
        }
        finally { opening = false; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs args) { base.OnMouseEnter(args); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs args) { base.OnMouseLeave(args); hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs args) { base.OnGotFocus(args); Invalidate(); }
    protected override void OnLostFocus(EventArgs args) { base.OnLostFocus(args); Invalidate(); }

    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.Clear(Parent.BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float scale = Height / 36f;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = SpeakerArt.Rounded(bounds, (int)Math.Round(16 * scale)))
        using (var fill = new SolidBrush(hover || MenuOpen ? Highlight : Surface))
        using (var border = new Pen(Focused || MenuOpen ? Accent : Theme.Border))
        using (var chevron = new Pen(Accent, 1.5f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            using (var brush = new SolidBrush(Ink))
            using (var format = new StringFormat
            {
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                graphics.DrawString(busy ? "Switching…" : Text, Font, brush,
                    new RectangleF(10 * scale, 0, Width - 33 * scale, Height), format);
            }
            float x = Width - 16 * scale;
            float y = Height / 2f;
            int direction = menu.Visible ? -1 : 1;
            float rise = 2 * scale * direction;
            graphics.DrawLines(chevron, new[] { new PointF(x - 4 * scale, y - rise), new PointF(x, y + rise), new PointF(x + 4 * scale, y - rise) });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { if (menu != null) menu.Dispose(); menuFont.Dispose(); }
        base.Dispose(disposing);
    }

    internal sealed class PickerRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs args)
        {
            args.Graphics.Clear(Surface);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs args)
        {
            var item = (ToolStripMenuItem)args.Item;
            if (!item.Selected && !item.Checked) return;
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = SpeakerArt.Rounded(new Rectangle(0, 0, item.Width, item.Height), 12))
            using (var fill = new SolidBrush(item.Selected ? Highlight : Surface))
                args.Graphics.FillPath(fill, path);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs args)
        {
            var rect = args.ImageRectangle;
            float x = rect.Left + rect.Width / 2f;
            float y = rect.Top + rect.Height / 2f;
            float size = Math.Max(4, rect.Width / 4f);
            args.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Accent, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                args.Graphics.DrawLines(pen, new[] { new PointF(x - size, y), new PointF(x - size / 3, y + size * 0.55f), new PointF(x + size, y - size * 0.7f) });
        }
    }
}

sealed class CrispLabel : Label
{
    protected override void OnPaint(PaintEventArgs args)
    {
        args.Graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        base.OnPaint(args);
    }
}

sealed class FadingMenu : ContextMenuStrip
{
    readonly Timer fade = new Timer { Interval = 15 };
    readonly Stopwatch clock = new Stopwatch();
    double fadeStart;
    bool closing;
    bool finishClose;

    public FadingMenu()
    {
        fade.Tick += (sender, args) =>
        {
            double progress = Math.Min(1, clock.Elapsed.TotalMilliseconds / (closing ? 90 : 125));
            double ease = 1 - Math.Pow(1 - progress, 3);
            Opacity = fadeStart + ((closing ? 0 : 1) - fadeStart) * ease;
            if (progress < 1) return;
            fade.Stop();
            if (closing)
            {
                finishClose = true;
                try { Close(); }
                finally { finishClose = false; closing = false; }
            }
        };
    }

    public void ShowAnimated(Control anchor)
    {
        bool animate = SystemInformation.IsMenuAnimationEnabled;
        Opacity = animate ? 0.12 : 1;
        Show(anchor, new Point(anchor.Width, anchor.Height + 4), ToolStripDropDownDirection.BelowLeft);
        if (animate && Visible) StartFade(false);
    }

    void StartFade(bool close)
    {
        closing = close;
        fadeStart = Opacity;
        clock.Restart();
        fade.Start();
    }

    protected override void OnSizeChanged(EventArgs args)
    {
        base.OnSizeChanged(args);
        if (Width < 14 || Height < 14) return;
        using (var path = SpeakerArt.Rounded(ClientRectangle, 14))
        {
            var previous = Region;
            Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
    }

    protected override void OnClosing(ToolStripDropDownClosingEventArgs args)
    {
        base.OnClosing(args);
        if (args.Cancel || finishClose || !SystemInformation.IsMenuAnimationEnabled ||
            (args.CloseReason != ToolStripDropDownCloseReason.ItemClicked &&
             args.CloseReason != ToolStripDropDownCloseReason.Keyboard &&
             args.CloseReason != ToolStripDropDownCloseReason.CloseCalled)) return;
        args.Cancel = true;
        if (!closing) StartFade(true);
    }

    protected override void OnClosed(ToolStripDropDownClosedEventArgs args)
    {
        fade.Stop();
        closing = false;
        base.OnClosed(args);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) fade.Dispose();
        base.Dispose(disposing);
    }
}

sealed class HeaderButton : Button
{
    readonly bool gear;
    bool hover;

    public HeaderButton(bool gear)
    {
        this.gear = gear;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs args) { base.OnMouseEnter(args); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs args) { base.OnMouseLeave(args); hover = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs args) { base.OnGotFocus(args); Invalidate(); }
    protected override void OnLostFocus(EventArgs args) { base.OnLostFocus(args); Invalidate(); }

    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.Clear(Theme.Canvas);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shape = SpeakerArt.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Math.Max(10, Height / 2)))
        using (var fill = new SolidBrush(hover ? Theme.Highlight : Theme.Card))
        using (var pen = new Pen(Theme.Ink, 1.35f))
        using (var focus = new Pen(Theme.Accent))
        {
            graphics.FillPath(fill, shape);
            if (Focused) graphics.DrawPath(focus, shape);
            graphics.TranslateTransform(Width / 2f, Height / 2f);
            graphics.ScaleTransform(Height / 30f, Height / 30f);
            if (gear)
            {
                var teeth = new PointF[32];
                for (int index = 0; index < teeth.Length; index++)
                {
                    double angle = Math.PI * index / 16;
                    double radius = index % 4 == 1 || index % 4 == 2 ? 8 : 6.1;
                    teeth[index] = new PointF((float)(Math.Cos(angle) * radius), (float)(Math.Sin(angle) * radius));
                }
                graphics.DrawPolygon(pen, teeth);
                graphics.DrawEllipse(pen, -2.4f, -2.4f, 4.8f, 4.8f);
            }
            else if (Theme.Dark)
            {
                graphics.DrawEllipse(pen, -3.5f, -3.5f, 7, 7);
                for (int ray = 0; ray < 8; ray++)
                {
                    graphics.DrawLine(pen, 0, -6, 0, -8);
                    graphics.RotateTransform(45);
                }
            }
            else
            {
                using (var crescent = new GraphicsPath())
                {
                    crescent.AddBezier(2, -7, -7, -8, -10, 4, -2, 7);
                    crescent.AddBezier(-2, 7, 2, 9, 6, 6, 7, 3);
                    crescent.AddBezier(7, 3, 0, 5, -3, -2, 2, -7);
                    crescent.CloseFigure();
                    graphics.DrawPath(pen, crescent);
                }
            }
        }
    }
}

sealed class AppBadge : Control
{
    readonly Bitmap icon;
    readonly bool music;

    public AppBadge(Bitmap icon, bool music)
    {
        this.icon = icon;
        this.music = music;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.Clear(Theme.Card);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (icon != null)
        {
            graphics.DrawImage(icon, new Rectangle(1, 1, Width - 2, Height - 2));
            return;
        }

        float unit = Width / 24f;
        if (!music)
        {
            graphics.ScaleTransform(unit, unit);
            using (var pen = new Pen(Theme.Muted, 1.2f))
            using (var outline = SpeakerArt.Rounded(new Rectangle(4, 5, 16, 14), 4))
            {
                graphics.DrawPath(pen, outline);
                graphics.DrawLine(pen, 4, 9, 20, 9);
            }
            return;
        }
        using (var red = new SolidBrush(Color.FromArgb(218, 49, 76)))
        using (var white = new Pen(Color.White, 1.6f * unit))
        {
            graphics.FillEllipse(red, 2 * unit, 2 * unit, 20 * unit, 20 * unit);
            graphics.DrawLines(white, new[] { new PointF(11 * unit, 16 * unit), new PointF(11 * unit, 8 * unit), new PointF(16 * unit, 9 * unit) });
            graphics.DrawEllipse(white, 8 * unit, 14 * unit, 3 * unit, 3 * unit);
        }
    }
}
