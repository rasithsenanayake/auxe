using System.IO;

static class IconBuild
{
    static void Main(string[] args)
    {
        using (var icon = SpeakerArt.Draw(0))
        using (var file = File.Create(args[0])) icon.Save(file);
    }
}
