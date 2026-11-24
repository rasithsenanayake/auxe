using System;
using System.Diagnostics;
using System.Drawing;
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
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var icon = SpeakerArt.Draw(0))
        using (var logo = icon.ToBitmap())
        using (var bodyFont = new Font("Segoe UI", 10))
        using (var titleFont = new Font("Segoe UI Semibold", 24))
        using (var window = new SetupWindow
        {
            Text = "Auxe Setup",
            Icon = icon,
            ClientSize = new Size(430, 252),
            StartPosition = FormStartPosition.CenterScreen,
            FormBorderStyle = FormBorderStyle.None,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(246, 246, 244),
            ForeColor = Color.FromArgb(35, 37, 42),
            Font = bodyFont
        })
        {
            using (var shape = SpeakerArt.Rounded(window.ClientRectangle, 32)) window.Region = new Region(shape);
            var mark = new PictureBox
            {
                Image = logo, SizeMode = PictureBoxSizeMode.CenterImage,
                BackColor = Color.FromArgb(228, 234, 246), Bounds = new Rectangle(25, 24, 48, 48)
            };
            var title = new Label { Text = "Auxe", Font = titleFont, Bounds = new Rectangle(82, 23, 280, 48) };
            var description = new Label
            {
                Text = "Choose where each Windows app plays. Auxe installs for your account and opens in the notification area.",
                Bounds = new Rectangle(26, 91, 378, 64)
            };
            var install = new Button
            {
                Text = "Install Auxe", Bounds = new Rectangle(254, 184, 150, 40),
                BackColor = Color.FromArgb(35, 95, 184), ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
            };
            install.FlatAppearance.BorderSize = 0;
            var cancel = new Button
            {
                Text = "Cancel", Bounds = new Rectangle(151, 184, 91, 40),
                BackColor = Color.White, FlatStyle = FlatStyle.Flat
            };
            cancel.FlatAppearance.BorderColor = Color.FromArgb(225, 227, 232);
            foreach (var control in new Control[] { mark, install, cancel })
            {
                using (var shape = SpeakerArt.Rounded(control.ClientRectangle, 18))
                    control.Region = new Region(shape);
            }
            cancel.Click += (sender, args) => window.Close();
            install.Click += (sender, args) =>
            {
                if (install.Text == "Done") { window.Close(); return; }
                install.Enabled = false;
                cancel.Enabled = false;
                try
                {
                    Install();
                    description.Text = "Auxe is running near the clock. Open the tray overflow arrow if the speaker is hidden.";
                    install.Text = "Done";
                    cancel.Visible = false;
                }
                catch (Exception error)
                {
                    description.Text = "Setup couldn't finish. Exit any running copy of Auxe and try again.";
                    MessageBox.Show(window, error.Message, "Auxe Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    cancel.Enabled = true;
                }
                finally { install.Enabled = true; }
            };
            window.Controls.AddRange(new Control[] { mark, title, description, cancel, install });
            window.AcceptButton = install;
            window.CancelButton = cancel;
            Application.Run(window);
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
            key.SetValue("DisplayVersion", "0.1.2");
            key.SetValue("DisplayIcon", exe);
            key.SetValue("InstallLocation", root);
            key.SetValue("UninstallString", "\"powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "uninstall.ps1") + "\"");
            key.SetValue("NoModify", 1);
            key.SetValue("NoRepair", 1);
        }
        Process.Start(exe);
    }

    static void Extract(string name, string path)
    {
        using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var output = File.Create(path)) input.CopyTo(output);
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
