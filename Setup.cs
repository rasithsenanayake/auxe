using System;
using System.Diagnostics;
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
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Auxe");
        var exe = Path.Combine(root, "Auxe.exe");
        var chrome = Path.Combine(root, "chrome");
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Auxe.lnk");
        if (MessageBox.Show("Install Auxe for your Windows account?\n\nThe included Chrome companion can be enabled separately.",
            "Auxe Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

        try
        {
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
                key.SetValue("DisplayVersion", "0.1.1");
                key.SetValue("DisplayIcon", exe);
                key.SetValue("InstallLocation", root);
                key.SetValue("UninstallString", "\"powershell.exe\" -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "uninstall.ps1") + "\"");
                key.SetValue("NoModify", 1);
                key.SetValue("NoRepair", 1);
            }
            Process.Start(exe);
            MessageBox.Show("Auxe is running in the notification area. For per-tab routing, load this folder as an unpacked extension in Chrome:\n\n" + chrome,
                "Auxe installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message + "\n\nIf Auxe is running, exit it from the tray before reinstalling.",
                "Auxe Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    static void Extract(string name, string path)
    {
        using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using (var output = File.Create(path)) input.CopyTo(output);
    }
}
