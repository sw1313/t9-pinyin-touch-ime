namespace T9Pane.Services;

internal enum NativeOsArch
{
    X86,
    X64,
    Unsupported
}

internal sealed record SetupArchPlan(
    NativeOsArch Os,
    bool InstallX64Ime,
    bool InstallX86Ime,
    string PaneHostRid,
    string DotNetRuntimeRid,
    bool UninstallKeyWow64,
    bool UseSysnativeForX64Regsvr);

/// <summary>
/// 安装程序是 32 位，才能在 32 位 Windows 上启动。
/// 64 位系统仍装 64 位键盘进程，并同时注册 x64/x86 IME；32 位系统只装 x86。
/// </summary>
internal static class SetupArchPolicy
{
    public const int ProcessorIntel = 0;
    public const int ProcessorAmd64 = 9;
    public const int ProcessorArm64 = 12;

    public static NativeOsArch FromProcessorArchitecture(int architecture) =>
        architecture switch
        {
            ProcessorAmd64 => NativeOsArch.X64,
            ProcessorIntel => NativeOsArch.X86,
            _ => NativeOsArch.Unsupported
        };

    public static bool CanInstall(NativeOsArch os) =>
        os is NativeOsArch.X64 or NativeOsArch.X86;

    public static SetupArchPlan For(NativeOsArch os, bool setupIs32Bit)
    {
        return os switch
        {
            NativeOsArch.X64 => new(
                NativeOsArch.X64,
                InstallX64Ime: true,
                InstallX86Ime: true,
                PaneHostRid: "win-x64",
                DotNetRuntimeRid: "win-x64",
                UninstallKeyWow64: true,
                UseSysnativeForX64Regsvr: setupIs32Bit),
            NativeOsArch.X86 => new(
                NativeOsArch.X86,
                InstallX64Ime: false,
                InstallX86Ime: true,
                PaneHostRid: "win-x86",
                DotNetRuntimeRid: "win-x86",
                UninstallKeyWow64: false,
                UseSysnativeForX64Regsvr: false),
            _ => throw new ArgumentOutOfRangeException(nameof(os), os, "只支持 32 位或 64 位 Windows")
        };
    }

    public static SetupArchPlan ForCurrentProcess() =>
        For(
            Environment.Is64BitOperatingSystem ? NativeOsArch.X64 : NativeOsArch.X86,
            setupIs32Bit: !Environment.Is64BitProcess);

    public static string[] ImeArches(SetupArchPlan plan)
    {
        if (plan.InstallX64Ime && plan.InstallX86Ime)
        {
            return ["x64", "x86"];
        }

        return plan.InstallX86Ime ? ["x86"] : plan.InstallX64Ime ? ["x64"] : [];
    }

    public static string X86InprocKey(SetupArchPlan plan, string clsid) =>
        plan.Os == NativeOsArch.X64
            ? $@"Software\Classes\Wow6432Node\CLSID\{clsid}\InprocServer32"
            : $@"Software\Classes\CLSID\{clsid}\InprocServer32";

    public static string X64InprocKey(string clsid) =>
        $@"Software\Classes\CLSID\{clsid}\InprocServer32";

    public static string Regsvr32RelativePath(SetupArchPlan plan, string imeArch)
    {
        if (imeArch.Equals("x64", StringComparison.OrdinalIgnoreCase))
        {
            return plan.UseSysnativeForX64Regsvr ? @"sysnative\regsvr32.exe" : @"System32\regsvr32.exe";
        }

        return plan.Os == NativeOsArch.X64 ? @"SysWOW64\regsvr32.exe" : @"System32\regsvr32.exe";
    }

    public static string DotNetRuntimeUrl(string rid) =>
        $"https://aka.ms/dotnet/8.0/windowsdesktop-runtime-{rid}.exe";
}
