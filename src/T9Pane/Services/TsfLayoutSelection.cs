using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using T9Pane.Native;

namespace T9Pane.Services;

/// <summary>
/// 语言栏当前键盘 TIP。Vista+ 官方接口是
/// ITfInputProcessorProfileMgr::GetActiveProfile(GUID_TFCAT_TIP_KEYBOARD)。
/// CTF Assemblies / GetDefaultLanguageProfile 只是语言默认值，切 T9 不会改。
/// 本进程不要 Activate ITfThreadMgr，否则 GetActiveProfile 会变成这条线程自己的配置。
/// </summary>
internal static class TsfLayoutSelection
{
    private static readonly Guid ClsidProfiles = new("33C53A50-F456-4884-B049-85FD643ECFED");
    private static readonly Guid IidProfileMgr = new("71C6E74C-0F28-11D8-A82A-00065B84435C");
    private static readonly object Gate = new();
    private static readonly BlockingCollection<WorkItem> Queue = new();
    private static Thread? _sta;
    private static int _staThreadId;
    private static ITfInputProcessorProfileMgr? _mgr;
    private static Guid _loggedClsid = new("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");

    public static bool IsT9Selected()
    {
        if (TryGetActive(out var clsid, out var profile))
        {
            return OfficialT9ProfilePolicy.IsT9Layout(clsid, profile);
        }

        return TryReadAssembly(OfficialT9ProfilePolicy.SimplifiedChinese, out clsid, out profile)
            && OfficialT9ProfilePolicy.IsT9Layout(clsid, profile);
    }

    public static bool TryGetActive(out Guid clsid, out Guid profile)
    {
        EnsureSta();
        if (Environment.CurrentManagedThreadId == _staThreadId)
        {
            return ReadActive(out clsid, out profile);
        }

        var done = new ManualResetEventSlim(false);
        var result = false;
        var activeClsid = Guid.Empty;
        var activeProfile = Guid.Empty;
        Queue.Add(new WorkItem(() =>
        {
            result = ReadActive(out activeClsid, out activeProfile);
        }, done));
        if (!done.Wait(TimeSpan.FromSeconds(1)))
        {
            clsid = default;
            profile = default;
            return false;
        }

        clsid = activeClsid;
        profile = activeProfile;
        return result;
    }

    public static bool TryReadAssembly(ushort langid, out Guid clsid, out Guid profile)
    {
        clsid = default;
        profile = default;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(OfficialT9ProfilePolicy.AssemblyKey(langid));
            if (key is null)
            {
                return false;
            }

            return OfficialT9ProfilePolicy.TryParseAssembly(
                key.GetValue("Default") as string,
                key.GetValue("Profile") as string,
                out clsid,
                out profile);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureSta()
    {
        lock (Gate)
        {
            if (_sta is not null)
            {
                return;
            }

            var ready = new ManualResetEventSlim(false);
            _sta = new Thread(() => StaLoop(ready))
            {
                IsBackground = true,
                Name = "T9-TSF-ActiveProfile"
            };
            _sta.SetApartmentState(ApartmentState.STA);
            _sta.Start();
            ready.Wait(TimeSpan.FromSeconds(3));
        }
    }

    private static void StaLoop(ManualResetEventSlim ready)
    {
        _staThreadId = Environment.CurrentManagedThreadId;
        NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.CoinitApartmentThreaded);
        BindManager();
        ready.Set();
        foreach (var work in Queue.GetConsumingEnumerable())
        {
            try
            {
                work.Run();
            }
            catch (Exception ex)
            {
                Log.Warn($"读当前输入法配置失败: {ex.Message}");
            }
            finally
            {
                work.Done.Set();
            }
        }
    }

    private static void BindManager()
    {
        var clsid = ClsidProfiles;
        var iid = IidProfileMgr;
        var hr = NativeMethods.CoCreateInstance(
            ref clsid,
            IntPtr.Zero,
            NativeMethods.ClsctxInprocServer,
            ref iid,
            out var ptr);
        if (hr != 0 || ptr == IntPtr.Zero)
        {
            Log.Warn($"创建 ITfInputProcessorProfileMgr 失败: 0x{hr:X8}");
            return;
        }

        _mgr = (ITfInputProcessorProfileMgr)Marshal.GetObjectForIUnknown(ptr);
        Marshal.Release(ptr);
    }

    private static bool ReadActive(out Guid clsid, out Guid profile)
    {
        clsid = default;
        profile = default;
        if (_mgr is null)
        {
            BindManager();
        }

        if (_mgr is null)
        {
            return false;
        }

        var cat = OfficialT9ProfilePolicy.TipKeyboard;
        var hr = _mgr.GetActiveProfile(ref cat, out var active);
        if (hr != 0)
        {
            Log.Warn($"GetActiveProfile 失败: 0x{hr:X8}");
            return false;
        }

        clsid = active.Clsid;
        profile = active.Profile;
        if (clsid != _loggedClsid)
        {
            _loggedClsid = clsid;
            Log.Info($"GetActiveProfile {NameTip(clsid)} {clsid:D}");
        }

        return clsid != Guid.Empty;
    }

    private static string NameTip(Guid clsid)
    {
        if (clsid == OfficialT9ProfilePolicy.T9Clsid)
        {
            return "T9";
        }

        if (clsid == new Guid("81D4E9C9-1D3B-41BC-9E6C-4B40BF79E35E"))
        {
            return "微软拼音";
        }

        return "其他";
    }

    private sealed class WorkItem
    {
        public WorkItem(Action run, ManualResetEventSlim done)
        {
            Run = run;
            Done = done;
        }

        public Action Run { get; }
        public ManualResetEventSlim Done { get; }
    }
}

[ComImport]
[Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfInputProcessorProfileMgr
{
    [PreserveSig]
    int ActivateProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);

    [PreserveSig]
    int DeactivateProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, uint flags);

    [PreserveSig]
    int GetProfile(uint type, ushort langid, ref Guid clsid, ref Guid profile, IntPtr hkl, out TfInputProcessorProfile info);

    [PreserveSig]
    int EnumProfiles(ushort langid, out IntPtr enumerator);

    [PreserveSig]
    int ReleaseInputProcessor(ref Guid clsid, uint flags);

    [PreserveSig]
    int RegisterProfile(
        ref Guid clsid,
        ushort langid,
        ref Guid profile,
        [MarshalAs(UnmanagedType.LPWStr)] string desc,
        uint descLength,
        [MarshalAs(UnmanagedType.LPWStr)] string icon,
        uint iconLength,
        uint iconIndex,
        IntPtr substitute,
        uint preferredLayout,
        int enabledByDefault,
        uint flags);

    [PreserveSig]
    int UnregisterProfile(ref Guid clsid, ushort langid, ref Guid profile, uint flags);

    [PreserveSig]
    int GetActiveProfile(ref Guid category, out TfInputProcessorProfile profile);
}

[StructLayout(LayoutKind.Sequential)]
internal struct TfInputProcessorProfile
{
    public uint ProfileType;
    public ushort Langid;
    public Guid Clsid;
    public Guid Profile;
    public Guid Category;
    public IntPtr Substitute;
    public uint Capabilities;
    public IntPtr Hkl;
    public uint Flags;
}
