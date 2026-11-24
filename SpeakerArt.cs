using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

static class SpeakerArt
{
    public static Icon Draw(int frame)
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

    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
}
