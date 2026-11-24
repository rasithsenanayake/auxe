using System.IO;
using System.Drawing.Imaging;

static class IconBuild
{
    static void Main(string[] args)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var frames = new byte[sizes.Length][];
        for (int index = 0; index < sizes.Length; index++)
        {
            using (var bitmap = SpeakerArt.DrawInstaller(sizes[index]))
            using (var png = new MemoryStream())
            {
                bitmap.Save(png, ImageFormat.Png);
                frames[index] = png.ToArray();
            }
        }

        using (var file = new BinaryWriter(File.Create(args[0])))
        {
            file.Write((ushort)0);
            file.Write((ushort)1);
            file.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int index = 0; index < sizes.Length; index++)
            {
                byte dimension = (byte)(sizes[index] % 256);
                file.Write(dimension);
                file.Write(dimension);
                file.Write((byte)0);
                file.Write((byte)0);
                file.Write((ushort)1);
                file.Write((ushort)32);
                file.Write(frames[index].Length);
                file.Write(offset);
                offset += frames[index].Length;
            }
            foreach (var frame in frames) file.Write(frame);
        }
    }
}
