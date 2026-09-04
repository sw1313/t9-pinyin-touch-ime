namespace T9Pane.Services;

/// <summary>
/// 「显示触摸键盘」跟语言栏当前键盘 TIP，不跟后台进程残留的 T9 线程。
/// Vista+ 官方读法是 ITfInputProcessorProfileMgr::GetActiveProfile(GUID_TFCAT_TIP_KEYBOARD)。
/// CTF Assemblies / GetDefaultLanguageProfile 只是该语言的默认 TIP，切语言栏不会改。
/// </summary>
internal static class OfficialT9ProfilePolicy
{
    public static readonly Guid T9Clsid = new("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1001");
    public static readonly Guid T9Profile = new("A7E91C20-4B3D-4F18-9C2A-1B8E6D0A1002");
    public static readonly Guid TipKeyboard = new("34745C63-B2F0-4784-8B67-5E12C8701A31");
    public const ushort SimplifiedChinese = 0x0804;

    public static bool IsT9Layout(Guid clsid, Guid profile) =>
        clsid == T9Clsid && (profile == Guid.Empty || profile == T9Profile);

    /// <summary>
    /// 当前语言栏才算选中。CTF 默认程序集即使仍是 T9，切到微软拼音后也不算。
    /// </summary>
    public static bool IsCurrentSelection(bool getActiveSucceeded, bool isT9Layout) =>
        getActiveSucceeded && isT9Layout;

    public static bool TryParseAssembly(string? clsidText, string? profileText, out Guid clsid, out Guid profile)
    {
        clsid = default;
        profile = default;
        if (!Guid.TryParse(clsidText, out clsid))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(profileText))
        {
            return true;
        }

        return Guid.TryParse(profileText, out profile);
    }

    public static ushort LangidFromHkl(IntPtr hkl)
    {
        var raw = unchecked((uint)hkl.ToInt64());
        return unchecked((ushort)(raw & 0xFFFF));
    }

    public static string AssemblyKey(ushort langid) =>
        $@"Software\Microsoft\CTF\Assemblies\0x{langid:X8}\{{{TipKeyboard.ToString("D").ToUpperInvariant()}}}";
}
