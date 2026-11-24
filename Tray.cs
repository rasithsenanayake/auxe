using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    readonly Label heading = new Label();
    readonly PictureBox mark = new PictureBox();
    Button settings;
    readonly ToolStripMenuItem lightItem = new ToolStripMenuItem("Light appearance");
    readonly Timer themeFade = new Timer { Interval = 15 };
    readonly Timer updateTimer = new Timer { Interval = 30000 };
    readonly ToolStripMenuItem updatesItem = new ToolStripMenuItem("Automatic updates");
    readonly ToolStripMenuItem downloadItem = new ToolStripMenuItem("Check for updates");
    bool checkingUpdate;
    string readyUpdate;
    readonly Font rowFont = Typography.Heading(10);
    readonly Label status = new Label();
    readonly FlowLayoutPanel rows = new FlowLayoutPanel();
    readonly Dictionary<string, Tuple<string, long>> requested = new Dictionary<string, Tuple<string, long>>();
    readonly object audioLock = new object();
    Audio routingAudio;
    bool refreshing;
    bool routing;
    bool disposed;
    bool nativeAudioUnavailable;
    readonly ContextMenuStrip settingsMenu = new ContextMenuStrip();
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
            if (panel.Visible) panel.Hide(); else ShowPanel();
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
            panel.Hide();
        };
        panel.VisibleChanged += (s, e) => { if (panel.Visible) refresh.Start(); else refresh.Stop(); };
        panel.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) panel.Hide(); };
        heading.Text = "Auxe";
        heading.Font = headingFont;
        mark.Bounds = new Rectangle(20, 16, 36, 36);
        mark.BackColor = Color.FromArgb(228, 234, 246);
        mark.SizeMode = PictureBoxSizeMode.CenterImage;
        mark.Image = frames[0].ToBitmap();
        using (var shape = SpeakerArt.Rounded(mark.ClientRectangle, 18)) mark.Region = new Region(shape);
        heading.SetBounds(62, 13, 330, 44);
        rows.SetBounds(20, 66, 398, 50);
        rows.FlowDirection = FlowDirection.TopDown;
        rows.WrapContents = false;
        rows.AutoScroll = true;
        status.ForeColor = Color.FromArgb(92, 98, 110);
        status.AutoEllipsis = true;
        settings = new Button
        {
            Text = "Settings", AccessibleName = "Auxe settings", Font = bodyFont,
            Bounds = new Rectangle(342, 19, 76, 30), FlatStyle = FlatStyle.Flat,
            BackColor = Color.White, Cursor = Cursors.Hand
        };
        settings.FlatAppearance.BorderSize = 0;
        using (var shape = SpeakerArt.Rounded(settings.ClientRectangle, 16)) settings.Region = new Region(shape);
        settingsMenu.Font = bodyFont;
        settingsMenu.Items.Add(startupItem);
        settingsMenu.Items.Add(lightItem);
        settingsMenu.Items.Add(updatesItem);
        settingsMenu.Items.Add(downloadItem);
        settingsMenu.Renderer = new OutputPicker.PickerRenderer();
        settingsMenu.ShowImageMargin = false; settingsMenu.ShowCheckMargin = false;
        startupItem.Padding = lightItem.Padding = updatesItem.Padding = downloadItem.Padding = new Padding(7, 4, 23, 4);
        settingsMenu.Opening += (sender, args) =>
        {
            startupItem.Checked = Startup.Enabled;
            lightItem.Checked = !Theme.Dark;
            updatesItem.Checked = Updates.Enabled;
            downloadItem.Text = readyUpdate == null ? "Check for updates" : "Install downloaded update";
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
                SetStatus("Install Auxe to receive updates.");
                return;
            }
            if (readyUpdate != null) ApplyUpdate(true);
            else CheckForUpdate(true);
        };
        lightItem.Click += (sender, args) => { Theme.Toggle(); ApplyTheme(); signature = null; BeginRefresh(); };
        startupItem.Click += (sender, args) =>
        {
            try { Startup.Set(!Startup.Enabled); startupItem.Checked = Startup.Enabled; }
            catch (InvalidOperationException error)
            {
                MessageBox.Show(panel, error.Message, "Auxe settings", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        };
        settings.Click += (sender, args) =>
            settingsMenu.Show(settings, new Point(settings.Width, settings.Height + 4), ToolStripDropDownDirection.BelowLeft);
        panel.Controls.AddRange(new Control[] { mark, heading, rows, status, settings });
        themeFade.Tick += (sender, args) => { panel.Opacity = Math.Min(1, panel.Opacity + 0.075); if (panel.Opacity >= 1) themeFade.Stop(); };
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
            if (manual) SetStatus("Checking GitHub for an update…");
            updateTimer.Interval = 12 * 60 * 60 * 1000;
            try
            {
                var version = typeof(Tray).Assembly.GetName().Version;
                var installer = await Task.Run(() => Updates.Download(version));
                if (disposed) return;
                readyUpdate = installer;
                if (manual) SetStatus(installer == null ? "Auxe is up to date." : "Update downloaded. Install it from Settings.");
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
        signature = null;
        if (panel.Visible) BeginRefresh();
    }

    void BeginRefresh()
    {
        if (panel.IsHandleCreated && !disposed) panel.BeginInvoke((Action)RefreshRows);
    }

    void ApplyUpdate(bool manual)
    {
        if (bridge.Sources().Any(source => source.Enabled) || (!manual && panel.Visible))
        {
            if (manual) SetStatus("Turn off Chrome tab routing before updating.");
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
            if (manual) SetStatus("Windows could not open the update installer.");
            else updateTimer.Interval = 60 * 60 * 1000;
        }
    }

    void ApplyTheme()
    {
        panel.BackColor = Theme.Canvas;
        panel.ForeColor = Theme.Ink;
        mark.BackColor = Theme.Mark;
        status.ForeColor = Theme.Muted;
        settings.BackColor = Theme.Card;
        startupItem.ForeColor = lightItem.ForeColor = updatesItem.ForeColor = downloadItem.ForeColor = Theme.Ink;
        foreach (var row in rows.Controls.OfType<Panel>())
        {
            row.BackColor = Theme.Card;
            foreach (var detail in row.Controls.OfType<Label>().Where(label => label.Top == 30))
                detail.ForeColor = Theme.Muted;
            row.Invalidate(true);
        }
        if (panel.Visible && SystemInformation.IsMenuAnimationEnabled)
        {
            panel.Opacity = 0.86;
            themeFade.Start();
        }
    }

    void ShowPanel()
    {
        anchor = Cursor.Position;
        signature = null;
        var area = Screen.FromPoint(anchor).WorkingArea;
        panel.Location = new Point(Math.Max(area.Left, Math.Min(anchor.X - 205, area.Right - panel.Width)), Math.Max(area.Top, area.Bottom - panel.Height - 9));
        panel.Show();
        RefreshRows();
        panel.Activate();
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
        if (disposed || refreshing || routing || PickerOpen() || settingsMenu.Visible) return;
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
            if (snapshot == null || disposed || !panel.Visible || routing || PickerOpen() || settingsMenu.Visible) return;
            if (nativeAudioUnavailable) { nativeAudioUnavailable = false; status.Text = ""; }
            RenderRows(snapshot.Item1, snapshot.Item2, snapshot.Item3);
        }
        catch (COMException)
        {
            if (!disposed) { status.Text = "Windows audio is unavailable."; nativeAudioUnavailable = true; }
        }
        finally { refreshing = false; }
    }

    void RenderRows(List<AudioApp> apps, List<Output> outputs, Dictionary<string, string> preferences)
    {
        var chrome = bridge.Sources();
        var next = string.Join("|", apps.Select(app => app.Key + ":" + string.Join(",", app.Outputs.OrderBy(x => x)) + "/" + string.Join(",", app.ActiveOutputs.OrderBy(x => x)))) +
            string.Join("|", outputs.Select(output => output.Id + output.Name)) +
            string.Join("|", chrome.Select(source => source.Signature())) + expanded + status.Text +
            string.Join("|", preferences.Select(item => item.Key + ":" + item.Value)) +
            string.Join("|", requested.Select(item => item.Key + ":" + (Stopwatch.GetTimestamp() >= item.Value.Item2)));
        if (next == signature) return;
        signature = next;
        var scroll = -rows.AutoScrollPosition.Y;
        rows.SuspendLayout();
        try
        {
            while (rows.Controls.Count != 0) rows.Controls[0].Dispose();
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
                    if (app.Active && app.ActiveOutputs.SetEquals(new[] { pending.Item1 }))
                        requested.Remove(app.Key);
                    else
                        subtitle = Stopwatch.GetTimestamp() < pending.Item2
                            ? "Switching output…" : "Restart playback to switch";
                }
                string preference;
                if (!preferences.TryGetValue(app.Key, out preference)) preference = id;
                bool chromeOverrides = isChrome && chrome.Any(source => source.Enabled);
                AddRow(app.Name, subtitle, outputs, preference, selected => ChangeWindowsOutput(app.Key, selected.Id, chromeOverrides),
                    isChrome && chrome.Any(source => !source.Standalone));
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
                AddRow("No apps playing", "Start playback to choose an output.", null, null, null, false);
            }
            var area = Screen.FromPoint(anchor).WorkingArea;
            int contentHeight = Math.Min(Math.Max(60, rows.Controls.Count * 66), Math.Min(396, Math.Max(64, area.Height - 112)));
            panel.ClientSize = new Size(438, contentHeight + (string.IsNullOrEmpty(status.Text) ? 82 : 106));
            rows.Height = contentHeight;
            status.SetBounds(21, 68 + contentHeight, 395, 30);
            panel.Location = new Point(Math.Max(area.Left, Math.Min(anchor.X - 205, area.Right - panel.Width)),
                Math.Max(area.Top, Math.Min(anchor.Y, area.Bottom) - panel.Height - 9));
            using (var clip = SpeakerArt.Rounded(new Rectangle(0, 0, panel.Width, panel.Height), 38))
            {
                var old = panel.Region;
                panel.Region = new Region(clip);
                if (old != null) old.Dispose();
            }
            rows.AutoScrollPosition = new Point(0, scroll);
        }
        finally { rows.ResumeLayout(); }
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
            status.Text = chromeOverrides ? "Chrome output saved. Tab overrides stay active."
                : output == null ? "Using Windows defaults for new playback." : "";
            return true;
        }
        finally { routing = false; signature = null; }
    }

    void AddChromeRows(IEnumerable<ChromeSource> sources, bool child)
    {
        foreach (var source in sources)
        {
            AddRow((child ? "   " : "") + source.Name,
                source.Status ?? (source.Enabled ? "Chrome audio" : "Enable in Chrome with Ctrl+Shift+U"),
                source.Enabled ? source.Outputs : null, source.Selected,
                                async selected =>
                {
                    await bridge.Route(source, selected.Id);
                    return false; // The Chrome snapshot confirms selection.
                }, false);
        }
    }

    void AddRow(string name, string subtitle, List<Output> outputs, string selected, Func<Output, Task<bool>> change, bool expander)
    {
        var row = new Panel { Width = 381, Height = 60, Margin = new Padding(0, 0, 0, 6), BackColor = Theme.Card };
        using (var clip = SpeakerArt.Rounded(row.ClientRectangle, 20)) row.Region = new Region(clip);
        var title = new Label { Text = name, UseMnemonic = false, AutoEllipsis = true, Font = rowFont, Bounds = new Rectangle(15, 9, outputs == null ? 310 : 160, 22) };
        var detail = new Label { Text = subtitle, UseMnemonic = false, AutoEllipsis = true, ForeColor = Theme.Muted, Bounds = new Rectangle(15, 30, outputs == null ? 310 : 160, 22) };
        row.Controls.AddRange(new Control[] { title, detail });
        if (outputs != null)
        {
            var picker = new OutputPicker(outputs, selected, async output =>
            {
                try { return await change(output); }
                catch (COMException) { status.Text = "Windows could not change the output."; signature = null; }
                catch (InvalidOperationException error) { status.Text = error.Message; signature = null; }
                catch (System.IO.IOException) { status.Text = "Chrome disconnected. Re-enable this tab in Chrome."; signature = null; }
                return false;
            })
            {
                AccessibleName = name + " output",
                Bounds = new Rectangle(177, 12, expander ? 155 : 190, 36)
            };
            row.Controls.Add(picker);
        }
        if (expander)
        {
            var button = new Button { Text = expanded ? "⌃" : "⌄", AccessibleName = expanded ? "Collapse Chrome tabs" : "Expand Chrome tabs",
                Bounds = new Rectangle(339, 15, 30, 28), FlatStyle = FlatStyle.Flat };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) => { expanded = !expanded; RefreshRows(); };
            row.Controls.Add(button);
        }
        rows.Controls.Add(row);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refresh.Dispose(); animation.Dispose(); themeFade.Dispose(); updateTimer.Dispose(); tray.Visible = false;
            tray.ContextMenuStrip.Dispose(); tray.Dispose(); mark.Image.Dispose(); panel.Dispose();
            disposed = true;
            lock (audioLock) { if (routingAudio != null) routingAudio.Dispose(); }
            settingsMenu.Dispose(); bridge.Dispose(); audio.Dispose(); rowFont.Dispose(); bodyFont.Dispose(); headingFont.Dispose();
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
    readonly ContextMenuStrip menu;
    readonly Font menuFont = Typography.Text(10);
    bool hover;
    bool opening;
    bool busy;
    readonly Timer fade = new Timer { Interval = 15 };
    readonly Stopwatch fadeClock = new Stopwatch();
    bool closing;
    bool finishClose;
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
        menu = new ContextMenuStrip
        {
            Renderer = new PickerRenderer(),
            ShowImageMargin = false,
            ShowCheckMargin = false,
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
                Padding = new Padding(9, 5, 26, 5)
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
        menu.SizeChanged += (sender, args) =>
        {
            if (menu.Width < 14 || menu.Height < 14) return;
            using (var path = SpeakerArt.Rounded(menu.ClientRectangle, 14))
            {
                var previous = menu.Region;
                menu.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        };
        fade.Tick += (sender, args) =>
        {
            double progress = Math.Min(1, fadeClock.Elapsed.TotalMilliseconds / (closing ? 90 : 125));
            menu.Opacity = closing ? 1 - progress : 1 - Math.Pow(1 - progress, 3);
            if (progress < 1) return;
            fade.Stop();
            if (closing)
            {
                finishClose = true;
                try { menu.Close(); }
                finally { finishClose = false; closing = false; }
            }
        };
        menu.Closing += (sender, args) =>
        {
            if (finishClose || !SystemInformation.IsMenuAnimationEnabled ||
                (args.CloseReason != ToolStripDropDownCloseReason.ItemClicked &&
                 args.CloseReason != ToolStripDropDownCloseReason.Keyboard &&
                 args.CloseReason != ToolStripDropDownCloseReason.CloseCalled)) return;
            args.Cancel = true;
            if (closing) return;
            closing = true;
            fadeClock.Restart();
            fade.Start();
        };
        menu.Closed += (sender, args) =>
        {
            fade.Stop();
            closing = false;
            Invalidate();
        };
    }

    protected override void OnClick(EventArgs args)
    {
        base.OnClick(args);
        if (busy) return;
        if (menu.Visible) { menu.Close(); return; }
        opening = true;
        try
        {
            bool animate = SystemInformation.IsMenuAnimationEnabled;
            menu.Opacity = animate ? 0.10 : 1;
            menu.Show(this, new Point(Width, Height + 4), ToolStripDropDownDirection.BelowLeft);
            if (animate && menu.Visible)
            {
                fadeClock.Restart();
                fade.Start();
            }
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
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = SpeakerArt.Rounded(bounds, 16))
        using (var fill = new SolidBrush(hover || MenuOpen ? Highlight : Surface))
        using (var border = new Pen(Focused || MenuOpen ? Accent : Theme.Border))
        using (var chevron = new Pen(Accent, 1.5f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(11, 0, Width - 35, Height), Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            var x = Width - 17;
            var y = Height / 2;
            int direction = menu.Visible ? -1 : 1;
            graphics.DrawLines(chevron, new[] { new Point(x - 4, y - 2 * direction), new Point(x, y + 2 * direction), new Point(x + 4, y - 2 * direction) });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { fade.Dispose(); if (menu != null) menu.Dispose(); menuFont.Dispose(); }
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
            if (item.Checked)
            {
                var x = item.Width - 15;
                var y = item.Height / 2;
                using (var pen = new Pen(Accent, 1.7f))
                    args.Graphics.DrawLines(pen, new[] { new Point(x - 5, y), new Point(x - 2, y + 3), new Point(x + 4, y - 4) });
            }
        }
    }
}
