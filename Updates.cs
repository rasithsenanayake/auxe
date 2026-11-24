using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

static class Updates
{
    const string Releases = "https://api.github.com/repos/rasithsenanayake/auxe/releases/latest";
    const string Downloads = "https://github.com/rasithsenanayake/auxe/releases/download/";
    const int MaximumBytes = 8 * 1024 * 1024;

    public static bool Enabled
    {
        get
        {
            using (var settings = Registry.CurrentUser.OpenSubKey(@"Software\Auxe"))
                return settings == null || !Equals(settings.GetValue("AutomaticUpdates"), 0);
        }
        set
        {
            using (var settings = Registry.CurrentUser.CreateSubKey(@"Software\Auxe"))
                settings.SetValue("AutomaticUpdates", value ? 1 : 0);
        }
    }

    public static string Download(Version installed)
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var request = Request(Releases);
        string json;
        using (var response = request.GetResponse())
        using (var reader = new StreamReader(response.GetResponseStream())) json = reader.ReadToEnd();
        var release = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
        object value;
        if (release == null || !release.TryGetValue("tag_name", out value)) return null;
        string tag = value as string;
        Version version;
        if (tag == null || !Regex.IsMatch(tag, @"^v[0-9]+\.[0-9]+\.[0-9]+\z") ||
            !Version.TryParse(tag.Substring(1) + ".0", out version) || version <= installed) return null;
        if (!release.TryGetValue("assets", out value)) return null;
        var assets = value as object[];
        if (assets == null) return null;
        var expectedUrl = Downloads + tag + "/AuxeSetup.exe";
        foreach (var entry in assets)
        {
            var asset = entry as Dictionary<string, object>;
            object url, digest, size;
            if (asset == null || !asset.TryGetValue("browser_download_url", out url) ||
                !string.Equals(url as string, expectedUrl, StringComparison.Ordinal) ||
                !asset.TryGetValue("digest", out digest) || !asset.TryGetValue("size", out size)) continue;
            string hash = digest as string;
            if (hash == null || !Regex.IsMatch(hash, @"^sha256:[0-9a-f]{64}\z") || !(size is int)) continue;
            int length = (int)size;
            if (length <= 0 || length > MaximumBytes) continue;
            return DownloadVerified(expectedUrl, hash.Substring(7), length);
        }
        return null;
    }

    static string DownloadVerified(string url, string expectedHash, int length)
    {
        string file = Path.Combine(Path.GetTempPath(), "Auxe-update-" + Guid.NewGuid().ToString("N") + ".exe");
        bool verified = false;
        try
        {
            using (var response = Request(url).GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var sha = SHA256.Create())
            {
                byte[] buffer = new byte[8192];
                int count;
                while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > length) throw new IOException("Update exceeds its published size.");
                    output.Write(buffer, 0, count);
                }
                output.Position = 0;
                var hash = BitConverter.ToString(sha.ComputeHash(output)).Replace("-", "");
                if (output.Length != length || !string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Update does not match the published SHA-256 digest.");
            }
            verified = true;
            return file;
        }
        finally { if (!verified && File.Exists(file)) File.Delete(file); }
    }

    static HttpWebRequest Request(string url)
    {
        var request = (HttpWebRequest)WebRequest.Create(url);
        request.UserAgent = "Auxe-Windows-Updater";
        request.Timeout = 15000;
        request.ReadWriteTimeout = 15000;
        return request;
    }
}
