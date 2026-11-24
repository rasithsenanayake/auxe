using System;
using System.IO;
using Microsoft.Win32;

static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string SettingsKey = @"Software\Auxe";
    public static string Executable
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Auxe", "Auxe.exe"); }
    }

    public static bool Enabled
    {
        get
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && string.Equals(key.GetValue("Auxe") as string, Quote(Executable), StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        if (enabled && !File.Exists(Executable)) throw new InvalidOperationException("Install Auxe to start it with Windows.");
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            if (enabled) run.SetValue("Auxe", Quote(Executable));
            else run.DeleteValue("Auxe", false);
        }
        using (var settings = Registry.CurrentUser.CreateSubKey(SettingsKey)) settings.SetValue("StartWithWindows", enabled ? 1 : 0);
    }

    public static void ApplyInstallDefault()
    {
        using (var settings = Registry.CurrentUser.OpenSubKey(SettingsKey))
        {
            var previous = settings == null ? null : settings.GetValue("StartWithWindows");
            if (previous == null) Set(true);
        }
    }

    static string Quote(string path) { return "\"" + path + "\""; }
}
