using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Audeze Maxwell Volume Linker")]
[assembly: AssemblyDescription("Synchronizes Game and Chat or custom audio stream volumes on Windows")]
[assembly: AssemblyProduct("Audeze Maxwell Volume Linker")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace VolumeLinker
{
    #region CoreAudio COM Interop

    internal enum EDataFlow
    {
        eRender = 0,
        eCapture = 1,
        eAll = 2
    }

    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [Flags]
    internal enum DeviceState : uint
    {
        ACTIVE = 0x00000001,
        DISABLED = 0x00000002,
        NOTPRESENT = 0x00000004,
        UNPLUGGED = 0x00000008,
        MASK_ALL = 0x0000000F
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;

        public PROPERTYKEY(Guid fmtid, uint pid)
        {
            this.fmtid = fmtid;
            this.pid = pid;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public IntPtr p;
        public int p2;

        public string GetString()
        {
            if (vt == 31 && p != IntPtr.Zero) // VT_LPWSTR
            {
                return Marshal.PtrToStringUni(p);
            }
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AUDIO_VOLUME_NOTIFICATION_DATA
    {
        public Guid guidEventContext;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bMuted;
        public float fMasterVolume;
        public uint nChannels;
        public float afChannelVolumes;
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorComObject
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState dwStateMask, out IMMDeviceCollection ppDevices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
    }

    [ComImport]
    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint pcDevices);

        [PreserveSig]
        int Item(uint nDevice, out IMMDevice ppDevice);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

        [PreserveSig]
        int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);

        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

        [PreserveSig]
        int GetState(out DeviceState pdwState);
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint cProps);

        [PreserveSig]
        int GetAt(uint iProp, out PROPERTYKEY pkey);

        [PreserveSig]
        int GetValue(ref PROPERTYKEY key, out PropVariant pv);

        [PreserveSig]
        int SetValue(ref PROPERTYKEY key, ref PropVariant propvar);

        [PreserveSig]
        int Commit();
    }

    [ComImport]
    [Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMNotificationClient
    {
        [PreserveSig]
        int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, DeviceState dwNewState);

        [PreserveSig]
        int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

        [PreserveSig]
        int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);

        [PreserveSig]
        int OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);

        [PreserveSig]
        int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig]
        int RegisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);

        [PreserveSig]
        int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback pNotify);

        [PreserveSig]
        int GetChannelCount(out uint pnChannelCount);

        [PreserveSig]
        int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);

        [PreserveSig]
        int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);

        [PreserveSig]
        int GetMasterVolumeLevel(out float pfLevelDB);

        [PreserveSig]
        int GetMasterVolumeLevelScalar(out float pfLevel);

        [PreserveSig]
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);

        [PreserveSig]
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);

        [PreserveSig]
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);

        [PreserveSig]
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);

        [PreserveSig]
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);

        [PreserveSig]
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);

        [PreserveSig]
        int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);

        [PreserveSig]
        int VolumeStepUp(ref Guid pguidEventContext);

        [PreserveSig]
        int VolumeStepDown(ref Guid pguidEventContext);

        [PreserveSig]
        int QueryHardwareSupport(out uint pdwHardwareSupportMask);

        [PreserveSig]
        int GetVolumeRange(out float pflVolumeMinDB, out float pflVolumeMaxDB, out float pflVolumeIncrementDB);
    }

    [ComImport]
    [Guid("657804FA-D6AD-4496-8A60-352752AF4F89")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolumeCallback
    {
        [PreserveSig]
        int OnNotify(IntPtr pNotifyData);
    }

    internal static class NativeMethods
    {
        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PropVariant pvar);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();
    }

    #endregion

    #region Settings & Models

    internal sealed class AudioEndpointInfo
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public override string ToString()
        {
            return Name ?? Id ?? "Unknown Device";
        }
    }

    internal sealed class LinkerPreferences
    {
        private const string RegistryKeyPath = @"Software\AudezeMaxwellVolumeLinker";

        public bool AutoDetectMaxwell { get; set; }
        public string Device1Id { get; set; }
        public string Device1Name { get; set; }
        public string Device2Id { get; set; }
        public string Device2Name { get; set; }

        public LinkerPreferences()
        {
            AutoDetectMaxwell = true;
            Device1Id = "";
            Device1Name = "Speakers (Game-Audeze Maxwell)";
            Device2Id = "";
            Device2Name = "Speakers (Chat-Audeze Maxwell)";
        }

        public static LinkerPreferences Load()
        {
            var prefs = new LinkerPreferences();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    if (key != null)
                    {
                        object autoVal = key.GetValue("AutoDetectMaxwell");
                        if (autoVal != null)
                        {
                            prefs.AutoDetectMaxwell = Convert.ToInt32(autoVal) != 0;
                        }
                        prefs.Device1Id = (key.GetValue("Device1Id") as string) ?? "";
                        prefs.Device1Name = (key.GetValue("Device1Name") as string) ?? prefs.Device1Name;
                        prefs.Device2Id = (key.GetValue("Device2Id") as string) ?? "";
                        prefs.Device2Name = (key.GetValue("Device2Name") as string) ?? prefs.Device2Name;
                    }
                }
            }
            catch { }
            return prefs;
        }

        public void Save()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
                {
                    if (key != null)
                    {
                        key.SetValue("AutoDetectMaxwell", AutoDetectMaxwell ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("Device1Id", Device1Id ?? "", RegistryValueKind.String);
                        key.SetValue("Device1Name", Device1Name ?? "", RegistryValueKind.String);
                        key.SetValue("Device2Id", Device2Id ?? "", RegistryValueKind.String);
                        key.SetValue("Device2Name", Device2Name ?? "", RegistryValueKind.String);
                    }
                }
            }
            catch { }
        }
    }

    #endregion

    #region Audio Linker Engine

    internal sealed class VolumeNotificationHandler : IAudioEndpointVolumeCallback
    {
        private readonly Action<float, bool, Guid> _onVolumeChanged;

        public VolumeNotificationHandler(Action<float, bool, Guid> onVolumeChanged)
        {
            _onVolumeChanged = onVolumeChanged;
        }

        public int OnNotify(IntPtr pNotifyData)
        {
            if (pNotifyData == IntPtr.Zero) return 0;
            try
            {
                var data = (AUDIO_VOLUME_NOTIFICATION_DATA)Marshal.PtrToStructure(
                    pNotifyData, typeof(AUDIO_VOLUME_NOTIFICATION_DATA));
                _onVolumeChanged(data.fMasterVolume, data.bMuted, data.guidEventContext);
            }
            catch
            {
                // Ignore transient marshaling errors
            }
            return 0;
        }
    }

    internal sealed class DeviceNotificationHandler : IMMNotificationClient
    {
        private readonly Action _onTopologyChanged;

        public DeviceNotificationHandler(Action onTopologyChanged)
        {
            _onTopologyChanged = onTopologyChanged;
        }

        public int OnDeviceStateChanged(string pwstrDeviceId, DeviceState dwNewState)
        {
            _onTopologyChanged();
            return 0;
        }

        public int OnDeviceAdded(string pwstrDeviceId)
        {
            _onTopologyChanged();
            return 0;
        }

        public int OnDeviceRemoved(string pwstrDeviceId)
        {
            _onTopologyChanged();
            return 0;
        }

        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
        {
            if (flow == EDataFlow.eRender)
            {
                _onTopologyChanged();
            }
            return 0;
        }

        public int OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key)
        {
            return 0;
        }
    }

    internal sealed class AudioStreamLink : IDisposable
    {
        private static readonly Guid IID_IAudioEndpointVolume = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        private static readonly PROPERTYKEY PKEY_Device_FriendlyName = new PROPERTYKEY(
            new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);

        private readonly object _syncLock = new object();
        private readonly Guid _appContextGuid = Guid.NewGuid();

        private IMMDeviceEnumerator _enumerator;
        private DeviceNotificationHandler _deviceCallback;

        private IMMDevice _device1;
        private IAudioEndpointVolume _volume1;
        private VolumeNotificationHandler _callback1;
        private string _device1Name = "Game-Audeze Maxwell";
        private string _device1Id = "";

        private IMMDevice _device2;
        private IAudioEndpointVolume _volume2;
        private VolumeNotificationHandler _callback2;
        private string _device2Name = "Chat-Audeze Maxwell";
        private string _device2Id = "";

        private LinkerPreferences _prefs;
        private bool _syncEnabled = true;
        private bool _disposed = false;

        public event Action StateChanged;

        public bool SyncEnabled
        {
            get { lock (_syncLock) return _syncEnabled; }
            set
            {
                lock (_syncLock)
                {
                    if (_syncEnabled == value) return;
                    _syncEnabled = value;
                    if (_syncEnabled)
                    {
                        SyncDevice1ToDevice2Internal();
                    }
                }
                NotifyStateChanged();
            }
        }

        public LinkerPreferences Preferences
        {
            get { lock (_syncLock) return _prefs; }
        }

        public bool IsConnected { get; private set; }
        public float CurrentVolume { get; private set; }
        public bool IsMuted { get; private set; }
        public string StatusText { get; private set; }
        public string Device1EndpointName { get { lock (_syncLock) return _device1Name; } }
        public string Device2EndpointName { get { lock (_syncLock) return _device2Name; } }
        public string Device1EndpointId { get { lock (_syncLock) return _device1Id; } }
        public string Device2EndpointId { get { lock (_syncLock) return _device2Id; } }

        public AudioStreamLink()
        {
            _prefs = LinkerPreferences.Load();
            StatusText = "Searching for audio devices...";
        }

        public void Initialize()
        {
            lock (_syncLock)
            {
                _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                _deviceCallback = new DeviceNotificationHandler(OnDeviceTopologyChanged);
                _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
                RefreshEndpointsInternal();
            }
            NotifyStateChanged();
        }

        public void UpdatePreferences(LinkerPreferences newPrefs)
        {
            if (newPrefs == null) return;
            lock (_syncLock)
            {
                _prefs = newPrefs;
                _prefs.Save();
                RefreshEndpointsInternal();
            }
            NotifyStateChanged();
        }

        public List<AudioEndpointInfo> GetActiveRenderEndpoints()
        {
            var list = new List<AudioEndpointInfo>();
            lock (_syncLock)
            {
                if (_enumerator == null) return list;
                IMMDeviceCollection collection;
                if (_enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.ACTIVE, out collection) != 0 || collection == null)
                {
                    return list;
                }

                try
                {
                    uint count;
                    collection.GetCount(out count);
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice dev;
                        if (collection.Item(i, out dev) != 0 || dev == null) continue;
                        try
                        {
                            string id;
                            dev.GetId(out id);
                            string name = GetFriendlyName(dev);
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                            {
                                list.Add(new AudioEndpointInfo { Id = id, Name = name });
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(collection);
                }
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public void ForceRefreshAndSync()
        {
            lock (_syncLock)
            {
                RefreshEndpointsInternal();
            }
            NotifyStateChanged();
        }

        private void OnDeviceTopologyChanged()
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(250);
                lock (_syncLock)
                {
                    if (_disposed) return;
                    RefreshEndpointsInternal();
                }
                NotifyStateChanged();
            });
        }

        private void RefreshEndpointsInternal()
        {
            DetachEndpointsInternal();

            if (_enumerator == null) return;

            IMMDeviceCollection collection;
            int hr = _enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.ACTIVE, out collection);
            if (hr != 0 || collection == null)
            {
                UpdateStatusSnapshot(false, 0f, false, "No active audio outputs");
                return;
            }

            try
            {
                uint count;
                collection.GetCount(out count);

                IMMDevice found1 = null;
                string found1Name = null;
                string found1Id = null;

                IMMDevice found2 = null;
                string found2Name = null;
                string found2Id = null;

                for (uint i = 0; i < count; i++)
                {
                    IMMDevice dev;
                    if (collection.Item(i, out dev) != 0 || dev == null) continue;

                    string devId;
                    dev.GetId(out devId);
                    string friendlyName = GetFriendlyName(dev);
                    if (string.IsNullOrEmpty(friendlyName))
                    {
                        Marshal.ReleaseComObject(dev);
                        continue;
                    }

                    if (_prefs.AutoDetectMaxwell)
                    {
                        bool isMaxwell = friendlyName.IndexOf("Maxwell", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         friendlyName.IndexOf("Audeze", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isMaxwell)
                        {
                            if (friendlyName.IndexOf("Game", StringComparison.OrdinalIgnoreCase) >= 0 && found1 == null)
                            {
                                found1 = dev;
                                found1Name = friendlyName;
                                found1Id = devId;
                                continue;
                            }
                            if (friendlyName.IndexOf("Chat", StringComparison.OrdinalIgnoreCase) >= 0 && found2 == null)
                            {
                                found2 = dev;
                                found2Name = friendlyName;
                                found2Id = devId;
                                continue;
                            }
                        }
                    }
                    else
                    {
                        // Match by ID first, or fall back to exact FriendlyName match if Windows reassigned endpoint ID on USB reconnect
                        bool match1 = (!string.IsNullOrEmpty(_prefs.Device1Id) && string.Equals(devId, _prefs.Device1Id, StringComparison.OrdinalIgnoreCase)) ||
                                      (!string.IsNullOrEmpty(_prefs.Device1Name) && string.Equals(friendlyName, _prefs.Device1Name, StringComparison.OrdinalIgnoreCase));

                        if (match1 && found1 == null)
                        {
                            found1 = dev;
                            found1Name = friendlyName;
                            found1Id = devId;
                            continue;
                        }

                        bool match2 = (!string.IsNullOrEmpty(_prefs.Device2Id) && string.Equals(devId, _prefs.Device2Id, StringComparison.OrdinalIgnoreCase)) ||
                                      (!string.IsNullOrEmpty(_prefs.Device2Name) && string.Equals(friendlyName, _prefs.Device2Name, StringComparison.OrdinalIgnoreCase));

                        if (match2 && found2 == null && !string.Equals(devId, found1Id, StringComparison.OrdinalIgnoreCase))
                        {
                            found2 = dev;
                            found2Name = friendlyName;
                            found2Id = devId;
                            continue;
                        }
                    }

                    Marshal.ReleaseComObject(dev);
                }

                if (found1 != null && found2 != null)
                {
                    _device1 = found1;
                    _device1Name = found1Name;
                    _device1Id = found1Id;

                    _device2 = found2;
                    _device2Name = found2Name;
                    _device2Id = found2Id;

                    Guid iid = IID_IAudioEndpointVolume;
                    object volObj1, volObj2;

                    int hr1 = _device1.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out volObj1);
                    int hr2 = _device2.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out volObj2);

                    if (hr1 == 0 && hr2 == 0 && volObj1 != null && volObj2 != null)
                    {
                        _volume1 = (IAudioEndpointVolume)volObj1;
                        _volume2 = (IAudioEndpointVolume)volObj2;

                        _callback1 = new VolumeNotificationHandler(OnDevice1VolumeChanged);
                        _callback2 = new VolumeNotificationHandler(OnDevice2VolumeChanged);

                        _volume1.RegisterControlChangeNotify(_callback1);
                        _volume2.RegisterControlChangeNotify(_callback2);

                        if (_syncEnabled)
                        {
                            SyncDevice1ToDevice2Internal();
                        }
                        else
                        {
                            float vol;
                            bool mute;
                            _volume1.GetMasterVolumeLevelScalar(out vol);
                            _volume1.GetMute(out mute);
                            UpdateStatusSnapshot(true, vol, mute, "Connected (Sync Paused)");
                        }
                        return;
                    }
                    else
                    {
                        DetachEndpointsInternal();
                        UpdateStatusSnapshot(false, 0f, false, "Error activating volume interface");
                        return;
                    }
                }
                else
                {
                    if (found1 != null) Marshal.ReleaseComObject(found1);
                    if (found2 != null) Marshal.ReleaseComObject(found2);

                    if (found1 != null || found2 != null)
                    {
                        string waitingFor = found1 == null
                            ? (_prefs.AutoDetectMaxwell ? "Game-Audeze Maxwell" : ShortenName(_prefs.Device1Name))
                            : (_prefs.AutoDetectMaxwell ? "Chat-Audeze Maxwell" : ShortenName(_prefs.Device2Name));
                        UpdateStatusSnapshot(false, 0f, false, "Waiting for " + waitingFor + "...");
                    }
                    else
                    {
                        UpdateStatusSnapshot(false, 0f, false,
                            _prefs.AutoDetectMaxwell
                                ? "Disconnected (Waiting for Audeze Maxwell...)"
                                : "Disconnected (Waiting for selected devices...)");
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(collection);
            }
        }

        private static string ShortenName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "second stream";
            if (name.Length > 28) return name.Substring(0, 25) + "...";
            return name;
        }

        private void SyncDevice1ToDevice2Internal()
        {
            if (_volume1 == null || _volume2 == null) return;

            try
            {
                float vol1, vol2;
                bool mute1, mute2;

                if (_volume1.GetMasterVolumeLevelScalar(out vol1) == 0 &&
                    _volume1.GetMute(out mute1) == 0)
                {
                    _volume2.GetMasterVolumeLevelScalar(out vol2);
                    _volume2.GetMute(out mute2);

                    Guid ctx = _appContextGuid;
                    if (Math.Abs(vol1 - vol2) > 0.001f)
                    {
                        _volume2.SetMasterVolumeLevelScalar(vol1, ref ctx);
                    }
                    if (mute1 != mute2)
                    {
                        _volume2.SetMute(mute1, ref ctx);
                    }

                    UpdateStatusSnapshot(true, vol1, mute1,
                        string.Format("Linked: {0}%{1}", Math.Round(vol1 * 100), mute1 ? " (Muted)" : ""));
                }
            }
            catch
            {
            }
        }

        private void OnDevice1VolumeChanged(float newVolume, bool newMute, Guid eventContext)
        {
            if (eventContext == _appContextGuid) return;

            bool shouldNotify = false;
            lock (_syncLock)
            {
                if (_disposed || _volume2 == null) return;

                if (_syncEnabled)
                {
                    try
                    {
                        Guid ctx = _appContextGuid;
                        float targetVol;
                        if (_volume2.GetMasterVolumeLevelScalar(out targetVol) == 0 &&
                            Math.Abs(targetVol - newVolume) > 0.001f)
                        {
                            _volume2.SetMasterVolumeLevelScalar(newVolume, ref ctx);
                        }

                        bool targetMute;
                        if (_volume2.GetMute(out targetMute) == 0 && targetMute != newMute)
                        {
                            _volume2.SetMute(newMute, ref ctx);
                        }
                    }
                    catch
                    {
                    }
                }

                UpdateStatusSnapshot(true, newVolume, newMute,
                    _syncEnabled
                        ? string.Format("Linked: {0}%{1}", Math.Round(newVolume * 100), newMute ? " (Muted)" : "")
                        : "Connected (Sync Paused)");
                shouldNotify = true;
            }

            if (shouldNotify) NotifyStateChanged();
        }

        private void OnDevice2VolumeChanged(float newVolume, bool newMute, Guid eventContext)
        {
            if (eventContext == _appContextGuid) return;

            bool shouldNotify = false;
            lock (_syncLock)
            {
                if (_disposed || _volume1 == null) return;

                if (_syncEnabled)
                {
                    try
                    {
                        Guid ctx = _appContextGuid;
                        float targetVol;
                        if (_volume1.GetMasterVolumeLevelScalar(out targetVol) == 0 &&
                            Math.Abs(targetVol - newVolume) > 0.001f)
                        {
                            _volume1.SetMasterVolumeLevelScalar(newVolume, ref ctx);
                        }

                        bool targetMute;
                        if (_volume1.GetMute(out targetMute) == 0 && targetMute != newMute)
                        {
                            _volume1.SetMute(newMute, ref ctx);
                        }
                    }
                    catch
                    {
                    }
                }

                UpdateStatusSnapshot(true, newVolume, newMute,
                    _syncEnabled
                        ? string.Format("Linked: {0}%{1}", Math.Round(newVolume * 100), newMute ? " (Muted)" : "")
                        : "Connected (Sync Paused)");
                shouldNotify = true;
            }

            if (shouldNotify) NotifyStateChanged();
        }

        private void UpdateStatusSnapshot(bool connected, float volume, bool muted, string statusText)
        {
            IsConnected = connected;
            CurrentVolume = volume;
            IsMuted = muted;
            StatusText = statusText;
        }

        private static string GetFriendlyName(IMMDevice device)
        {
            IPropertyStore props = null;
            try
            {
                if (device.OpenPropertyStore(0 /* STGM_READ */, out props) == 0 && props != null)
                {
                    PROPERTYKEY key = PKEY_Device_FriendlyName;
                    PropVariant val;
                    if (props.GetValue(ref key, out val) == 0)
                    {
                        try
                        {
                            return val.GetString();
                        }
                        finally
                        {
                            NativeMethods.PropVariantClear(ref val);
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                if (props != null) Marshal.ReleaseComObject(props);
            }
            return null;
        }

        private void DetachEndpointsInternal()
        {
            if (_volume1 != null)
            {
                if (_callback1 != null)
                {
                    try { _volume1.UnregisterControlChangeNotify(_callback1); } catch { }
                    _callback1 = null;
                }
                try { Marshal.ReleaseComObject(_volume1); } catch { }
                _volume1 = null;
            }

            if (_volume2 != null)
            {
                if (_callback2 != null)
                {
                    try { _volume2.UnregisterControlChangeNotify(_callback2); } catch { }
                    _callback2 = null;
                }
                try { Marshal.ReleaseComObject(_volume2); } catch { }
                _volume2 = null;
            }

            if (_device1 != null)
            {
                try { Marshal.ReleaseComObject(_device1); } catch { }
                _device1 = null;
            }

            if (_device2 != null)
            {
                try { Marshal.ReleaseComObject(_device2); } catch { }
                _device2 = null;
            }
        }

        private void NotifyStateChanged()
        {
            var handler = StateChanged;
            if (handler != null)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { handler(); } catch { }
                });
            }
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_disposed) return;
                _disposed = true;

                DetachEndpointsInternal();

                if (_enumerator != null)
                {
                    if (_deviceCallback != null)
                    {
                        try { _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback); } catch { }
                        _deviceCallback = null;
                    }
                    try { Marshal.ReleaseComObject(_enumerator); } catch { }
                    _enumerator = null;
                }
            }
        }
    }

    #endregion

    #region Device Selection Dialog

    internal sealed class DeviceSelectionForm : Form
    {
        private readonly AudioStreamLink _linker;
        private readonly RadioButton _rbAutoMaxwell;
        private readonly RadioButton _rbCustom;
        private readonly ComboBox _cbDevice1;
        private readonly ComboBox _cbDevice2;
        private readonly Button _btnRefresh;
        private readonly Button _btnSave;
        private readonly Label _lblCurrentStatus;
        private List<AudioEndpointInfo> _endpoints = new List<AudioEndpointInfo>();

        public DeviceSelectionForm(AudioStreamLink linker, Icon appIcon)
        {
            _linker = linker;
            Icon = appIcon;
            Text = "Select Audio Devices to Sync";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 355);
            BackColor = Color.FromArgb(249, 250, 251);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Color.FromArgb(17, 24, 39)
            };

            var titleLabel = new Label
            {
                Text = "Select Synced Audio Streams",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(20, 12)
            };

            var subLabel = new Label
            {
                Text = "Choose which two playback streams should mirror volume & mute state",
                ForeColor = Color.FromArgb(156, 163, 175),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(22, 37)
            };

            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subLabel);

            _rbAutoMaxwell = new RadioButton
            {
                Text = "Auto-detect Audeze Maxwell (Game && Chat streams)",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                AutoSize = true,
                Location = new Point(22, 80)
            };

            _rbCustom = new RadioButton
            {
                Text = "Custom audio devices:",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                AutoSize = true,
                Location = new Point(22, 112)
            };

            var lblDev1 = new Label
            {
                Text = "Primary Stream (Device 1):",
                AutoSize = true,
                Location = new Point(44, 144),
                ForeColor = Color.FromArgb(55, 65, 81)
            };

            _cbDevice1 = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(44, 166),
                Size = new Size(412, 26)
            };

            var lblDev2 = new Label
            {
                Text = "Secondary Stream (Device 2):",
                AutoSize = true,
                Location = new Point(44, 202),
                ForeColor = Color.FromArgb(55, 65, 81)
            };

            _cbDevice2 = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(44, 224),
                Size = new Size(412, 26)
            };

            _lblCurrentStatus = new Label
            {
                Text = "",
                AutoSize = true,
                Location = new Point(22, 266),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(107, 114, 128)
            };

            var divider = new Panel
            {
                BackColor = Color.FromArgb(229, 231, 235),
                Location = new Point(0, 298),
                Size = new Size(480, 1)
            };

            _btnRefresh = new Button
            {
                Text = "Refresh List",
                Location = new Point(22, 310),
                Size = new Size(100, 32),
                FlatStyle = FlatStyle.System
            };
            _btnRefresh.Click += (s, e) => PopulateDevices();

            _btnSave = new Button
            {
                Text = "Save && Sync",
                Location = new Point(256, 310),
                Size = new Size(108, 32),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += OnSaveClick;

            var btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(372, 310),
                Size = new Size(84, 32),
                FlatStyle = FlatStyle.System
            };
            btnCancel.Click += (s, e) => Close();

            _rbAutoMaxwell.CheckedChanged += (s, e) => UpdateControlStates();
            _rbCustom.CheckedChanged += (s, e) => UpdateControlStates();

            Controls.Add(headerPanel);
            Controls.Add(_rbAutoMaxwell);
            Controls.Add(_rbCustom);
            Controls.Add(lblDev1);
            Controls.Add(_cbDevice1);
            Controls.Add(lblDev2);
            Controls.Add(_cbDevice2);
            Controls.Add(_lblCurrentStatus);
            Controls.Add(divider);
            Controls.Add(_btnRefresh);
            Controls.Add(_btnSave);
            Controls.Add(btnCancel);

            AcceptButton = _btnSave;
            CancelButton = btnCancel;

            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            LinkerPreferences prefs = _linker.Preferences;
            _rbAutoMaxwell.Checked = prefs.AutoDetectMaxwell;
            _rbCustom.Checked = !prefs.AutoDetectMaxwell;

            PopulateDevices();
            UpdateControlStates();
        }

        private void PopulateDevices()
        {
            LinkerPreferences prefs = _linker.Preferences;
            _endpoints = _linker.GetActiveRenderEndpoints();

            // If user had a saved device that is currently unplugged/off, keep it selectable in the list
            EnsureSavedEndpointPresent(prefs.Device1Id, prefs.Device1Name);
            EnsureSavedEndpointPresent(prefs.Device2Id, prefs.Device2Name);

            _cbDevice1.Items.Clear();
            _cbDevice2.Items.Clear();

            int sel1 = -1;
            int sel2 = -1;

            string target1Id = !string.IsNullOrEmpty(prefs.Device1Id) ? prefs.Device1Id : _linker.Device1EndpointId;
            string target1Name = !string.IsNullOrEmpty(prefs.Device1Name) ? prefs.Device1Name : _linker.Device1EndpointName;
            string target2Id = !string.IsNullOrEmpty(prefs.Device2Id) ? prefs.Device2Id : _linker.Device2EndpointId;
            string target2Name = !string.IsNullOrEmpty(prefs.Device2Name) ? prefs.Device2Name : _linker.Device2EndpointName;

            for (int i = 0; i < _endpoints.Count; i++)
            {
                AudioEndpointInfo ep = _endpoints[i];
                _cbDevice1.Items.Add(ep);
                _cbDevice2.Items.Add(ep);

                if (sel1 < 0 && ((!string.IsNullOrEmpty(target1Id) && string.Equals(ep.Id, target1Id, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(target1Name) && ep.Name.IndexOf(target1Name, StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    sel1 = i;
                }

                if (sel2 < 0 && ((!string.IsNullOrEmpty(target2Id) && string.Equals(ep.Id, target2Id, StringComparison.OrdinalIgnoreCase)) ||
                                 (!string.IsNullOrEmpty(target2Name) && ep.Name.IndexOf(target2Name, StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    sel2 = i;
                }
            }

            if (_cbDevice1.Items.Count > 0)
            {
                _cbDevice1.SelectedIndex = sel1 >= 0 ? sel1 : 0;
            }
            if (_cbDevice2.Items.Count > 0)
            {
                int fallback2 = _cbDevice2.Items.Count > 1 ? 1 : 0;
                _cbDevice2.SelectedIndex = sel2 >= 0 ? sel2 : fallback2;
            }

            _lblCurrentStatus.Text = "Currently " + _linker.StatusText;
        }

        private void EnsureSavedEndpointPresent(string id, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            foreach (var ep in _endpoints)
            {
                if ((!string.IsNullOrEmpty(id) && string.Equals(ep.Id, id, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(ep.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            _endpoints.Add(new AudioEndpointInfo
            {
                Id = id ?? "",
                Name = name
            });
        }

        private void UpdateControlStates()
        {
            bool custom = _rbCustom.Checked;
            _cbDevice1.Enabled = custom;
            _cbDevice2.Enabled = custom;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            if (_rbCustom.Checked)
            {
                var dev1 = _cbDevice1.SelectedItem as AudioEndpointInfo;
                var dev2 = _cbDevice2.SelectedItem as AudioEndpointInfo;

                if (dev1 == null || dev2 == null)
                {
                    MessageBox.Show(
                        this,
                        "Please select two audio output devices to link.",
                        "Select Devices",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                if ((!string.IsNullOrEmpty(dev1.Id) && string.Equals(dev1.Id, dev2.Id, StringComparison.OrdinalIgnoreCase)) ||
                    string.Equals(dev1.Name, dev2.Name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        this,
                        "Please select two different audio devices to link together.",
                        "Duplicate Selection",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                var newPrefs = new LinkerPreferences
                {
                    AutoDetectMaxwell = false,
                    Device1Id = dev1.Id,
                    Device1Name = dev1.Name,
                    Device2Id = dev2.Id,
                    Device2Name = dev2.Name
                };
                _linker.UpdatePreferences(newPrefs);
            }
            else
            {
                var current = _linker.Preferences;
                var newPrefs = new LinkerPreferences
                {
                    AutoDetectMaxwell = true,
                    Device1Id = current.Device1Id,
                    Device1Name = current.Device1Name,
                    Device2Id = current.Device2Id,
                    Device2Name = current.Device2Name
                };
                _linker.UpdatePreferences(newPrefs);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    #endregion

    #region Tray Application UI

    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupRegistryValueName = "AudezeMaxwellVolumeLinker";

        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _headerItem;
        private readonly ToolStripMenuItem _statusItem;
        private readonly ToolStripMenuItem _pairInfoItem;
        private readonly ToolStripMenuItem _selectDevicesItem;
        private readonly ToolStripMenuItem _syncEnabledItem;
        private readonly ToolStripMenuItem _resyncNowItem;
        private readonly ToolStripMenuItem _startupItem;
        private readonly SynchronizationContext _uiContext;
        private readonly AudioStreamLink _linker;
        private readonly System.Windows.Forms.Timer _watchdogTimer;

        private Icon _iconConnected;
        private Icon _iconMuted;
        private Icon _iconDisconnected;
        private DeviceSelectionForm _activeSelectionForm;

        public TrayApplicationContext()
        {
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _iconConnected = CreateStatusIcon(Color.FromArgb(16, 185, 129), true);   // Emerald green
            _iconMuted = CreateStatusIcon(Color.FromArgb(245, 158, 11), true);       // Amber
            _iconDisconnected = CreateStatusIcon(Color.FromArgb(107, 114, 128), false); // Cool gray

            _menu = new ContextMenuStrip();

            _headerItem = new ToolStripMenuItem("Audeze Maxwell Volume Linker")
            {
                Enabled = false,
                Font = new Font(_menu.Font, FontStyle.Bold)
            };

            _statusItem = new ToolStripMenuItem("Status: Initializing...")
            {
                Enabled = false
            };

            _pairInfoItem = new ToolStripMenuItem("Mode: Auto (Audeze Maxwell)")
            {
                Enabled = false
            };

            _selectDevicesItem = new ToolStripMenuItem("Select Synced Devices...");
            _selectDevicesItem.Click += (s, e) => ShowDeviceSelectionDialog();

            _syncEnabledItem = new ToolStripMenuItem("Link Audio Volumes")
            {
                Checked = true,
                CheckOnClick = true
            };
            _syncEnabledItem.CheckedChanged += (s, e) =>
            {
                _linker.SyncEnabled = _syncEnabledItem.Checked;
            };

            _resyncNowItem = new ToolStripMenuItem("Reconnect / Sync Now");
            _resyncNowItem.Click += (s, e) =>
            {
                _linker.ForceRefreshAndSync();
            };

            _startupItem = new ToolStripMenuItem("Start Automatically on Login")
            {
                Checked = IsStartupEnabled(),
                CheckOnClick = true
            };
            _startupItem.CheckedChanged += (s, e) =>
            {
                SetStartupEnabled(_startupItem.Checked);
            };

            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplication();

            _menu.Items.Add(_headerItem);
            _menu.Items.Add(_statusItem);
            _menu.Items.Add(_pairInfoItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_selectDevicesItem);
            _menu.Items.Add(_syncEnabledItem);
            _menu.Items.Add(_resyncNowItem);
            _menu.Items.Add(_startupItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);

            _trayIcon = new NotifyIcon
            {
                Icon = _iconDisconnected,
                Text = "Audeze Maxwell Volume Linker",
                ContextMenuStrip = _menu,
                Visible = true
            };

            _trayIcon.DoubleClick += (s, e) =>
            {
                ShowDeviceSelectionDialog();
            };

            if (IsStartupEnabled())
            {
                SetStartupEnabled(true);
            }

            _linker = new AudioStreamLink();
            _linker.StateChanged += OnLinkerStateChanged;
            _linker.Initialize();

            _watchdogTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            _watchdogTimer.Tick += (s, e) =>
            {
                if (!_linker.IsConnected)
                {
                    _linker.ForceRefreshAndSync();
                }
            };
            _watchdogTimer.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        private void ShowDeviceSelectionDialog()
        {
            if (_activeSelectionForm != null && !_activeSelectionForm.IsDisposed)
            {
                _activeSelectionForm.Activate();
                return;
            }

            using (_activeSelectionForm = new DeviceSelectionForm(_linker, _iconConnected))
            {
                _activeSelectionForm.ShowDialog();
            }
            _activeSelectionForm = null;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(1500);
                    _linker.ForceRefreshAndSync();
                });
            }
        }

        private void OnLinkerStateChanged()
        {
            _uiContext.Post(_ => UpdateTrayUI(), null);
        }

        private void UpdateTrayUI()
        {
            if (_trayIcon == null) return;

            bool connected = _linker.IsConnected;
            bool muted = _linker.IsMuted;
            bool syncEnabled = _linker.SyncEnabled;
            string status = _linker.StatusText;
            var prefs = _linker.Preferences;

            _statusItem.Text = status;

            if (prefs.AutoDetectMaxwell)
            {
                _pairInfoItem.Text = "Devices: Auto (Audeze Maxwell Game & Chat)";
            }
            else
            {
                _pairInfoItem.Text = string.Format("Devices: {0} <-> {1}",
                     prefs.Device1Name ?? "Device 1",
                     prefs.Device2Name ?? "Device 2");
            }

            if (!connected || !syncEnabled)
            {
                _trayIcon.Icon = _iconDisconnected;
            }
            else if (muted)
            {
                _trayIcon.Icon = _iconMuted;
            }
            else
            {
                _trayIcon.Icon = _iconConnected;
            }

            string tooltip = string.Format("Volume Linker\n{0}", status);
            if (tooltip.Length >= 63)
            {
                tooltip = tooltip.Substring(0, 62);
            }
            _trayIcon.Text = tooltip;
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false))
                {
                    if (key == null) return false;
                    object val = key.GetValue(StartupRegistryValueName);
                    return val != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SetStartupEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true))
                {
                    if (key == null) return;
                    if (enabled)
                    {
                        string exePath = Application.ExecutablePath;
                        key.SetValue(StartupRegistryValueName, "\"" + exePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(StartupRegistryValueName, false);
                    }
                }
            }
            catch
            {
            }
        }

        private static Icon CreateStatusIcon(Color accentColor, bool linked)
        {
            using (var bmp = new Bitmap(32, 32))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var bgBrush = new SolidBrush(accentColor))
                {
                    g.FillEllipse(bgBrush, 1, 1, 30, 30);
                }

                using (var pen = new Pen(Color.White, 2.6f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;

                    g.DrawArc(pen, 7, 6, 18, 16, 180, 180);

                    using (var cupBrush = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(cupBrush, 6, 14, 4, 8);
                        g.FillRectangle(cupBrush, 22, 14, 4, 8);
                    }

                    if (linked)
                    {
                        g.DrawLine(pen, 12, 18, 20, 18);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }

        private void ExitApplication()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _watchdogTimer.Stop();
            _watchdogTimer.Dispose();
            _trayIcon.Visible = false;
            _linker.Dispose();
            _trayIcon.Dispose();

            if (_iconConnected != null) _iconConnected.Dispose();
            if (_iconMuted != null) _iconMuted.Dispose();
            if (_iconDisconnected != null) _iconDisconnected.Dispose();

            Application.Exit();
        }
    }

    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--status", StringComparison.OrdinalIgnoreCase))
            {
                using (var link = new AudioStreamLink())
                {
                    link.Initialize();
                    Console.WriteLine("Connected: " + link.IsConnected);
                    Console.WriteLine("AutoDetectMaxwell: " + link.Preferences.AutoDetectMaxwell);
                    Console.WriteLine("Device1: " + link.Device1EndpointName);
                    Console.WriteLine("Device2: " + link.Device2EndpointName);
                    Console.WriteLine("Volume: " + Math.Round(link.CurrentVolume * 100) + "%");
                    Console.WriteLine("Muted: " + link.IsMuted);
                    Console.WriteLine("Status: " + link.StatusText);
                }
                return;
            }

            bool createdNew;
            _singleInstanceMutex = new Mutex(true, @"Local\AudezeMaxwellVolumeLinker_Mutex", out createdNew);
            if (!createdNew)
            {
                return;
            }

            try
            {
                NativeMethods.SetProcessDPIAware();
            }
            catch
            {
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool noStartupFlag = false;
            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "--no-startup", StringComparison.OrdinalIgnoreCase))
                    {
                        noStartupFlag = true;
                    }
                }
            }

            if (!noStartupFlag)
            {
                EnsureFirstRunStartupConfigured();
            }

            Application.Run(new TrayApplicationContext());
            GC.KeepAlive(_singleInstanceMutex);
        }

        private static void EnsureFirstRunStartupConfigured()
        {
            try
            {
                const string appSettingsKey = @"Software\AudezeMaxwellVolumeLinker";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(appSettingsKey))
                {
                    if (key != null && key.GetValue("InitializedStartup") == null)
                    {
                        TrayApplicationContext.SetStartupEnabled(true);
                        key.SetValue("InitializedStartup", 1);
                    }
                }
            }
            catch
            {
            }
        }
    }

    #endregion
}
