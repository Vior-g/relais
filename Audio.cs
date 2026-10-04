using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Relais
{
    /// <summary>
    /// Contrôle du volume par application (mélangeur de volume Windows, API Core Audio).
    /// Relais ne règle que les sessions audio des processus Dofus détectés.
    /// </summary>
    public static class AudioMixer
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            [PreserveSig] int GetAudioSessionControl(IntPtr sessionGuid, int flags, out IntPtr ctl);
            [PreserveSig] int GetSimpleAudioVolume(IntPtr sessionGuid, int flags, out IntPtr vol);
            [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
        }

        [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            // IAudioSessionControl
            [PreserveSig] int GetState(out int state);
            [PreserveSig] int GetDisplayName(out IntPtr name);
            [PreserveSig] int SetDisplayName(IntPtr name, IntPtr ctx);
            [PreserveSig] int GetIconPath(out IntPtr path);
            [PreserveSig] int SetIconPath(IntPtr path, IntPtr ctx);
            [PreserveSig] int GetGroupingParam(out Guid g);
            [PreserveSig] int SetGroupingParam(IntPtr g, IntPtr ctx);
            [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
            [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
            // IAudioSessionControl2
            [PreserveSig] int GetSessionIdentifier(out IntPtr id);
            [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
            [PreserveSig] int GetProcessId(out uint pid);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            [PreserveSig] int SetMasterVolume(float level, IntPtr ctx);
            [PreserveSig] int GetMasterVolume(out float level);
            [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr ctx);
            [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        }

        static bool failedOnce;
        // dernier état appliqué par pid, pour ne pas spammer l'API
        static readonly Dictionary<uint, int> applied = new Dictionary<uint, int>();

        /// <summary>Applique un volume (0–100, -1 = muet) à chaque processus listé.</summary>
        public static void Apply(Dictionary<uint, int> targets, bool force)
        {
            if (targets.Count == 0) return;
            if (!force)
            {
                bool same = true;
                foreach (KeyValuePair<uint, int> kv in targets)
                {
                    int v;
                    if (!applied.TryGetValue(kv.Key, out v) || v != kv.Value) { same = false; break; }
                }
                if (same) return;
            }
            IMMDeviceEnumerator en = null;
            IMMDevice dev = null;
            object mgrObj = null;
            IAudioSessionEnumerator sessions = null;
            try
            {
                en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (en.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out dev) != 0 || dev == null) return;
                Guid iid = typeof(IAudioSessionManager2).GUID;
                if (dev.Activate(ref iid, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out mgrObj) != 0 || mgrObj == null) return;
                IAudioSessionManager2 mgr = (IAudioSessionManager2)mgrObj;
                if (mgr.GetSessionEnumerator(out sessions) != 0 || sessions == null) return;
                int count;
                sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl2 ctl = null;
                    try
                    {
                        if (sessions.GetSession(i, out ctl) != 0 || ctl == null) continue;
                        uint pid;
                        if (ctl.GetProcessId(out pid) != 0) continue;
                        int target;
                        if (!targets.TryGetValue(pid, out target)) continue;
                        ISimpleAudioVolume vol = ctl as ISimpleAudioVolume;
                        if (vol == null) continue;
                        if (target < 0) vol.SetMute(true, IntPtr.Zero);
                        else
                        {
                            vol.SetMasterVolume(Math.Max(0, Math.Min(100, target)) / 100f, IntPtr.Zero);
                            vol.SetMute(false, IntPtr.Zero);
                        }
                    }
                    finally { if (ctl != null) Marshal.ReleaseComObject(ctl); }
                }
                foreach (KeyValuePair<uint, int> kv in targets) applied[kv.Key] = kv.Value;
            }
            catch (Exception ex)
            {
                if (!failedOnce) { failedOnce = true; Program.Log("Audio : " + ex.Message); }
            }
            finally
            {
                if (sessions != null) Marshal.ReleaseComObject(sessions);
                if (mgrObj != null) Marshal.ReleaseComObject(mgrObj);
                if (dev != null) Marshal.ReleaseComObject(dev);
                if (en != null) Marshal.ReleaseComObject(en);
            }
        }

        public static void Forget() { applied.Clear(); }
    }
}
