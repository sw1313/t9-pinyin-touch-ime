using System.Runtime.InteropServices;

namespace T9Pane.Services;

internal static class T9ProfileProbe
{
    private static readonly Guid ClsidTfInputProcessorProfiles =
        new("33C53A50-F456-4884-B049-85FD643ECFED");
    private static readonly Guid TipKeyboard =
        new("34745C63-B2F0-4784-8B67-5E12C8701A31");
    private static readonly Guid T9Clsid =
        new("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001");
    private static readonly Guid T9Profile =
        new("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002");

    private static DateTime _cachedUtc = DateTime.MinValue;
    private static bool _cached;

    public static bool IsSelected()
    {
        if (DateTime.UtcNow - _cachedUtc < TimeSpan.FromMilliseconds(400))
        {
            return _cached;
        }

        _cached = Query();
        _cachedUtc = DateTime.UtcNow;
        return _cached;
    }

    private static bool Query()
    {
        object? obj = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ClsidTfInputProcessorProfiles, throwOnError: false);
            if (type is null)
            {
                return false;
            }

            obj = Activator.CreateInstance(type);
            if (obj is not ITfInputProcessorProfileMgr mgr)
            {
                return false;
            }

            var catid = TipKeyboard;
            if (mgr.GetActiveProfile(ref catid, out var profile) != 0)
            {
                return false;
            }

            return profile.clsid == T9Clsid && profile.guidProfile == T9Profile;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (obj is not null && Marshal.IsComObject(obj))
            {
                Marshal.FinalReleaseComObject(obj);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TfInputProcessorProfile
    {
        public uint dwProfileType;
        public ushort langid;
        public ushort padding;
        public Guid clsid;
        public Guid guidProfile;
        public Guid catid;
        public IntPtr hkl;
        public uint dwFlags;
    }

    [ComImport]
    [Guid("71C6E74C-0F28-11D8-A82A-00065B84435C")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITfInputProcessorProfileMgr
    {
        [PreserveSig]
        int ActivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);

        [PreserveSig]
        int DeactivateProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, uint dwFlags);

        [PreserveSig]
        int GetProfile(uint dwProfileType, ushort langid, ref Guid clsid, ref Guid guidProfile, IntPtr hkl, out TfInputProcessorProfile profile);

        [PreserveSig]
        int EnumProfiles(ushort langid, out IntPtr ppEnum);

        [PreserveSig]
        int ReleaseInputProcessor(ref Guid rclsid, uint dwFlags);

        [PreserveSig]
        int RegisterProfile(
            ref Guid rclsid,
            ushort langid,
            ref Guid guidProfile,
            [MarshalAs(UnmanagedType.LPWStr)] string pchDesc,
            uint cchDesc,
            [MarshalAs(UnmanagedType.LPWStr)] string pchIconFile,
            uint cchFile,
            uint uIconIndex,
            IntPtr hklsubstitute,
            uint dwPreferredLayout,
            [MarshalAs(UnmanagedType.Bool)] bool enabledByDefault,
            uint dwFlags);

        [PreserveSig]
        int UnregisterProfile(ref Guid rclsid, ushort langid, ref Guid guidProfile, uint dwFlags);

        [PreserveSig]
        int GetActiveProfile(ref Guid catid, out TfInputProcessorProfile profile);
    }
}
