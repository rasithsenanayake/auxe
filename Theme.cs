using System;
using System.Drawing;
using Microsoft.Win32;

static class Theme
{
    public static bool Dark { get; private set; }

    static Theme()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Auxe"))
            Dark = key == null || !Equals(key.GetValue("LightTheme"), 1);
    }

    public static void Toggle()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Auxe"))
            key.SetValue("LightTheme", Dark ? 1 : 0);
        Dark = !Dark;
    }

    public static Color Canvas { get { return Dark ? Color.FromArgb(15, 15, 15) : Color.FromArgb(246, 246, 244); } }
    public static Color Card { get { return Dark ? Color.FromArgb(25, 25, 25) : Color.White; } }
    public static Color Surface { get { return Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 241, 244); } }
    public static Color Highlight { get { return Dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(225, 234, 249); } }
    public static Color Ink { get { return Dark ? Color.FromArgb(244, 244, 244) : Color.FromArgb(35, 37, 42); } }
    public static Color Muted { get { return Dark ? Color.FromArgb(172, 172, 172) : Color.FromArgb(95, 100, 111); } }
    public static Color Border { get { return Dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(225, 227, 232); } }
    public static Color Accent { get { return Dark ? Color.FromArgb(215, 215, 215) : Color.FromArgb(35, 95, 184); } }
    public static Color Mark { get { return Dark ? Color.FromArgb(36, 36, 36) : Color.FromArgb(228, 234, 246); } }
}
