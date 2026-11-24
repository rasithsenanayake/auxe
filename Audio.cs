using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

sealed class Output
{
    public string Id { get; set; }
    public string Name { get; set; }
    public override string ToString() { return Name; }
}

sealed class AudioApp
{
    public string Name;
    public string Key;
    public readonly HashSet<uint> Processes = new HashSet<uint>();
    public readonly HashSet<string> Outputs = new HashSet<string>();
    public bool Active;
    public readonly HashSet<string> ActiveOutputs = new HashSet<string>();
}

sealed class Audio : IDisposable
{
    internal const string Prefix = @"\\?\SWD#MMDEVAPI#";
    internal const string Suffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    readonly IDevices devices = (IDevices)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
    IPolicy policy;
    readonly List<IMeter> meters = new List<IMeter>();
    long nextMeterRefresh;
    readonly Dictionary<string, SessionWatch> watchers = new Dictionary<string, SessionWatch>();

    static void Release(object value) { if (value != null) Marshal.ReleaseComObject(value); }

    public List<Output> Read(out List<AudioApp> apps, bool watch = false)
    {
        var outputs = new List<Output>();
        var groups = new Dictionary<string, AudioApp>(StringComparer.OrdinalIgnoreCase);
        IDeviceList list = null;
        try
        {
            devices.EnumAudioEndpoints(0, 1, out list);
            uint count;
            list.GetCount(out count);
            for (uint index = 0; index < count; index++)
            {
                IDevice device = null;
                try
                {
                    list.Item(index, out device);
                    string id;
                    device.GetId(out id);
                    outputs.Add(new Output { Id = id, Name = FriendlyName(device, id) });
                    SessionWatch watcher;
                    if (watch && !watchers.ContainsKey(id))
                    {
                        try { watchers.Add(id, new SessionWatch(device)); }
                        catch (COMException) { } // Enumeration still works when notifications are unavailable.
                    }
                    ReadSessions(device, id, groups);
                    if (watchers.TryGetValue(id, out watcher)) watcher.Read(id, groups);
                }
                catch (COMException) { } // Endpoint removed during enumeration.
                finally { Release(device); }
            }
        }
        finally { Release(list); }
        foreach (var id in watchers.Keys.Where(id => !outputs.Any(output => output.Id == id)).ToArray())
        {
            watchers[id].Dispose();
            watchers.Remove(id);
        }
        apps = groups.Values.OrderByDescending(app => app.Active).ThenBy(app => app.Name).ToList();
        return outputs.OrderBy(output => output.Name).ToList();
    }

    static string FriendlyName(IDevice device, string fallback)
    {
        IProperties properties = null;
        PropValue value = new PropValue();
        try
        {
            device.OpenPropertyStore(0, out properties);
            var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
            properties.GetValue(ref key, out value);
            return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : fallback;
        }
        catch (COMException) { return fallback; }
        finally { PropVariantClear(ref value); Release(properties); }
    }

    static void ReadSessions(IDevice device, string output, Dictionary<string, AudioApp> groups)
    {
        object manager = null;
        ISessions sessions = null;
        try
        {
            Guid iid = typeof(ISessionManager).GUID;
            device.Activate(ref iid, 1, IntPtr.Zero, out manager);
            ((ISessionManager)manager).GetSessionEnumerator(out sessions);
            int count;
            sessions.GetCount(out count);
            for (int index = 0; index < count; index++)
            {
                ISessionBase control = null;
                try
                {
                    sessions.GetSession(index, out control);
                    ReadSession(control, output, groups);
                }
                catch (COMException) { }
                finally { Release(control); }
            }
        }
        finally { Release(sessions); Release(manager); }
    }

    static bool ReadSession(ISessionBase control, string output, Dictionary<string, AudioApp> groups)
    {
        try
        {
            var session = (ISession)control;
            int state;
            uint pid;
            session.GetState(out state);
            session.GetProcessId(out pid);
            if (state == 2 || pid == 0 || pid > int.MaxValue) return false;
            using (var process = Process.GetProcessById((int)pid))
            {
                string name = process.ProcessName;
                string key = name;
                try
                {
                    var module = process.MainModule;
                    key = module.FileName;
                    var description = module.FileVersionInfo.FileDescription;
                    if (!string.IsNullOrWhiteSpace(description)) name = description;
                }
                catch (System.ComponentModel.Win32Exception) { }
                if (process.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase)) name = "Chrome";
                if (process.ProcessName.Equals("discord", StringComparison.OrdinalIgnoreCase)) name = "Discord";
                if (process.ProcessName.Equals("WhatsApp.Root", StringComparison.OrdinalIgnoreCase)) name = "WhatsApp";
                AudioApp app;
                if (!groups.TryGetValue(key, out app))
                {
                    app = new AudioApp { Key = key, Name = name };
                    groups.Add(key, app);
                }
                app.Processes.Add(pid);
                app.Outputs.Add(output);
                app.Active |= state == 1;
                if (state == 1) app.ActiveOutputs.Add(output);
            }
            return true;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (COMException) { return false; }
    }

    sealed class SessionWatch : ISessionNotification, IDisposable
    {
        readonly ISessionManager manager;
        readonly List<ISessionBase> added = new List<ISessionBase>();
        bool disposed;

        public SessionWatch(IDevice device)
        {
            Guid iid = typeof(ISessionManager).GUID;
            object result;
            device.Activate(ref iid, 1, IntPtr.Zero, out result);
            manager = (ISessionManager)result;
            ISessions initial = null;
            try
            {
                manager.RegisterSessionNotification(this);
                manager.GetSessionEnumerator(out initial);
                int count;
                initial.GetCount(out count);
            }
            catch (COMException)
            {
                Dispose();
                throw;
            }
            finally { Release(initial); }
        }

        public int OnSessionCreated(ISessionBase session)
        {
            lock (added)
                if (!disposed) added.Add(session);
            return 0;
        }

        public void Read(string output, Dictionary<string, AudioApp> groups)
        {
            ISessionBase[] snapshot;
            lock (added) snapshot = added.ToArray();
            foreach (var session in snapshot)
            {
                if (ReadSession(session, output, groups)) continue;
                lock (added) added.Remove(session);
            }
        }

        public void Dispose()
        {
            lock (added)
            {
                disposed = true;
                added.Clear();
            }
            try { manager.UnregisterSessionNotification(this); }
            catch (COMException) { }
            finally { Release(manager); }
        }
    }

    public bool Playing()
    {
        long now = Stopwatch.GetTimestamp();
        if (now >= nextMeterRefresh)
        {
            nextMeterRefresh = now + Stopwatch.Frequency * 5;
            RefreshMeters();
        }

        foreach (var meter in meters)
        {
            try
            {
                float value;
                meter.GetPeakValue(out value);
                if (value > 0.002f) return true;
            }
            catch (COMException) { } // A changed endpoint is picked up on refresh.
        }
        return false;
    }

    void RefreshMeters()
    {
        foreach (var meter in meters) Release(meter);
        meters.Clear();
        IDeviceList list = null;
        try
        {
            devices.EnumAudioEndpoints(0, 1, out list);
            uint count;
            list.GetCount(out count);
            for (uint index = 0; index < count; index++)
            {
                IDevice device = null;
                try
                {
                    list.Item(index, out device);
                    Guid iid = typeof(IMeter).GUID;
                    object meter;
                    device.Activate(ref iid, 1, IntPtr.Zero, out meter);
                    meters.Add((IMeter)meter);
                }
                catch (COMException) { }
                finally { Release(device); }
            }
        }
        catch (COMException) { }
        finally { Release(list); }
    }

    IPolicy Policy()
    {
        if (policy != null) return policy;
        IntPtr name = MakeString("Windows.Media.Internal.AudioPolicyConfig");
        try
        {
            Guid iid = typeof(IPolicy).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(name, ref iid, out policy));
            return policy;
        }
        finally { WindowsDeleteString(name); }
    }

    static IntPtr MakeString(string value)
    {
        IntPtr result;
        Marshal.ThrowExceptionForHR(WindowsCreateString(value, value.Length, out result));
        return result;
    }

    public string Get(uint pid, int role)
    {
        IntPtr value = IntPtr.Zero;
        try
        {
            int result = Policy().GetEndpoint(pid, 0, role, out value);
            if (result == unchecked((int)0x80070490) || result == unchecked((int)0x80070002)) return null;
            Marshal.ThrowExceptionForHR(result);
            if (value == IntPtr.Zero) return null;
            uint length;
            var id = Marshal.PtrToStringUni(WindowsGetStringRawBuffer(value, out length), (int)length);
            if (id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && id.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
                return id.Substring(Prefix.Length, id.Length - Prefix.Length - Suffix.Length);
            return id.Length == 0 ? null : id;
        }
        finally { if (value != IntPtr.Zero) WindowsDeleteString(value); }
    }

    public void Set(AudioApp app, string id)
    {
        var target = Policy();
        var changes = new List<Tuple<uint, int, string>>();
        foreach (uint pid in app.Processes)
            for (int role = 0; role < 3; role++)
            {
                string previous = Get(pid, role);
                if (previous != id) changes.Add(Tuple.Create(pid, role, previous));
            }

        int applied = 0;
        try
        {
            foreach (var change in changes)
            {
                WriteEndpoint(target, change.Item1, change.Item2, id);
                applied++;
            }
        }
        catch (COMException)
        {
            for (int index = applied - 1; index >= 0; index--)
            {
                var change = changes[index];
                try { WriteEndpoint(target, change.Item1, change.Item2, change.Item3); }
                catch (COMException) { } // The app may have exited during the change.
            }
            throw;
        }
    }

    static void WriteEndpoint(IPolicy target, uint pid, int role, string id)
    {
        IntPtr value = id == null ? IntPtr.Zero : MakeString(Prefix + id + Suffix);
        try { Marshal.ThrowExceptionForHR(target.SetEndpoint(pid, 0, role, value)); }
        finally { if (value != IntPtr.Zero) WindowsDeleteString(value); }
    }

    public void Dispose()
    {
        foreach (var meter in meters) Release(meter);
        Release(policy);
        Release(devices);
    }

    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string value, int length, out IntPtr result);
    [DllImport("combase.dll")] static extern IntPtr WindowsGetStringRawBuffer(IntPtr value, out uint length);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPolicy result);
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropValue value);

    [StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PropValue
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevices { void EnumAudioEndpoints(int flow, uint state, out IDeviceList list); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceList { void GetCount(out uint count); void Item(uint index, out IDevice device); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.Interface)] out object result);
        void OpenPropertyStore(uint mode, out IProperties properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IProperties
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropValue value);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionManager
    {
        void GetAudioSessionControl();
        void GetSimpleAudioVolume();
        void GetSessionEnumerator(out ISessions sessions);
        void RegisterSessionNotification(ISessionNotification notification);
        void UnregisterSessionNotification(ISessionNotification notification);
    }
    [Guid("641DD20B-4D41-49CC-ABA3-174B9477BB08"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionNotification
    {
        [PreserveSig] int OnSessionCreated(ISessionBase session);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessions { void GetCount(out int count); void GetSession(int index, out ISessionBase session); }
    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISessionBase { }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISession
    {
        void GetState(out int state);
        void GetDisplayName(); void SetDisplayName();
        void GetIconPath(); void SetIconPath();
        void GetGroupingParam(); void SetGroupingParam();
        void RegisterAudioSessionNotification(); void UnregisterAudioSessionNotification();
        void GetSessionIdentifier(); void GetSessionInstanceIdentifier();
        void GetProcessId(out uint pid);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMeter { void GetPeakValue(out float value); }

    // Windows 11 IInspectable ABI. The preceding slots are never invoked.
    [ComImport, Guid("AB3D4648-E242-459F-B02F-541C70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicy
    {
        void GetIids(); void GetRuntimeClassName(); void GetTrustLevel();
        void AddContextVolume(); void RemoveContextVolume();
        void AddRinger(); void RemoveRinger();
        void SetVolumeGroupGain(); void GetVolumeGroupGain();
        void GetActiveVolumeGroup(); void GetVolumeGroups();
        void GetCurrentVolumeContext(); void SetVolumeGroupMute(); void GetVolumeGroupMute();
        void SetRingerState(); void GetRingerState();
        void SetPreferredChat(); void ResetPreferredChat(); void GetPreferredChat();
        void GetCurrentChat(); void AddChatContext(); void RemoveChatContext();
        [PreserveSig] int SetEndpoint(uint process, int flow, int role, IntPtr id);
        [PreserveSig] int GetEndpoint(uint process, int flow, int role, out IntPtr id);
    }
}
