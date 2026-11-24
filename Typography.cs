using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

static class Typography
{
    static readonly PrivateFontCollection Faces = new PrivateFontCollection();
    static readonly FontFamily Body = Load("Geist.ttf");
    static readonly FontFamily Display = Load("Poppins.ttf");

    static FontFamily Load(string name)
    {
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream == null) return null;
            byte[] bytes;
            using (var data = new MemoryStream()) { stream.CopyTo(data); bytes = data.ToArray(); }
            var existing = Faces.Families.Length;
            // GDI and GDI+ each need the face for native menus and custom rendering.
            var memory = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            uint count = 0;
            AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, ref count);
            Faces.AddMemoryFont(memory, bytes.Length);
            var families = Faces.Families;
            return families.Length > existing ? families[existing] : null;
        }
    }

    public static Font Text(float size) { return Create(Body, size); }
    public static Font Heading(float size) { return Create(Display, size); }

    static Font Create(FontFamily face, float size)
    {
        return face == null ? new Font("Segoe UI", size) : new Font(face, size);
    }

    [DllImport("gdi32.dll")]
    static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint count);
}
