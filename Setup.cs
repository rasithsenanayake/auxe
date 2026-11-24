using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

static class Setup
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Auxe";
    const string ChromeKey = @"Software\Google\Chrome\NativeMessagingHosts\com.auxe.companion";
    const string ExtensionId = "fifgmenkkbfeanfdcjglbpoijipdmklj";

    [STAThread]
    static int Main(string[] launchArgs)
    {
        int priorProcess;
        if (launchArgs.Length == 2 && launchArgs[0] == "--update" &&
            int.TryParse(launchArgs[1], out priorProcess) && priorProcess > 0)
            return Update(priorProcess);
        bool uninstall = launchArgs.Length == 1 && launchArgs[0] == "--uninstall";
        bool confirmed = false;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(true);
        using (var icon = SpeakerArt.Draw(0))
        using (var logo = icon.ToBitmap())
        using (var bodyFont = Typography.Text(10.5f))
        using (var actionFont = Typography.Heading(10))
        using (var titleFont = Typography.Heading(22))
        using (var window = new SetupWindow
        {
            Text = uninstall ? "Uninstall Auxe" : "Auxe Setup",
            Icon = icon,
            ClientSize = new Size(430, 252),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.None,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Theme.Canvas,
            ForeColor = Theme.Ink,
            Font = bodyFont
        })
        {
            using (var shape = SpeakerArt.Rounded(window.ClientRectangle, 32)) window.Region = new Region(shape);
            var mark = new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Theme.Mark, Bounds = new Rectangle(25, 24, 48, 48)
            };
            var title = new Label { Text = uninstall ? "Uninstall Auxe" : "Auxe", Font = titleFont, Bounds = new Rectangle(83, 16, 320, 43) };
            var publisher = new Label
            {
                Text = "Web X Agency", ForeColor = Theme.Muted,
                Bounds = new Rectangle(85, 56, 270, 23)
            };
            var description = new Label
            {
                Text = uninstall
                    ? "Remove Auxe, its Start menu shortcut, and its startup setting from this Windows account."
                    : "Choose where each app plays. Auxe installs for this Windows account and opens beside the clock.",
                Bounds = new Rectangle(26, 91, 378, 64)
            };
            var install = new SetupButton(true)
            {
                Text = uninstall ? "Uninstall Auxe" : "Install Auxe", Font = actionFont,
                Bounds = new Rectangle(243, 184, 161, 42)
            };
            var cancel = new SetupButton(false)
            {
                Text = "Cancel", Font = actionFont, Bounds = new Rectangle(140, 184, 91, 42)
            };
            using (var shape = SpeakerArt.Rounded(mark.ClientRectangle, 24)) mark.Region = new Region(shape);
            cancel.Click += (sender, args) => window.Close();
            install.Click += (sender, args) =>
            {
                if (uninstall) { confirmed = true; window.Close(); return; }
                if (install.Text == "Done") { window.Close(); return; }
                install.Enabled = false;
                cancel.Enabled = false;
                try
                {
                    Install();
                    description.Text = "Auxe is ready. Look for the speaker beside the clock or under the tray arrow.";
                    install.Text = "Done";
                    cancel.Visible = false;
                }
                catch (Exception error)
                {
                    description.Text = "Setup couldn’t finish. Quit Auxe from the tray, then try again.";
                    MessageBox.Show(window, error.Message, "Auxe Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    cancel.Enabled = true;
                }
                finally { install.Enabled = true; }
            };
            window.Controls.AddRange(new Control[] { mark, title, publisher, description, cancel, install });
            window.AcceptButton = install;
            window.CancelButton = cancel;
            Application.Run(window);
        }
        return uninstall && !confirmed ? 1 : 0;
    }

    static int Update(int priorProcess)
    {
        try
        {
            try
            {
                using (var running = Process.GetProcessById(priorProcess))
                    if (!running.WaitForExit(10000)) return 1;
            }
            catch (ArgumentException) { } // Auxe already exited.
            Install();
            return 0;
        }
        catch (Exception)
        {
            try { if (File.Exists(Startup.Executable)) Process.Start(Startup.Executable); }
            catch (System.ComponentModel.Win32Exception) { }
            return 1;
        }
    }

    static void Install()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Auxe");
        var exe = Path.Combine(root, "Auxe.exe");
        var chrome = Path.Combine(root, "chrome");
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Auxe.lnk");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.GetDirectoryName(startMenu));
        Extract("app", exe);
        File.Copy(Assembly.GetExecutingAssembly().Location, Path.Combine(root, "AuxeMaintenance.exe"), true);
        Extract("uninstall", Path.Combine(root, "uninstall.ps1"));
        using (var zip = new ZipArchive(Assembly.GetExecutingAssembly().GetManifestResourceStream("chrome"), ZipArchiveMode.Read))
        {
            Directory.CreateDirectory(chrome);
            foreach (var entry in zip.Entries)
            {
                if (entry.Name != entry.FullName || string.IsNullOrEmpty(entry.Name))
                    throw new InvalidDataException("Invalid companion package.");
                entry.ExtractToFile(Path.Combine(chrome, entry.Name), true);
            }
        }
        var host = Path.Combine(root, "chrome-host.json");
        File.WriteAllText(host, new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {
            name = "com.auxe.companion", description = "Auxe tray connection",
            path = exe, type = "stdio", allowed_origins = new[] { "chrome-extension://" + ExtensionId + "/" }
        }));
        using (var key = Registry.CurrentUser.CreateSubKey(ChromeKey)) key.SetValue("", host);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        dynamic shortcut = shell.CreateShortcut(startMenu);
        shortcut.TargetPath = exe;
        shortcut.WorkingDirectory = root;
        shortcut.Description = "Choose an audio output for each app";
        shortcut.Save();
        using (var key = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            key.SetValue("DisplayName", "Auxe");
            key.SetValue("DisplayVersion", "0.1.6");
            key.SetValue("Publisher", "Web X Agency");
            key.SetValue("DisplayIcon", exe);
            key.SetValue("InstallLocation", root);
            key.SetValue("UninstallString", "\"powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "uninstall.ps1") + "\"");
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
        }
        Startup.ApplyInstallDefault();
        Process.Start(exe);
    }

    static void Extract(string name, string path)
    {
        var stage = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            using (var output = new FileStream(stage, FileMode.CreateNew)) input.CopyTo(output);
            if (File.Exists(path)) File.Replace(stage, path, null);
            else File.Move(stage, path);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
}

sealed class SetupWindow : Form
{
    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x84 || message.Result != new IntPtr(1)) return;
        long coordinates = message.LParam.ToInt64();
        var point = PointToClient(new Point(unchecked((short)coordinates), unchecked((short)(coordinates >> 16))));
        if (point.Y >= 0 && point.Y < 78) message.Result = new IntPtr(2);
    }
}

sealed class SetupButton : Button
{
    readonly bool primary;
    bool hover;

    public SetupButton(bool primary)
    {
        this.primary = primary;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        UseMnemonic = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnMouseEnter(EventArgs args) { base.OnMouseEnter(args); hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs args) { base.OnMouseLeave(args); hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs args)
    {
        var graphics = args.Graphics;
        graphics.Clear(Parent.BackColor);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        var blue = Color.FromArgb(35, 95, 184);
        var fill = primary
            ? (hover ? Color.FromArgb(29, 79, 156) : blue)
            : (hover ? Theme.Highlight : Theme.Card);
        using (var path = SpeakerArt.Rounded(bounds, 22))
        using (var brush = new SolidBrush(Enabled ? fill : Theme.Surface))
        using (var border = new Pen(Focused ? Theme.Accent : Theme.Border))
        using (var text = new SolidBrush(primary && Enabled ? Color.White : Theme.Ink))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            graphics.FillPath(brush, path);
            if (!primary || Focused) graphics.DrawPath(border, path);
            graphics.DrawString(Text, Font, text, bounds, format);
        }
    }
}
