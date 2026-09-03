using System.Runtime.InteropServices;

namespace T9Pane.Services;

internal enum NativeOsArch
{
    X86,
    X64,
    Arm64,
    Unsupported
}

internal sealed record SetupArchPlan(
    NativeOsArch Os,
    bool InstallArm64Ime,
    bool InstallX64Ime,
    bool InstallX86Ime,
    string PaneHostRid,
    string DotNetRuntimeRid,
    bool UninstallKeyWow64,
    bool UseSysnativeForNative64Regsvr);

/// <summary>
/// 安装程序是 32 位，才能在 32 位 Windows 上启动，ARM64 Surface 上走系统自带的 x86 模拟。
/// ARM64 系统装原生键盘进程，64 位 IME 用 Arm64X 转发：ARM64 进程进原生 DLL，x64 模拟进程进 x64 DLL。
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
            ProcessorArm64 => NativeOsArch.Arm64,
            _ => NativeOsArch.Unsupported
        };

    public static NativeOsArch CurrentOs() =>
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => NativeOsArch.Arm64,
            Architecture.X64 => NativeOsArch.X64,
            Architecture.X86 => NativeOsArch.X86,
            _ => NativeOsArch.Unsupported
        };

    public static string CurrentProcessImeArch() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            _ => Environment.Is64BitProcess ? "x64" : "x86"
        };

    public static bool CanInstall(NativeOsArch os) =>
        os is NativeOsArch.X64 or NativeOsArch.X86 or NativeOsArch.Arm64;

    public static SetupArchPlan For(NativeOsArch os, bool setupIs32Bit)
    {
        return os switch
        {
            NativeOsArch.X64 => new(
                NativeOsArch.X64,
                InstallArm64Ime: false,
                InstallX64Ime: true,
                InstallX86Ime: true,
                PaneHostRid: "win-x64",
                DotNetRuntimeRid: "win-x64",
                UninstallKeyWow64: true,
                UseSysnativeForNative64Regsvr: setupIs32Bit),
            NativeOsArch.X86 => new(
                NativeOsArch.X86,
                InstallArm64Ime: false,
                InstallX64Ime: false,
                InstallX86Ime: true,
                PaneHostRid: "win-x86",
                DotNetRuntimeRid: "win-x86",
                UninstallKeyWow64: false,
                UseSysnativeForNative64Regsvr: false),
            NativeOsArch.Arm64 => new(
                NativeOsArch.Arm64,
                InstallArm64Ime: true,
                InstallX64Ime: true,
                InstallX86Ime: true,
                PaneHostRid: "win-arm64",
                DotNetRuntimeRid: "win-arm64",
                UninstallKeyWow64: true,
                UseSysnativeForNative64Regsvr: setupIs32Bit),
            _ => throw new ArgumentOutOfRangeException(nameof(os), os, "只支持 32 位、64 位或 ARM64 Windows")
        };
    }

    public static SetupArchPlan ForCurrentProcess() =>
        For(CurrentOs(), setupIs32Bit: !Environment.Is64BitProcess);

    public static string[] ImeArches(SetupArchPlan plan)
    {
        var arches = new List<string>(3);
        if (plan.InstallArm64Ime)
        {
            arches.Add("arm64");
        }

        if (plan.InstallX64Ime)
        {
            arches.Add("x64");
        }

        if (plan.InstallX86Ime)
        {
            arches.Add("x86");
        }

        return [.. arches];
    }

    public static bool UsesArm64X(SetupArchPlan plan) =>
        plan.Os == NativeOsArch.Arm64;

    public static string[] RegisterImeArches(SetupArchPlan plan) =>
        UsesArm64X(plan) ? ["arm64x", "x86"] : ImeArches(plan);

    public static string Native64ImeFolder(SetupArchPlan plan) =>
        UsesArm64X(plan) ? "arm64x" : "x64";

    public static bool UsesWow6432Node(NativeOsArch os) =>
        os is NativeOsArch.X64 or NativeOsArch.Arm64;

    public static string X86InprocKey(SetupArchPlan plan, string clsid) =>
        UsesWow6432Node(plan.Os)
            ? $@"Software\Classes\Wow6432Node\CLSID\{clsid}\InprocServer32"
            : $@"Software\Classes\CLSID\{clsid}\InprocServer32";

    public static string Native64InprocKey(string clsid) =>
        $@"Software\Classes\CLSID\{clsid}\InprocServer32";

    public static string Regsvr32RelativePath(SetupArchPlan plan, string imeArch)
    {
        if (imeArch.Equals("x86", StringComparison.OrdinalIgnoreCase))
        {
            return UsesWow6432Node(plan.Os) ? @"SysWOW64\regsvr32.exe" : @"System32\regsvr32.exe";
        }

        return plan.UseSysnativeForNative64Regsvr ? @"sysnative\regsvr32.exe" : @"System32\regsvr32.exe";
    }

    public static string DotNetRuntimeUrl(string rid) =>
        DotNetRuntimePolicy.ChannelUrl(rid);
}
