using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
            Application.SetCompatibleTextRenderingDefault(false);
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
    readonly Icon[] frames = Enumerable.Range(0, 5).Select(DrawSpeaker).ToArray();
    readonly Form panel = new Form();
    readonly Label heading = new Label();
    readonly Font rowFont = new Font("Segoe UI Semibold", 10);
    readonly Label status = new Label();
    readonly FlowLayoutPanel rows = new FlowLayoutPanel();
    readonly Dictionary<string, string> requested = new Dictionary<string, string>();
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
        panel.Font = new Font("Segoe UI", 10);
        panel.BackColor = Color.FromArgb(25, 29, 38);
        panel.ForeColor = Color.FromArgb(239, 242, 249);
        panel.AutoScaleMode = AutoScaleMode.None;
        panel.Deactivate += (s, e) =>
        {
            if (rows.Controls.OfType<Panel>().SelectMany(row => row.Controls.OfType<ComboBox>()).Any(combo => combo.DroppedDown)) return;
            panel.Hide();
        };
        panel.VisibleChanged += (s, e) => { if (panel.Visible) refresh.Start(); else refresh.Stop(); };
        panel.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) panel.Hide(); };
        heading.Text = "Auxe";
        heading.Font = new Font("Segoe UI Semibold", 16);
        heading.SetBounds(16, 11, 370, 36);
        rows.SetBounds(16, 52, 388, 50);
        rows.FlowDirection = FlowDirection.TopDown;
        rows.WrapContents = false;
        rows.AutoScroll = true;
        status.ForeColor = Color.FromArgb(174, 190, 218);
        status.AutoEllipsis = true;
        panel.Controls.AddRange(new Control[] { heading, rows, status });
    }

    void ShowPanel()
    {
        anchor = Cursor.Position;
        signature = null;
        RefreshRows();
        panel.Show();
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

    void RefreshRows()
    {
        if (rows.Controls.OfType<Panel>().SelectMany(row => row.Controls.OfType<ComboBox>()).Any(combo => combo.DroppedDown)) return;
        List<AudioApp> apps;
        List<Output> outputs;
        try { outputs = audio.Read(out apps); }
        catch (COMException) { status.Text = "Windows audio is unavailable."; return; }
        var chrome = bridge.Sources();
        var next = string.Join("|", apps.Select(app => app.Key + ":" + string.Join(",", app.Outputs.OrderBy(x => x)))) +
            string.Join("|", outputs.Select(output => output.Id + output.Name)) +
            string.Join("|", chrome.Select(source => source.Signature())) + expanded;
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
                string id = app.Outputs.Count == 1 ? app.Outputs.First() : null;
                var actual = outputs.FirstOrDefault(output => output.Id == id);
                string pending;
                string subtitle = actual != null && id != null ? actual.Name : "Multiple outputs";
                if (requested.TryGetValue(app.Key, out pending))
                {
                    if (pending != null && (id != pending)) subtitle = "Playing on " + subtitle + " · restart if needed";
                    else requested.Remove(app.Key);
                }
                string preference = id;
                try { preference = audio.Get(app.Processes.First(), 1); }
                catch (COMException) { }
                AddRow(app.Name, subtitle, outputs, preference, selected =>
                {
                    List<AudioApp> fresh;
                    var available = audio.Read(out fresh);
                    if (selected.Id != null && !available.Any(output => output.Id == selected.Id))
                        throw new InvalidOperationException("That output is no longer connected.");
                    var current = fresh.FirstOrDefault(item => item.Key == app.Key);
                    if (current == null) throw new InvalidOperationException("This app is no longer playing.");
                    audio.Set(current, selected.Id);
                    requested[app.Key] = selected.Id;
                    status.Text = "Output preference saved. Restart playback if needed.";
                    signature = null;
                }, isChrome && chrome.Any(source => !source.Standalone));
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
            int contentHeight = Math.Min(Math.Max(60, rows.Controls.Count * 64), Math.Min(384, Math.Max(64, area.Height - 112)));
            panel.ClientSize = new Size(420, contentHeight + 94);
            rows.Height = contentHeight;
            status.SetBounds(19, 60 + contentHeight, 380, 30);
            panel.Location = new Point(Math.Max(area.Left, Math.Min(anchor.X - 205, area.Right - panel.Width)),
                Math.Max(area.Top, Math.Min(anchor.Y, area.Bottom) - panel.Height - 9));
            using (var clip = Rounded(new Rectangle(0, 0, panel.Width, panel.Height), 26))
            {
                var old = panel.Region;
                panel.Region = new Region(clip);
                if (old != null) old.Dispose();
            }
            rows.AutoScrollPosition = new Point(0, scroll);
        }
        finally { rows.ResumeLayout(); }
    }

    void AddChromeRows(IEnumerable<ChromeSource> sources, bool child)
    {
        foreach (var source in sources)
        {
            AddRow((child ? "   " : "") + source.Name,
                source.Status ?? (source.Enabled ? "Chrome audio" : "Enable in Chrome with Ctrl+Shift+U"),
                source.Enabled ? source.Outputs : null, source.Selected,
                selected => { bridge.Route(source, selected.Id); status.Text = ""; }, false);
        }
    }

    void AddRow(string name, string subtitle, List<Output> outputs, string selected, Action<Output> change, bool expander)
    {
        var row = new Panel { Width = 367, Height = 59, Margin = new Padding(0, 0, 0, 5), BackColor = Color.FromArgb(36, 41, 53) };
        using (var clip = Rounded(row.ClientRectangle, 20)) row.Region = new Region(clip);
        var title = new Label { Text = name, UseMnemonic = false, AutoEllipsis = true, Font = rowFont, Bounds = new Rectangle(11, 7, outputs == null ? 306 : 158, 22) };
        var detail = new Label { Text = subtitle, UseMnemonic = false, AutoEllipsis = true, ForeColor = Color.FromArgb(170, 185, 210), Bounds = new Rectangle(11, 29, outputs == null ? 306 : 158, 23) };
        row.Controls.AddRange(new Control[] { title, detail });
        if (outputs != null)
        {
            var picker = new OutputPicker(outputs, selected, output =>
            {
                try { change(output); return true; }
                catch (COMException) { status.Text = "Windows could not change the output."; signature = null; }
                catch (InvalidOperationException error) { status.Text = error.Message; signature = null; }
                return false;
            })
            {
                AccessibleName = name + " output",
                Bounds = new Rectangle(175, 12, expander ? 148 : 180, 35)
            };
            row.Controls.Add(picker);
        }
        if (expander)
        {
            var button = new Button { Text = expanded ? "−" : "+", AccessibleName = expanded ? "Collapse Chrome tabs" : "Expand Chrome tabs",
                Bounds = new Rectangle(328, 15, 30, 28), FlatStyle = FlatStyle.Flat };
            button.FlatAppearance.BorderSize = 0;
            button.Click += (s, e) => { expanded = !expanded; RefreshRows(); };
            row.Controls.Add(button);
        }
        rows.Controls.Add(row);
    }

    internal static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
        path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Icon DrawSpeaker(int frame)
    {
        using (var bitmap = new Bitmap(32, 32))
        using (var g = Graphics.FromImage(bitmap))
        using (var body = new SolidBrush(Color.FromArgb(203, 217, 239)))
        using (var side = new SolidBrush(Color.FromArgb(96, 124, 170)))
        using (var driver = new SolidBrush(Color.FromArgb(32, 44, 66)))
        using (var note = new Pen(Color.FromArgb(112, 222, 250), 1.8f))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var state = g.Save();
            g.TranslateTransform(12, 16);
            g.RotateTransform(new[] { 0, -9, -3, 8, 3 }[frame]);
            g.TranslateTransform(-12, -16);
            g.FillPolygon(side, new[] { new Point(4, 8), new Point(10, 5), new Point(21, 7), new Point(15, 28), new Point(4, 24) });
            g.FillPolygon(body, new[] { new Point(10, 5), new Point(21, 7), new Point(21, 25), new Point(10, 28) });
            g.FillEllipse(driver, 13, 8, 5, 5);
            g.FillEllipse(driver, 11, 15, 9, 9);
            g.FillEllipse(side, 14, 18, 3, 3);
            g.Restore(state);
            if (frame != 0)
            {
                int x = 22 + frame;
                int y = 15 - frame * 2;
                g.DrawLine(note, x + 2, y + 7, x + 2, y);
                g.DrawLine(note, x + 2, y, x + 5, y + 2);
                g.FillEllipse(note.Brush, x - 1, y + 6, 4, 3);
            }
            IntPtr handle = bitmap.GetHicon();
            try { using (var icon = Icon.FromHandle(handle)) return (Icon)icon.Clone(); }
            finally { DestroyIcon(handle); }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            refresh.Dispose(); animation.Dispose(); tray.Visible = false;
            tray.ContextMenuStrip.Dispose(); tray.Dispose(); panel.Dispose();
            bridge.Dispose(); audio.Dispose(); rowFont.Dispose();
            foreach (var icon in frames) icon.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}

sealed class OutputPicker : Button
{
    static readonly Color Surface = Color.FromArgb(41, 49, 66);
    static readonly Color Highlight = Color.FromArgb(52, 65, 88);
    static readonly Color Ink = Color.FromArgb(239, 242, 249);
    static readonly Color Accent = Color.FromArgb(133, 206, 249);
    readonly ContextMenuStrip menu;
    bool hover;
    bool opening;
    ToolStripMenuItem current;

    public bool MenuOpen { get { return opening || menu.Visible; } }

    public OutputPicker(List<Output> outputs, string selected, Func<Output, bool> commit)
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
            Font = Font,
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
            item.Click += (sender, args) =>
            {
                if (!commit(output)) return;
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
            using (var path = Tray.Rounded(menu.ClientRectangle, 14))
            {
                var previous = menu.Region;
                menu.Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        };
        menu.Closed += (sender, args) => Invalidate();
    }

    protected override void OnClick(EventArgs args)
    {
        base.OnClick(args);
        if (menu.Visible) { menu.Close(); return; }
        opening = true;
        try
        {
            menu.Show(this, new Point(Width, Height + 4), ToolStripDropDownDirection.BelowLeft);
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
        using (var path = Tray.Rounded(bounds, 16))
        using (var fill = new SolidBrush(hover || MenuOpen ? Highlight : Surface))
        using (var border = new Pen(Focused || MenuOpen ? Accent : Color.FromArgb(73, 85, 107)))
        using (var chevron = new Pen(Accent, 1.5f))
        {
            graphics.FillPath(fill, path);
            graphics.DrawPath(border, path);
            TextRenderer.DrawText(graphics, Text, Font, new Rectangle(11, 0, Width - 35, Height), Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            var x = Width - 17;
            var y = Height / 2;
            graphics.DrawLines(chevron, new[] { new Point(x - 4, y - 2), new Point(x, y + 2), new Point(x + 4, y - 2) });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && menu != null) menu.Dispose();
        base.Dispose(disposing);
    }

    sealed class PickerRenderer : ToolStripRenderer
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
            using (var path = Tray.Rounded(new Rectangle(0, 0, item.Width, item.Height), 12))
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
