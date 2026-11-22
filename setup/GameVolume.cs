// Copyright (c) 2026 eirros. All rights reserved.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

internal static class GameVolume
{
    internal static void Monitor(Process game, string root, Action<string> log)
    {
        try
        {
            int pid = game.Id;
            string state = Path.Combine(root, "setup", "game-mixer-initialized.txt");
            var initialized = new HashSet<string>(File.Exists(state) ? File.ReadAllLines(state) : new string[0]);
            var clock = Stopwatch.StartNew();
            while (!game.HasExited)
            {
                var pending = new HashSet<string>();
                bool waitingForOutputVolume = false;
                bool found = VisitSessions((uint)pid, (session, volume, readOutputVolume) =>
                {
                    if (initialized.Contains(session) || pending.Contains(session)) return;
                    if (TryInitializeSession(volume, readOutputVolume, pid, log)) pending.Add(session);
                    else waitingForOutputVolume = true;
                });
                if (pending.Count > 0)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(state));
                    File.AppendAllLines(state, pending);
                    initialized.UnionWith(pending);
                }
                // The legacy client opens its audio sessions after channel selection.
                // Keep watching for new sessions while the client is alive.
                int interval = found && !waitingForOutputVolume ? 2000 : (clock.ElapsedMilliseconds < 15000 ? 100 : 250);
                if (game.WaitForExit(interval)) return;
            }
        }
        catch (COMException error) { log("AUDIO Windows mixer unavailable: " + error.ErrorCode.ToString("X8")); }
        catch (InvalidCastException) { log("AUDIO Windows mixer interface unavailable"); }
        catch (IOException error) { log("AUDIO local mixer state unavailable: " + error.GetType().Name); }
        catch (UnauthorizedAccessException error) { log("AUDIO local mixer state unavailable: " + error.GetType().Name); }
    }

    internal static bool TryInitializeSession(ISimpleAudioVolume volume, Func<float> readOutputVolume,
        int pid, Action<string> log)
    {
        float current;
        bool muted;
        volume.GetMasterVolume(out current);
        volume.GetMute(out muted);
        if (muted || current < 0.999f) return true;

        float outputVolume = readOutputVolume();
        // At zero, keep waiting instead of saving a permanently silent app slider.
        if (outputVolume == 0f) return false;

        float initialVolume = outputVolume * 0.25f;
        Guid context = Guid.Empty;
        volume.SetMasterVolume(initialVolume, ref context);
        log("AUDIO pid=" + pid + " game mixer initialized to " + Percent(initialVolume) +
            " (Windows output " + Percent(outputVolume) + ")");
        return true;
    }

    private static string Percent(float volume)
    {
        return volume.ToString("P1", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static bool VisitSessions(uint pid, Action<string, ISimpleAudioVolume, Func<float>> visit)
    {
        var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true);
        var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type);
        IMMDeviceCollection devices = null;
        bool found = false;
        try
        {
            enumerator.EnumAudioEndpoints(0, 1, out devices); // Rendering endpoints that are active.
            uint count;
            devices.GetCount(out count);
            for (uint i = 0; i < count; i++)
            {
                IMMDevice device;
                devices.Item(i, out device);
                try { found |= VisitDevice(device, pid, visit); }
                finally { Marshal.ReleaseComObject(device); }
            }
            return found;
        }
        finally
        {
            if (devices != null) Marshal.ReleaseComObject(devices);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static bool VisitDevice(IMMDevice device, uint pid, Action<string, ISimpleAudioVolume, Func<float>> visit)
    {
        Guid iid = typeof(IAudioSessionManager2).GUID;
        object manager;
        device.Activate(ref iid, 23, IntPtr.Zero, out manager);
        IAudioSessionEnumerator sessions = null;
        bool found = false;
        try
        {
            ((IAudioSessionManager2)manager).GetSessionEnumerator(out sessions);
            int count;
            sessions.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                object session;
                sessions.GetSession(i, out session);
                try
                {
                    var control = (IAudioSessionControl2)session;
                    int state;
                    uint owner;
                    control.GetState(out state);
                    // GetProcessId returns a different success status for shared processes.
                    if (state == 2 || control.GetProcessId(out owner) != 0 || owner != pid) continue;
                    string identifier;
                    control.GetSessionIdentifier(out identifier);
                    visit(identifier, (ISimpleAudioVolume)session, () => ReadOutputVolume(device));
                    found = true;
                }
                finally { Marshal.ReleaseComObject(session); }
            }
            return found;
        }
        finally
        {
            if (sessions != null) Marshal.ReleaseComObject(sessions);
            Marshal.ReleaseComObject(manager);
        }
    }

    private static float ReadOutputVolume(IMMDevice device)
    {
        Guid iid = typeof(IAudioEndpointVolume).GUID;
        object endpointVolume;
        device.Activate(ref iid, 23, IntPtr.Zero, out endpointVolume);
        try
        {
            float volume;
            ((IAudioEndpointVolume)endpointVolume).GetMasterVolumeLevelScalar(out volume);
            return volume;
        }
        finally { Marshal.ReleaseComObject(endpointVolume); }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint states, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        void GetCount(out uint count);
        void Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr callback);
        void UnregisterControlChangeNotify(IntPtr callback);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float levelDb, IntPtr context);
        void SetMasterVolumeLevelScalar(float level, IntPtr context);
        void GetMasterVolumeLevel(out float levelDb);
        void GetMasterVolumeLevelScalar(out float level);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr session, uint flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr session, uint flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        void GetState(out int state);
        void GetDisplayName(out IntPtr name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath(out IntPtr path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid group);
        void SetGroupingParam(ref Guid group, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr events);
        void UnregisterAudioSessionNotification(IntPtr events);
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint pid);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        void SetMasterVolume(float volume, ref Guid context);
        void GetMasterVolume(out float volume);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
