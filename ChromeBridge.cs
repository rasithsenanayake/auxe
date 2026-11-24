using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

public sealed class ChromeSource
{
    public int Id { get; set; }
    public string Name { get; set; }
    public bool Standalone { get; set; }
    public bool Enabled { get; set; }
    public string Status { get; set; }
    public string Selected { get; set; }
    internal ChromeBridge.Connection Connection;
    public List<ChromeDevice> Devices { get; set; }
    internal List<Output> Outputs
    {
        get
        {
            var outputs = new List<Output> { new Output { Name = "Chrome default" } };
            if (Devices != null) outputs.AddRange(Devices.Where(device => !string.IsNullOrEmpty(device.Id))
                .Select(device => new Output { Id = device.Id, Name = device.Name }));
            return outputs;
        }
    }

    internal string Signature()
    {
        return Id + ":" + Name + ":" + Standalone + ":" + Enabled + ":" + Status + ":" + Selected +
            string.Join(",", Outputs.Select(output => output.Id + output.Name));
    }
}

public sealed class ChromeDevice
{
    public string Id { get; set; }
    public string Name { get; set; }
}

sealed class ChromeBridge : IDisposable
{
    static readonly System.Security.Principal.SecurityIdentifier User = System.Security.Principal.WindowsIdentity.GetCurrent().User;
    static readonly string PipeName = "Auxe." + User.Value;
    const int Limit = 512 * 1024;
    readonly object gate = new object();
    readonly List<Connection> connections = new List<Connection>();
    NamedPipeServerStream listener;
    bool stopping;

    internal sealed class Connection
    {
        public NamedPipeServerStream Pipe;
        public List<ChromeSource> Sources = new List<ChromeSource>();
        public readonly object WriteLock = new object();
    }

    public ChromeBridge()
    {
        new Thread(Listen) { IsBackground = true, Name = "Auxe Chrome listener" }.Start();
    }

    void Listen()
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(User, PipeAccessRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
        while (true)
        {
            NamedPipeServerStream pipe;
            lock (gate)
            {
                if (stopping) return;
                pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 16, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
                listener = pipe;
            }
            try { pipe.WaitForConnection(); }
            catch (IOException) { pipe.Dispose(); return; }
            catch (ObjectDisposedException) { return; }
            lock (gate)
            {
                listener = null;
                if (stopping) { pipe.Dispose(); return; }
                if (connections.Count >= 8) { pipe.Dispose(); continue; }
                var connection = new Connection { Pipe = pipe };
                connections.Add(connection);
                new Thread(() => Read(connection)) { IsBackground = true, Name = "Auxe Chrome connection" }.Start();
            }
        }
    }

    void Read(Connection connection)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = Limit, RecursionLimit = 12 };
        try
        {
            byte[] bytes;
            while ((bytes = ReadFrame(connection.Pipe)) != null)
            {
                var sources = json.Deserialize<List<ChromeSource>>(Encoding.UTF8.GetString(bytes));
                if (sources == null || sources.Count > 256 || sources.Any(source => source == null ||
                    source.Id < 0 || string.IsNullOrWhiteSpace(source.Name) ||
                    (source.Devices != null && source.Devices.Any(device => device == null || string.IsNullOrWhiteSpace(device.Name))))) break;
                foreach (var source in sources) source.Connection = connection;
                lock (gate) connection.Sources = sources;
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
        finally
        {
            connection.Pipe.Dispose();
            lock (gate) connections.Remove(connection);
        }
    }

    public List<ChromeSource> Sources()
    {
        lock (gate) return connections.SelectMany(connection => connection.Sources).ToList();
    }

    public System.Threading.Tasks.Task Route(ChromeSource source, string output)
    {
        return System.Threading.Tasks.Task.Run(() =>
        {
            var connection = source.Connection;
            try
            {
                lock (connection.WriteLock)
                {
                    var json = new JavaScriptSerializer().Serialize(new { Id = source.Id, Output = output });
                    WriteFrame(connection.Pipe, Encoding.UTF8.GetBytes(json));
                }
            }
            catch (ObjectDisposedException error) { throw new IOException("Chrome disconnected.", error); }
        });
    }

    public static void RunHost()
    {
        using (var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            try { pipe.Connect(500); }
            catch (TimeoutException) { return; }
            catch (IOException) { return; }
            var input = Console.OpenStandardInput();
            var output = Console.OpenStandardOutput();
            var reader = new Thread(() => { Forward(pipe, output); Environment.Exit(0); }) { IsBackground = true };
            reader.Start();
            Forward(input, pipe);
        }
    }

    static void Forward(Stream input, Stream output)
    {
        try
        {
            byte[] frame;
            while ((frame = ReadFrame(input)) != null) WriteFrame(output, frame);
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    static byte[] ReadFrame(Stream input)
    {
        var header = new byte[4];
        if (!ReadAll(input, header)) return null;
        int length = BitConverter.ToInt32(header, 0);
        if (length < 1 || length > Limit) throw new IOException("Invalid message length.");
        var data = new byte[length];
        return ReadAll(input, data) ? data : null;
    }

    static bool ReadAll(Stream input, byte[] data)
    {
        for (int offset = 0; offset < data.Length;)
        {
            int count = input.Read(data, offset, data.Length - offset);
            if (count == 0) return false;
            offset += count;
        }
        return true;
    }

    static void WriteFrame(Stream output, byte[] data)
    {
        output.Write(BitConverter.GetBytes(data.Length), 0, 4);
        output.Write(data, 0, data.Length);
        output.Flush();
    }

    public void Dispose()
    {
        lock (gate)
        {
            stopping = true;
            if (listener != null) listener.Dispose();
            foreach (var connection in connections) connection.Pipe.Dispose();
        }
    }
}
